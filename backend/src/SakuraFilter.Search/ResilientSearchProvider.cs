using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using SakuraFilter.Core.DTOs;

namespace SakuraFilter.Search;

/// <summary>
/// 弹性搜索提供者 (主备切换 + 熔断)
/// - 主: MeiliSearch (typo 容错,体验好)
/// - 备: PostgreSQL (100% 可靠,无 typo)
/// - 策略: 超时 1s + 重试 1 次 + 连续 3 次失败熔断 30s
/// - 熔断期间直接走 PG,30s 后尝试探活 Meili
/// </summary>
public class ResilientSearchProvider : ISearchProvider
{
    private readonly MeiliSearchProvider _primary;
    private readonly PostgresSearchProvider _fallback;
    private readonly ILogger<ResilientSearchProvider> _logger;
    private readonly ResiliencePipeline _pipeline;
    private volatile bool _primaryAvailable = true;

    public string Name => "resilient(meili→pg)";

    public ResilientSearchProvider(
        MeiliSearchProvider primary,
        PostgresSearchProvider fallback,
        ILogger<ResilientSearchProvider> logger)
    {
        _primary = primary;
        _fallback = fallback;
        _logger = logger;

        // Polly v8 弹性管道: timeout + retry + circuit breaker
        _pipeline = new ResiliencePipelineBuilder()
            .AddTimeout(TimeSpan.FromSeconds(1))           // 单次调用 1s 超时
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 1,                      // 失败重试 1 次
                Delay = TimeSpan.FromMilliseconds(200),
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => ex is not OperationCanceledException)
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,                        // 50% 失败率熔断
                MinimumThroughput = 4,                     // 至少 4 次采样
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = TimeSpan.FromSeconds(30),  // 熔断 30s
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                OnOpened = args =>
                {
                    _primaryAvailable = false;
                    _logger.LogWarning("Meili 熔断开启,自动切换 PG 兜底 ({Duration}s)", args.BreakDuration.TotalSeconds);
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    _primaryAvailable = true;
                    _logger.LogInformation("Meili 熔断关闭,恢复主搜索");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = _ =>
                {
                    _logger.LogInformation("Meili 半开,尝试探活");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        var primary = await _primary.HealthCheckAsync(ct);
        var fallback = await _fallback.HealthCheckAsync(ct);
        _logger.LogInformation("搜索健康: Meili={Primary}, PG={Fallback}, Active={Active}",
            primary, fallback, primary ? "meili" : "pg");
        return primary || fallback;  // 任一可用即健康
    }

    // P1-6.1: 单独探活方法 (不重试, 不触发熔断)
    //   WHY: /health/ready 需要分别暴露 meili / fallback 状态,
    //        原 HealthCheckAsync 会把两者结果合并, 无法区分降级 vs 全面故障
    //   不走 _pipeline: 避免 Polly 熔断器/重试污染探活结果
    public async Task<bool> IsPrimaryHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            return await _primary.HealthCheckAsync(ct);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsFallbackHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            return await _fallback.HealthCheckAsync(ct);
        }
        catch
        {
            return false;
        }
    }

    // P3-5.3: 暴露熔断状态供 /metrics 端点读取 (1=open/failing, 0=closed/healthy)
    //   WHY 只读属性: /metrics 端点轮询读取, 不应触发探活 (探活走 IsPrimaryHealthyAsync)
    public bool IsCircuitBreakerOpen => !_primaryAvailable;

    /// <summary>
    /// 启动时初始化 (P3-6.2)
    /// 如果 primary 不可用,立即标记为降级,避免首次搜索等 1s 超时
    /// WHY 只设置 _primaryAvailable: 不触发熔断器状态变更 (熔断器由 Polly 内部统计驱动),
    ///      仅标记降级让 SearchAsync 首次请求直接走 PG 兜底
    /// </summary>
    public void Initialize(bool primaryAvailable)
    {
        _primaryAvailable = primaryAvailable;
        if (!primaryAvailable)
        {
            _logger.LogWarning("启动探活: Meili 不可用,立即降级到 PG 兜底");
        }
    }

    public async Task<SearchResult> SearchAsync(SearchRequest req, CancellationToken ct = default)
    {
        if (!_primaryAvailable)
        {
            // 熔断中,直接走 PG
            return await _fallback.SearchAsync(req, ct);
        }

        try
        {
            var result = await _pipeline.ExecuteAsync(
                async token => await _primary.SearchAsync(req, token),
                ct);

            // 成功时记录延迟 (生产可加 metric)
            return result;
        }
        catch (Exception ex) when (ex is BrokenCircuitException or TimeoutException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Meili 搜索失败,降级到 PG 兜底");
            return await _fallback.SearchAsync(req, ct);
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            // P3-6.3: Meili 连接拒绝 (SocketException),立即降级不等重试
            //   WHY 显式设 _primaryAvailable=false: SocketException 通常是 Meili 进程未启动,
            //        重试无意义,直接标记降级让后续请求走 PG,避免每次都等 1s 超时
            _primaryAvailable = false;
            _logger.LogWarning(ex, "Meili 连接拒绝 (SocketException),立即降级到 PG 兜底");
            return await _fallback.SearchAsync(req, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Meili 搜索异常,降级到 PG 兜底");
            return await _fallback.SearchAsync(req, ct);
        }
    }

    public async Task IndexAsync(IEnumerable<Mr1IndexDoc> docs, CancellationToken ct = default)
    {
        // 双写: 主备都写,主失败不阻塞备 (后台 worker 会补偿)
        // V2: 文档类型从 ProductIndexDoc 改为 Mr1IndexDoc (嵌套结构)
        var primaryTask = SafeIndexAsync(_primary, docs, ct);
        var fallbackTask = SafeIndexAsync(_fallback, docs, ct);
        await Task.WhenAll(primaryTask, fallbackTask);
    }

    /// <summary>
    /// V2: DeleteAsync 签名从 IEnumerable&lt;long&gt; ids 改为 IEnumerable&lt;string&gt; mr1s
    /// WHY: V2 主键改为 mr_1 (字符串),按主键删除更准确
    /// </summary>
    public async Task DeleteAsync(IEnumerable<string> mr1s, CancellationToken ct = default)
    {
        var primaryTask = SafeDeleteAsync(_primary, mr1s, ct);
        var fallbackTask = SafeDeleteAsync(_fallback, mr1s, ct);
        await Task.WhenAll(primaryTask, fallbackTask);
    }

    private async Task SafeIndexAsync(ISearchProvider p, IEnumerable<Mr1IndexDoc> docs, CancellationToken ct)
    {
        try { await p.IndexAsync(docs, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "{Provider} 索引失败(将由后台 worker 补偿)", p.Name); }
    }

    /// <summary>V2: SafeDeleteAsync 签名同步改为 IEnumerable&lt;string&gt; mr1s</summary>
    private async Task SafeDeleteAsync(ISearchProvider p, IEnumerable<string> mr1s, CancellationToken ct)
    {
        try { await p.DeleteAsync(mr1s, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "{Provider} 删除失败(将由后台 worker 补偿)", p.Name); }
    }
}
