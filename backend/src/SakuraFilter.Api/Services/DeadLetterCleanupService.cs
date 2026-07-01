using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// Meili 死信队列清理服务 (Day 7.9)
/// - 默认保留 7 天 (dead_letter.retention_days = 7)
/// - 默认关闭,需运维开启 (dead_letter.retention_enabled = false)
/// - 每天凌晨 5 点执行 (避开 history=3 点、etl_log=4 点)
/// - 分批删除防止长事务 + 表锁
///
/// WHY 单独服务而非塞进 IndexReplayWorker:
///   - IndexReplayWorker 是高频实时任务 (10s 轮询),混合清理逻辑会拖慢补偿延迟
///   - 清理策略 (保留 7/30/90 天) 独立可调
///   - 后续可单独调整 cron
/// </summary>
public class DeadLetterCleanupService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<DeadLetterCleanupService> _logger;

    // 默认配置 (启动时若 system_settings 中无则插入)
    private static readonly (string Key, string Value, string Description)[] Defaults = new[]
    {
        ("dead_letter.retention_enabled", "false", "死信清理全局开关 (true/false, 默认关闭)"),
        ("dead_letter.retention_days", "7", "保留天数 (默认 7 天,排查完成后建议调长)"),
        ("dead_letter.cleanup_batch_size", "2000", "单批删除上限"),
    };

    public DeadLetterCleanupService(IServiceProvider sp, ILogger<DeadLetterCleanupService> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动时确保默认配置存在
        await EnsureDefaultSettingsAsync(stoppingToken);

        // 简单 24h 循环 (与其它 cleanup 服务保持一致,避免引入 cron 解析依赖)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "死信清理任务异常,下一轮重试");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
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
                _logger.LogInformation("插入死信清理默认配置: {Key} = {Value}", key, value);
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
            .Where(s => s.Key == "dead_letter.retention_enabled"
                     || s.Key == "dead_letter.retention_days"
                     || s.Key == "dead_letter.cleanup_batch_size")
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        if (settings.GetValueOrDefault("dead_letter.retention_enabled") != "true")
        {
            _logger.LogInformation("死信清理已禁用 (dead_letter.retention_enabled != true),跳过");
            return;
        }

        var retentionDays = int.Parse(settings.GetValueOrDefault("dead_letter.retention_days") ?? "7");
        var batchSize = int.Parse(settings.GetValueOrDefault("dead_letter.cleanup_batch_size") ?? "2000");

        if (retentionDays <= 0)
        {
            _logger.LogInformation("死信永久保留 (dead_letter.retention_days=0),跳过清理");
            return;
        }

        // 2) 计算截止时间 (按 moved_at 算,而非 created_at:
        //    moved_at = 进入死信的时间;created_at = 进入 pending 的时间,可能远早于 moved_at)
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        // 3) 先 Count 整体规模
        var totalCandidate = await db.SearchIndexDeadLetters
            .Where(d => d.MovedAt < cutoff)
            .LongCountAsync(ct);
        if (totalCandidate == 0)
        {
            _logger.LogInformation("死信无需清理 (cutoff={Cutoff}, 无候选记录)", cutoff);
            return;
        }
        _logger.LogInformation("开始清理 {Cutoff} 之前死信,候选 {Total} 条 (保留 {Days} 天, 批大小 {Batch})",
            cutoff, totalCandidate, retentionDays, batchSize);

        // 4) 分批删除
        long totalDeleted = 0;
        while (!ct.IsCancellationRequested)
        {
            var idsToDelete = await db.SearchIndexDeadLetters
                .Where(d => d.MovedAt < cutoff)
                .OrderBy(d => d.Id)
                .Take(batchSize)
                .Select(d => d.Id)
                .ToListAsync(ct);

            if (idsToDelete.Count == 0) break;

            var deleted = await db.SearchIndexDeadLetters
                .Where(d => idsToDelete.Contains(d.Id))
                .ExecuteDeleteAsync(ct);

            totalDeleted += deleted;
            _logger.LogInformation("本批删除 {Deleted} 条, 累计 {Total}/{Candidate}",
                deleted, totalDeleted, totalCandidate);

            if (deleted < batchSize) break;
        }

        _logger.LogInformation("死信清理完成: 共删除 {Total} 条 (cutoff={Cutoff})", totalDeleted, cutoff);
    }
}
