using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using SakuraFilter.Api.Extensions;
using SakuraFilter.Core.Extensions;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// 公开搜索页 8 字段 typeahead 候选项服务
/// WHY: 演示场景下用户手动输入 OEM/机型/发动机等字段非常困难 (百万级数据),
///      提供 distinct ILIKE 候选下拉, 2 字符起查避免全表扫描, 限 20 条
/// 设计:
///   - 字段映射到 3 张表 (products/cross_references/machine_applications)
///   - 走 EscapeLikePattern + EF.Functions.ILike 三参重载 (与 PublicSearchController 一致)
///   - AsNoTracking + Take(20) 性能优先
///   - q 长度 &lt; 2 返回空, 避免短前缀命中过多
///   - 每字段独立 Where 表达式 (避免 selector.Compile() 在表达式树中无法翻译)
///   - IMemoryCache 5 分钟 TTL: 同 (field, q_lower, limit) 命中缓存直接返回, 不查 PG
///
/// 2026-08-20 性能修复:
///   - typeahead_dict 实际 1465 万行 (oem-no3 占 1249 万), 全局 GIN 索引导致每字段查询都全表扫 → 12s。
///     024 迁移已改为每字段 partial GIN, 低/中基数字段毫秒级 (实测 &lt;0.2ms)。
///   - 基数守卫: oem-no3(1249万)/engine-type(199万) 近唯一, 下拉提示无意义且字典扫描极慢,
///     distinct 数超阈值 (Typeahead:MaxCardinality, 默认 100万) 直接短路返回空, 不查 PG、不写缓存。
/// </summary>
public class PublicTypeaheadService
{
    private readonly ProductDbContext _db;
    private readonly ILogger<PublicTypeaheadService> _logger;
    private readonly IMemoryCache _cache;

    /// <summary>基数守卫阈值: distinct 数超过此值的字段视为"近唯一", 下拉无意义, 短路返回空。
    /// 取自配置 Typeahead:MaxCardinality, 默认 100 万 (排除 oem-no3 1249万 / engine-type 199万, 保留 machine-model 158K)。</summary>
    private readonly int _maxCardinality;

    /// <summary>测试注入的基数种子 (非空时跳过 DB 加载, 直接用于守卫判定)</summary>
    private readonly Dictionary<string, int>? _seedCardinality;

    /// <summary>守卫日志去重: 每个字段只记一次</summary>
    private readonly ConcurrentDictionary<string, bool> _guardLogged = new();

    /// <summary>缓存键: 各字段 distinct 计数 (5~10 分钟 TTL, 避免每请求 GROUP BY 1465 万行)</summary>
    private const string CardinalityCacheKey = "typeahead:cardinality";
    private static readonly TimeSpan CardinalityCacheTtl = TimeSpan.FromMinutes(10);

    /// <summary>缓存 TTL (秒): 5 分钟, 平衡新鲜度与 PG 压力</summary>
    private const int CacheTtlSeconds = 300;

    // 字段名 → 中文说明 (日志用)
    private static readonly Dictionary<string, string> FieldNames = new()
    {
        ["oem-brand"]     = "OEM Brand (cross_references.oem_brand)",
        ["oem-no2"]       = "OEM 2 (products.oem_2)",
        ["oem-no3"]       = "OEM 3 (cross_references.oem_no_3)",
        ["machine-brand"] = "Machine Brand (machine_applications.machine_brand)",
        ["machine-model"] = "Machine Model (machine_applications.machine_model)",
        ["model-name"]    = "Model Name (machine_applications.model_name)",
        ["engine-brand"]  = "Engine Brand (machine_applications.engine_brand)",
        ["engine-type"]   = "Engine Type (machine_applications.engine_type)",
    };

    public PublicTypeaheadService(
        ProductDbContext db,
        ILogger<PublicTypeaheadService> logger,
        IMemoryCache cache,
        IConfiguration? config = null,
        Dictionary<string, int>? seedCardinality = null)
    {
        _db = db;
        _logger = logger;
        _cache = cache;
        _maxCardinality = config?.GetValue("Typeahead:MaxCardinality", 1_000_000) ?? 1_000_000;
        _seedCardinality = seedCardinality;
    }

