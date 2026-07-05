// P1-3: Prometheus 业务指标
//   - 暴露标准 /metrics 端点 (prometheus-net.AspNetCore)
//   - 关键业务指标:
//     * HTTP 请求计数 + 耗时 (UseHttpMetrics 自动)
//     * 后台服务心跳陈旧数 (gauge, 由 BusinessMetricsRefreshWorker 周期刷新)
//     * ETL 导入记录 / 失败 (counter)
//     * 死信队列深度 (gauge)
//     * 鉴权失败 / 限流命中 (counter)
//   - 沿用现有 IHostedServiceStatus 模式, 周期刷新深度类指标
//   WHY: 业务侧无现成 metrics, 监控只能从日志反推。
//        暴露标准 /metrics 后可对接 Grafana / AlertManager, 隐患提前告警
using Microsoft.EntityFrameworkCore;
using Prometheus;
using SakuraFilter.Etl;
using SakuraFilter.Infrastructure.Data;
using SakuraFilter.Search;

namespace SakuraFilter.Api.Services;

/// <summary>
/// 业务指标注册表 (P1-3) — 单例, 通过 DI 注入到各业务服务中使用
/// 所有指标名以 sakura_ 前缀, 避免与 prometheus-net 默认指标冲突
/// </summary>
public class BusinessMetrics
{
    /// <summary>ETL 导入记录数 (counter, 按 entity/status 维度)</summary>
    public readonly Counter EtlRecordsProcessed = Prometheus.Metrics.CreateCounter(
        "sakura_etl_records_processed_total",
        "ETL 处理的记录总数",
        new CounterConfiguration { LabelNames = new[] { "entity", "status" } });

    /// <summary>ETL 失败原因计数 (counter, 按 entity/reason_code 维度)</summary>
    public readonly Counter EtlFailures = Prometheus.Metrics.CreateCounter(
        "sakura_etl_failures_total",
        "ETL 失败原因计数 (reason_code 见 EtlReasonCode 枚举)",
        new CounterConfiguration { LabelNames = new[] { "entity", "reason_code" } });

    /// <summary>ETL 任务持续时间直方图 (histogram, 按 entity 维度)</summary>
    public readonly Histogram EtlTaskDuration = Prometheus.Metrics.CreateHistogram(
        "sakura_etl_task_duration_seconds",
        "ETL 任务持续时间 (秒)",
        new HistogramConfiguration
        {
            LabelNames = new[] { "entity" },
            Buckets = Histogram.ExponentialBuckets(0.1, 2, 12) // 0.1s ~ ~200s
        });

    /// <summary>死信队列深度 (gauge, 由 BusinessMetricsRefreshWorker 周期刷新)</summary>
    public readonly Gauge DeadLetterDepth = Prometheus.Metrics.CreateGauge(
        "sakura_dead_letter_depth",
        "死信队列表当前 active 行数");

    /// <summary>死信恢复成功数 (counter)</summary>
    public readonly Counter DeadLetterRecovered = Prometheus.Metrics.CreateCounter(
        "sakura_dead_letter_recovered_total",
        "死信恢复成功总数");

    /// <summary>后台服务陈旧数 (gauge, 由 BusinessMetricsRefreshWorker 周期刷新)</summary>
    public readonly Gauge HostedServicesStale = Prometheus.Metrics.CreateGauge(
        "sakura_hosted_services_stale",
        "心跳陈旧的后台服务数 (超过 5min 未报心跳)");

    /// <summary>后台服务总跟踪数 (gauge)</summary>
    public readonly Gauge HostedServicesTracked = Prometheus.Metrics.CreateGauge(
        "sakura_hosted_services_tracked",
        "已注册的后台服务总数");

    /// <summary>鉴权失败数 (counter, 按 reason 维度)</summary>
    public readonly Counter AuthFailures = Prometheus.Metrics.CreateCounter(
        "sakura_auth_failures_total",
        "鉴权失败次数 (reason: invalid_token / expired / revoked / missing / hash_mismatch)",
        new CounterConfiguration { LabelNames = new[] { "reason" } });

    /// <summary>限流命中数 (counter, 按 endpoint 维度)</summary>
    public readonly Counter RateLimitHits = Prometheus.Metrics.CreateCounter(
        "sakura_rate_limit_hits_total",
        "限流命中次数 (按 endpoint 分类)",
        new CounterConfiguration { LabelNames = new[] { "endpoint" } });

