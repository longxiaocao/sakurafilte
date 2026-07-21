using SakuraFilter.Api.Services;

namespace SakuraFilter.Api.Services;

/// <summary>
/// 性能告警纯函数集 (v30-13 提取自 PerfAlertService)
/// WHY 提取独立类: 原 ParseDouble 为 private static, TryEmit 的抑制判断逻辑混在私有方法中无法直接单测
///   方案 C (本方案) 提取到 public 静态类, 既可单测又提升代码结构
///
/// 提取的纯函数:
///   - ParseDouble: system_settings 字典解析 double (带默认值兜底)
///   - IsSuppressed: 判断告警是否在抑制窗口内 (5min 内同 level+rule 不重发)
///   - BuildSuppressKey: 构造抑制 key (level|rule)
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
}
