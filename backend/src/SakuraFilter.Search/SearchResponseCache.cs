using Microsoft.Extensions.Caching.Memory;

namespace SakuraFilter.Search;

/// <summary>
/// 搜索结果响应缓存 (SearchAsync / AggregateSearchAsync 专用, 2026-08 性能优化)
///
/// WHY 必要: 6 核共享机器上 Meili 搜索 CPU 饱和 (吞吐 ~330 req/s) 是高并发延迟上限,
///   高亮收窄/关 ranking/并发槽位调优后仍 c50 P95≈265ms / c100 P95≈500ms (>200ms 红线)。
///   目录数据(oem/机型)低频变化 → 热点查询 30s 缓存命中, 直接跳过 Meili+富化+Sanitize, 延迟降至 ~10ms。
///
/// 设计:
///   - 独立 MemoryCache 单例 (同 EnrichmentCache 模式, 不与全局 10000 槽竞争)
///   - 键 = 请求签名 (q/type/尺寸/螺纹/includeDiscontinued/page/pageSize 全字段)
///   - TTL 默认 30s: 搜索结果最多滞后 30s; 后台编辑/详情视图直读 PG 不受影响
///   - 缓存值为 Sanitize 后最终结果 (XSS 安全), 调用方只读
///   - 降级: miss → 原逻辑 (Meili+富化+Sanitize), 缓存永不作为正确性依赖
/// </summary>
public sealed class SearchResponseCache
{
    private const int SizeLimit = 10_000;

    private readonly TimeSpan _ttl;
    private readonly MemoryCache _cache;

    public SearchResponseCache(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromSeconds(30);
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = SizeLimit });
    }

    /// <summary>取缓存 (过期/缺失返回 false)。缓存对象视为只读。</summary>
    public bool TryGet<T>(string key, out T? value) where T : class
        => _cache.TryGetValue(key, out value);

    /// <summary>写入缓存 (SizeLimit 已设置, 必须显式 Size)</summary>
    public void Set<T>(string key, T value) where T : class
        => _cache.Set(key, value,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _ttl,
                Size = 1
            });
}
