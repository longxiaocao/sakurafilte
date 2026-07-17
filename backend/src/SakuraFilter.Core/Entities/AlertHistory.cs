using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace SakuraFilter.Core.Entities;

/// <summary>
/// 告警历史 (P2-1)
/// - 每次 EmitAsync 持久化一行 (status=sent/failed/suppressed)
/// - 后台审计页查询 (按 type/severity/status 过滤)
/// - 7 日统计聚合 (后台 /admin/alerts 顶部 KPI)
/// WHY 独立 JSONB content_json:
///   - 不同告警类型 payload 结构差异大 (ETL 有 etl_id/rows,登录有 username/ip)
///   - 强 schema 化会拖慢扩展
///   - 仍可按 content_json->>'xxx' 走 GIN 索引 (后续按需)
/// </summary>
public class AlertHistory
{
    public long Id { get; set; }

    [Column("type")]
    public string Type { get; set; } = "";

    [Column("severity")]
    public string Severity { get; set; } = "";

    [Column("title")]
    public string Title { get; set; } = "";

    [Column("content_json", TypeName = "jsonb")]
    public JsonDocument ContentJson { get; set; } = JsonDocument.Parse("{}");

    [Column("channel")]
    public string Channel { get; set; } = "";

    [Column("status")]
    public string Status { get; set; } = "";

    [Column("response")]
    public string? Response { get; set; }

    [Column("error")]
    public string? Error { get; set; }

    [Column("recipients", TypeName = "jsonb")]
    public JsonDocument? Recipients { get; set; }

    [Column("correlation_id")]
    public Guid? CorrelationId { get; set; }

    [Column("sent_at")]
    public DateTimeOffset SentAt { get; set; }
}
