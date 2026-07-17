using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace SakuraFilter.Core.Entities;

/// <summary>
/// 告警规则 (P2-1 简化版)
/// - 覆盖 system_settings 默认配置, 支持按 type 个性化
/// - conditions 存 JSONB (按 type 异构)
/// - 字段平铺, 不引入 rule_severity 等规范化 (首期够用)
/// 设计权衡:
///   - 与 alert_history 表解耦: 规则可独立编辑, 历史只追加
///   - UNIQUE(type) 保证每类型唯一规则, alertCenter 读时按 type 索引
/// </summary>
public class AlertRule
{
    public long Id { get; set; }

    [Column("type")]
    public string Type { get; set; } = "";

    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    [Column("severity")]
    public string Severity { get; set; } = "";

    /// <summary>渠道列表, 例: ["dingtalk","wechat","webhook"]</summary>
    [Column("channels", TypeName = "jsonb")]
    public JsonDocument Channels { get; set; } = JsonDocument.Parse("[]");

    [Column("conditions", TypeName = "jsonb")]
    public JsonDocument? Conditions { get; set; }

    [Column("recipients", TypeName = "jsonb")]
    public JsonDocument? Recipients { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// 安全事件 (P2-1)
/// - 登录失败/权限变更/限流触发/爬虫嫌疑
/// - 与 alert_history 解耦: 一个事件可能不触发告警 (低危) 或触发多条 (多渠道)
/// - 30 天清理 (后续 AlertHistoryCleanupService 实现, P2-1 阶段不清理)
/// </summary>
public class SecurityEvent
{
    public long Id { get; set; }

    [Column("event_type")]
    public string EventType { get; set; } = "";

    [Column("user_id")]
    public long? UserId { get; set; }

    [Column("username")]
    public string? Username { get; set; }

    [Column("ip")]
    public string? Ip { get; set; }

    [Column("user_agent")]
    public string? UserAgent { get; set; }

    [Column("details", TypeName = "jsonb")]
    public JsonDocument? Details { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
