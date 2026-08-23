using Microsoft.Extensions.Caching.Memory;
using SakuraFilter.Core.DTOs;

namespace SakuraFilter.Search;

/// <summary>
/// 富化结果缓存 (EnrichFromPgAsync 专用, 2026-08 性能优化)
///
/// WHY 独立实例而非全局 IMemoryCache: 全局 SizeLimit=10000 已被 typeahead/字典/sitemap 共享,
///   聚合搜索每请求最多 100 个 mr1 的富化条目会瞬间挤爆共享预算, 反噬其他缓存命中率。
///
/// 设计:
///   - 按 mr1 缓存解析后的 oem_list/machine_list 对象列表 (免二次 JSON 解析)
///   - TTL 默认 3 分钟: 目录数据(oem/机型)低频变化; 搜索热数据由流量自然刷新, 冷数据过期淘汰
///   - 一致性说明: 后台编辑/详情视图始终直读 PG, 不受本缓存影响; 公开搜索最多滞后一个 TTL
///   - 扩展点: Remove(mr1) 已预留, 供后续在产品变更点 (search_index_pending 写入处) 做即时失效
///   - 降级: 缓存 miss → 查 PG (原逻辑), 缓存永不作为正确性依赖
///   - 容量: SizeLimit=50000 条目 + MemoryCache 周期压缩 (超限按新旧程度淘汰, 只保热点)
/// </summary>
public sealed class EnrichmentCache
{
    private const int SizeLimit = 50_000;

    private readonly TimeSpan _ttl;
    private readonly MemoryCache _cache;

    public EnrichmentCache(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromMinutes(3);
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = SizeLimit });
    }

    /// <summary>按 mr1 取缓存 (过期/缺失返回 false)。缓存内 List 视为只读, 调用方不得修改。</summary>
    public bool TryGet(string mr1, out EnrichedLists? value)
        => _cache.TryGetValue(CacheKey(mr1), out value);

    /// <summary>写入 mr1 的富化结果</summary>
    public void Set(string mr1, List<AggregateOemItem> oemList, List<AggregateMachineItem> machineList)
        => _cache.Set(CacheKey(mr1), new EnrichedLists(oemList, machineList),
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _ttl,
                Size = 1   // SizeLimit 已设置, 每个条目必须显式声明 Size
            });

    /// <summary>产品数据变更时清除 (预留接入点; 当前版本仅 TTL 兜底)</summary>
    public void Remove(string mr1) => _cache.Remove(CacheKey(mr1));

    private static string CacheKey(string mr1) => "enrich:" + mr1;
}

/// <summary>富化缓存条目 (oem_list + machine_list, 解析后对象; 内部 List 视为只读)</summary>
public sealed record EnrichedLists(
    List<AggregateOemItem> OemList,
    List<AggregateMachineItem> MachineList);
