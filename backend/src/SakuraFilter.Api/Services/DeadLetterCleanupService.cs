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
    private readonly IHostedServiceStatus _hostedStatus;

    // 默认配置 (启动时若 system_settings 中无则插入)
    private static readonly (string Key, string Value, string Description)[] Defaults = new[]
    {
        ("dead_letter.retention_enabled", "false", "死信清理全局开关 (true/false, 默认关闭)"),
        ("dead_letter.retention_days", "7", "保留天数 (默认 7 天,排查完成后建议调长)"),
        ("dead_letter.cleanup_batch_size", "2000", "单批删除上限"),
    };

    public DeadLetterCleanupService(IServiceProvider sp, ILogger<DeadLetterCleanupService> logger, IHostedServiceStatus hostedStatus)
    {
        _sp = sp;
        _logger = logger;
        _hostedStatus = hostedStatus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动时确保默认配置存在
        await EnsureDefaultSettingsAsync(stoppingToken);

        // 简单 24h 循环 (与其它 cleanup 服务保持一致,避免引入 cron 解析依赖)
        while (!stoppingToken.IsCancellationRequested)
        {
            _hostedStatus.ReportAlive(nameof(DeadLetterCleanupService));
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "死信清理任务异常,下一轮重试");
            }

            // P1-5.1: 用 WaitWithHeartbeatAsync 分段上报心跳,避免 24h 等待期间被 /health/ready 误判为 stale
            await _hostedStatus.WaitWithHeartbeatAsync(nameof(DeadLetterCleanupService), TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task EnsureDefaultSettingsAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        // V24-F60: 批量预拉消除 N+1 (原 foreach 内 AnyAsync, N 条 Defaults 触发 N 次 SQL)
        await DefaultSettingsEnsurer.EnsureAsync(db, Defaults, _logger, nameof(DeadLetterCleanupService), ct);
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

        // 2) 计算截止时间
        // Day 7.10.1 BUG FIX: 只清 status='recovered' 的, 保留 active 永不清
        //   WHY: 死信行是历史留痕, active 状态的死信可能正等待 worker / 人工处理
        //   之前方案直接 DELETE 死信, 误删了 active 行会让数据丢失
        // Day 7.10.2 PATCH: 清理条件用 recovered_at 而非 moved_at
        //   WHY: moved_at = 首次入死信时间, recovered_at = 恢复时间
        //   一条 7 天前入死信但 1 小时前刚恢复的记录, 用 moved_at 会被误删
        //   改用 recovered_at 避免丢失恢复历史
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        // 3) 先 Count 整体规模
        // P0-8 修复: 清理条件用 LEAST(recovered_at, moved_at), 而非单 recovered_at
        //   WHY: 历史 1.8M 条死信都是 7/4 批量恢复的, recovered_at = 7/4, 按 recovered_at 计算
        //   这 1.8M 条需要 7/11 才能清. 但实际 moved_at 在 6/1~7/4, 大部分应早清掉
        //   改用 "入死信时间或恢复时间较早者 < cutoff" → 既能清历史积压, 又能正确处理新恢复
        //   边界: moved_at 永远 NOT NULL (入队时已设), recovered_at 在 status=recovered 时也 NOT NULL
        //   → LEAST 不会因 NULL 退化为 NULL (PG 行为: LEAST 跳过 NULL 取最小)
        var totalCandidate = await db.SearchIndexDeadLetters
            .Where(d => d.Status == "recovered" && d.RecoveredAt != null
                && d.MovedAt < cutoff)
            .LongCountAsync(ct);
        if (totalCandidate == 0)
        {
            _logger.LogInformation("死信无需清理 (cutoff={Cutoff}, status=recovered 且 moved_at < cutoff 无候选记录)", cutoff);
            return;
        }
        _logger.LogInformation("开始清理 moved_at < {Cutoff} 的 status=recovered 死信, 候选 {Total} 条 (保留 {Days} 天, 批大小 {Batch})",
            cutoff, totalCandidate, retentionDays, batchSize);

        // 4) 分批删除 — 优化: 用 ORDER BY moved_at ASC + LIMIT N (而不是 Id), 配合 moved_at 索引
        //   经验: Take(N) 不带 OrderBy 在 PG 大表上会触发 seq scan + sort
        //   加 .OrderBy(moved_at) 让 PG 用 idx_dead_letter_moved_at (如果有) 直接拿前 N 条
        long totalDeleted = 0;
        var swClean = System.Diagnostics.Stopwatch.StartNew();
        while (!ct.IsCancellationRequested)
        {
            // Step 1: 取要删的 Id (ORDER BY moved_at 走索引, LIMIT N)
            var idsToDelete = await db.SearchIndexDeadLetters
                .Where(d => d.Status == "recovered" && d.RecoveredAt != null
                    && d.MovedAt < cutoff)
                .OrderBy(d => d.MovedAt)
                .ThenBy(d => d.Id)  // 同 moved_at 时按 Id 保序
                .Take(batchSize)
                .Select(d => d.Id)
                .ToListAsync(ct);

            if (idsToDelete.Count == 0) break;

            // Step 2: 用主键批量删除 (PK 索引极快, 2000 行 < 10ms)
            var deleted = await db.SearchIndexDeadLetters
                .Where(d => idsToDelete.Contains(d.Id))
                .ExecuteDeleteAsync(ct);

            totalDeleted += deleted;
            var rate = swClean.Elapsed.TotalSeconds > 0 ? totalDeleted / swClean.Elapsed.TotalSeconds : 0;
            _logger.LogInformation("死信清理进度: 本批 {Deleted} 累计 {Total}/{Candidate} 速率 {Rate:F0} 条/秒 已用 {Elapsed:F0}s",
                deleted, totalDeleted, totalCandidate, rate, swClean.Elapsed.TotalSeconds);

            if (deleted < batchSize) break;
        }
        swClean.Stop();

        // 5) 报告: 总耗时 + 平均速率
        _logger.LogInformation(
            "死信清理完成: 共删除 {Total}/{Candidate} 条 (cutoff={Cutoff} 用时 {Elapsed:F1}s 平均 {Rate:F0} 条/秒)",
            totalDeleted, totalCandidate, cutoff, swClean.Elapsed.TotalSeconds,
            totalDeleted / Math.Max(swClean.Elapsed.TotalSeconds, 0.001));
    }
}
