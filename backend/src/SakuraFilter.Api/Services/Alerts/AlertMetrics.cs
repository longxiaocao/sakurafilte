using Prometheus;

namespace SakuraFilter.Api.Services.Alerts;

/// <summary>
/// P2-1 告警 Prometheus 指标
/// - 暴露给 Grafana / AlertManager
/// - 端点: GET /metrics (通过 prometheus-net.AspNetCore UseHttpMetrics 注册)
/// 指标:
///   sakura_alerts_sent_total{type,channel}       已发送成功次数
///   sakura_alerts_failed_total{type,channel}    发送失败次数
///   sakura_alerts_suppressed_total{type}        抑制窗口内丢弃次数
///   sakura_alerts_emit_duration_seconds{type}   EmitAsync 耗时 (histogram)
/// </summary>
public static class AlertMetrics
{
    public static readonly Counter Sent = Prometheus.Metrics.CreateCounter(
        "sakura_alerts_sent_total",
        "Total alerts sent successfully.",
        new CounterConfiguration { LabelNames = new[] { "type", "channel" } });

    public static readonly Counter Failed = Prometheus.Metrics.CreateCounter(
        "sakura_alerts_failed_total",
        "Total alerts failed to send.",
        new CounterConfiguration { LabelNames = new[] { "type", "channel" } });

    public static readonly Counter Suppressed = Prometheus.Metrics.CreateCounter(
        "sakura_alerts_suppressed_total",
        "Total alerts suppressed by dedup window.",
        new CounterConfiguration { LabelNames = new[] { "type" } });

    public static readonly Counter Disabled = Prometheus.Metrics.CreateCounter(
        "sakura_alerts_disabled_total",
        "Total alerts skipped because alert.enabled=false.",
        new CounterConfiguration { LabelNames = new[] { "type" } });

    public static readonly Histogram EmitDuration = Prometheus.Metrics.CreateHistogram(
        "sakura_alerts_emit_duration_seconds",
        "AlertCenter.EmitAsync duration in seconds.",
        new HistogramConfiguration
        {
            LabelNames = new[] { "type", "outcome" },
            // 1ms ~ 5s 区间, 8 桶 (匹配典型场景: 缓存命中 ~2ms, webhook 推送 ~200ms, 抑制检查 ~5ms)
            Buckets = new[] { 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1.0, 5.0 }
        });
}