    /// <summary>
    /// 8 字段统一入口: 按 field 名分发到对应 distinct 查询
    /// </summary>
    public async Task<List<string>> TypeaheadAsync(string field, string? q, int limit, CancellationToken ct)
    {
        if (!FieldNames.ContainsKey(field))
            return new List<string>();

        q = q?.Trim();
        if (string.IsNullOrEmpty(q) || q.Length < 2)
            return new List<string>();

        limit = Math.Clamp(limit, 1, 50);

        // 基数守卫: 近唯一高基数字段直接短路, 不查 PG、不写缓存 (下拉提示无意义 + 扫描极慢)
        var card = await GetCardinalityAsync(ct);
        if (card.TryGetValue(field, out var c) && c > _maxCardinality)
        {
            if (_guardLogged.TryAdd(field, true))
                _logger.LogInformation(
                    "typeahead 基数守卫: field={Field} distinct={Card} > 阈值 {Max}, 短路返回空 (下拉无意义)",
                    field, c, _maxCardinality);
            return new List<string>();
        }

        // 缓存键: 字段 + 小写查询 + 限数 (大小写不敏感场景)
        var cacheKey = $"typeahead:{field}:{q.ToLowerInvariant()}:{limit}";
        if (_cache.TryGetValue(cacheKey, out List<string>? cached) && cached is not null)
        {
            return cached;
        }

        var pattern = $"%{q.EscapeLikePattern()}%";
        // 2026-08-20 typeahead 性能修复: 三参 ILike (带 ESCAPE) 会让 PG 放弃 GIN trgm 索引
        // (trgm 索引仅支持无 ESCAPE 的 LIKE/ILIKE), 15.5M 行 machine_applications 上退化成
        // Index Only Scan + 过滤数百万行 → 3-5s。q 不含需转义字符时用两参 (走 trgm, 毫秒级);
        // 含 \ % _ 时才保留 ESCAPE (安全兜底, 该场景极少)。
        var needsEscape = q.IndexOfAny(new[] { '\\', '%', '_' }) >= 0;

        try
        {
            var result = await QueryAsync(field, pattern, needsEscape ? "\\" : null, limit, ct);
            // V24-F85: 用 SetWithSize 替代手写 MemoryCacheEntryOptions (5 分钟 TTL + Size=1 配合 SizeLimit=10000)
            _cache.SetWithSize(cacheKey, result, TimeSpan.FromSeconds(CacheTtlSeconds));
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "typeahead field={Field} q={Q} failed", field, q);
            return new List<string>();
        }
    }

    /// <summary>
    /// 启动预热: 加载各字段基数到缓存, 避免首个请求承担 1465 万行 GROUP BY (~820ms)。
    /// 失败仅告警 (首个请求会惰性加载, 不致命)。
    /// </summary>
    public async Task WarmupAsync()
    {
        try
        {
            var card = await GetCardinalityAsync(CancellationToken.None);
            _logger.LogInformation("typeahead 基数预热完成: 共 {Count} 个字段", card.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "typeahead 基数预热失败, 首个请求将惰性加载");
        }
    }

    /// <summary>
    /// 加载各字段 distinct 计数 (用于基数守卫)。
    /// seed 非空 (测试) → 直接返回; 否则从 typeahead_dict GROUP BY 加载并缓存 10 分钟。
    /// 加载失败 → 返回空 (放行, 不守卫), 避免计数异常禁用全部 typeahead。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, int>> GetCardinalityAsync(CancellationToken ct)
    {
        if (_seedCardinality is not null)
            return _seedCardinality;

        if (_cache.TryGetValue(CardinalityCacheKey, out Dictionary<string, int>? cached) && cached is not null)
            return cached;

        try
        {
            var counts = await _db.TypeaheadDict.AsNoTracking()
                .GroupBy(x => x.Field)
                .Select(g => new { Field = g.Key, Cnt = g.Count() })
                .ToDictionaryAsync(g => g.Field, g => g.Cnt, ct);
            _cache.SetWithSize(CardinalityCacheKey, counts, CardinalityCacheTtl);
            return counts;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "typeahead 基数统计失败, 本次放行 (不守卫)");
            return new Dictionary<string, int>();
        }
    }

    /// <summary>
    /// 实际查询: 仅查 typeahead_dict (全量 distinct 字典表, 由 023 迁移从明细表 GROUP BY 填充, 与明细表 distinct 一致)。
    /// WHY 不再 fallback 到明细表: 实测 machine_applications(1550万行) 的 trgm 索引对短模式 (如 %ka%) 产生
    ///   数百万假阳性候选, recheck 扫全表 → 9.9s; 而 dict 命中 0 时明细表 Distinct 结果同样为 0 (数据同源),
    ///   回退纯属徒增延迟。dict 即权威源, 命中 0 直接返回空。PK (field,value) 范围扫描, 中基数字段毫秒级,
    ///   oem-no3/engine-type 已由服务层基数守卫短路, 不会到这。
    /// escape == null → 两参 ILike (trgm 可用); 非 null → 三参带 ESCAPE (特殊字符安全路径)。
    /// </summary>
    private async Task<List<string>> QueryAsync(string field, string pattern, string? escape, int limit, CancellationToken ct)
    {
        return await _db.TypeaheadDict.AsNoTracking()
            .Where(x => x.Field == field && (escape == null
                ? EF.Functions.ILike(x.Value, pattern)
                : EF.Functions.ILike(x.Value, pattern, escape)))
            .OrderBy(x => x.Value)
            .Take(limit)
            .Select(x => x.Value)
            .ToListAsync(ct);
    }
}
