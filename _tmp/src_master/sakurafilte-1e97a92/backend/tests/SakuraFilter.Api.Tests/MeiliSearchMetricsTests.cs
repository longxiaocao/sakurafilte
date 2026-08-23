using FluentAssertions;
using SakuraFilter.Search;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// v30-20: MeiliSearchMetrics 单元测试
///
/// 测试目标 (覆盖: Meili 主路径性能指标采集):
///   - Record + GetSnapshot: 基本记录 + 快照计算
///   - PrimarySuccess 样本独立计算 P50/P95/P99 (Fallback 不混入百分位)
///   - FallbackRate = FallbackCount / TotalSearchCount * 100
///   - ring buffer 容量限制 (1000 条)
///   - 空快照兜底 (P99=0, FallbackRate=0)
///   - PrimaryError 计数 (虽然 ResilientSearchProvider 当前不调, 保留单测覆盖未来扩展)
///
/// WHY 单测: P99 计算错误会导致告警误报或漏报, FallbackRate 错误会掩盖 Meili 不可用问题
/// </summary>
public class MeiliSearchMetricsTests
{
    // ===== 基本记录 + 快照 =====

    [Fact]
    public void GetSnapshot_Empty_Returns_Zeros()
    {
        // 覆盖: 无样本时返回全 0 快照
        var metrics = new MeiliSearchMetrics();

        var snap = metrics.GetSnapshot();

        snap.SampleCount.Should().Be(0);
        snap.PrimarySuccessCount.Should().Be(0);
        snap.FallbackCount.Should().Be(0);
        snap.PrimaryErrorCount.Should().Be(0);
        snap.TotalSearchCount.Should().Be(0);
        snap.FallbackRate.Should().Be(0);
        snap.PrimaryErrorRate.Should().Be(0);
        snap.P50Ms.Should().Be(0);
        snap.P95Ms.Should().Be(0);
        snap.P99Ms.Should().Be(0);
        snap.MaxMs.Should().Be(0);
    }

    [Fact]
    public void Record_PrimarySuccess_Increments_Counter()
    {
        // 覆盖: PrimarySuccess 计数 + 样本数
        var metrics = new MeiliSearchMetrics();

        metrics.Record(MeiliSearchOutcome.PrimarySuccess, 100);
        metrics.Record(MeiliSearchOutcome.PrimarySuccess, 200);

        var snap = metrics.GetSnapshot();
        snap.PrimarySuccessCount.Should().Be(2);
        snap.SampleCount.Should().Be(2);
        snap.TotalSearchCount.Should().Be(2);
        snap.FallbackRate.Should().Be(0);
    }

    [Fact]
    public void Record_Fallback_Increments_Counter()
    {
        // 覆盖: Fallback 计数 + FallbackRate 计算
        var metrics = new MeiliSearchMetrics();

        metrics.Record(MeiliSearchOutcome.PrimarySuccess, 100);
        metrics.Record(MeiliSearchOutcome.Fallback, 500);
        metrics.Record(MeiliSearchOutcome.Fallback, 600);

        var snap = metrics.GetSnapshot();
        snap.PrimarySuccessCount.Should().Be(1);
        snap.FallbackCount.Should().Be(2);
        snap.TotalSearchCount.Should().Be(3);
        snap.FallbackRate.Should().Be(Math.Round(2 * 100.0 / 3, 2));  // 66.67
    }

    [Fact]
    public void Record_PrimaryError_Increments_Counter()
    {
        // 覆盖: PrimaryError 计数 (ResilientSearchProvider 当前不调, 但保留单测)
        var metrics = new MeiliSearchMetrics();

        metrics.Record(MeiliSearchOutcome.PrimarySuccess, 100);
        metrics.Record(MeiliSearchOutcome.PrimaryError, 0);

        var snap = metrics.GetSnapshot();
        snap.PrimarySuccessCount.Should().Be(1);
        snap.PrimaryErrorCount.Should().Be(1);
        snap.TotalSearchCount.Should().Be(2);
        snap.PrimaryErrorRate.Should().Be(50.0);
    }

    // ===== P99 百分位计算 =====

    [Fact]
    public void GetSnapshot_P99_Single_PrimarySuccess_Returns_That_Value()
    {
        // 覆盖: 单样本时 P50=P95=P99=该样本
        var metrics = new MeiliSearchMetrics();

        metrics.Record(MeiliSearchOutcome.PrimarySuccess, 42.5);

        var snap = metrics.GetSnapshot();
        snap.P50Ms.Should().Be(42.5);
        snap.P95Ms.Should().Be(42.5);
        snap.P99Ms.Should().Be(42.5);
    }

