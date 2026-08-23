using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// V24-F60: 默认配置项批量确保器 (消除 6 个 Service 的 N+1 反模式)
///
/// 原反模式 (EtlAlertService/HistoryCleanupService/EtlLogCleanupService/DeadLetterCleanupService/PerfAlertService/DeadLetterRecoveryService):
///   foreach (var (key, value, desc) in Defaults) { var exists = await db.SystemSettings.AnyAsync(s => s.Key == key, ct); }
///   N 条 Defaults 触发 N 次 SQL 查询
///
/// 修复: 1 次 SQL 批量预拉已存在的 key, 内存 HashSet 判断
///   参考 IndexReplayWorker.cs L245-264 批量预拉模板 (V24-F55 复用同模式)
///
/// WHY 静态 helper 而非基类:
///   - 6 个 Service 已分别继承 BackgroundService / object, 无法共享基类
///   - 静态 helper 无状态, 调用简单 (EnsureAsync(db, Defaults, _logger, nameof(Xxx), ct))
///   - 避免引入新抽象, 符合"最小设计"原则
/// </summary>
public static class DefaultSettingsEnsurer
{
    /// <summary>
    /// 确保 defaults 中的所有配置项存在于 system_settings 表, 缺失则插入。
    /// 1 次 SQL 批量预拉已存在 key, 内存 HashSet 判断, 避免循环内 AnyAsync 的 N+1。
    /// </summary>
    /// <param name="db">ProductDbContext (调用方负责 scope 创建和 dispose)</param>
    /// <param name="defaults">默认配置项列表 (Key, Value, Description)</param>
    /// <param name="logger">调用方 logger, 用于记录插入日志</param>
    /// <param name="serviceName">服务名 (日志上下文, 如 nameof(EtlAlertService))</param>
    /// <param name="ct">取消令牌</param>
    public static async Task EnsureAsync(
        ProductDbContext db,
        IEnumerable<(string Key, string Value, string Description)> defaults,
        ILogger logger,
        string serviceName,
        CancellationToken ct)
    {
        var defaultsList = defaults.ToList();
        if (defaultsList.Count == 0) return;

        var keys = defaultsList.Select(d => d.Key).ToList();

        // V24-F60: 1 次 SQL 批量预拉已存在的 key (原反模式: foreach 内 AnyAsync 触发 N 次 SQL)
        var existingKeys = await db.SystemSettings
            .Where(s => keys.Contains(s.Key))
            .Select(s => s.Key)
            .ToListAsync(ct);
        var existingSet = existingKeys.ToHashSet();

        foreach (var (key, value, desc) in defaultsList)
        {
            // V24-F61: 同时检查 DB 已存在和本次循环已添加, 防御 defaults 数组内重复 key
            //   WHY: 重复 Add 同 key 会触发 EF Core 实体跟踪冲突 (InvalidOperationException)
            //        或 DB 唯一约束冲突 (23505), 应在内存层提前去重
            if (existingSet.Contains(key)) continue;

            db.SystemSettings.Add(new SystemSetting
            {
                Key = key,
                Value = value,
                Description = desc,
                UpdatedAt = DateTime.UtcNow
            });
            existingSet.Add(key);  // 标记本次已添加, 防御 defaults 数组内重复 key
            // V24-F99 (P2-3, 规则 6.3): 敏感 key (webhook_url/secret/token/password) 的 value 脱敏为 ***
            //   WHY: 当前 EtlAlertService Defaults 中 webhook_url* 为空字符串, 不泄漏
            //     但未来若添加非空默认值 (如配置默认 webhook URL 含 secret), 会被日志记录
            //     防御性脱敏, 防止未来回归风险
            var displayValue = IsSensitiveKey(key) ? "***" : value;
            logger.LogInformation("插入 {Service} 默认配置: {Key} = {Value}", serviceName, key, displayValue);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>判断 system_setting key 是否为敏感配置 (需脱敏)</summary>
    private static bool IsSensitiveKey(string key)
    {
        // WHY 关键字匹配: 覆盖 webhook_url (含签名 secret), secret, token, password 等常见敏感配置
        return key.Contains("webhook_url", StringComparison.OrdinalIgnoreCase)
            || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("token", StringComparison.OrdinalIgnoreCase)
            || key.Contains("password", StringComparison.OrdinalIgnoreCase)
            || key.Contains("api_key", StringComparison.OrdinalIgnoreCase);
    }
}
