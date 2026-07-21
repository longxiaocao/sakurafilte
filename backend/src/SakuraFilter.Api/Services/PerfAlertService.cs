// P6: 性能阈值告警服务 (基于 P5.5 PerfMetrics)
//   - 定时 (默认 60s) 扫描 PerfMetrics.GetSnapshot() + MeiliSearchMetrics.GetSnapshot()
//   - 阈值规则 (system_settings 可在线调整,无需重启):
//       perf.alert.enabled           全局开关 (true/false)
//       perf.alert.poll_seconds      轮询周期 (默认 60s)
//       perf.alert.p95_warn_ms       P95 WARN 阈值 (默认 1000ms)
//       perf.alert.p95_error_ms      P95 ERROR 阈值 (默认 3000ms)
//       perf.alert.error_rate_pct    错误率 ERROR 阈值 (默认 5%)
//       perf.alert.max_ms            单请求最大耗时 ERROR 阈值 (默认 10000ms)
//       perf.alert.meili_p99_warn_ms    Meili P99 WARN 阈值 (默认 500ms, v30-20)
//       perf.alert.meili_p99_error_ms   Meili P99 ERROR 阈值 (默认 1500ms, v30-20)
//       perf.alert.meili_fallback_rate_pct  Meili 降级率 ERROR 阈值 (默认 20%, v30-20)
//   - 告警抑制: 5min 窗口内同 (level+rule) 不重发 (参考 EtlAlertService 模式)
//   - 持久化: 内存 FIFO 最近 100 条 + 日志 (不引入新表, 走 system_settings 配置)
//   - 暴露: GET /api/admin/perf/alerts 查询当前告警列表 (需 X-Admin-Token)
//
// v30-20: Meili 主路径告警
//   - 复用 AlertCenter 推送 webhook (不重新实现 webhook 逻辑, 与 EtlAlertService 走老路径不同)
//   - AlertCenter 自动处理: 抑制窗口 (5min) + 持久化 alert_history + 路由 dingtalk/wechat/webhook
//   - 三条规则:
//       meili_p99_error (P0): Meili P99 超 ERROR 阈值 → 主路径严重慢
//       meili_p99_warn (P1): Meili P99 超 WARN 阈值 → 提前预警
//       meili_fallback_rate_error (P0): Meili 降级率超阈值 → 频繁降级=不可用
//
// WHY 独立 BackgroundService 而非 PerfMetrics 内联:
//   - 解耦告警逻辑与指标聚合 (单一职责)
//   - 告警策略可独立调整 (阈值/周期/抑制窗口)
//   - 后续可扩展为 webhook 推送 (参考 EtlAlertService)
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Api.Services.Alerts;
using SakuraFilter.Infrastructure.Data;
using SakuraFilter.Search;

namespace SakuraFilter.Api.Services;

