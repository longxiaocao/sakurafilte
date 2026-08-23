using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// ETL 失败告警服务 (Day 7.9)
/// - 每 N s 扫描 etl_progress_log (status='failed' AND alert_sent=false)
/// - POST 到配置的 webhook URL (钉钉/飞书/Slack/自定义)
/// - 推送成功后置 alert_sent=true,避免重复告警
/// - 推送失败:不置位,下次轮询重试
///
/// WHY 独立 BackgroundService 而非 EtlProgress.Fail() 内联:
///   - 解耦告警可靠性与 ETL 业务逻辑
///   - webhook 暂时不可用时不影响 ETL 完结
///   - 失败可重试 (不置位)
///   - 告警策略可独立调整 (间隔、批大小、目标 URL)
///
/// WHY 用 system_settings 而非 appsettings:
///   - 运维可在线修改 webhook URL (例如切换钉钉群),不必重启
///   - 与现有 retention_* 配置同源
/// </summary>
public class EtlAlertService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<EtlAlertService> _logger;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IHostedServiceStatus _hostedStatus;

    // 默认配置 (启动时若 system_settings 中无则插入)
    private static readonly (string Key, string Value, string Description)[] Defaults = new[]
    {
        ("alert.enabled", "false", "ETL 失败告警全局开关 (true/false, 默认关闭)"),
        ("alert.webhook_url", "", "告警 webhook URL (钉钉/飞书/Slack/自定义 POST,通用兜底)"),
        ("alert.webhook_url_p0", "", "P0 严重告警 webhook (Meili 连接/500/timeout, 必配)"),
        ("alert.webhook_url_p1", "", "P1 数据问题告警 webhook (schema/列名/字段错, 可空则用通用 URL)"),
        ("alert.webhook_url_p2", "", "P2 一般告警 webhook (其它,可空则用通用 URL)"),
        ("alert.poll_seconds", "60", "轮询周期 (秒),失败时按此间隔重试"),
        ("alert.batch_size", "50", "单批推送上限,避免一次推太多"),
    };

    public EtlAlertService(
        IServiceProvider sp,
        ILogger<EtlAlertService> logger,
        IHttpClientFactory httpFactory,
        IHostedServiceStatus hostedStatus)
    {
        _sp = sp;
        _logger = logger;
        _httpFactory = httpFactory;
        _hostedStatus = hostedStatus;
    }

    // Day 7.10: 失败退避状态
    //   连续推送失败时,_consecutiveFailures 递增,poll_seconds 指数放大
    //   任意一次成功即清零
    //   WHY: webhook 持续不可用时,避免每分钟打日志/触发 ALERT 告警
    private int _consecutiveFailures = 0;
    private const int MaxBackoffMultiplier = 8;  // 8x 即最大 8*60=480s ≈ 8min
    private const int MaxBackoffSeconds = 300;   // 硬上限 5min,避免 webhook 恢复后仍长时间沉默

    // Day 7.10: 告警抑制 (per-process in-memory,5min 内同源不重发)
    //   key = "{entity_type}|{error_class}",value = 上次推送时间
    //   WHY: 失败风暴时 (例如 100 条同样错误) 不刷屏 webhook
    //   牺牲: 进程重启后抑制状态丢失,但 alert_sent 列在 DB 防重,只是可能多推一次
    private readonly Dictionary<string, DateTime> _suppressedKeys = new();
    private static readonly TimeSpan SuppressionWindow = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // V24-F87 (P2-2): 启动时确保默认配置存在 (内联, 原 EnsureDefaultSettingsAsync 仅一处调用)
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            await DefaultSettingsEnsurer.EnsureAsync(db, Defaults, _logger, nameof(EtlAlertService), stoppingToken);
        }

        // 自适应轮询: 失败多时按 poll_seconds,空闲时按 5x
        //   简化: 始终按 poll_seconds 轮询 (避免自适应逻辑复杂度)
        int pollSec = 60;
        while (!stoppingToken.IsCancellationRequested)
        {
            _hostedStatus.ReportAlive(nameof(EtlAlertService));
            try
            {
                pollSec = await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ETL 告警任务异常,下一轮重试");
            }

            await Task.Delay(TimeSpan.FromSeconds(pollSec), stoppingToken);
        }
    }

    private async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();

        // 1) 读配置
        var settings = await db.SystemSettings
            .Where(s => s.Key.StartsWith("alert."))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        var enabled = settings.GetValueOrDefault("alert.enabled") == "true";
        var webhookUrl = settings.GetValueOrDefault("alert.webhook_url") ?? "";
        var webhookUrlP0 = settings.GetValueOrDefault("alert.webhook_url_p0") ?? "";
        var webhookUrlP1 = settings.GetValueOrDefault("alert.webhook_url_p1") ?? "";
        var webhookUrlP2 = settings.GetValueOrDefault("alert.webhook_url_p2") ?? "";
        var pollSec = int.TryParse(settings.GetValueOrDefault("alert.poll_seconds"), out var ps) && ps > 0 ? ps : 60;
        var batchSize = int.TryParse(settings.GetValueOrDefault("alert.batch_size"), out var bs) && bs > 0 ? bs : 50;

        if (!enabled)
        {
            _logger.LogDebug("ETL 告警已禁用 (alert.enabled != true),跳过");
            return pollSec;
        }
        if (string.IsNullOrWhiteSpace(webhookUrl)
            && string.IsNullOrWhiteSpace(webhookUrlP0)
            && string.IsNullOrWhiteSpace(webhookUrlP1)
            && string.IsNullOrWhiteSpace(webhookUrlP2))
        {
            _logger.LogWarning("ETL 告警已启用但所有 webhook URL 都为空,跳过 (请配置 alert.webhook_url*)");
            return pollSec;
        }

        // 2) 取出未告警的失败记录
        //   Day 9.5: 显式排除 status='cancelled' 防止误告警
        //     Day 9.4 修复后, 取消走 status='cancelled', 但 "被取消" 不应触发 P0/P1 告警
        //     (用户主动取消是设计行为, 不算故障; 系统取消需要单独监控)
        var failed = await db.EtlProgressLogs
            .Where(l => l.Status == "failed" && !l.AlertSent)
            .OrderBy(l => l.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        if (failed.Count == 0)
        {
            _logger.LogDebug("无未告警的 ETL 失败记录");
            return pollSec;
        }

        _logger.LogInformation("发现 {Count} 条未告警的失败记录,开始推送 webhook (consecutive_failures={Fail})",
            failed.Count, _consecutiveFailures);

        // 3) 逐条推送 (避免一条失败影响整批)
        int pushed = 0, failed_push = 0, suppressed = 0;
        foreach (var item in failed)
        {
            if (ct.IsCancellationRequested) break;

            // Day 7.10: 告警抑制 — 5min 内同 entity_type + error_class 不重推
            var key = EtlAlertClassifier.BuildSuppressionKey(item);
            if (_suppressedKeys.TryGetValue(key, out var lastAlertedAt)
                && DateTime.UtcNow - lastAlertedAt < SuppressionWindow)
            {
                // 不推送,但仍置 alert_sent=true (避免下次轮询再处理)
                item.AlertSent = true;
                suppressed++;
                continue;
            }

            try
            {
                var payload = EtlAlertClassifier.BuildPayload(item);
                // Day 7.10: 按严重度选 webhook URL (P0 > P1 > P2 > 通用兜底)
                var severity = EtlAlertClassifier.ClassifySeverity(item);
                var targetUrl = severity switch
                {
                    "P0" => EtlAlertClassifier.FirstNonEmpty(webhookUrlP0, webhookUrlP1, webhookUrlP2, webhookUrl),
                    "P1" => EtlAlertClassifier.FirstNonEmpty(webhookUrlP1, webhookUrlP2, webhookUrl, webhookUrlP0),
                    _    => EtlAlertClassifier.FirstNonEmpty(webhookUrlP2, webhookUrl, webhookUrlP0, webhookUrlP1),
                };
                if (string.IsNullOrWhiteSpace(targetUrl))
                {
                    _logger.LogWarning("无可用 webhook URL 推送 {Severity} 告警: id={Id} severity={Sev}",
                        severity, item.Id, severity);
                    failed_push++;
                    continue;
                }
                using var http = _httpFactory.CreateClient("EtlAlert");
                var resp = await http.PostAsJsonAsync(targetUrl, payload, ct);
                if (resp.IsSuccessStatusCode)
                {
                    item.AlertSent = true;
                    pushed++;
                    _consecutiveFailures = 0;  // 成功即清零
                    _suppressedKeys[key] = DateTime.UtcNow;
                }
                else
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    // V24-F99 (P2-3, 规则 6.3): 禁止日志 webhook 错误响应 body
                    //   WHY: 第三方 webhook (钉钉/飞书/Generic) 在错误响应中可能 echo 请求 URL (含签名 secret 参数)
                    //     或 echo 出完整请求 payload, 泄漏 webhook 配置信息
                    //   仅记录状态码 + body 长度, 不记录 body 内容
                    _logger.LogWarning("webhook 推送失败: id={Id} severity={Sev} status={Status} bodyLen={BodyLen}",
                        item.Id, severity, (int)resp.StatusCode, body.Length);
                    failed_push++;
                    _consecutiveFailures++;
                    // 不置 alert_sent,下次轮询重试
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "webhook 推送异常: id={Id}", item.Id);
                failed_push++;
                _consecutiveFailures++;
            }
        }

        if (pushed > 0 || suppressed > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        _logger.LogInformation("本轮告警推送: 成功 {Pushed} / 失败 {Failed} / 抑制 {Suppressed} / 候选 {Total}",
            pushed, failed_push, suppressed, failed.Count);

        // Day 7.10: 退避计算 — 连续失败时 pollSec 指数放大,任意成功即下次回基础
        if (_consecutiveFailures > 0)
        {
            var multiplier = Math.Min(MaxBackoffMultiplier, _consecutiveFailures);
            var backoff = Math.Min(MaxBackoffSeconds, pollSec * multiplier);
            _logger.LogInformation("退避中: consecutive_failures={N}, pollSec {Old}s → {New}s",
                _consecutiveFailures, pollSec, backoff);
            return backoff;
        }
        return pollSec;
    }
}
