using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// 死信自动恢复服务 (Day 7.10 Item 4)
///
/// 设计动机:
///   当前 dead_letter 只能人工 /api/admin/dead-letter/{id}/recover,
///   当故障是"瞬时"性质 (Meili 5xx / connection refused / timeout / OOM 短期) 时
///   人工 recover 太多,且可能漏掉凌晨告警的恢复窗口
///
/// 行为:
///   1) 每 N 分钟扫描 dead_letter,过滤:
///      - last_error 属于"瞬时错误" (ConnectionRefused / Timeout / 5xx / network)
///      - recovery_count < max_recovery_count (默认 3, 防止反复死循环)
///      - last_recovery_at 为空 OR 距 now >= cooling_minutes (默认 10min)
///   2) 把候选条目移回 search_index_pending (retry=0, next_retry_at=now)
///      → IndexReplayWorker 立即接管
///   3) 同步更新 dead_letter 的 recovery_count / last_recovery_at / last_recovery_error
///
/// 限位:
///   - max_recovery_count 硬上限 3: 超过即放弃,必须人工 recover
///   - cooling_minutes 硬下限 10min: 避免反复打 Meili
///   - 默认关闭: 需运维开启 dead_letter.auto_recovery_enabled = true
///
/// WHY 关键词白名单 (而非所有 last_error):
///   死信也可能因 "payload 永久损坏 / schema 不兼容" 入队,这类不应自动恢复
///   只对"明确是服务可用性问题" 的死信做自动恢复
/// </summary>
public class DeadLetterRecoveryService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<DeadLetterRecoveryService> _logger;

    // 默认配置 (启动时若 system_settings 中无则插入)
    private static readonly (string Key, string Value, string Description)[] Defaults = new[]
    {
        ("dead_letter.auto_recovery_enabled", "false", "死信自动恢复全局开关 (true/false, 默认关闭,需运维确认策略后开启)"),
        ("dead_letter.recovery_poll_minutes", "5", "扫描周期 (分钟),默认 5min"),
        ("dead_letter.recovery_cooling_minutes", "10", "自动恢复冷却 (分钟),同一 entry 至少隔 10min 再自动重试"),
        ("dead_letter.recovery_max_count", "3", "单条死信自动恢复次数硬上限,超过后必须人工 recover"),
        ("dead_letter.recovery_batch_size", "50", "单批移回 pending 的条数上限,避免一次推太多"),
    };

    public DeadLetterRecoveryService(IServiceProvider sp, ILogger<DeadLetterRecoveryService> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动时确保默认配置存在
        await EnsureDefaultSettingsAsync(stoppingToken);

        // 初始 delay: 等应用其它 worker (IndexReplayWorker / EtlAlertService) 起来
        //   避免启动瞬间多个 worker 并发改 dead_letter
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        int pollMinutes = 5;
        while (!stoppingToken.IsCancellationRequested)
        {
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
                _logger.LogInformation("插入死信自动恢复默认配置: {Key} = {Value}", key, value);
            }
        }
        await db.SaveChangesAsync(ct);
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

        // 2) 候选扫描
        //    条件: recovery_count < max + 冷却已到 + last_error 含瞬时错误关键词
        //    WHY ILIKE: 兼容多种写法 (ConnectionRefused / connection_refused / CONNECTION REFUSED)
        var now = DateTime.UtcNow;
        var coolingCutoff = now.AddMinutes(-coolingMinutes);

        // EF Core 不支持 | 拼接 ILIKE 的 OR 简写,展开为多个 OR
        var candidates = await db.SearchIndexDeadLetters
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

        if (candidates.Count == 0)
        {
            _logger.LogDebug("无符合自动恢复条件的死信 (max_count={Max}, cooling_minutes={Cool})",
                maxCount, coolingMinutes);
            return pollMinutes;
        }

        _logger.LogInformation("发现 {Count} 条死信符合自动恢复条件,移回 pending (max_count={Max}, cooling={Cool}min)",
            candidates.Count, maxCount, coolingMinutes);

        // 3) 移回 pending
        //    WHY 用同一 DB context 事务: 保证 dead_letter.delete + pending.insert 原子
        //    IndexReplayWorker 下次轮询 (10s) 即可拾取
        int moved = 0;
        var now2 = DateTime.UtcNow;
        foreach (var dead in candidates)
        {
            if (ct.IsCancellationRequested) break;

            // 防御: 二次校验 max_count,避免并发条件下超额
            if (dead.RecoveryCount >= maxCount)
            {
                _logger.LogWarning("跳过: id={Id} recovery_count={Rc} >= max={Max}",
                    dead.Id, dead.RecoveryCount, maxCount);
                continue;
            }

            try
            {
                var pending = new SearchIndexPending
                {
                    Operation = dead.Operation,
                    Payload = dead.Payload,
                    RetryCount = 0,
                    LastError = null,
                    CreatedAt = dead.CreatedAt,
                    NextRetryAt = now2,
                };
                db.SearchIndexPending.Add(pending);

                // 同步更新 dead_letter 元数据 (即使移回失败,也要记录尝试轨迹)
                dead.RecoveryCount += 1;
                dead.LastRecoveryAt = now2;
                dead.LastRecoveryError = null;

                db.SearchIndexDeadLetters.Remove(dead);
                moved++;
            }
            catch (Exception ex)
            {
                // 单条失败不影响其它,记录到 last_recovery_error (因为 dead 还存在)
                dead.LastRecoveryAt = now2;
                dead.LastRecoveryError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                _logger.LogWarning(ex, "死信 {Id} 自动恢复失败,记录到 last_recovery_error", dead.Id);
            }
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("死信自动恢复完成: 移回 pending {Moved}/{Total} 条", moved, candidates.Count);
        return pollMinutes;
    }
}
