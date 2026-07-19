using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// 产品变更历史清理服务
/// - 默认永久保留 (retention_days = 0)
/// - 客户可在 system_settings 修改
/// - 每天凌晨 3 点 (默认 cron) 执行清理
/// - 清理前先 Count,避免误删
/// </summary>
public class HistoryCleanupService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<HistoryCleanupService> _logger;
    private readonly IHostedServiceStatus _hostedStatus;

    // 默认配置 (启动时若 system_settings 中无则插入)
    private static readonly (string Key, string Value, string Description)[] Defaults = new[]
    {
        ("history.retention_enabled", "true", "历史清理全局开关 (true/false)"),
        ("history.retention_days", "0", "保留天数 (0=永久;>0=N天前清理)"),
        ("history.cleanup_batch_size", "10000", "单批删除上限"),
        ("history.cleanup_cron", "0 3 * * *", "执行时间 (Cron, 服务器本地时区)"),
    };

    public HistoryCleanupService(IServiceProvider sp, ILogger<HistoryCleanupService> logger, IHostedServiceStatus hostedStatus)
    {
        _sp = sp;
        _logger = logger;
        _hostedStatus = hostedStatus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // V24-F87 (P2-2): 启动时确保默认配置存在 (内联, 原 EnsureDefaultSettingsAsync 仅一处调用)
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            await DefaultSettingsEnsurer.EnsureAsync(db, Defaults, _logger, nameof(HistoryCleanupService), stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            _hostedStatus.ReportAlive(nameof(HistoryCleanupService));
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "历史清理任务异常,下一轮重试");
            }

            // 简化:每 24h 跑一次 (后续可换 Cronos / NCrontab 解析 cron 表达式)
            // WHY: 不引入 cron 解析依赖,24h 间隔满足 1 天 1 次的需求
            // P1-5.1: 用 WaitWithHeartbeatAsync 分段上报心跳,避免 24h 等待期间被 /health/ready 误判为 stale
            await _hostedStatus.WaitWithHeartbeatAsync(nameof(HistoryCleanupService), TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();

        // 1) 读配置
        var settings = await db.SystemSettings
            .Where(s => s.Key == "history.retention_enabled"
                     || s.Key == "history.retention_days"
                     || s.Key == "history.cleanup_batch_size")
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        if (settings.GetValueOrDefault("history.retention_enabled") != "true")
        {
            _logger.LogInformation("历史清理已禁用 (history.retention_enabled != true)");
            return;
        }

        var retentionDays = int.Parse(settings.GetValueOrDefault("history.retention_days") ?? "0");
        var batchSize = int.Parse(settings.GetValueOrDefault("history.cleanup_batch_size") ?? "10000");

        if (retentionDays <= 0)
        {
            _logger.LogInformation("历史永久保留 (retention_days=0),跳过清理");
            return;
        }

        // 2) 计算截止时间
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        _logger.LogInformation("开始清理 {Cutoff} 之前的历史记录 (保留 {Days} 天, 批大小 {Batch})",
            cutoff, retentionDays, batchSize);

        // 3) 分批删除 (避免长事务 + 表锁)
        long totalDeleted = 0;
        while (!ct.IsCancellationRequested)
        {
            // 先取出待删的 ID (避免大批 DELETE 锁表)
            var idsToDelete = await db.ProductHistory
                .Where(h => h.ChangedAt < cutoff)
                .OrderBy(h => h.Id)
                .Take(batchSize)
                .Select(h => h.Id)
                .ToListAsync(ct);

            if (idsToDelete.Count == 0) break;

            var deleted = await db.ProductHistory
                .Where(h => idsToDelete.Contains(h.Id))
                .ExecuteDeleteAsync(ct);

            totalDeleted += deleted;
            _logger.LogInformation("本批删除 {Deleted} 条, 累计 {Total}", deleted, totalDeleted);

            if (deleted < batchSize) break;  // 没有更多了
        }

        _logger.LogInformation("历史清理完成: 共删除 {Total} 条 (cutoff={Cutoff})", totalDeleted, cutoff);
    }
}
