using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// ETL 运行历史清理服务 (Day 7.8)
/// - 默认永久保留 (etl_log.retention_days = 0)
/// - 运维可在 system_settings 修改
/// - 每天凌晨 4 点 (避开 history.cleanup_cron=3 点) 执行
/// - 清理前先 Count,避免误删
/// - 分批删除防止长事务 + 表锁
///
/// WHY 单独服务而非塞进 HistoryCleanupService:
///   - 关注点分离: 产品历史 vs ETL 历史的保留策略可独立调
///   - 测试隔离: ETL 清理出问题不影响 product_history 业务
///   - 调度解耦: 后续可单独调整 cron
/// </summary>
public class EtlLogCleanupService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<EtlLogCleanupService> _logger;
    private readonly IHostedServiceStatus _hostedStatus;

    // 默认配置 (启动时若 system_settings 中无则插入)
    private static readonly (string Key, string Value, string Description)[] Defaults = new[]
    {
        ("etl_log.retention_enabled", "false", "ETL 历史清理全局开关 (true/false, 默认关闭=永久保留)"),
        ("etl_log.retention_days", "90", "保留天数 (0=永久;>0=N天前清理,默认 90 天)"),
        ("etl_log.cleanup_batch_size", "5000", "单批删除上限"),
    };

    public EtlLogCleanupService(IServiceProvider sp, ILogger<EtlLogCleanupService> logger, IHostedServiceStatus hostedStatus)
    {
        _sp = sp;
        _logger = logger;
        _hostedStatus = hostedStatus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动时确保默认配置存在
        await EnsureDefaultSettingsAsync(stoppingToken);

        // 简单 24h 循环 (与 HistoryCleanupService 保持一致,避免引入 cron 解析依赖)
        while (!stoppingToken.IsCancellationRequested)
        {
            _hostedStatus.ReportAlive(nameof(EtlLogCleanupService));
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ETL 日志清理任务异常,下一轮重试");
            }

            // P1-5.1: 用 WaitWithHeartbeatAsync 分段上报心跳,避免 24h 等待期间被 /health/ready 误判为 stale
            await _hostedStatus.WaitWithHeartbeatAsync(nameof(EtlLogCleanupService), TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task EnsureDefaultSettingsAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        foreach (var (key, value, desc) in Defaults)
        {
            var exists = await db.SystemSettings.AnyAsync(s => s.Key == key, ct);
            if (!exists)
            {
                db.SystemSettings.Add(new SystemSetting
                {
                    Key = key,
                    Value = value,
                    Description = desc,
                    UpdatedAt = DateTime.UtcNow
                });
                _logger.LogInformation("插入 ETL 日志清理默认配置: {Key} = {Value}", key, value);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();

        // 1) 读配置
        var settings = await db.SystemSettings
            .Where(s => s.Key == "etl_log.retention_enabled"
                     || s.Key == "etl_log.retention_days"
                     || s.Key == "etl_log.cleanup_batch_size")
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        if (settings.GetValueOrDefault("etl_log.retention_enabled") != "true")
        {
            _logger.LogInformation("ETL 历史清理已禁用 (etl_log.retention_enabled != true),跳过");
            return;
        }

        var retentionDays = int.Parse(settings.GetValueOrDefault("etl_log.retention_days") ?? "0");
        var batchSize = int.Parse(settings.GetValueOrDefault("etl_log.cleanup_batch_size") ?? "5000");

        if (retentionDays <= 0)
        {
            _logger.LogInformation("ETL 历史永久保留 (etl_log.retention_days=0),跳过清理");
            return;
        }

        // 2) 计算截止时间
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        // 3) 先 Count 整体规模
        var totalCandidate = await db.EtlProgressLogs
            .Where(l => l.FinishedAt < cutoff)
            .LongCountAsync(ct);
        if (totalCandidate == 0)
        {
            _logger.LogInformation("ETL 日志无需清理 (cutoff={Cutoff}, 无候选记录)", cutoff);
            return;
        }
        _logger.LogInformation("开始清理 {Cutoff} 之前 ETL 日志,候选 {Total} 条 (保留 {Days} 天, 批大小 {Batch})",
            cutoff, totalCandidate, retentionDays, batchSize);

        // 4) 分批删除 (避免长事务 + 表锁)
        long totalDeleted = 0;
        while (!ct.IsCancellationRequested)
        {
            // 先取出待删的 ID (避免大批 DELETE 锁表)
            var idsToDelete = await db.EtlProgressLogs
                .Where(l => l.FinishedAt < cutoff)
                .OrderBy(l => l.Id)
                .Take(batchSize)
                .Select(l => l.Id)
                .ToListAsync(ct);

            if (idsToDelete.Count == 0) break;

            var deleted = await db.EtlProgressLogs
                .Where(l => idsToDelete.Contains(l.Id))
                .ExecuteDeleteAsync(ct);

            totalDeleted += deleted;
            _logger.LogInformation("本批删除 {Deleted} 条, 累计 {Total}/{Candidate}",
                deleted, totalDeleted, totalCandidate);

            if (deleted < batchSize) break;  // 没有更多了
        }

        _logger.LogInformation("ETL 日志清理完成: 共删除 {Total} 条 (cutoff={Cutoff})", totalDeleted, cutoff);
    }
}
