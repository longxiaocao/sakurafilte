using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using SakuraFilter.Api.Extensions;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// V24-F85 (spec 26.17.3 P2-4): MemoryCacheExtensions.SetWithSize 单元测试
///
/// 覆盖:
///   - 默认 size=1 写入成功 (SizeLimit 已设置场景)
///   - 显式 size 参数写入成功
///   - 与原生 cache.Set 行为等价 (Get 返回相同值)
///
/// 关联 spec: 26.17.3 P2-4 / MemoryCacheExtensions.cs
/// </summary>
public class MemoryCacheExtensionsTests
{
    /// <summary>
    /// 默认 size=1 时, 写入 SizeLimit=10000 的 MemoryCache 应成功 (不抛 InvalidOperationException)
    /// 覆盖: V24-F71/F75 历史 bug 不再复发
    /// </summary>
    [Fact]
    public void SetWithSize_DefaultSize_WritesToCacheWithSizeLimit()
    {
        // Arrange: SizeLimit=10000 (与 ServiceCollectionExtensions L387 一致)
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });

        // Act
        cache.SetWithSize("key1", "value1", TimeSpan.FromMinutes(5));

        // Assert
        cache.TryGetValue("key1", out string? v).Should().BeTrue();
        v.Should().Be("value1");
    }

    /// <summary>
    /// 显式 size 参数 (大对象场景, 如 sitemap XML) 写入应成功
    /// 覆盖: SitemapEndpoints.cs L203 用法 (size=2000)
    /// </summary>
    [Fact]
    public void SetWithSize_ExplicitSize_WritesLargeEntry()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        var largeXml = new string('x', 100_000);  // 模拟 100KB sitemap

        // Act
        cache.SetWithSize("sitemap:1", (xml: largeXml, ts: DateTime.UtcNow), TimeSpan.FromSeconds(60), size: 2000);

        // Assert
        cache.TryGetValue("sitemap:1", out (string xml, DateTime ts) v).Should().BeTrue();
        v.xml.Should().Be(largeXml);
    }

    /// <summary>
    /// 写入复杂类型 (List) 也应成功
    /// 覆盖: PublicTypeaheadService / AdminXrefReorderEndpoints 用法
    /// </summary>
    [Fact]
    public void SetWithSize_ComplexType_WritesSuccessfully()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        var list = new List<string> { "Toyota", "Honda", "Bosch" };

        // Act
        cache.SetWithSize("typeahead:brand:to", list, TimeSpan.FromSeconds(300));

        // Assert
        cache.TryGetValue("typeahead:brand:to", out List<string>? v).Should().BeTrue();
        v.Should().BeEquivalentTo(list);
    }

    /// <summary>
    /// 过期后 Get 应返回 false (验证 AbsoluteExpirationRelativeToNow 正确传递)
    /// 注: 用 1ms TTL + 短延迟验证, 不依赖系统时钟
    /// </summary>
    [Fact]
    public async Task SetWithSize_AfterTtl_ExpiresAndReturnsFalse()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });

        // Act: TTL=1ms
        cache.SetWithSize("ephemeral", "value", TimeSpan.FromMilliseconds(1));

        // Assert: 等待过期 (10ms 足够 1ms TTL 过期)
        await Task.Delay(10);
        cache.TryGetValue("ephemeral", out _).Should().BeFalse("1ms TTL 应已过期");
    }
}
