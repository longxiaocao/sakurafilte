namespace SakuraFilter.Api.Services;

/// <summary>
/// ETL 日志清理纯函数集 (v30-16 提取自 EtlLogCleanupService)
/// WHY 提取独立类: 原 ParseSettings 逻辑混在 RunOnceAsync 中, 且用 int.Parse 在无效配置时
///   会抛异常崩溃 BackgroundService。方案 C 提取到 public 静态类, 既可单测又改用 TryParse 安全解析。
///
/// 提取的纯函数:
///   - ParseSettings: 解析 ETL 日志清理配置 (2 项: retentionDays + batchSize, 带默认值兜底)
///
/// 顺手修复 P1: int.Parse (无效配置崩溃) → TryParse + 默认值 (无效配置降级为默认值)
/// </summary>
public static class EtlLogCleanupClassifier
{
    /// <summary>
    /// 解析 ETL 日志清理配置 (2 项)
    ///   retentionDays: 保留天数, 默认 90 (0=永久保留, >0=N 天前清理)
    ///   batchSize: 单批删除上限, 默认 5000 (避免长事务 + 表锁)
    /// WHY 安全解析: system_settings 是 Dictionary&lt;string, string?&gt;, 需 TryParse + 默认值兜底
    ///   场景: key 缺失 / 值为空 / 值非数字 / 值 &lt; 0 时返回默认值
    ///   修复: 原代码 int.Parse 在无效配置时抛 FormatException 崩溃 BackgroundService,
    ///     改用 TryParse + 默认值, 无效配置降级为默认值 (服务继续运行, 不崩溃)
    /// </summary>
    public static EtlLogCleanupSettings ParseSettings(IReadOnlyDictionary<string, string?> settings)
    {
        return new EtlLogCleanupSettings
        {
            // RetentionDays: 允许 0/负数透传, 下游 RunOnceAsync 用 <= 0 判断跳过 (永久保留语义)
            RetentionDays = ParseInt(settings, "etl_log.retention_days", 90),
            // BatchSize: 必须 > 0, 原代码负数会导致 EF Core Take 抛异常, 这里修复为默认值兜底
            BatchSize = ParsePositiveInt(settings, "etl_log.cleanup_batch_size", 5000),
        };
    }

    private static int ParseInt(IReadOnlyDictionary<string, string?> settings, string key, int defaultValue)
    {
        return settings.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : defaultValue;
    }

    private static int ParsePositiveInt(IReadOnlyDictionary<string, string?> settings, string key, int defaultValue)
    {
        return settings.TryGetValue(key, out var v) && int.TryParse(v, out var n) && n > 0 ? n : defaultValue;
    }
}

/// <summary>
/// ETL 日志清理配置 (ParseSettings 返回值)
/// </summary>
public sealed class EtlLogCleanupSettings
{
    public int RetentionDays { get; init; }
    public int BatchSize { get; init; }
}