    [Fact]
    public void GetSnapshot_P99_Excludes_Fallback_Samples()
    {
        // 覆盖: P99 仅基于 PrimarySuccess 样本计算, Fallback 样本不混入
        //   WHY 重要: 混合计算会掩盖 Meili 真实问题
        //     场景: 100 个 PrimarySuccess 都 100ms (Meili 健康),
        //          但有 50 个 Fallback 1000ms (Meili 降级到 PG 慢路径)
        //          若混合计算 P99=1000ms 误报 Meili 慢, 实际 Meili P99=100ms
        var metrics = new MeiliSearchMetrics();

        for (int i = 0; i < 100; i++)
        {
            metrics.Record(MeiliSearchOutcome.PrimarySuccess, 100);
        }
        for (int i = 0; i < 50; i++)
        {
            metrics.Record(MeiliSearchOutcome.Fallback, 1000);
        }

        var snap = metrics.GetSnapshot();
        snap.PrimarySuccessCount.Should().Be(100);
        snap.FallbackCount.Should().Be(50);
        snap.TotalSearchCount.Should().Be(150);
        snap.FallbackRate.Should().Be(Math.Round(50 * 100.0 / 150, 2));  // 33.33
        // P99 应仅基于 100 个 PrimarySuccess (都 100ms), 不应包含 Fallback 1000ms
        snap.P99Ms.Should().Be(100);
        snap.P95Ms.Should().Be(100);
        snap.P50Ms.Should().Be(100);
        // MaxMs 包含所有样本 (含 fallback), 用于发现极端慢请求
        snap.MaxMs.Should().Be(1000);
    }

    [Fact]
    public void GetSnapshot_P99_Nearest_Rank_100_Samples()
    {
        // 覆盖: nearest-rank 百分位算法 (与 PerfMetrics 一致)
        //   场景: 100 个 PrimarySuccess, 延迟 1-100ms (升序)
        //   P50: rank = ceil(0.50 * 100) = 50 → sorted[49] = 50
        //   P95: rank = ceil(0.95 * 100) = 95 → sorted[94] = 95
        //   P99: rank = ceil(0.99 * 100) = 99 → sorted[98] = 99
        var metrics = new MeiliSearchMetrics();

        for (int i = 1; i <= 100; i++)
        {
            metrics.Record(MeiliSearchOutcome.PrimarySuccess, i);
        }

        var snap = metrics.GetSnapshot();
        snap.P50Ms.Should().Be(50);
        snap.P95Ms.Should().Be(95);
        snap.P99Ms.Should().Be(99);
        snap.MaxMs.Should().Be(100);
    }

    [Fact]
    public void GetSnapshot_P99_With_Mixed_Outcomes()
    {
        // 覆盖: 混合 PrimarySuccess + Fallback + PrimaryError 时, P99 仅基于 PrimarySuccess
        var metrics = new MeiliSearchMetrics();

        // 10 个 PrimarySuccess: 50, 60, 70, 80, 90, 100, 110, 120, 130, 140
        var primaryLatencies = new[] { 50.0, 60, 70, 80, 90, 100, 110, 120, 130, 140 };
        foreach (var lat in primaryLatencies)
        {
            metrics.Record(MeiliSearchOutcome.PrimarySuccess, lat);
        }
        // 5 个 Fallback: 500, 600, 700, 800, 900 (应不参与 P99 计算)
        foreach (var lat in new[] { 500.0, 600, 700, 800, 900 })
        {
            metrics.Record(MeiliSearchOutcome.Fallback, lat);
        }
        // 2 个 PrimaryError: 0 (应不参与 P99 计算)
        metrics.Record(MeiliSearchOutcome.PrimaryError, 0);
        metrics.Record(MeiliSearchOutcome.PrimaryError, 0);

        var snap = metrics.GetSnapshot();
        snap.SampleCount.Should().Be(17);
        snap.PrimarySuccessCount.Should().Be(10);
        snap.FallbackCount.Should().Be(5);
        snap.PrimaryErrorCount.Should().Be(2);
        snap.TotalSearchCount.Should().Be(17);
        snap.FallbackRate.Should().Be(Math.Round(5 * 100.0 / 17, 2));  // 29.41
        snap.PrimaryErrorRate.Should().Be(Math.Round(2 * 100.0 / 17, 2));  // 11.76

        // P99: 10 个 PrimarySuccess 升序 [50..140], rank = ceil(0.99 * 10) = 10 → sorted[9] = 140
        snap.P99Ms.Should().Be(140);
        // P95: rank = ceil(0.95 * 10) = 10 → sorted[9] = 140
        snap.P95Ms.Should().Be(140);
        // P50: rank = ceil(0.50 * 10) = 5 → sorted[4] = 90
        snap.P50Ms.Should().Be(90);
        // MaxMs 包含所有样本 (含 fallback), 用于发现极端慢请求
        snap.MaxMs.Should().Be(900);
    }

    // ===== ring buffer 容量限制 =====

    [Fact]
    public void Record_Over_Capacity_Drops_Oldest()
    {
        // 覆盖: 超过 Capacity (1000) 时丢弃最旧样本, 保持容量
        var metrics = new MeiliSearchMetrics();

        // 写入 1050 个样本 (超过 1000 容量)
        for (int i = 1; i <= 1050; i++)
        {
            metrics.Record(MeiliSearchOutcome.PrimarySuccess, i);
        }

        var snap = metrics.GetSnapshot();
        snap.SampleCount.Should().Be(1000);  // 不超过容量
        snap.PrimarySuccessCount.Should().Be(1050);  // counter 不受限
        // 最旧 50 个样本被丢弃, ring buffer 内保留 51-1050
        //   P99: rank = ceil(0.99 * 1000) = 990 → sorted[989] = 51 + 989 = 1040
        snap.P99Ms.Should().Be(1040);
        snap.MaxMs.Should().Be(1050);  // 最新写入的最大值保留
    }

