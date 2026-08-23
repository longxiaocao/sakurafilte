namespace SakuraFilter.Api.Services;

/// <summary>
/// 死信恢复纯函数集 (v30-15 提取自 DeadLetterRecoveryService)
/// WHY 提取独立类: 原 IsTransientError 逻辑混在 IQueryable Where 表达式树中无法直接单测,
///   ParseSettings 逻辑混在 RunOnceAsync 中无法直接单测。
///   方案 C (本方案) 提取到 public 静态类, 既可单测又提升代码结构。
///
/// 提取的纯函数:
///   - IsTransientError: 判断 LastError 是否为瞬时错误 (12 个关键词, 可自动恢复)
///   - ParseSettings: 解析死信恢复配置 (4 项: poll/cooling/max/batch, 带默认值兜底)
/// </summary>
public static class DeadLetterRecoveryClassifier
{
    /// <summary>
    /// 判断 LastError 是否为瞬时错误 (可自动恢复)
    /// WHY 12 个关键词: connectionrefused/connection refused/timeout/timed out/network/unreachable/5xx/internal server error/service unavailable
    ///   这些错误通常是临时性的 (网络抖动/服务重启), 重试有较大概率成功
    /// NOTE: 此方法用于内存判断 + 单测覆盖关键词逻辑。
    ///   DeadLetterRecoveryService.RecoverInternalAsync 中的 IQueryable Where 表达式树
    ///   仍保持原 12 个 Contains 写法 (EF Core 翻译成 SQL), 关键词列表需与此方法保持一致。
    /// </summary>
    public static bool IsTransientError(string? lastError)
    {
        if (string.IsNullOrEmpty(lastError)) return false;
        var err = lastError.ToLower();
        return err.Contains("connectionrefused")
            || err.Contains("connection refused")
            || err.Contains("timeout")
            || err.Contains("timed out")
            || err.Contains("network")
            || err.Contains("unreachable")
            || err.Contains(" 500 ")
            || err.Contains(" 502 ")
            || err.Contains(" 503 ")
            || err.Contains(" 504 ")
            || err.Contains("internal server error")
            || err.Contains("service unavailable");
    }

    /// <summary>
    /// 解析死信恢复配置 (4 项)
    ///   pollMinutes: 扫描周期, 默认 5min
    ///   coolingMinutes: 自动恢复冷却, 默认 10min (同一 entry 至少隔 N min 再自动重试)
    ///   maxCount: 单条死信自动恢复次数硬上限, 默认 3 (超过后必须人工 recover)
    ///   batchSize: 单批移回 pending 的条数上限, 默认 50 (避免一次推太多)
    /// WHY 安全解析: system_settings 是 Dictionary&lt;string, string&gt;, 需 TryParse + 默认值兜底
    ///   场景: key 缺失 / 值为空 / 值非数字 / 值 &lt;= 0 时返回默认值
    /// </summary>
    public static DeadLetterRecoverySettings ParseSettings(IReadOnlyDictionary<string, string?> settings)
    {
        return new DeadLetterRecoverySettings
        {
            PollMinutes = ParsePositiveInt(settings, "dead_letter.recovery_poll_minutes", 5),
            CoolingMinutes = ParsePositiveInt(settings, "dead_letter.recovery_cooling_minutes", 10),
            MaxCount = ParsePositiveInt(settings, "dead_letter.recovery_max_count", 3),
            BatchSize = ParsePositiveInt(settings, "dead_letter.recovery_batch_size", 50),
        };
    }

    private static int ParsePositiveInt(IReadOnlyDictionary<string, string?> settings, string key, int defaultValue)
    {
        return settings.TryGetValue(key, out var v) && int.TryParse(v, out var n) && n > 0 ? n : defaultValue;
    }
}

/// <summary>
/// 死信恢复配置 (ParseSettings 返回值)
/// </summary>
public sealed class DeadLetterRecoverySettings
{
    public int PollMinutes { get; init; }
    public int CoolingMinutes { get; init; }
    public int MaxCount { get; init; }
    public int BatchSize { get; init; }
}
