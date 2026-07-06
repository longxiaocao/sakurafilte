using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
///   - q 长度 < 2 返回空, 避免短前缀命中过多
///   - 每字段独立 Where 表达式 (避免 selector.Compile() 在表达式树中无法翻译)
///   - IMemoryCache 5 分钟 TTL: 同 (field, q_lower, limit) 命中缓存直接返回, 不查 PG
/// </summary>
public class PublicTypeaheadService
{
    private readonly ProductDbContext _db;
    private readonly ILogger<PublicTypeaheadService> _logger;
    private readonly IMemoryCache _cache;

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
        IMemoryCache cache)
    {
        _db = db;
        _logger = logger;
        _cache = cache;
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

        // 缓存键: 字段 + 小写查询 + 限数 (大小写不敏感场景)
        var cacheKey = $"typeahead:{field}:{q.ToLowerInvariant()}:{limit}";
        if (_cache.TryGetValue(cacheKey, out List<string>? cached) && cached is not null)
        {
            return cached;
        }

        var pattern = $"%{q.EscapeLikePattern()}%";
        var escape = "\\";

        try
        {
            var result = await QueryAsync(field, pattern, escape, limit, ct);
            // 写入缓存 (5 分钟 TTL, 即使同一用户连续输入同 q 也只查一次 PG)
            _cache.Set(cacheKey, result, TimeSpan.FromSeconds(CacheTtlSeconds));
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "typeahead field={Field} q={Q} failed", field, q);
            return new List<string>();
        }
    }

    /// <summary>
    /// 实际查询分发 (从 TypeaheadAsync 抽取, 保持缓存逻辑与查询逻辑分离)
    /// </summary>
    private async Task<List<string>> QueryAsync(string field, string pattern, string escape, int limit, CancellationToken ct)
    {
        return field switch
        {
            "oem-brand"     => await _db.CrossReferences.AsNoTracking()
                .Where(x => x.OemBrand != null && EF.Functions.ILike(x.OemBrand, pattern, escape))
                .Select(x => x.OemBrand!).Distinct().OrderBy(x => x).Take(limit).ToListAsync(ct),

            "oem-no2"       => await _db.Products.AsNoTracking()
                .Where(x => x.Oem2 != null && EF.Functions.ILike(x.Oem2, pattern, escape))
                .Select(x => x.Oem2!).Distinct().OrderBy(x => x).Take(limit).ToListAsync(ct),

            "oem-no3"       => await _db.CrossReferences.AsNoTracking()
                .Where(x => x.OemNo3 != null && EF.Functions.ILike(x.OemNo3, pattern, escape))
                .Select(x => x.OemNo3!).Distinct().OrderBy(x => x).Take(limit).ToListAsync(ct),

            "machine-brand" => await _db.MachineApplications.AsNoTracking()
                .Where(x => x.MachineBrand != null && EF.Functions.ILike(x.MachineBrand, pattern, escape))
                .Select(x => x.MachineBrand!).Distinct().OrderBy(x => x).Take(limit).ToListAsync(ct),

            "machine-model" => await _db.MachineApplications.AsNoTracking()
                .Where(x => x.MachineModel != null && EF.Functions.ILike(x.MachineModel, pattern, escape))
                .Select(x => x.MachineModel!).Distinct().OrderBy(x => x).Take(limit).ToListAsync(ct),

            "model-name"    => await _db.MachineApplications.AsNoTracking()
                .Where(x => x.ModelName != null && EF.Functions.ILike(x.ModelName, pattern, escape))
                .Select(x => x.ModelName!).Distinct().OrderBy(x => x).Take(limit).ToListAsync(ct),

            "engine-brand"  => await _db.MachineApplications.AsNoTracking()
                .Where(x => x.EngineBrand != null && EF.Functions.ILike(x.EngineBrand, pattern, escape))
                .Select(x => x.EngineBrand!).Distinct().OrderBy(x => x).Take(limit).ToListAsync(ct),

            "engine-type"   => await _db.MachineApplications.AsNoTracking()
                .Where(x => x.EngineType != null && EF.Functions.ILike(x.EngineType, pattern, escape))
                .Select(x => x.EngineType!).Distinct().OrderBy(x => x).Take(limit).ToListAsync(ct),

            _ => new List<string>()
        };
    }
}