    [Fact]
    public void Record_At_Exactly_Capacity_Does_Not_Drop()
    {
        // 覆盖: 恰好 1000 个样本时不丢弃
        var metrics = new MeiliSearchMetrics();

        for (int i = 1; i <= 1000; i++)
        {
            metrics.Record(MeiliSearchOutcome.PrimarySuccess, i);
        }

        var snap = metrics.GetSnapshot();
        snap.SampleCount.Should().Be(1000);
        snap.P99Ms.Should().Be(990);  // rank=990 → sorted[989] = 990
    }

    // ===== FallbackRate 边界场景 =====

    [Fact]
    public void GetSnapshot_All_Fallback_FallbackRate_100()
    {
        // 覆盖: 全部 Fallback 时 FallbackRate=100 (Meili 完全不可用)
        var metrics = new MeiliSearchMetrics();

        for (int i = 0; i < 10; i++)
        {
            metrics.Record(MeiliSearchOutcome.Fallback, 500);
        }

        var snap = metrics.GetSnapshot();
        snap.FallbackRate.Should().Be(100.0);
        snap.PrimarySuccessCount.Should().Be(0);
        // 无 PrimarySuccess 样本, P50/P95/P99 都 0
        snap.P99Ms.Should().Be(0);
        snap.MaxMs.Should().Be(500);  // MaxMs 仍记录 fallback 最大值
    }

    [Fact]
    public void GetSnapshot_Mixed_FallbackRate_Rounded()
    {
        // 覆盖: FallbackRate 四舍五入到 2 位小数 (Math.Round(..., 2))
        var metrics = new MeiliSearchMetrics();

        // 3 PrimarySuccess + 1 Fallback = 1/4 = 25.00%
        metrics.Record(MeiliSearchOutcome.PrimarySuccess, 100);
        metrics.Record(MeiliSearchOutcome.PrimarySuccess, 100);
        metrics.Record(MeiliSearchOutcome.PrimarySuccess, 100);
        metrics.Record(MeiliSearchOutcome.Fallback, 500);

        var snap = metrics.GetSnapshot();
        snap.FallbackRate.Should().Be(25.0);
    }

    // ===== 百分位边界 (少量样本) =====

    [Fact]
    public void GetSnapshot_P99_Two_Samples_Returns_Max()
    {
        // 覆盖: 2 个样本时 P50=P95=P99=较大值 (nearest-rank 都返回 rank=2)
        var metrics = new MeiliSearchMetrics();

        metrics.Record(MeiliSearchOutcome.PrimarySuccess, 100);
        metrics.Record(MeiliSearchOutcome.PrimarySuccess, 200);

        var snap = metrics.GetSnapshot();
        // rank(0.50, 2) = ceil(1.0) = 1 → sorted[0] = 100
        snap.P50Ms.Should().Be(100);
        // rank(0.95, 2) = ceil(1.9) = 2 → sorted[1] = 200
        snap.P95Ms.Should().Be(200);
        // rank(0.99, 2) = ceil(1.98) = 2 → sorted[1] = 200
        snap.P99Ms.Should().Be(200);
    }

    [Fact]
    public void GetSnapshot_P99_Only_Fallback_Returns_Zero_Percentiles()
    {
        // 覆盖: 仅 Fallback 样本 (无 PrimarySuccess) 时, 百分位返回 0
        //   WHY: 百分位仅基于 PrimarySuccess 计算, 无 PrimarySuccess 时无法计算
        var metrics = new MeiliSearchMetrics();

        metrics.Record(MeiliSearchOutcome.Fallback, 500);
        metrics.Record(MeiliSearchOutcome.Fallback, 600);

        var snap = metrics.GetSnapshot();
        snap.P50Ms.Should().Be(0);
        snap.P95Ms.Should().Be(0);
        snap.P99Ms.Should().Be(0);
        snap.MaxMs.Should().Be(600);  // MaxMs 仍记录所有样本最大值
    }

    // ===== 线程安全 (smoke test) =====

    [Fact]
    public async Task Record_Concurrent_Writes_Are_Thread_Safe()
    {
        // 覆盖: 多线程并发 Record 不抛异常, 最终样本数 = 写入数 (受 ring buffer 限制)
        //   WHY: ConcurrentQueue + Interlocked.Increment 保证线程安全
        var metrics = new MeiliSearchMetrics();

        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                metrics.Record(MeiliSearchOutcome.PrimarySuccess, i);
            }
        })).ToArray();
        await Task.WhenAll(tasks);

        var snap = metrics.GetSnapshot();
        snap.PrimarySuccessCount.Should().Be(1000);  // 10 * 100
        snap.SampleCount.Should().Be(1000);  // 不超过容量
    }
}
