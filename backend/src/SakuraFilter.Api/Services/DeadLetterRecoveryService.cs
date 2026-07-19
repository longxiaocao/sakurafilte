using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;  // GetDbTransaction 扩展
using Npgsql;  // Day 7.10.1: advisory lock 需要
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// 死信自动恢复服务 (Day 7.10.1 — Bug Fix 重构)
///
/// Day 7.10 初版 bug:
///   原代码先递增 recovery_count, 然后 db.SearchIndexDeadLetters.Remove(dead) 删行。
///   EF Core 在 SaveChanges 时, Delete 操作的 EntityState.Deleted 会清空其他属性变更,
///   导致 recovery_count 永远不会被持久化。若恢复后再次失败, 以新 id 入队时
///   recovery_count=0, max_recovery_count 限位完全失效。
///
/// Day 7.10.1 修复:
///   1) 死信永不删除, 改 status='recovered' + recovered_at + recovered_to_pending_id
///   2) IndexReplayWorker 转入死信时, 若发现 payload 相同的最近 dead_letter
///      已 recovered, 复用并递增其 recovery_count (跨循环保留计数)
///   3) PostgreSQL advisory lock 串行化后台 worker 与 admin 端点
///      避免并发条件竞争 (后台 worker 选中的行正好被 admin batch 改 status)
///
/// 行为 (不变):
///   - 扫描 dead_letter WHERE status='active' AND recovery_count &lt; max AND 冷却已到
///     AND last_error 含"瞬时错误"关键词
///   - 移回 search_index_pending (retry=0, next_retry_at=now) 让 IndexReplayWorker 接手
///   - 同步: status='recovered' + recovery_count+=1 + recovered_at=now + recovered_to_pending_id
/// </summary>
public class DeadLetterRecoveryService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<DeadLetterRecoveryService> _logger;
    private readonly IHostedServiceStatus _hostedStatus;

    // Day 7.10.1: advisory lock key (与 admin /recover-batch 端点共享)
    //   WHY: 32-bit signed int, 任意 4 字节常量即可, 这里用 "DRL" 字符串哈希
    //   锁的粒度: 整个死信恢复逻辑, 包括 worker 扫描 + admin batch
    private const int AdvisoryLockKey = 0x44524C44;  // "DRLD" ASCII
    private static readonly NpgsqlParameter LockKeyParam =
        new NpgsqlParameter("key", AdvisoryLockKey);

    // 默认配置 (启动时若 system_settings 中无则插入)
    private static readonly (string Key, string Value, string Description)[] Defaults = new[]
    {
        ("dead_letter.auto_recovery_enabled", "false", "死信自动恢复全局开关 (true/false, 默认关闭,需运维确认策略后开启)"),
        ("dead_letter.recovery_poll_minutes", "5", "扫描周期 (分钟),默认 5min"),
        ("dead_letter.recovery_cooling_minutes", "10", "自动恢复冷却 (分钟),同一 entry 至少隔 10min 再自动重试"),
        ("dead_letter.recovery_max_count", "3", "单条死信自动恢复次数硬上限,超过后必须人工 recover"),
        ("dead_letter.recovery_batch_size", "50", "单批移回 pending 的条数上限,避免一次推太多"),
    };

    public DeadLetterRecoveryService(IServiceProvider sp, ILogger<DeadLetterRecoveryService> logger, IHostedServiceStatus hostedStatus)
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
            await DefaultSettingsEnsurer.EnsureAsync(db, Defaults, _logger, nameof(DeadLetterRecoveryService), stoppingToken);
        }

        // 初始 delay: 等应用其它 worker (IndexReplayWorker / EtlAlertService) 起来
        //   避免启动瞬间多个 worker 并发改 dead_letter
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        int pollMinutes = 5;
        while (!stoppingToken.IsCancellationRequested)
        {
            _hostedStatus.ReportAlive(nameof(DeadLetterRecoveryService));
            try
            {
                pollMinutes = await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "死信自动恢复任务异常,下一轮重试");
            }

            await Task.Delay(TimeSpan.FromMinutes(pollMinutes), stoppingToken);
        }
    }

    /// <summary>
    /// Day 7.10.1: 对外暴露 advisory lock 持有方法
    ///   1) 显式开事务 (db.Database.BeginTransactionAsync)
    ///   2) 事务内执行 pg_try_advisory_xact_lock — 锁随事务提交/回滚释放
    ///   3) 拿锁后执行 work, 事务结束自动释放
    ///   WHY 显式事务:
    ///      - pg_try_advisory_xact_lock 是事务级锁, 不开事务会随每个语句自动 commit 而立即释放
    ///      - 之前实现是 bug, lock 实际只保护了 SELECT 一瞬间, SaveChanges 时已无锁
    ///   WHY pg_try 而非 pg_advisory: 非阻塞, 锁竞争时 worker 立即放弃本轮
    /// </summary>
    public static async Task<bool> TryWithAdvisoryLockAsync(
        ProductDbContext db,
        Func<Task> work,
        CancellationToken ct)
    {
        // 显式事务 — lock 跟随事务生命周期
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        bool got = false;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = (NpgsqlTransaction)tx.GetDbTransaction();
            cmd.CommandText = "SELECT pg_try_advisory_xact_lock(@key)";
            cmd.Parameters.Add(new NpgsqlParameter("key", AdvisoryLockKey));
            var result = await cmd.ExecuteScalarAsync(ct);
            got = result is bool b && b;
        }
        if (!got)
        {
            // 锁被占用, 回滚事务 (释放 BEGIN 资源)
            await tx.RollbackAsync(ct);
            return false;
        }
        // 拿锁: 在事务内执行 work + SaveChanges
        await work();
        await tx.CommitAsync(ct);
        return true;
    }

    private async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();

        // 1) 读配置
        var settings = await db.SystemSettings
            .Where(s => s.Key.StartsWith("dead_letter.recovery_") || s.Key == "dead_letter.auto_recovery_enabled")
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        var enabled = settings.GetValueOrDefault("dead_letter.auto_recovery_enabled") == "true";
        var pollMinutes = int.TryParse(settings.GetValueOrDefault("dead_letter.recovery_poll_minutes"), out var pm) && pm > 0 ? pm : 5;
        var coolingMinutes = int.TryParse(settings.GetValueOrDefault("dead_letter.recovery_cooling_minutes"), out var cm) && cm > 0 ? cm : 10;
        var maxCount = int.TryParse(settings.GetValueOrDefault("dead_letter.recovery_max_count"), out var mc) && mc > 0 ? mc : 3;
        var batchSize = int.TryParse(settings.GetValueOrDefault("dead_letter.recovery_batch_size"), out var bs) && bs > 0 ? bs : 50;

        if (!enabled)
        {
            _logger.LogDebug("死信自动恢复已禁用 (dead_letter.auto_recovery_enabled != true),跳过");
            return pollMinutes;
        }

        // Day 7.10.1: 用 advisory lock 包裹, 避免与 admin /recover-batch 端点竞争
        bool gotLock = false;
        int moved = 0;
        try
        {
            gotLock = await TryWithAdvisoryLockAsync(db, async () =>
            {
                moved = await RecoverInternalAsync(db, coolingMinutes, maxCount, batchSize, ct);
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "advisory lock 内恢复任务异常");
        }

        if (!gotLock)
        {
            _logger.LogDebug("advisory lock 被占用 (admin /recover-batch 正在执行), 跳过本轮");
            return pollMinutes;
        }

        if (moved > 0)
            _logger.LogInformation("死信自动恢复完成: 移回 pending {Moved} 条 (max_count={Max}, cooling={Cool}min)",
                moved, maxCount, coolingMinutes);
        else
            _logger.LogDebug("死信自动恢复: 无候选");

        return pollMinutes;
    }

    /// <summary>
    /// 实际恢复逻辑 — 仅在拿到 advisory lock 后执行
    /// Day 7.10.1 关键修复: 死信行不删除, 改 status='recovered'
    /// </summary>
    private async Task<int> RecoverInternalAsync(
        ProductDbContext db, int coolingMinutes, int maxCount, int batchSize, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var coolingCutoff = now.AddMinutes(-coolingMinutes);

        // Day 7.10.1: 加 status='active' 过滤, 排除已恢复的历史条目
        var candidates = await db.SearchIndexDeadLetters
            .Where(d => d.Status == "active")
            .Where(d => d.RecoveryCount < maxCount)
            .Where(d => d.LastRecoveryAt == null || d.LastRecoveryAt < coolingCutoff)
            .Where(d =>
                (d.LastError != null && d.LastError.ToLower().Contains("connectionrefused"))
                || (d.LastError != null && d.LastError.ToLower().Contains("connection refused"))
                || (d.LastError != null && d.LastError.ToLower().Contains("timeout"))
                || (d.LastError != null && d.LastError.ToLower().Contains("timed out"))
                || (d.LastError != null && d.LastError.ToLower().Contains("network"))
                || (d.LastError != null && d.LastError.ToLower().Contains("unreachable"))
                || (d.LastError != null && d.LastError.ToLower().Contains(" 500 "))
                || (d.LastError != null && d.LastError.ToLower().Contains(" 502 "))
                || (d.LastError != null && d.LastError.ToLower().Contains(" 503 "))
                || (d.LastError != null && d.LastError.ToLower().Contains(" 504 "))
                || (d.LastError != null && d.LastError.ToLower().Contains("internal server error"))
                || (d.LastError != null && d.LastError.ToLower().Contains("service unavailable")))
            .OrderBy(d => d.MovedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        if (candidates.Count == 0) return 0;

        _logger.LogInformation("发现 {Count} 条死信符合自动恢复条件, 移回 pending (max_count={Max}, cooling={Cool}min)",
            candidates.Count, maxCount, coolingMinutes);

        int moved = 0;
        // Day 7.10.1 PATCH: 跟踪新增的 pending entity, SaveChanges 后直接从 instance 读 Id
        // WHY: 之前用 Payload+CreatedAt+RetryCount==0 重查匹配, 并发下可能匹配错行
        var addedPending = new Dictionary<long, SearchIndexPending>();
        foreach (var dead in candidates)
        {
            if (ct.IsCancellationRequested) break;

            // 二次校验 max_count, 避免长事务期间其他进程改写
            if (dead.RecoveryCount >= maxCount)
            {
                _logger.LogWarning("跳过: id={Id} recovery_count={Rc} >= max={Max}",
                    dead.Id, dead.RecoveryCount, maxCount);
                continue;
            }

            var pending = new SearchIndexPending
            {
                Operation = dead.Operation,
                Payload = dead.Payload,
                RetryCount = 0,
                LastError = null,
                CreatedAt = dead.CreatedAt,
                NextRetryAt = now,
            };
            db.SearchIndexPending.Add(pending);

            // Day 7.10.1 BUG FIX: 死信行不删除,改 status + 递增 recovery_count
            dead.Status = "recovered";
            dead.RecoveryCount += 1;
            dead.LastRecoveryAt = now;
            dead.LastRecoveryError = null;
            dead.RecoveredAt = now;
            // 关联 pending entity (SaveChanges 后能从这里直接读 Id)
            addedPending[dead.Id] = pending;
            moved++;
        }

        // SaveChanges 让 pending.Id 生成 (EF Core 会填充到 entity 实例)
        await db.SaveChangesAsync(ct);

        // 直接从跟踪的 pending entity 读 Id 回填
        // WHY: 比 "重查 Payload+CreatedAt" 安全, 不受并发影响
        foreach (var dead in candidates)
        {
            if (dead.Status != "recovered") continue;
            if (dead.RecoveredToPendingId.HasValue) continue;
            if (addedPending.TryGetValue(dead.Id, out var pending))
            {
                dead.RecoveredToPendingId = pending.Id;
            }
        }
        await db.SaveChangesAsync(ct);

        return moved;
    }
}
