using System.Collections.Concurrent;
using System.Diagnostics;

namespace SakuraFilter.Api.Services;

/// <summary>
/// P5.5: 性能埋点指标聚合 (Singleton, 无状态依赖)
/// 设计:
///   - 记录每个 HTTP 请求的耗时 (毫秒)
///   - 用 lock-free ring buffer 存最近 1000 条请求样本
///   - 排除 /health/live, /api/perf (递归调用), /scalar, /openapi 等高频探针
///   - 暴露 GetSnapshot() 给 /api/perf 端点
///   - 不写入 X-Response-Time header (避免日志被观测污染)
/// WHY 独立 Singleton:
///   - 1000 条样本足够计算 P95 (统计学上 n=1000 误差 < 3%)
///   - 用 ConcurrentQueue + 自旋保持容量,避免 List 加锁开销
///   - 简单可靠,不需要 Prometheus/InfluxDB 等额外基础设施
///   - Middleware 通过构造注入本服务, 端点也注入本服务 (DI 友好)
/// </summary>
public class PerfMetrics
{
    public const int Capacity = 1000;
    private readonly ConcurrentQueue<RequestSample> _samples = new();
    private long _totalRequests;
    private long _errorRequests;  // status >= 500 或异常

    public void Record(string path, string method, int statusCode, double elapsedMs)
    {
        Interlocked.Increment(ref _totalRequests);
        if (statusCode >= 500) Interlocked.Increment(ref _errorRequests);

        var sample = new RequestSample(DateTime.UtcNow, method, path, statusCode, elapsedMs);
        _samples.Enqueue(sample);
        // ring buffer: 超容量时丢弃最旧
        while (_samples.Count > Capacity && _samples.TryDequeue(out _)) { }
    }

    /// <summary>
    /// 计算 P50/P95/P99 并返回快照
    /// WHY 用 array snapshot: 避免在计算时队列继续 Enqueue 导致排序结果错乱
    /// </summary>
    public PerfSnapshot GetSnapshot()
    {
        var arr = _samples.ToArray();
        var total = Interlocked.Read(ref _totalRequests);
        var errors = Interlocked.Read(ref _errorRequests);

        if (arr.Length == 0)
        {
            return new PerfSnapshot(
                SampleCount: 0,
                TotalRequests: total,
                ErrorRequests: errors,
                ErrorRate: total > 0 ? Math.Round(errors * 100.0 / total, 2) : 0,
                P50Ms: 0,
                P95Ms: 0,
                P99Ms: 0,
                MaxMs: 0,
                GeneratedAt: DateTime.UtcNow
            );
        }

        Array.Sort(arr, (a, b) => a.ElapsedMs.CompareTo(b.ElapsedMs));
        var p50 = Percentile(arr, 0.50);
        var p95 = Percentile(arr, 0.95);
        var p99 = Percentile(arr, 0.99);
        var max = arr[^1].ElapsedMs;

        return new PerfSnapshot(
            SampleCount: arr.Length,
            TotalRequests: total,
            ErrorRequests: errors,
            ErrorRate: total > 0 ? Math.Round(errors * 100.0 / total, 2) : 0,
            P50Ms: Math.Round(p50, 1),
            P95Ms: Math.Round(p95, 1),
            P99Ms: Math.Round(p99, 1),
            MaxMs: Math.Round(max, 1),
            GeneratedAt: DateTime.UtcNow
        );
    }

    private static double Percentile(RequestSample[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        // nearest-rank 方法: rank = ceil(p * n)
        var rank = (int)Math.Ceiling(p * sorted.Length);
        if (rank < 1) rank = 1;
        if (rank > sorted.Length) rank = sorted.Length;
        return sorted[rank - 1].ElapsedMs;
    }
}

public record RequestSample(DateTime AtUtc, string Method, string Path, int StatusCode, double ElapsedMs);

public record PerfSnapshot(
    int SampleCount,
    long TotalRequests,
    long ErrorRequests,
    double ErrorRate,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs,
    DateTime GeneratedAt
);

/// <summary>
/// P5.5: 响应时间埋点中间件
///   - 注入 PerfMetrics (Singleton), 中间件本体不注册到 DI
///   - 排除探针路径 — 这些请求不应该进入统计样本
///   - 记录 status >= 500 + 异常 → errorRequests
/// </summary>
public class ResponseTimeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ResponseTimeMiddleware> _logger;
    private readonly PerfMetrics _metrics;

    // 排除路径前缀 (匹配时跳过)
    private static readonly string[] ExcludedPathPrefixes = new[]
    {
        "/health/live",
        "/health/ready",
        "/api/perf",
        "/scalar",
        "/openapi",
        "/swagger",
        "/favicon.ico"
    };

    public ResponseTimeMiddleware(RequestDelegate next, ILogger<ResponseTimeMiddleware> logger, PerfMetrics metrics)
    {
        _next = next;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "/";
        foreach (var prefix in ExcludedPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                await _next(ctx);
                return;
            }
        }

        var sw = Stopwatch.StartNew();
        Exception? caught = null;
        try
        {
            await _next(ctx);
        }
        catch (Exception ex)
        {
            caught = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            var statusCode = caught != null ? 500 : ctx.Response.StatusCode;
            _metrics.Record(path, ctx.Request.Method, statusCode, sw.Elapsed.TotalMilliseconds);
        }
    }
}
