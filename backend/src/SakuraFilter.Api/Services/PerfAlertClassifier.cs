using SakuraFilter.Api.Services;
using SakuraFilter.Search;

namespace SakuraFilter.Api.Services;

/// <summary>
/// 性能告警纯函数集 (v30-13 提取自 PerfAlertService, v30-20 加 Meili 规则)
/// WHY 提取独立类: 原 ParseDouble 为 private static, TryEmit 的抑制判断逻辑混在私有方法中无法直接单测
///   方案 C (本方案) 提取到 public 静态类, 既可单测又提升代码结构
///
/// 提取的纯函数:
///   - ParseDouble: system_settings 字典解析 double (带默认值兜底)
///   - IsSuppressed: 判断告警是否在抑制窗口内 (5min 内同 level+rule 不重发)
///   - BuildSuppressKey: 构造抑制 key (level|rule)
///   - ClassifyMeiliSeverity (v30-20): Meili 规则严重度分类 (P0/P1/P2)
///   - BuildMeiliAlertContext (v30-20): 构造 Meili 告警上下文 (供 AlertCenter 持久化)
///   - BuildMeiliAlertMarkdown (v30-20): 构造 Meili 告警 Markdown 正文
/// </summary>
public static class PerfAlertClassifier
{
    /// <summary>
    /// 解析 system_settings 中的 double 值, 失败返回默认值
    /// WHY: system_settings 是 Dictionary&lt;string, string?&gt;, 需安全解析
    ///   场景: perf.alert.p95_warn_ms = "1000" → 1000.0
    ///         perf.alert.p95_warn_ms = "" / null / "abc" → 默认值
    /// </summary>
    public static double ParseDouble(Dictionary<string, string?> settings, string key, double defaultValue)
    {
        return settings.TryGetValue(key, out var s) && double.TryParse(s, out var v) ? v : defaultValue;
    }

    /// <summary>
    /// 构造告警抑制 key (level|rule)
    /// WHY: 同 level+rule 在 5min 窗口内不重发, 避免持续 P95 高时刷屏日志
    /// </summary>
    public static string BuildSuppressKey(string level, string rule)
    {
        return $"{level}|{rule}";
    }

    /// <summary>
    /// 判断告警是否在抑制窗口内
    /// </summary>
    /// <param name="suppressedKeys">抑制状态字典 (key=level|rule, value=上次告警时间)</param>
    /// <param name="level">告警级别 (WARN/ERROR)</param>
    /// <param name="rule">告警规则名 (p95_warn/p95_error/error_rate/max_ms)</param>
    /// <param name="now">当前时间 (UTC)</param>
    /// <param name="window">抑制窗口 (默认 5min)</param>
    /// <returns>true=在窗口内应抑制, false=可发送</returns>
    public static bool IsSuppressed(
        Dictionary<string, DateTime> suppressedKeys,
        string level,
        string rule,
        DateTime now,
        TimeSpan window)
    {
        var key = BuildSuppressKey(level, rule);
        if (suppressedKeys.TryGetValue(key, out var lastTime) &&
            now - lastTime < window)
        {
            return true;  // 抑制窗口内
        }
        return false;
    }

    /// <summary>
    /// 更新抑制状态 (发送告警后调用, 记录当前时间)
    /// </summary>
    public static void UpdateSuppression(
        Dictionary<string, DateTime> suppressedKeys,
        string level,
        string rule,
        DateTime now)
    {
        suppressedKeys[BuildSuppressKey(level, rule)] = now;
    }

    // ===== v30-20: Meili 主路径告警纯函数 =====

    /// <summary>
    /// Meili 告警规则名
    /// </summary>
    public static class MeiliRules
    {
        public const string P99Error = "meili_p99_error";        // P0: Meili P99 超 ERROR 阈值 (主路径严重慢)
        public const string P99Warn = "meili_p99_warn";          // P1: Meili P99 超 WARN 阈值 (提前预警)
        public const string FallbackRateError = "meili_fallback_rate_error";  // P0: 频繁降级=Meili 不可用
    }

    /// <summary>
    /// Meili 规则严重度分类 (映射到 AlertCenter severity)
    /// WHY 集中映射: 便于单测覆盖 + 后续规则扩展
    ///   - meili_p99_error → P0 (Meili 主路径严重慢, 影响用户体验)
    ///   - meili_p99_warn  → P1 (提前预警, P99 已偏高但未触发 ERROR)
    ///   - meili_fallback_rate_error → P0 (Meili 频繁不可用, 服务降级)
    /// </summary>
    public static string ClassifyMeiliSeverity(string rule) => rule switch
    {
        MeiliRules.P99Error => "P0",
        MeiliRules.P99Warn => "P1",
        MeiliRules.FallbackRateError => "P0",
        _ => "P2"  // 未知规则降级 P2 (兜底)
    };

    /// <summary>
    /// 构造 Meili 告警上下文 (持久化到 alert_history.content_json, 供运维排查)
    /// WHY 用 Dictionary 而非匿名对象: AlertMessage.Context 类型固定, 便于单测断言
    /// </summary>
    public static Dictionary<string, object?> BuildMeiliAlertContext(
        string rule,
        MeiliSearchSnapshot snapshot,
        double threshold)
    {
        return new Dictionary<string, object?>
        {
            ["rule"] = rule,
            ["severity"] = ClassifyMeiliSeverity(rule),
            ["threshold"] = threshold,
            ["p50_ms"] = snapshot.P50Ms,
            ["p95_ms"] = snapshot.P95Ms,
            ["p99_ms"] = snapshot.P99Ms,
            ["max_ms"] = snapshot.MaxMs,
            ["fallback_rate_pct"] = snapshot.FallbackRate,
            ["primary_error_rate_pct"] = snapshot.PrimaryErrorRate,
            ["sample_count"] = snapshot.SampleCount,
            ["primary_success_count"] = snapshot.PrimarySuccessCount,
            ["fallback_count"] = snapshot.FallbackCount,
            ["total_search_count"] = snapshot.TotalSearchCount,
            ["generated_at_utc"] = snapshot.GeneratedAt
        };
    }

    /// <summary>
    /// 构造 Meili 告警 Markdown 正文 (渠道无关, 由 AlertCenter 路由到具体渠道)
    /// </summary>
    public static string BuildMeiliAlertMarkdown(
        string rule,
        MeiliSearchSnapshot snapshot,
        double threshold)
    {
        var severity = ClassifyMeiliSeverity(rule);
        var title = rule switch
        {
            MeiliRules.P99Error => $"Meili P99 超阈值 (ERROR)",
            MeiliRules.P99Warn => $"Meili P99 偏高 (WARN)",
            MeiliRules.FallbackRateError => $"Meili 频繁降级 (ERROR)",
            _ => $"Meili 告警: {rule}"
        };
        return $"## [{severity}] {title}\n\n" +
               $"- **规则**: `{rule}`\n" +
               $"- **阈值**: {threshold}\n" +
               $"- **实际值**: P99={snapshot.P99Ms}ms / P95={snapshot.P95Ms}ms / P50={snapshot.P50Ms}ms\n" +
               $"- **最大耗时**: {snapshot.MaxMs}ms\n" +
               $"- **降级率**: {snapshot.FallbackRate}% (fallback={snapshot.FallbackCount}/{snapshot.TotalSearchCount})\n" +
               $"- **主路径异常率**: {snapshot.PrimaryErrorRate}%\n" +
               $"- **样本数**: {snapshot.SampleCount}\n" +
               $"- **生成时间 (UTC)**: {snapshot.GeneratedAt:yyyy-MM-dd HH:mm:ss}\n";
    }
}
