using FluentAssertions;
using SakuraFilter.Search;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// SearchResponseCache 单元测试 (2026-08 搜索响应缓存性能优化)
/// 覆盖: 写入读取 / 缺失 / TTL 过期
/// </summary>
public class SearchResponseCacheTests
{
    [Fact]
    public void SetThenGet_ReturnsValue()
    {
        var cache = new SearchResponseCache();
        var value = new object();

        cache.Set("srch:key1", value);

        cache.TryGet<object>("srch:key1", out var got).Should().BeTrue();
        got.Should().BeSameAs(value);
    }

    [Fact]
    public void MissingKey_ReturnsFalse()
    {
        var cache = new SearchResponseCache();

        cache.TryGet<object>("srch:nope", out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void ExpiredEntry_ReturnsFalse()
    {
        var cache = new SearchResponseCache(TimeSpan.FromMilliseconds(1));
        cache.Set("srch:key", new object());

        Thread.Sleep(20);

        cache.TryGet<object>("srch:key", out _).Should().BeFalse();
    }

    [Fact]
    public void SameKey_DifferentValue_Overwrites()
    {
        var cache = new SearchResponseCache();
        cache.Set("srch:key", new object());

        var newer = new object();
        cache.Set("srch:key", newer);

        cache.TryGet<object>("srch:key", out var got).Should().BeTrue();
        got.Should().BeSameAs(newer);
    }
}
