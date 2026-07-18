using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services.Alerts;

/// <summary>
/// 告警中心 (P2-1) — 统一告警管理器
/// 设计目标:
///   1. 触发源 (EtlAlertService / PerfAlertService / LoginAlertService / ...) 统一调用 EmitAsync
///   2. 解析 alert_rules (覆盖 system_settings 默认) + 抑制窗口 (5min)
///   3. 路由到已注册 IAlertChannel (dingtalk / wechat / webhook / wechat-mp)
///   4. 持久化 alert_history (sent / failed / suppressed)
///   5. 提供查询 API 给后台 /admin/alerts
///
/// 与现有 EtlAlertService 关系:
///   - P2-1 阶段 EtlAlertService 仍可用 (走老 webhook URL, 兼容)
///   - AlertCenter 可独立工作, 也可由 EtlAlertService 调用
///   - P3 阶段 EtlAlertService 改造为走 AlertCenter (统一管理)
///
/// 线程安全:
///   - _suppressionMap 用 ConcurrentDictionary (per-process, 与 EtlAlertService 同模式)
///   - 进程重启后抑制状态丢失, 但 alert_history.status='suppressed' 已在 DB 标记
/// </summary>
public class AlertCenter
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<AlertCenter> _logger;
    private readonly IHttpClientFactory _httpFactory;
    private readonly Dictionary<string, IAlertChannel> _channels;

    // 抑制窗口 (in-memory, key = "{type}|{suppressKey}", value = 上次推送时间)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _suppressionMap = new();
    private static readonly TimeSpan DefaultSuppressionWindow = TimeSpan.FromMinutes(5);

    public AlertCenter(
        IServiceProvider sp,
        ILogger<AlertCenter> logger,
        IHttpClientFactory httpFactory,
        IEnumerable<IAlertChannel> channels)
    {
        _sp = sp;
        _logger = logger;
        _httpFactory = httpFactory;
        _channels = channels.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 统一入口: 触发告警
    /// </summary>
    /// <param name="type">告警类型 (例: "etl.failed")</param>
    /// <param name="severity">严重度 (P0/P1/P2/ERROR/WARN/INFO)</param>
    /// <param name="title">标题</param>
    /// <param name="markdown">Markdown 正文 (渠道无关)</param>
    /// <param name="context">上下文 (持久化到 content_json)</param>
    /// <param name="suppressKey">抑制 key (可选, 例: "products|connection-refused" — 同源 5min 内不重发)</param>
    /// <param name="ct">取消令牌</param>
    public async Task<AlertEmitResult> EmitAsync(
        string type,
        string severity,
        string title,
        string markdown,
        Dictionary<string, object?>? context = null,
        string? suppressKey = null,
        CancellationToken ct = default)
    {
        // P2-1 指标埋点: 手动管理 Stopwatch + 二次记录
        //   WHY: prometheus-net Histogram.WithLabels 在 timer.NewTimer() 时就锁定 label,
        //   EmitCoreAsync 内修改 _lastOutcome 无法影响已绑定 series。
        //   解决: NewTimer 用占位 outcome, EmitCoreAsync 完成后用真实 outcome 二次 Observe。
        _lastOutcome = "nochannel";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return await EmitCoreAsync(type, severity, title, markdown, context, suppressKey, ct);
        }
        catch (Exception ex)
        {
            _lastOutcome = "failed";
            _logger.LogError(ex, "AlertCenter.EmitAsync 异常: type={Type}", type);
            throw;
        }
        finally
        {
            sw.Stop();
            // 用真实 outcome 标签重新记录 (覆盖 NewTimer 的占位记录)
            AlertMetrics.EmitDuration.WithLabels(type, _lastOutcome).Observe(sw.Elapsed.TotalSeconds);
        }
    }

    // P2-1 指标: EmitAsync 结束后由 EmitCoreAsync 写入, 用作 Histogram outcome 标签
    private string _lastOutcome = "nochannel";

    private async Task<AlertEmitResult> EmitCoreAsync(
        string type,
        string severity,
        string title,
        string markdown,
        Dictionary<string, object?>? context,
        string? suppressKey,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();

        // 1) 读 system_settings (全局开关 + 默认接收人)
        // WHY: SystemSetting.Value 是 string?, 显式标注字典值类型为 string? 让调用方知道可能为空
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var settings = await db.SystemSettings
            .Where(s => s.Key.StartsWith("alert."))
            .ToDictionaryAsync<SystemSetting, string, string?>(
                s => s.Key,
                s => s.Value,
                ct);

        var enabled = settings.GetValueOrDefault("alert.enabled") == "true";
        if (!enabled)
        {
            _logger.LogDebug("AlertCenter 全局禁用 (alert.enabled != true), 跳过 type={Type}", type);
            _lastOutcome = "disabled";
            AlertMetrics.Disabled.WithLabels(type).Inc();
            return AlertEmitResult.DisabledInstance();
        }

        // 2) 解析规则 (alert_rules 覆盖 system_settings 默认)
        var rule = await db.AlertRules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Type == type, ct);
        var channels = rule?.Enabled == true
            ? ParseStringArray(rule.Channels)
            : new List<string> { "webhook" };  // 兜底: 通用 webhook

        var recipients = rule?.Recipients != null
            ? ParseStringArray(rule.Recipients)
            : ParseStringArrayFromJsonText(settings.GetValueOrDefault("alert.recipients_admin") ?? "[]");

        if (channels.Count == 0)
        {
            _logger.LogDebug("AlertCenter 无可用渠道: type={Type}", type);
            return AlertEmitResult.NoChannelInstance();
        }

        // 3) 解析渠道 targets (webhook URL + secrets)
        var targets = ResolveChannelTargets(channels, settings);

        // 4) 抑制检查 (5min 内同 suppressKey 不重发)
        var suppressWindow = TimeSpan.FromMinutes(
            int.TryParse(settings.GetValueOrDefault("alert.suppress_minutes"), out var sm) && sm > 0 ? sm : 5);
        var fullSuppressKey = suppressKey != null ? $"{type}|{suppressKey}" : null;
        if (fullSuppressKey != null &&
            _suppressionMap.TryGetValue(fullSuppressKey, out var lastAlert) &&
            DateTime.UtcNow - lastAlert < suppressWindow)
        {
            _logger.LogDebug("AlertCenter 抑制: type={Type} key={Key}", type, fullSuppressKey);
            await PersistAsync(db, type, severity, title, context, "webhook", "suppressed",
                recipients, correlationId, null, "suppressed within window", ct);
            _lastOutcome = "suppressed";
            AlertMetrics.Suppressed.WithLabels(type).Inc();
            return AlertEmitResult.SuppressedInstance();
        }

        // 5) 路由到每个渠道
        var results = new List<(string Channel, AlertSendResult Result)>();
        foreach (var ch in channels)
        {
            if (!_channels.TryGetValue(ch, out var channel))
            {
                _logger.LogWarning("未知告警渠道: {Channel} (type={Type})", ch, type);
                continue;
            }
            if (!targets.ContainsKey(ch) || string.IsNullOrWhiteSpace(targets[ch]))
            {
                _logger.LogWarning("渠道 {Channel} 缺少 target URL (type={Type})", ch, type);
                continue;
            }
            var msg = new AlertMessage
            {
                Type = type,
                Severity = severity,
                Title = title,
                Markdown = markdown,
                Context = context ?? new(),
                CorrelationId = correlationId,
                Recipients = recipients,
                ChannelTargets = targets
            };
            var r = await channel.SendAsync(msg, ct);
            results.Add((ch, r));
            await PersistAsync(db, type, severity, title, context, ch,
                r.Success ? "sent" : "failed", recipients, correlationId, r.Response, r.Error, ct);

            // 埋点: 成功/失败按 channel + type 维度统计
            if (r.Success) AlertMetrics.Sent.WithLabels(type, ch).Inc();
            else AlertMetrics.Failed.WithLabels(type, ch).Inc();
        }

        // 6) 更新抑制
        if (fullSuppressKey != null && results.Any(r => r.Result.Success))
        {
            _suppressionMap[fullSuppressKey] = DateTime.UtcNow;
        }

        // outcome 反映整体结果 (任一成功 = sent, 否则 failed)
        _lastOutcome = results.Any(r => r.Result.Success) ? "sent" : "failed";

        return new AlertEmitResult
        {
            Success = results.Any(r => r.Result.Success),
            CorrelationId = correlationId,
            SentCount = results.Count(r => r.Result.Success),
            FailedCount = results.Count(r => !r.Result.Success),
            Results = results.Select(r => new AlertChannelResult
            {
                Channel = r.Channel,
                Success = r.Result.Success,
                Error = r.Result.Error,
                Response = r.Result.Response
            }).ToList()
        };
    }

    /// <summary>
    /// 解析渠道 targets (从 system_settings 读 webhook URL + 加签 secret)
    /// key: dingtalk / wechat / webhook / wechat-mp
    /// 子键: {name}_secret (钉钉加签 secret)
    /// </summary>
    private static Dictionary<string, string> ResolveChannelTargets(List<string> channels, Dictionary<string, string?> settings)
    {
        // WHY: 接收 string? 类型避免 CS8620 (settings.GetValueOrDefault 返回 string?),
        //   函数内统一用 ?? "" 收窄为非空 string
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ch in channels)
        {
            var url = ch switch
            {
                "dingtalk" => FirstNonEmpty(settings.GetValueOrDefault("alert.dingtalk_webhook_url"),
                                              settings.GetValueOrDefault("alert.webhook_url")),
                "wechat" => FirstNonEmpty(settings.GetValueOrDefault("alert.wechat_webhook_url"),
                                            settings.GetValueOrDefault("alert.webhook_url")),
                "webhook" => settings.GetValueOrDefault("alert.webhook_url") ?? "",
                "wechat-mp" => FirstNonEmpty(settings.GetValueOrDefault("alert.wechat_mp_webhook_url"),
                                              settings.GetValueOrDefault("alert.webhook_url")),
                _ => ""
            };
            if (!string.IsNullOrWhiteSpace(url)) targets[ch] = url;

            // 钉钉加签 secret (可能多个用 ; 分隔, 取第一个)
            if (ch == "dingtalk")
            {
                var secret = settings.GetValueOrDefault("alert.dingtalk_secret");
                if (!string.IsNullOrWhiteSpace(secret))
                {
                    targets["dingtalk_secret"] = secret!.Split(';', 2)[0].Trim();
                }
            }
        }
        return targets;
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var c in candidates) if (!string.IsNullOrWhiteSpace(c)) return c!;
        return "";
    }

    private static List<string> ParseStringArray(JsonDocument doc)
    {
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return new();
        return doc.RootElement.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.ToString())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    private static List<string> ParseStringArrayFromJsonText(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return ParseStringArray(JsonDocument.Parse(json));
        }
        catch
        {
            return new();
        }
    }

    private async Task PersistAsync(
        ProductDbContext db,
        string type, string severity, string title,
        Dictionary<string, object?>? context,
        string channel, string status,
        List<string> recipients, Guid correlationId,
        string? response, string? error,
        CancellationToken ct)
    {
        try
        {
            var ctxJson = JsonSerializer.Serialize(context ?? new());
            var rcptJson = JsonSerializer.Serialize(recipients);
            db.AlertHistories.Add(new AlertHistory
            {
                Type = type,
                Severity = severity,
                Title = title,
                ContentJson = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    type,
                    severity,
                    title,
                    context = ctxJson
                })),
                Channel = channel,
                Status = status,
                Response = response,
                Error = error,
                Recipients = JsonDocument.Parse(rcptJson),
                CorrelationId = correlationId,
                SentAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // 持久化失败不能阻塞告警流程, 仅记日志 (V24-F53: 改用 _logger 替代 Console.WriteLine, 遵循规则 4.3 日志规范)
            _logger.LogWarning(ex, "[AlertCenter] 告警历史持久化失败 type={Type} channel={Channel} correlationId={Cid}", type, channel, correlationId);
        }
    }
}

public class AlertEmitResult
{
    public bool Success { get; set; }
    public Guid CorrelationId { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    public bool Disabled { get; set; }
    public bool NoChannel { get; set; }
    public bool Suppressed { get; set; }
    public List<AlertChannelResult> Results { get; set; } = new();

    public static AlertEmitResult DisabledInstance() => new() { Disabled = true };
    public static AlertEmitResult NoChannelInstance() => new() { NoChannel = true };
    public static AlertEmitResult SuppressedInstance() => new() { Suppressed = true };
}

public class AlertChannelResult
{
    public string Channel { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Response { get; set; }
}
