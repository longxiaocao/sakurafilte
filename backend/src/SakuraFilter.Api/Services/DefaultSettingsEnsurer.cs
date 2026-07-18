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
            if (existingSet.Contains(key)) continue;

            db.SystemSettings.Add(new SystemSetting
            {
                Key = key,
                Value = value,
                Description = desc,
                UpdatedAt = DateTime.UtcNow
            });
            logger.LogInformation("插入 {Service} 默认配置: {Key} = {Value}", serviceName, key, value);
        }
        await db.SaveChangesAsync(ct);
    }
}