    /// <summary>公开搜索 QPS (counter, 按 provider 维度, MeiliSearch / PostgresSearch)</summary>
    public readonly Counter SearchQueries = Prometheus.Metrics.CreateCounter(
        "sakura_search_queries_total",
        "公开搜索查询次数 (按 provider 维度)",
        new CounterConfiguration { LabelNames = new[] { "provider" } });

    // ===== P3-5.3 兼容: 迁移原有手动 StringBuilder 输出的指标到 prometheus-net 注册表 =====
    //   迁移原因: 与 HTTP/business 指标统一到 /metrics 端点, 避免运维配置两套 scrape job

    /// <summary>Meili 搜索熔断器状态 (1=open, 0=closed) — 迁移自 /metrics 手动输出</summary>
    public readonly Gauge MeiliCircuitBreaker = Prometheus.Metrics.CreateGauge(
        "sakurafilter_meili_circuit_breaker",
        "Meili search circuit breaker state (1=open/failing, 0=closed/healthy)");

    /// <summary>ETL 当前任务已读取行数 (无活动任务时为 0) — 迁移自 /metrics 手动输出</summary>
    public readonly Gauge EtlProgressRead = Prometheus.Metrics.CreateGauge(
        "sakurafilter_etl_progress_read",
        "ETL 当前任务已读取行数 (无活动任务时为 0)");

    /// <summary>死信队列深度 (status=active 行数) — 迁移自 /metrics 手动输出</summary>
    public readonly Gauge DeadLetterDepthLegacy = Prometheus.Metrics.CreateGauge(
        "sakurafilter_dead_letter_depth",
        "死信队列当前深度 (status=active 行数, 等待人工/worker 处理)");
}

/// <summary>
/// 周期刷新深度类业务指标的 BackgroundService (P1-3)
/// 职责: 每 30s 拉取深度类指标 (死信队列深度、后台服务状态)
/// WHY: 深度类指标 (gauge) 必须有数据源持续写入, 否则 Prometheus 拉取时永远是 0
/// </summary>
public class BusinessMetricsRefreshWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IHostedServiceStatus _hostedStatus;
    private readonly BusinessMetrics _metrics;
    private readonly ILogger<BusinessMetricsRefreshWorker> _logger;

    public BusinessMetricsRefreshWorker(
        IServiceProvider sp,
        IHostedServiceStatus hostedStatus,
        BusinessMetrics metrics,
        ILogger<BusinessMetricsRefreshWorker> logger)
    {
        _sp = sp;
        _hostedStatus = hostedStatus;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[BusinessMetrics] 周期刷新 Worker 启动 (30s 间隔)");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. 后台服务陈旧数
                var stale = _hostedStatus.GetStaleServices(TimeSpan.FromMinutes(5));
                _metrics.HostedServicesStale.Set(stale.Count);
                _metrics.HostedServicesTracked.Set(_hostedStatus.TrackedServices.Count);
                if (stale.Count > 0)
                {
                    _logger.LogWarning("[BusinessMetrics] 后台服务陈旧: {Stale}", string.Join(",", stale));
                }

                // 2. 死信队列深度 (PG 实时查询, 命中部分索引 idx_dead_letter_active_recovery)
                try
                {
                    using var scope = _sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
                    var depth = await db.SearchIndexDeadLetters
                        .CountAsync(d => d.Status == "active", stoppingToken);
                    _metrics.DeadLetterDepth.Set(depth);
                    _metrics.DeadLetterDepthLegacy.Set(depth); // 兼容旧名
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[BusinessMetrics] 死信深度刷新失败 (非致命)");
                }

                // 3. Meili 熔断器状态 (从 ResilientSearchProvider.IsCircuitBreakerOpen 读内存)
                try
                {
                    var search = _sp.GetService<SakuraFilter.Search.ISearchProvider>();
                    if (search is ResilientSearchProvider rsp)
                    {
                        _metrics.MeiliCircuitBreaker.Set(rsp.IsCircuitBreakerOpen ? 1 : 0);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[BusinessMetrics] Meili 熔断器状态读取失败 (非致命)");
                }

                // 4. ETL 进度 (从 EtlImportService.Progress.Read 读内存, Interlocked 线程安全)
                try
                {
                    var etl = _sp.GetService<EtlImportService>();
                    if (etl != null)
                    {
                        _metrics.EtlProgressRead.Set(etl.Progress.Read);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[BusinessMetrics] ETL 进度读取失败 (非致命)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BusinessMetrics] 周期刷新异常 (下次重试)");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        _logger.LogInformation("[BusinessMetrics] 周期刷新 Worker 停止");
    }
}
