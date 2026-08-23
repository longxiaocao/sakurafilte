using System.Collections.Concurrent;

namespace SakuraFilter.Search;

/// <summary>
/// v30-20: Meili 主路径性能指标采集 (Singleton, 无状态依赖)
/// 设计:
///   - 记录每次 ResilientSearchProvider.SearchAsync 的实际耗时 + 走哪条路径 (Primary/Fallback)
///   - 用 lock-free ConcurrentQueue ring buffer 存最近 1000 条样本
///   - 暴露 GetSnapshot() 给 /api/admin/perf/meili/snapshot 端点 + PerfAlertService 告警判断
///   - 与 PerfMetrics 同模式 (P5.5),不引入新依赖
/// WHY 独立 Singleton (不混入 PerfMetrics):
///   - PerfMetrics 是全局 HTTP 请求指标 (所有端点),无法区分 Meili vs PG fallback
///   - ResilientSearchProvider 在 Search 项目,无法引用 Api 项目的 PerfMetrics (循环引用)
///   - Meili P99 是生产可观测性关键缺口,需独立采集 + 独立告警规则
///   - FallbackRate 是 Meili 健康度核心指标 (Meili 频繁降级=服务降级),需独立统计
/// </summary>
public class MeiliSearchMetrics
{
    public const int Capacity = 1000;

    private readonly ConcurrentQueue<MeiliSearchSample> _samples = new();
    // WHY 双 counter 分别记录: FallbackRate = FallbackCount / (PrimaryCount + FallbackCount)
    //   不用单一 _totalRequests: 与 PerfMetrics 保持差异 (PerfMetrics 记所有 HTTP, 这里只记搜索调用)
    private long _primarySuccessCount;
    private long _fallbackCount;
    private long _primaryErrorCount;  // Meili 异常 (含熔断/超时/连接拒绝)

    /// <summary>
    /// 记录一次搜索样本
    /// </summary>
    /// <param name="outcome">primary_success / fallback / primary_error</param>
    /// <param name="elapsedMs">ResilientSearchProvider.SearchAsync 总耗时 (含降级切换时间)</param>
    public void Record(MeiliSearchOutcome outcome, double elapsedMs)
    {
        switch (outcome)
        {
            case MeiliSearchOutcome.PrimarySuccess:
                Interlocked.Increment(ref _primarySuccessCount);
                break;
            case MeiliSearchOutcome.Fallback:
                Interlocked.Increment(ref _fallbackCount);
                break;
            case MeiliSearchOutcome.PrimaryError:
                Interlocked.Increment(ref _primaryErrorCount);
                break;
        }

        var sample = new MeiliSearchSample(DateTime.UtcNow, outcome, elapsedMs);
        _samples.Enqueue(sample);
        // ring buffer: 超容量时丢弃最旧
        while (_samples.Count > Capacity && _samples.TryDequeue(out _)) { }
    }