public class PerfAlertService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<PerfAlertService> _logger;
    private readonly PerfMetrics _metrics;
    private readonly IHostedServiceStatus _hostedStatus;
    // v30-20: Meili 主路径指标 (注入而非构造函数, 因 PerfAlertService 是 Singleton, MeiliSearchMetrics 也是 Singleton, DI 解析无环)
    private readonly MeiliSearchMetrics _meiliMetrics;
    // v30-20: 复用 AlertCenter (统一告警路由 + 抑制 + 持久化)
    private readonly AlertCenter _alertCenter;

    // 默认配置 (启动时若 system_settings 中无则插入)
    private static readonly (string Key, string Value, string Description)[] Defaults = new[]
    {
        ("perf.alert.enabled", "true", "性能告警全局开关 (true/false)"),
        ("perf.alert.poll_seconds", "60", "扫描周期 (秒), 建议不低于 30"),
        ("perf.alert.p95_warn_ms", "1000", "P95 WARN 阈值 (ms), 超过触发 WARN 告警"),
        ("perf.alert.p95_error_ms", "3000", "P95 ERROR 阈值 (ms), 超过触发 ERROR 告警"),
        ("perf.alert.error_rate_pct", "5", "错误率 ERROR 阈值 (%), 超过触发 ERROR 告警"),
        ("perf.alert.max_ms", "10000", "单请求最大耗时 ERROR 阈值 (ms)"),
        // v30-20: Meili 主路径告警阈值
        ("perf.alert.meili_p99_warn_ms", "500", "Meili P99 WARN 阈值 (ms), 超过触发 P1 告警"),
        ("perf.alert.meili_p99_error_ms", "1500", "Meili P99 ERROR 阈值 (ms), 超过触发 P0 告警"),
        ("perf.alert.meili_fallback_rate_pct", "20", "Meili 降级率 ERROR 阈值 (%), 超过触发 P0 告警"),
    };

    // 内存 FIFO 最近 100 条告警 (线程安全)
    //   WHY 不入库: 高频写, 无业务价值, 运维从日志聚合即可
    private readonly ConcurrentQueue<PerfAlert> _recentAlerts = new();
    private const int MaxRecentAlerts = 100;

    // 告警抑制 (per-process in-memory, 5min 内同 level+rule 不重发)
    //   WHY: 持续 P95 高时避免每分钟刷屏日志
    //   牺牲: 进程重启后抑制状态丢失, 可能多记一条日志 (可接受)
    //   注: Meili 告警走 AlertCenter, AlertCenter 内部有独立抑制机制, 这里只控制 PerfMetrics 告警的日志刷屏
    private readonly Dictionary<string, DateTime> _suppressedKeys = new();
    private static readonly TimeSpan SuppressionWindow = TimeSpan.FromMinutes(5);

    public PerfAlertService(
        IServiceProvider sp,
        ILogger<PerfAlertService> logger,
        PerfMetrics metrics,
        IHostedServiceStatus hostedStatus,
        MeiliSearchMetrics meiliMetrics,
        AlertCenter alertCenter)
    {
        _sp = sp;
        _logger = logger;
        _metrics = metrics;
        _hostedStatus = hostedStatus;
        _meiliMetrics = meiliMetrics;
        _alertCenter = alertCenter;
    }

    /// <summary>查询最近告警列表 (供 /api/admin/perf/alerts 端点调用)</summary>
    public List<PerfAlert> GetRecentAlerts(int limit = 50)
    {
        var cap = Math.Clamp(limit, 1, 100);
        return _recentAlerts.ToArray().TakeLast(cap).ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // V24-F87 (P2-2): 启动时确保默认配置存在 (内联, 原 EnsureDefaultSettingsAsync 仅一处调用)
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            await DefaultSettingsEnsurer.EnsureAsync(db, Defaults, _logger, nameof(PerfAlertService), stoppingToken);
        }

        int pollSec = 60;
        while (!stoppingToken.IsCancellationRequested)
        {
            _hostedStatus.ReportAlive(nameof(PerfAlertService));
            try
            {
                pollSec = await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PerfAlert 扫描异常, 下一轮重试");
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
            .Where(s => s.Key.StartsWith("perf.alert."))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        if (!settings.TryGetValue("perf.alert.enabled", out var enabledStr) ||
            !bool.TryParse(enabledStr, out var enabled) || !enabled)
        {
            return 60;  // 全局关闭, 默认 60s 后重试
        }

        int pollSec = settings.TryGetValue("perf.alert.poll_seconds", out var psStr) &&
                      int.TryParse(psStr, out var ps) && ps >= 30 ? ps : 60;

        double p95WarnMs = PerfAlertClassifier.ParseDouble(settings, "perf.alert.p95_warn_ms", 1000);
        double p95ErrorMs = PerfAlertClassifier.ParseDouble(settings, "perf.alert.p95_error_ms", 3000);
        double errorRatePct = PerfAlertClassifier.ParseDouble(settings, "perf.alert.error_rate_pct", 5);
        double maxMs = PerfAlertClassifier.ParseDouble(settings, "perf.alert.max_ms", 10000);
        // v30-20: Meili 主路径阈值
        double meiliP99WarnMs = PerfAlertClassifier.ParseDouble(settings, "perf.alert.meili_p99_warn_ms", 500);
        double meiliP99ErrorMs = PerfAlertClassifier.ParseDouble(settings, "perf.alert.meili_p99_error_ms", 1500);
        double meiliFallbackRatePct = PerfAlertClassifier.ParseDouble(settings, "perf.alert.meili_fallback_rate_pct", 20);

        var now = DateTime.UtcNow;

        // 2) PerfMetrics 规则评估 (全局 HTTP 请求指标)
        var snapshot = _metrics.GetSnapshot();
        if (snapshot.SampleCount >= 30)
        {
            // 样本足够, 评估规则
            //   P95 ERROR
            if (snapshot.P95Ms >= p95ErrorMs)
            {
                TryEmit("p95_error", "ERROR", $"P95 = {snapshot.P95Ms}ms (阈值 {p95ErrorMs}ms)", now, snapshot);
            }
            //   P95 WARN (不与 ERROR 重复)
            else if (snapshot.P95Ms >= p95WarnMs)
            {
                TryEmit("p95_warn", "WARN", $"P95 = {snapshot.P95Ms}ms (阈值 {p95WarnMs}ms)", now, snapshot);
            }
            //   ErrorRate ERROR
            if (snapshot.ErrorRate >= errorRatePct)
            {
                TryEmit("error_rate", "ERROR",
                    $"错误率 {snapshot.ErrorRate}% (阈值 {errorRatePct}%, 错误 {snapshot.ErrorRequests}/{snapshot.TotalRequests})",
                    now, snapshot);
            }
            //   MaxMs ERROR
            if (snapshot.MaxMs >= maxMs)
            {
                TryEmit("max_ms", "ERROR", $"最大耗时 {snapshot.MaxMs}ms (阈值 {maxMs}ms)", now, snapshot);
            }
        }

        // 3) v30-20: Meili 主路径规则评估 (独立于 PerfMetrics, 走 AlertCenter 推送)
        //   WHY 独立: MeiliSearchMetrics 样本与 PerfMetrics 不同 (前者只记搜索调用, 后者记所有 HTTP)
        //         样本数门槛也不同 (Meili 搜索频率低, 30 太高, 用 10)
        var meiliSnapshot = _meiliMetrics.GetSnapshot();
        if (meiliSnapshot.SampleCount >= 10)
        {
            await EvaluateMeiliRulesAsync(meiliSnapshot, meiliP99WarnMs, meiliP99ErrorMs, meiliFallbackRatePct, ct);
        }

        return pollSec;
    }

    /// <summary>
    /// v30-20: 评估 Meili 主路径告警规则 (走 AlertCenter 推送)
    /// WHY 走 AlertCenter 而非 TryEmit:
    ///   - AlertCenter 自动路由到 dingtalk/wechat/webhook 渠道
    ///   - AlertCenter 自带 5min 抑制窗口 (基于 suppressKey)
    ///   - AlertCenter 自动持久化 alert_history (优于 TryEmit 仅内存 + 日志)
    ///   - 与 P2-1 告警基础设施统一 (EtlAlertService 后续也会迁移到 AlertCenter)
    /// </summary>
    private async Task EvaluateMeiliRulesAsync(
        MeiliSearchSnapshot snapshot,
        double meiliP99WarnMs,
        double meiliP99ErrorMs,
        double meiliFallbackRatePct,
        CancellationToken ct)
    {
        // 规则 1: meili_p99_error (P0) — Meili P99 超 ERROR 阈值
        if (snapshot.P99Ms >= meiliP99ErrorMs)
        {
            await EmitMeiliViaAlertCenterAsync(
                PerfAlertClassifier.MeiliRules.P99Error,
                snapshot,
                meiliP99ErrorMs,
                ct);
        }
        // 规则 2: meili_p99_warn (P1) — 不与 ERROR 重复
        else if (snapshot.P99Ms >= meiliP99WarnMs)
        {
            await EmitMeiliViaAlertCenterAsync(
                PerfAlertClassifier.MeiliRules.P99Warn,
                snapshot,
                meiliP99WarnMs,
                ct);
        }
        // 规则 3: meili_fallback_rate_error (P0) — 频繁降级=Meili 不可用
        if (snapshot.FallbackRate >= meiliFallbackRatePct)
        {
            await EmitMeiliViaAlertCenterAsync(
                PerfAlertClassifier.MeiliRules.FallbackRateError,
                snapshot,
                meiliFallbackRatePct,
                ct);
        }
    }

    /// <summary>
    /// v30-20: 通过 AlertCenter 推送 Meili 告警
    /// </summary>
    private async Task EmitMeiliViaAlertCenterAsync(
        string rule,
        MeiliSearchSnapshot snapshot,
        double threshold,
        CancellationToken ct)
    {
        var severity = PerfAlertClassifier.ClassifyMeiliSeverity(rule);
        var title = $"[{severity}] Meili 主路径告警: {rule}";
        var markdown = PerfAlertClassifier.BuildMeiliAlertMarkdown(rule, snapshot, threshold);
        var context = PerfAlertClassifier.BuildMeiliAlertContext(rule, snapshot, threshold);

        // suppressKey 用 rule (AlertCenter 内部按 type|suppressKey 组合, 5min 内不重发)
        //   WHY 用 rule: 同规则 5min 内不重发, 不同规则可同时触发 (P99 ERROR + FallbackRate 同时)
        try
        {
            var result = await _alertCenter.EmitAsync(
                type: "perf.meili",
                severity: severity,
                title: title,
                markdown: markdown,
                context: context,
                suppressKey: rule,
                ct: ct);

            // 同步记入 _recentAlerts (供 /api/admin/perf/alerts 查询)
            //   WHY 同步: 运维从 /api/admin/perf/alerts 能看到 Meili 告警, 不必另查 alert_history
            var alert = new PerfAlert(
                AtUtc: DateTime.UtcNow,
                Level: severity,
                Rule: rule,
                Message: $"{title} | P99={snapshot.P99Ms}ms FallbackRate={snapshot.FallbackRate}% N={snapshot.SampleCount}",
                P50Ms: snapshot.P50Ms,
                P95Ms: snapshot.P95Ms,
                P99Ms: snapshot.P99Ms,
                MaxMs: snapshot.MaxMs,
                ErrorRate: snapshot.FallbackRate,  // 复用 ErrorRate 字段存 FallbackRate (语义近似)
                SampleCount: snapshot.SampleCount
            );
            _recentAlerts.Enqueue(alert);
            while (_recentAlerts.Count > MaxRecentAlerts && _recentAlerts.TryDequeue(out _)) { }

            if (severity == "P0" || severity == "ERROR")
            {
                _logger.LogError("[MEILI-ALERT] {Rule}: P99={P99}ms FallbackRate={FR}% N={N} (result={Result})",
                    rule, snapshot.P99Ms, snapshot.FallbackRate, snapshot.SampleCount,
                    result.Success ? "sent" : (result.Suppressed ? "suppressed" : (result.Disabled ? "disabled" : "failed")));
            }
            else
            {
                _logger.LogWarning("[MEILI-ALERT] {Rule}: P99={P99}ms FallbackRate={FR}% N={N} (result={Result})",
                    rule, snapshot.P99Ms, snapshot.FallbackRate, snapshot.SampleCount,
                    result.Success ? "sent" : (result.Suppressed ? "suppressed" : (result.Disabled ? "disabled" : "failed")));
            }
        }
        catch (Exception ex)
        {
            // AlertCenter 异常不应阻塞 PerfAlertService 主循环
            _logger.LogError(ex, "[MEILI-ALERT] AlertCenter.EmitAsync 异常: rule={Rule}", rule);
        }
    }

    private void TryEmit(string rule, string level, string message, DateTime at, PerfSnapshot snapshot)
    {
        // v30-13: 抑制判断逻辑提取到 PerfAlertClassifier.IsSuppressed (可单测)
        lock (_suppressedKeys)
        {
            if (PerfAlertClassifier.IsSuppressed(_suppressedKeys, level, rule, at, SuppressionWindow))
            {
                return;  // 抑制窗口内, 不重发
            }
            PerfAlertClassifier.UpdateSuppression(_suppressedKeys, level, rule, at);
        }

        var alert = new PerfAlert(at, level, rule, message, snapshot.P50Ms, snapshot.P95Ms,
            snapshot.P99Ms, snapshot.MaxMs, snapshot.ErrorRate, snapshot.SampleCount);
        _recentAlerts.Enqueue(alert);
        while (_recentAlerts.Count > MaxRecentAlerts && _recentAlerts.TryDequeue(out _)) { }

        if (level == "ERROR")
            _logger.LogError("[PERF-ALERT] {Rule}: {Message} (P50={P50} P95={P95} P99={P99} Max={Max} ErrRate={ErrRate}% N={N})",
                rule, message, snapshot.P50Ms, snapshot.P95Ms, snapshot.P99Ms, snapshot.MaxMs, snapshot.ErrorRate, snapshot.SampleCount);
        else
            _logger.LogWarning("[PERF-ALERT] {Rule}: {Message} (P50={P50} P95={P95} P99={P99} Max={Max} ErrRate={ErrRate}% N={N})",
                rule, message, snapshot.P50Ms, snapshot.P95Ms, snapshot.P99Ms, snapshot.MaxMs, snapshot.ErrorRate, snapshot.SampleCount);
    }
}

/// <summary>性能告警记录 (内存 FIFO, 不持久化)</summary>
public record PerfAlert(
    DateTime AtUtc,
    string Level,     // WARN / ERROR
    string Rule,      // p95_warn / p95_error / error_rate / max_ms
    string Message,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs,
    double ErrorRate,
    int SampleCount
);
