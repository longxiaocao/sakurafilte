using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace SakuraFilter.Api.Services.Alerts;

/// <summary>
/// 企业微信群机器人渠道 (P2-1)
/// - 无需 appsecret, 仅 webhook URL
/// - Markdown 格式: { msgtype: "markdown", markdown: { content } }
/// - 不支持 @手机号 (微信 API 限制)
/// - 4096 字节长度限制, 超出自动截断
/// </summary>
public class WeChatChannel : IAlertChannel
{
    public string Name => "wechat";

    private const int MaxContentLength = 4096;

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WeChatChannel> _logger;

    public WeChatChannel(IHttpClientFactory httpFactory, ILogger<WeChatChannel> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<AlertSendResult> SendAsync(AlertMessage msg, CancellationToken ct)
    {
        if (!msg.ChannelTargets.TryGetValue(Name, out var webhookUrl) || string.IsNullOrWhiteSpace(webhookUrl))
        {
            return AlertSendResult.Fail("企业微信 webhook URL 未配置", msg.Recipients);
        }

        // 截断 Markdown (微信 4KB 限制)
        var content = msg.Markdown;
        if (content.Length > MaxContentLength)
        {
            content = content[..(MaxContentLength - 30)] + "\n\n_...内容过长已截断_";
            _logger.LogWarning("微信告警内容超过 4KB, 已自动截断: type={Type}", msg.Type);
        }

        // 微信 markdown 字段名为 "content" 而非 "text"
        var payload = new
        {
            msgtype = "markdown",
            markdown = new { content }
        };

        try
        {
            using var http = _httpFactory.CreateClient("AlertChannel");
            var resp = await http.PostAsJsonAsync(webhookUrl, payload, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            var truncated = body.Length > 1024 ? body[..1024] : body;

            if (resp.IsSuccessStatusCode)
            {
                _logger.LogDebug("微信告警已发送: type={Type} severity={Severity}", msg.Type, msg.Severity);
                return AlertSendResult.Ok(truncated, msg.Recipients);
            }
            return AlertSendResult.Fail($"HTTP {(int)resp.StatusCode}: {truncated}", msg.Recipients);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "微信告警发送失败: type={Type}", msg.Type);
            return AlertSendResult.Fail(ex.Message, msg.Recipients);
        }
    }
}
