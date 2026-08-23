using System.Text.Json;

namespace SakuraFilter.Api.Services.Alerts;

/// <summary>
/// 告警消息 (P2-1)
/// - 触发源构造 AlertMessage, AlertCenter 路由到渠道
/// - 渠道无关: 不同渠道有不同 Markdown 模板
/// - Recipients 来自 alert_rules.recipients 覆盖 system_settings 全局
/// </summary>
public class AlertMessage
{
    /// <summary>告警类型, 例: etl.failed / admin.login / login.brute_force</summary>
    public string Type { get; set; } = "";

    /// <summary>严重度: P0 / P1 / P2 / ERROR / WARN / INFO</summary>
    public string Severity { get; set; } = "INFO";

    /// <summary>告警标题, Markdown 渲染时作为首行</summary>
    public string Title { get; set; } = "";

    /// <summary>Markdown 主体 (渠道无关, 由 renderer 适配)</summary>
    public string Markdown { get; set; } = "";

    /// <summary>上下文 JSON, 持久化到 alert_history.content_json</summary>
    public Dictionary<string, object?> Context { get; set; } = new();

    /// <summary>可选 correlation_id (同一事件多渠道共享, P2-1 由 AlertCenter 注入)</summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>实际接收人 (由 AlertCenter 注入, 渠道不必关心来源)</summary>
    public List<string> Recipients { get; set; } = new();

    /// <summary>由 AlertCenter 注入: 渠道 webhook URL (从 system_settings 解析)</summary>
    public Dictionary<string, string> ChannelTargets { get; set; } = new();

    public JsonDocument ToContentJson()
    {
        return JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            type = Type,
            severity = Severity,
            title = Title,
            markdown = Markdown,
            context = Context
        }));
    }
}

/// <summary>
/// 渠道发送结果 (P2-1)
/// </summary>
public class AlertSendResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    /// <summary>渠道返回内容 (截断 1KB)</summary>
    public string? Response { get; set; }
    /// <summary>实际接收人快照</summary>
    public List<string> Recipients { get; set; } = new();

    public static AlertSendResult Ok(string response, List<string> recipients) =>
        new() { Success = true, Response = response, Recipients = recipients };

    public static AlertSendResult Fail(string error, List<string> recipients) =>
        new() { Success = false, Error = error, Recipients = recipients };
}

/// <summary>
/// 告警渠道抽象 (P2-1)
/// - 实现: DingTalkChannel / WeChatChannel / GenericWebhookChannel
/// - AlertCenter 解析 alert_rules.channels 列表, 依次调用
/// - 不做抑制/重试, 由 AlertCenter 统一管理
/// </summary>
public interface IAlertChannel
{
    /// <summary>渠道名: dingtalk / wechat / webhook / wechat-mp</summary>
    string Name { get; }

    /// <summary>发送告警消息
    /// <param name="msg">AlertCenter 注入的完整消息 (含 recipients, targetUrl)</param>
    /// <param name="ct">取消令牌 (来自 AlertCenter 批处理)</param>
    /// </summary>
    Task<AlertSendResult> SendAsync(AlertMessage msg, CancellationToken ct);
}
