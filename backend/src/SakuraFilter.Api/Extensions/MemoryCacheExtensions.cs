using Microsoft.Extensions.Caching.Memory;

namespace SakuraFilter.Api.Extensions;

/// <summary>
/// V24-F85 (spec 26.17.3 P2-4): IMemoryCache 扩展方法, 封装 Size 显式声明
///
/// WHY 必要:
///   - ServiceCollectionExtensions L387 设置了 options.SizeLimit=10000
///   - Microsoft.Extensions.Caching.Memory 要求: SizeLimit 设置后, 每个 cache.Set 必须显式指定 Size
///   - 否则抛 InvalidOperationException: "Cache entry must specify a value for Size when SizeLimit is set"
///
/// 历史踩坑 (2 次):
///   - V24-F71 (PublicTypeaheadService): 遗漏 Size 导致 500
///   - V24-F75 (AdminProductImageService.GetNamingFieldAsync): 遗漏 Size 导致 500
///
/// 设计:
///   - 默认 size=1 (适用于普通小对象, 如配置值、typeahead 列表)
///   - 大对象 (如 sitemap XML) 调用方显式传 size
///   - 仅封装 AbsoluteExpirationRelativeToNow + Size 两个最常用字段
///   - 复杂场景 (SlidingExpiration, Priority, PostEvictionCallbacks) 仍用原生 MemoryCacheEntryOptions
/// </summary>
public static class MemoryCacheExtensions
{
    /// <summary>
    /// 设置缓存条目 (绝对过期 + 显式 Size, 默认 size=1)
    /// </summary>
    /// <typeparam name="T">缓存值类型</typeparam>
    /// <param name="cache">IMemoryCache 实例</param>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="absoluteExpiration">绝对过期时间 (相对 now)</param>
    /// <param name="size">缓存条目大小 (默认 1, 配合 SizeLimit=10000)</param>
    public static void SetWithSize<T>(this IMemoryCache cache, object key, T value, TimeSpan absoluteExpiration, long size = 1)
    {
        cache.Set(key, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = absoluteExpiration,
            Size = size
        });
    }
}
