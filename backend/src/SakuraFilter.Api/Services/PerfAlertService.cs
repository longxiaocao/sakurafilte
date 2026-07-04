// P6: 性能阈值告警服务 (基于 P5.5 PerfMetrics)
//   - 定时 (默认 60s) 扫描 PerfMetrics.GetSnapshot()
//   - 阈值规则 (system_settings 可在线调整,无需重启):
//       perf.alert.enabled           全局开关 (true/false)
//       perf.alert.poll_seconds      轮询周期 (默认 60s)
//       perf.alert.p95_warn_ms       P95 WARN 阈值 (默认 1000ms)
//       perf.alert.p95_error_ms      P95 ERROR 阈值 (默认 3000ms)
//       perf.alert.error_rate_pct    错误率 ERROR 阈值 (默认 5%)
//       perf.alert.max_ms            单请求最大耗时 ERROR 阈值 (默认 10000ms)
//   - 告警抑制: 5min 窗口内同 (level+rule) 不重发 (参考 EtlAlertService 模式)
//   - 持久化: 内存 FIFO 最近 100 条 + 日志 (不引入新表, 走 system_settings 配置)
//   - 暴露: GET /api/admin/perf/alerts 查询当前告警列表 (需 X-Admin-Token)
//
// WHY 独立 BackgroundService 而非 PerfMetrics 内联:
//   - 解耦告警逻辑与指标聚合 (单一职责)
//   - 告警策略可独立调整 (阈值/周期/抑制窗口)
//   - 后续可扩展为 webhook 推送 (参考 EtlAlertService)
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

public class PerfAlertService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<PerfAlertService> _logger;
    private readonly PerfMetrics _metrics;

    // 默认配置 (启动时若 system_settings 中无则插入)
    private static readonly (string Key, string Value, string Description)[] Defaults = new[]
    {
        ("perf.alert.enabled", "true", "性能告警全局开关 (true/false)"),
        ("perf.alert.poll_seconds", "60", "扫描周期 (秒), 建议不低于 30"),
        ("perf.alert.p95_warn_ms", "1000", "P95 WARN 阈值 (ms), 超过触发 WARN 告警"),
        ("perf.alert.p95_error_ms", "3000", "P95 ERROR 阈值 (ms), 超过触发 ERROR 告警"),
        ("perf.alert.error_rate_pct", "5", "错误率 ERROR 阈值 (%), 超过触发 ERROR 告警"),
        ("perf.alert.max_ms", "10000", "单请求最大耗时 ERROR 阈值 (ms)"),
    };

    // 内存 FIFO 最近 100 条告警 (线程安全)
    //   WHY 不入库: 高频写, 无业务价值, 运维从日志聚合即可
    private readonly ConcurrentQueue<PerfAlert> _recentAlerts = new();
    private const int MaxRecentAlerts = 100;

    // 告警抑制 (per-process in-memory, 5min 内同 level+rule 不重发)
    //   WHY: 持续 P95 高时避免每分钟刷屏日志
    //   牺牲: 进程重启后抑制状态丢失, 可能多记一条日志 (可接受)
    private readonly Dictionary<string, DateTime> _suppressedKeys = new();
    private static readonly TimeSpan SuppressionWindow = TimeSpan.FromMinutes(5);

    public PerfAlertService(
        IServiceProvider sp,
        ILogger<PerfAlertService> logger,
        PerfMetrics metrics)
    {
        _sp = sp;
        _logger = logger;
        _metrics = metrics;
    }

    /// <summary>查询最近告警列表 (供 /api/admin/perf/alerts 端点调用)</summary>
    public List<PerfAlert> GetRecentAlerts(int limit = 50)
    {
        var cap = Math.Clamp(limit, 1, 100);
        return _recentAlerts.ToArray().TakeLast(cap).ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureDefaultSettingsAsync(stoppingToken);

        int pollSec = 60;
        while (!stoppingToken.IsCancellationRequested)
        {
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

    private async Task EnsureDefaultSettingsAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        foreach (var (key, value, desc) in Defaults)
        {
            var exists = await db.SystemSettings.AnyAsync(s => s.Key == key, ct);
            if (!exists)
            {
                db.SystemSettings.Add(new Core.Entities.SystemSetting
                {
                    Key = key,
                    Value = value,
                    Description = desc,
                    UpdatedAt = DateTime.UtcNow
                });
                _logger.LogInformation("插入 PerfAlert 默认配置: {Key} = {Value}", key, value);
            }
        }
        await db.SaveChangesAsync(ct);
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

        double p95WarnMs = ParseDouble(settings, "perf.alert.p95_warn_ms", 1000);
        double p95ErrorMs = ParseDouble(settings, "perf.alert.p95_error_ms", 3000);
        double errorRatePct = ParseDouble(settings, "perf.alert.error_rate_pct", 5);
        double maxMs = ParseDouble(settings, "perf.alert.max_ms", 10000);

        // 2) 取 PerfMetrics 快照
        var snapshot = _metrics.GetSnapshot();
        if (snapshot.SampleCount < 30)
        {
            // 样本太少, 不告警 (避免冷启动误报)
            return pollSec;
        }

        var now = DateTime.UtcNow;
        // 3) 评估规则
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

        return pollSec;
    }

    private static double ParseDouble(Dictionary<string, string> settings, string key, double defaultValue)
    {
        return settings.TryGetValue(key, out var s) && double.TryParse(s, out var v) ? v : defaultValue;
    }

    private void TryEmit(string rule, string level, string message, DateTime at, PerfSnapshot snapshot)
    {
        var suppressKey = $"{level}|{rule}";
        lock (_suppressedKeys)
        {
            if (_suppressedKeys.TryGetValue(suppressKey, out var lastTime) &&
                at - lastTime < SuppressionWindow)
            {
                return;  // 抑制窗口内, 不重发
            }
            _suppressedKeys[suppressKey] = at;
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
