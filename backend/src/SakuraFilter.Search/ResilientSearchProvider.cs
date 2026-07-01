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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Meili 搜索异常,降级到 PG 兜底");
            return await _fallback.SearchAsync(req, ct);
        }
    }

    public async Task IndexAsync(IEnumerable<ProductIndexDoc> docs, CancellationToken ct = default)
    {
        // 双写: 主备都写,主失败不阻塞备 (后台 worker 会补偿)
        var primaryTask = SafeIndexAsync(_primary, docs, ct);
        var fallbackTask = SafeIndexAsync(_fallback, docs, ct);
        await Task.WhenAll(primaryTask, fallbackTask);
    }

    public async Task DeleteAsync(IEnumerable<long> ids, CancellationToken ct = default)
    {
        var primaryTask = SafeDeleteAsync(_primary, ids, ct);
        var fallbackTask = SafeDeleteAsync(_fallback, ids, ct);
        await Task.WhenAll(primaryTask, fallbackTask);
    }

    private async Task SafeIndexAsync(ISearchProvider p, IEnumerable<ProductIndexDoc> docs, CancellationToken ct)
    {
        try { await p.IndexAsync(docs, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "{Provider} 索引失败(将由后台 worker 补偿)", p.Name); }
    }

    private async Task SafeDeleteAsync(ISearchProvider p, IEnumerable<long> ids, CancellationToken ct)
    {
        try { await p.DeleteAsync(ids, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "{Provider} 删除失败(将由后台 worker 补偿)", p.Name); }
    }
}
