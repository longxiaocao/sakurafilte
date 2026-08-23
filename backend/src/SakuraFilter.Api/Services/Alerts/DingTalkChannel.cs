using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SakuraFilter.Api.Services.Alerts;

/// <summary>
/// 钉钉自定义机器人渠道 (P2-1)
/// - 加签模式: HMAC-SHA256(timestamp + "\n" + secret, key) → URL Safe Base64
/// - Markdown 格式: { msgtype: "markdown", markdown: { title, text }, at: { atMobiles } }
/// - 支持 @手机号 (atMobiles)
/// WHY 加签: 防止伪造 webhook URL 推送垃圾消息
/// </summary>
public class DingTalkChannel : IAlertChannel
{
    public string Name => "dingtalk";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<DingTalkChannel> _logger;

    public DingTalkChannel(IHttpClientFactory httpFactory, ILogger<DingTalkChannel> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<AlertSendResult> SendAsync(AlertMessage msg, CancellationToken ct)
    {
        if (!msg.ChannelTargets.TryGetValue(Name, out var webhookUrl) || string.IsNullOrWhiteSpace(webhookUrl))
        {
            return AlertSendResult.Fail("钉钉 webhook URL 未配置", msg.Recipients);
        }

        // 1. 计算加签 (如果配置了 secret)
        var finalUrl = webhookUrl;
        if (msg.ChannelTargets.TryGetValue($"{Name}_secret", out var secret) && !string.IsNullOrWhiteSpace(secret))
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var stringToSign = $"{timestamp}\n{secret}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
            var sign = Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
            finalUrl = $"{webhookUrl}&timestamp={timestamp}&sign={sign}";
        }

        // 2. 构造 payload
        var payload = new
        {
            msgtype = "markdown",
            markdown = new
            {
                title = msg.Title,
                text = msg.Markdown
            },
            at = new
            {
                atMobiles = msg.Recipients.Where(r => r.All(char.IsDigit)).ToArray(),
                isAtAll = false
            }
        };

        try
        {
            using var http = _httpFactory.CreateClient("AlertChannel");
            var resp = await http.PostAsJsonAsync(finalUrl, payload, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            var truncated = body.Length > 1024 ? body[..1024] : body;

            if (resp.IsSuccessStatusCode)
            {
                _logger.LogDebug("钉钉告警已发送: type={Type} severity={Severity}", msg.Type, msg.Severity);
                return AlertSendResult.Ok(truncated, msg.Recipients);
            }
            return AlertSendResult.Fail($"HTTP {(int)resp.StatusCode}: {truncated}", msg.Recipients);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "钉钉告警发送失败: type={Type}", msg.Type);
            return AlertSendResult.Fail(ex.Message, msg.Recipients);
        }
    }
}
