using FluentAssertions;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Search;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// EnrichmentCache 单元测试 (2026-08 富化缓存性能优化)
/// 覆盖: 写入读取 / 缺失 / 主动移除 / TTL 过期
/// </summary>
public class EnrichmentCacheTests
{
    [Fact]
    public void SetThenGet_ReturnsSameLists()
    {
        var cache = new EnrichmentCache();
        var oem = new List<AggregateOemItem>
        {
            new("BOSCH", "F000000001", "F000000001", 1, null, true, null)
        };
        var machine = new List<AggregateMachineItem>
        {
            new("CAT", "320D", "construction", "CAT")
        };

        cache.Set("MR1", oem, machine);

        cache.TryGet("MR1", out var value).Should().BeTrue();
        value!.OemList.Should().BeEquivalentTo(oem);
        value.MachineList.Should().BeEquivalentTo(machine);
    }

    [Fact]
    public void MissingKey_ReturnsFalse()
    {
        var cache = new EnrichmentCache();

        cache.TryGet("nope", out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void Remove_InvalidatesEntry()
    {
        var cache = new EnrichmentCache();
        cache.Set("MR1", new List<AggregateOemItem>(), new List<AggregateMachineItem>());

        cache.Remove("MR1");

        cache.TryGet("MR1", out _).Should().BeFalse();
    }

    [Fact]
    public void ExpiredEntry_ReturnsFalse()
    {
        // 构造 1ms TTL: 过期的条目 TryGet 应返回 false (触发 PG 兜底路径)
        var cache = new EnrichmentCache(TimeSpan.FromMilliseconds(1));
        cache.Set("MR1", new List<AggregateOemItem>(), new List<AggregateMachineItem>());

        Thread.Sleep(20);

        cache.TryGet("MR1", out _).Should().BeFalse();
    }
}