    /// <summary>
    /// 计算 P50/P95/P99 并返回快照
    /// WHY 用 array snapshot: 避免在计算时队列继续 Enqueue 导致排序结果错乱 (与 PerfMetrics 同模式)
    /// WHY 只对 PrimarySuccess 样本计算 P99:
    ///   Fallback 样本反映 PG 兜底性能 (不反映 Meili 性能),混合计算会掩盖 Meili 真实问题
    ///   但 MaxMs 记录所有样本最大值 (含 fallback),用于发现极端慢请求
    /// </summary>
    public MeiliSearchSnapshot GetSnapshot()
    {
        var arr = _samples.ToArray();
        var primarySuccess = Interlocked.Read(ref _primarySuccessCount);
        var fallback = Interlocked.Read(ref _fallbackCount);
        var primaryError = Interlocked.Read(ref _primaryErrorCount);
        var total = primarySuccess + fallback + primaryError;

        if (arr.Length == 0)
        {
            return new MeiliSearchSnapshot(
                SampleCount: 0,
                PrimarySuccessCount: primarySuccess,
                FallbackCount: fallback,
                PrimaryErrorCount: primaryError,
                TotalSearchCount: total,
                FallbackRate: total > 0 ? Math.Round(fallback * 100.0 / total, 2) : 0,
                PrimaryErrorRate: total > 0 ? Math.Round(primaryError * 100.0 / total, 2) : 0,
                P50Ms: 0,
                P95Ms: 0,
                P99Ms: 0,
                MaxMs: 0,
                GeneratedAt: DateTime.UtcNow
            );
        }

        // 仅对 PrimarySuccess 样本计算百分位 (反映 Meili 真实性能)
        var primaryLatencies = arr
            .Where(s => s.Outcome == MeiliSearchOutcome.PrimarySuccess)
            .Select(s => s.ElapsedMs)
            .OrderBy(x => x)
            .ToArray();

        var p50 = primaryLatencies.Length > 0 ? Percentile(primaryLatencies, 0.50) : 0;
        var p95 = primaryLatencies.Length > 0 ? Percentile(primaryLatencies, 0.95) : 0;
        var p99 = primaryLatencies.Length > 0 ? Percentile(primaryLatencies, 0.99) : 0;
        // MaxMs: 所有样本最大值 (含 fallback, 用于发现极端慢请求)
        var max = arr.Max(s => s.ElapsedMs);

        return new MeiliSearchSnapshot(
            SampleCount: arr.Length,
            PrimarySuccessCount: primarySuccess,
            FallbackCount: fallback,
            PrimaryErrorCount: primaryError,
            TotalSearchCount: total,
            FallbackRate: total > 0 ? Math.Round(fallback * 100.0 / total, 2) : 0,
            PrimaryErrorRate: total > 0 ? Math.Round(primaryError * 100.0 / total, 2) : 0,
            P50Ms: Math.Round(p50, 1),
            P95Ms: Math.Round(p95, 1),
            P99Ms: Math.Round(p99, 1),
            MaxMs: Math.Round(max, 1),
            GeneratedAt: DateTime.UtcNow
        );
    }

    /// <summary>
    /// nearest-rank 百分位计算 (与 PerfMetrics.Percentile 一致)
    /// </summary>
    private static double Percentile(double[] sortedAsc, double p)
    {
        if (sortedAsc.Length == 0) return 0;
        var rank = (int)Math.Ceiling(p * sortedAsc.Length);
        if (rank < 1) rank = 1;
        if (rank > sortedAsc.Length) rank = sortedAsc.Length;
        return sortedAsc[rank - 1];
    }
}

/// <summary>
/// 搜索样本 (单次 SearchAsync 调用)
/// </summary>
public record MeiliSearchSample(DateTime AtUtc, MeiliSearchOutcome Outcome, double ElapsedMs);

/// <summary>
/// 搜索结果分类
/// </summary>
public enum MeiliSearchOutcome
{
    /// <summary>Meili 主路径成功 (反映 Meili 真实性能)</summary>
    PrimarySuccess,
    /// <summary>Meili 失败降级到 PG 兜底 (反映 Meili 不可用频率)</summary>
    Fallback,
    /// <summary>Meili 异常但未降级 (理论上不会出现, ResilientSearchProvider 都会兜底, 保留用于未来扩展)</summary>
    PrimaryError
}

/// <summary>
/// Meili 主路径性能快照
/// </summary>
public record MeiliSearchSnapshot(
    int SampleCount,
    long PrimarySuccessCount,
    long FallbackCount,
    long PrimaryErrorCount,
    long TotalSearchCount,
    /// <summary>降级率 = FallbackCount / TotalSearchCount * 100 (反映 Meili 不可用频率)</summary>
    double FallbackRate,
    /// <summary>主路径异常率 = PrimaryErrorCount / TotalSearchCount * 100</summary>
    double PrimaryErrorRate,
    /// <summary>Meili 主路径 P50 耗时 (ms, 仅 PrimarySuccess 样本)</summary>
    double P50Ms,
    /// <summary>Meili 主路径 P95 耗时 (ms)</summary>
    double P95Ms,
    /// <summary>Meili 主路径 P99 耗时 (ms)</summary>
    double P99Ms,
    /// <summary>所有样本最大耗时 (ms, 含 fallback)</summary>
    double MaxMs,
    DateTime GeneratedAt
);
