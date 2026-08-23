using System.Net.Http.Json;
using System.Text.Json;

namespace SakuraFilter.Api.Services.Alerts;

/// <summary>
/// 通用 Webhook 渠道 (P2-1)
/// - 兜底渠道: Slack / Teams / 飞书 / 自定义 HTTP 接收端
/// - 通用 JSON 格式: { type, severity, title, content, context, recipients, sentAt }
/// - 接收方自行解析 (无固定 schema, 通过 context 字段可携带结构化数据)
/// WHY 通用兜底: 即使前 3 个渠道 (钉钉/微信群/微信 MP) 都没配,通用 webhook 仍可工作
/// </summary>
public class GenericWebhookChannel : IAlertChannel
{
    public string Name => "webhook";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<GenericWebhookChannel> _logger;

    public GenericWebhookChannel(IHttpClientFactory httpFactory, ILogger<GenericWebhookChannel> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<AlertSendResult> SendAsync(AlertMessage msg, CancellationToken ct)
    {
        if (!msg.ChannelTargets.TryGetValue(Name, out var webhookUrl) || string.IsNullOrWhiteSpace(webhookUrl))
        {
            return AlertSendResult.Fail("通用 webhook URL 未配置", msg.Recipients);
        }

        var payload = new
        {
            @event = msg.Type,
            severity = msg.Severity,
            title = msg.Title,
            content = msg.Markdown,
            context = msg.Context,
            recipients = msg.Recipients,
            correlationId = msg.CorrelationId,
            sentAt = DateTimeOffset.UtcNow.ToString("o")
        };

        try
        {
            using var http = _httpFactory.CreateClient("AlertChannel");
            var resp = await http.PostAsJsonAsync(webhookUrl, payload, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            var truncated = body.Length > 1024 ? body[..1024] : body;

            if (resp.IsSuccessStatusCode)
            {
                _logger.LogDebug("通用 webhook 告警已发送: type={Type} severity={Severity}", msg.Type, msg.Severity);
                return AlertSendResult.Ok(truncated, msg.Recipients);
            }
            return AlertSendResult.Fail($"HTTP {(int)resp.StatusCode}: {truncated}", msg.Recipients);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "通用 webhook 告警发送失败: type={Type}", msg.Type);
            return AlertSendResult.Fail(ex.Message, msg.Recipients);
        }
    }
}
