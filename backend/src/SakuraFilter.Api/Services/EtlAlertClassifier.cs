using SakuraFilter.Core.Entities;

namespace SakuraFilter.Api.Services;

/// <summary>
/// ETL 告警纯函数集 (v30-12 提取自 EtlAlertService)
/// WHY 提取独立类: 原为 EtlAlertService private static 方法, 无法直接单测
///   方案 A (InternalsVisibleTo) 侵入小但破坏封装, 方案 B (反射) 脆弱
///   方案 C (本方案) 提取到 public 静态类, 既可单测又提升代码结构
///
/// 提取的纯函数:
///   - ClassifySeverity: 按 LastError 关键词分类 P0/P1/P2 严重度, 决定 webhook 路由
///   - FirstNonEmpty: webhook URL 4 候选按优先级选第一个非空
///   - BuildSuppressionKey: 告警抑制 key (entity_type|error_class 前 50 字符)
///   - BuildPayload: 构造 webhook JSON payload (通用结构, 接收端 adapter 解析)
/// </summary>
public static class EtlAlertClassifier
{
    /// <summary>
    /// 严重度分类 — Day 7.10
    ///   P0: Meili/网络/服务可用性问题 (ConnectionRefused/Timeout/HTTP 5xx)
    ///   P1: 数据 schema 问题 (列名/字段错/malformed)
    ///   P2: 其它未归类
    /// WHY 关键词匹配而非堆栈追踪: 错误来自 ETL log LastError 字符串,
    ///   性能开销低,无需引入异常分类器
    /// </summary>
    public static string ClassifySeverity(EtlProgressLog item)
    {
        var err = item.LastError?.ToLowerInvariant() ?? "";
        if (err.Length == 0) return "P2";

        // P0: Meili 连接 / 5xx / timeout
        if (err.Contains("connectionrefused")
            || err.Contains("connection refused")
            || err.Contains("timeout")
            || err.Contains("timed out")
            || err.Contains(" 500 ")
            || err.Contains(" 502 ")
            || err.Contains(" 503 ")
            || err.Contains(" 504 ")
            || err.Contains("internal server error")
            || err.Contains("network")
            || err.Contains("dns")
            || err.Contains("unreachable"))
            return "P0";

        // P1: 数据 schema / 列名 / 字段错
        if (err.Contains("column")
            || err.Contains("schema")
            || err.Contains("malformed")
            || err.Contains("invalid")
            || err.Contains("null value")
            || err.Contains("constraint")
            || err.Contains("duplicate key")
            || err.Contains("violates")
            || err.Contains("type ")
            || err.Contains("cast"))
            return "P1";

        return "P2";
    }

    /// <summary>
    /// 返回第一个非空字符串
    /// WHY: webhook URL 有 4 个候选 (P0/P1/P2/通用),严重度路由时按优先级选第一个非空
    /// </summary>
    public static string FirstNonEmpty(params string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c)) return c;
        }
        return "";
    }

    /// <summary>
    /// 告警抑制 key (entity_type|error_class 前 50 字符)
    /// WHY error_class: 同根因的失败应合并告警,例如 100 条都是 "Meili ConnectionRefused" 不应推 100 次
    /// </summary>
    public static string BuildSuppressionKey(EtlProgressLog item)
    {
        var errClass = item.LastError?.Length > 50 ? item.LastError[..50] : item.LastError ?? "";
        return $"{item.EntityType}|{errClass}";
    }

    /// <summary>
    /// 构造 webhook payload (通用 JSON,支持钉钉/飞书/Slack/自定义)
    /// WHY 用通用结构: 不同 webhook 接收格式不同,通用 JSON 由接收端 adapter 解析
    /// Day 9.5: 加入 reason_code + cancel_reason, 让告警接收方能区分 "用户取消" 与 "真异常"
    /// </summary>
    public static object BuildPayload(EtlProgressLog item)
    {
        return new
        {
            @event = "etl.failed",
            timestamp = DateTime.UtcNow.ToString("o"),
            etl = new
            {
                id = item.Id,
                entity_type = item.EntityType,
                mode = item.Mode,
                file_path = item.FilePath,
                read_count = item.ReadCount,
                inserted_count = item.InsertedCount,
                updated_count = item.UpdatedCount,
                skipped_count = item.SkippedCount,
                skipped_missing_oem = item.SkippedMissingOem,
                skipped_null_field = item.SkippedNullField,
                skipped_duplicate = item.SkippedDuplicate,
                error_count = item.ErrorCount,
                indexed_count = item.IndexedCount,
                index_pending_count = item.IndexPendingCount,
                last_error = item.LastError,
                // Day 9.5: 取消审计 (NULL 表示非取消)
                cancel_reason = item.CancelReason,
                cancelled_at = item.CancelledAt?.ToString("o"),
                reason_code = item.ReasonCode,
                started_at = item.StartedAt.ToString("o"),
                finished_at = item.FinishedAt.ToString("o"),
                duration_sec = item.DurationSec,
            },
            text = $"[ETL FAILED] {item.EntityType} {item.Mode} {item.FilePath} | err={item.LastError?.Substring(0, Math.Min(120, item.LastError?.Length ?? 0))}"
        };
    }
}
