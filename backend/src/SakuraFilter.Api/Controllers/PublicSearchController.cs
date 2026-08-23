using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SakuraFilter.Api.Extensions;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Core.Extensions;
using SakuraFilter.Infrastructure.Data;
using SakuraFilter.Search;

namespace SakuraFilter.Api.Controllers;

/// <summary>
/// P3.2 (Task 10): 公开搜索端点 (无需 token, 走 "search" 限流分区)
/// 用途: 前台搜索页面的批量粘贴查询
/// 设计: 与 AdminProductService 共享 ProductDbContext, 走 AsNoTracking 性能最优
/// V2 Task 1.2: 新增 POST /aggregate 聚合搜索端点 (Meili 主 + PG 兜底)
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/search")]
public class PublicSearchController : ControllerBase
{
    private readonly ProductDbContext _db;
    private readonly ILogger<PublicSearchController> _logger;
    // V2 Task 1.2: 聚合搜索直接注入两个 provider (不走 Resilient 包装,需区分 provider 标识)
    private readonly MeiliSearchProvider _meili;
    private readonly PostgresSearchProvider _pg;
    // 改进 1.2: max_page_depth 配置缓存 (5 分钟, 避免每次请求查 DB)
    private readonly IMemoryCache _cache;

    public PublicSearchController(
        ProductDbContext db,
        ILogger<PublicSearchController> logger,
        MeiliSearchProvider meili,
        PostgresSearchProvider pg,
        IMemoryCache cache)
    {
        _db = db;
        _logger = logger;
        _meili = meili;
        _pg = pg;
        _cache = cache;
    }

    /// <summary>
    /// 批量 OEM 查询 (Excel 多行粘贴)
    /// 入参: oems (1-500 个字符串, 自动 trim + 去重)
    /// 返: 每个 OEM 一条结果
    ///   命中: {oem, hit=true, productId, oemBrand, productName1, oem2}
    ///   未命中: {oem, hit=false}
    /// 匹配字段: oem_2 (与前台搜索一致)
    /// </summary>
    /// <remarks>
    /// 示例请求:
    ///
    ///     POST /api/public/search/batch-oem
    ///     { "oems": ["P00050000", "11427622448", "C-204"] }
    ///
    /// 成功响应 (200):
    ///
    ///     {
    ///       "total": 3,
    ///       "hits": 2,
    ///       "miss": 1,
    ///       "results": [
    ///         { "oem": "P00050000", "hit": true, "productId": 12345, "oemBrand": "Mann", "productName1": "OIL FILTER", "oem2": "P00050000" },
    ///         { "oem": "11427622448", "hit": true, "productId": 67890, "oemBrand": "Bosch", "productName1": "AIR FILTER", "oem2": "11427622448" },
    ///         { "oem": "C-204", "hit": false }
    ///       ]
    ///     }
    /// </remarks>
    [HttpPost("batch-oem")]
    [ProducesResponseType(typeof(BatchOemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BatchOem(
        [FromBody] BatchOemRequest? req,
        CancellationToken ct)
    {
        if (req?.Oems is null || req.Oems.Count == 0)
            return BadRequest(new { error = "oems 不能为空" });
        if (req.Oems.Count > 500)
            return BadRequest(new { error = "oems 最多 500 个", given = req.Oems.Count });

        // 保留去重后的 OEM 列表 (trim + 过滤空白, 不破坏中英文/斜杠/引号)
        var distinctOems = req.Oems
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct()
            .ToList();
        if (distinctOems.Count == 0)
            return Ok(new BatchOemResponse(0, 0, 0, new List<BatchOemResult>()));

        // 🔧 fix(2026-08-23 走查实测): 批量 API 仅查 products.Oem2 一个字段 (旧实现),
        //   走查 U0000014 等真实 OEM 在 xrefs.oem_no_3 命中但批量 miss。
        //   对齐单条 GetByOemAsync (V2-251aaa5 拆段索引查询), 分 3 段走索引,
        //   优先级: xrefs.OemNo3 → products.OemNoDisplay → products.Oem2 (BitmapOr 走单列索引)。
        //   每段 ANY(oems) 走单列索引, 总耗时约 3× 段查询 + 1 详情查询, 500 OEM 实测 < 100ms。
        // 第 1 段: xrefs.oem_no_3 (用户最常贴的 OEM 主表, ix_xrefs_oem_no_3_trgm 索引)
        var xrefHits = await _db.CrossReferences.AsNoTracking()
            .Where(x => x.OemNo3 != null && distinctOems.Contains(x.OemNo3))
            .Select(x => new
            {
                x.OemNo3,
                x.ProductId,
                x.OemBrand,
                x.ProductName1,
                x.Oem2
            })
            .ToListAsync(ct);
        var xrefByOem = xrefHits
            .GroupBy(x => x.OemNo3!)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.ProductId).First());
        var remaining = distinctOems
            .Where(o => !xrefByOem.ContainsKey(o))
            .ToList();

        // 第 2 段: products.oem_no_display (主 OEM, 走单列索引)
        var productHits2 = new List<(string Oem, long Id, string? OemNoDisplay, string? Oem2, string? ProductName1)>();
        var remaining2 = new List<string>();
        if (remaining.Count > 0)
        {
            var pByDisplay = await _db.Products.AsNoTracking()
                .Where(p => p.IsPublished && !p.IsDiscontinued
                    && p.OemNoDisplay != null && remaining.Contains(p.OemNoDisplay))
                .Select(p => new { p.Id, p.OemNoDisplay, p.Oem2, p.ProductName1 })
                .ToListAsync(ct);
            var pDisplayMap = pByDisplay
                .GroupBy(x => x.OemNoDisplay!)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).First());
            foreach (var oem in remaining)
            {
                if (pDisplayMap.TryGetValue(oem, out var hit))
                {
                    productHits2.Add((oem, hit.Id, hit.OemNoDisplay, hit.Oem2, hit.ProductName1));
                }
                else
                {
                    remaining2.Add(oem);
                }
            }
        }

        // 第 3 段: products.oem_2 (BitmapOr 索引, 兜底)
        var productHits3 = new List<(string Oem, long Id, string? OemNoDisplay, string? Oem2, string? ProductName1)>();
        if (remaining2.Count > 0)
        {
            var pByOem2 = await _db.Products.AsNoTracking()
                .Where(p => p.IsPublished && !p.IsDiscontinued
                    && p.Oem2 != null && remaining2.Contains(p.Oem2))
                .Select(p => new { p.Id, p.OemNoDisplay, p.Oem2, p.ProductName1 })
                .ToListAsync(ct);
            var pOem2Map = pByOem2
                .GroupBy(x => x.Oem2!)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).First());
            foreach (var oem in remaining2)
            {
                if (pOem2Map.TryGetValue(oem, out var hit))
                {
                    productHits3.Add((oem, hit.Id, hit.OemNoDisplay, hit.Oem2, hit.ProductName1));
                }
            }
        }

        // 合并所有命中: xrefs 优先 (有 oem_brand), products 段无 brand (BrandMap 查)
        var allProductIds = xrefHits.Select(x => x.ProductId)
            .Concat(productHits2.Select(x => x.Id))
            .Concat(productHits3.Select(x => x.Id))
            .Distinct()
            .ToList();
        object? brandGroupsObj;
        if (allProductIds.Count == 0)
        {
            brandGroupsObj = null;
        }
        else
        {
            brandGroupsObj = await _db.CrossReferences.AsNoTracking()
                .Where(x => allProductIds.Contains(x.ProductId) && x.OemBrand != null)
                .GroupBy(x => x.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    Brands = g.Select(x => x.OemBrand!).Distinct().ToList()
                })
                .ToListAsync(ct);
        }
        var brandGroups = (brandGroupsObj as IEnumerable<dynamic>)?.ToList() ?? new List<dynamic>();
        var brandMap = brandGroups.ToDictionary(
            b => (long)b.ProductId,
            b => b.Brands.Count == 1 ? b.Brands[0] : string.Join(", ", b.Brands));

        // 按请求顺序产出 (3 段结果优先级合并: xrefs > oem_no_display > oem_2)
        var product2Map = productHits2.ToDictionary(x => x.Oem, x => x);
        var product3Map = productHits3.ToDictionary(x => x.Oem, x => x);
        var results = distinctOems.Select(oem =>
        {
            if (xrefByOem.TryGetValue(oem, out var x))
            {
                return new BatchOemResult(
                    Oem: oem,
                    Hit: true,
                    ProductId: x.ProductId,
                    OemBrand: x.OemBrand,
                    ProductName1: x.ProductName1,
                    Oem2: x.Oem2
                );
            }
            if (product2Map.TryGetValue(oem, out var p2))
            {
                return new BatchOemResult(
                    Oem: oem,
                    Hit: true,
                    ProductId: p2.Id,
                    OemBrand: brandMap.GetValueOrDefault(p2.Id),
                    ProductName1: p2.ProductName1,
                    Oem2: p2.Oem2 ?? oem
                );
            }
            if (product3Map.TryGetValue(oem, out var p3))
            {
                return new BatchOemResult(
                    Oem: oem,
                    Hit: true,
                    ProductId: p3.Id,
                    OemBrand: brandMap.GetValueOrDefault(p3.Id),
                    ProductName1: p3.ProductName1,
                    Oem2: p3.Oem2
                );
            }
            return new BatchOemResult(oem, Hit: false);
        }).ToList();

        var hitCount = results.Count(r => r.Hit);
        _logger.LogInformation("batch-oem: distinct={Total} hit={Hit} miss={Miss} (xref+display+oem2 三段)",
            distinctOems.Count, hitCount, distinctOems.Count - hitCount);

        return Ok(new BatchOemResponse(
            Total: distinctOems.Count,
            Hits: hitCount,
            Miss: distinctOems.Count - hitCount,
            Results: results
        ));
    }

    /// <summary>
    /// P3.4 (Task 11.5): 公开搜索页 8 字段多框模糊搜索
    /// URL: GET /api/public/search?oemBrand=...&amp;oemNo2=...&amp;oemNo3=...&amp;machineBrand=...&amp;machineModel=...&amp;modelName=...&amp;engineBrand=...&amp;engineType=...
    /// 规格 (新思路.xlsx R2/R8): 8 字段同时支持模糊搜索,任一字段命中即返回
    ///  - 8 字段全部 optional, 全部空 → 400
    ///  - 多字段 = AND 关系 (收窄范围)
    ///  - 全部走 P0.1 ILIKE ESCAPE (防止下划线/百分号被 PG 当通配符)
    ///  - xref 2 字段 (oemBrand + oemNo3) → 1 个 EXISTS 合并
    ///  - machine 5 字段 → 1 个 EXISTS 合并 (避免 5 次 EXISTS 嵌套扫描)
    ///  - 排除 is_discontinued=true
    ///  - 性能: 1M 数据 + 5M xref + 1M apps 预计 50-300ms
    /// </summary>
    [HttpGet("")]
    [ProducesResponseType(typeof(PublicEightResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EightField(
        [FromQuery(Name = "oemBrand")]    string? oemBrand,
        [FromQuery(Name = "oemNo2")]      string? oemNo2,
        [FromQuery(Name = "oemNo3")]      string? oemNo3,
        [FromQuery(Name = "machineBrand")] string? machineBrand,
        [FromQuery(Name = "machineModel")] string? machineModel,
        [FromQuery(Name = "modelName")]    string? modelName,
        [FromQuery(Name = "engineBrand")]  string? engineBrand,
        [FromQuery(Name = "engineType")]   string? engineType,
        [FromQuery(Name = "fuzzy")]        string? fuzzy,
        [FromQuery(Name = "page")]         int page = 1,
        [FromQuery(Name = "pageSize")]     int pageSize = 20,
        CancellationToken ct = default)
    {
        // 全部 trim + null 化 (空字符串也当作未填)
        oemBrand    = oemBrand?.Trim();
        oemNo2      = oemNo2?.Trim();
        oemNo3      = oemNo3?.Trim();
        machineBrand = machineBrand?.Trim();
        machineModel = machineModel?.Trim();
        modelName    = modelName?.Trim();
        engineBrand  = engineBrand?.Trim();
        engineType   = engineType?.Trim();
        fuzzy        = fuzzy?.Trim();

        // 🔧 fix(2026-08-23 走查): 融合搜索 fuzzy — 不区分 8 字段, 在全部字段 OR 匹配
        //   (用户"随便输入啥都自动查"), 任一命中即返回。与 8 字段 AND 互补。
        //   fuzzy 与 8 字段可同时使用 (fuzzy OR + 8 字段 AND 收窄), 全部空才 400。
        var hasField = !string.IsNullOrEmpty(oemBrand) || !string.IsNullOrEmpty(oemNo2)
            || !string.IsNullOrEmpty(oemNo3) || !string.IsNullOrEmpty(machineBrand)
            || !string.IsNullOrEmpty(machineModel) || !string.IsNullOrEmpty(modelName)
            || !string.IsNullOrEmpty(engineBrand) || !string.IsNullOrEmpty(engineType)
            || !string.IsNullOrEmpty(fuzzy);
        if (!hasField)
        {
            return BadRequest(new { error = "至少需要输入 1 个搜索字段" });
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 起手: active products
        var query = _db.Products.AsNoTracking().Where(p => p.IsPublished && !p.IsDiscontinued);

        // ===== fuzzy 融合搜索 (全部字段 OR, 任一命中即返回) =====
        //   WHY 放最前: 与 8 字段 AND 合并时先收集"宽匹配"候选再收窄
        //   覆盖字段: products 主表 (oem_no_display/oem_2/product_name_1/product_name_2/type)
        //     + xrefs (oem_brand/oem_no_3/oem_2) + machine_applications (machine_brand/machine_model/...)
        //   🔧 fix(2026-08-23 走查): 拆段查询 — 原单条 OR+2 个 EXISTS 在 1M 行 Seq Scan (实测 10s+ 超时)。
        //     拆成 3 段独立查询 (products/xrefs/machine), 每段走 trgm GIN 索引 (025 迁移),
        //     合并 product_id 集合再 IN 过滤 (走主键索引)。每段 LIMIT 5000 防超宽结果。
        if (!string.IsNullOrEmpty(fuzzy))
        {
            var f = fuzzy.EscapeLikePattern();
            // 段1: products 主表 (oem_2 已有 trgm; 025 补 oem_no_display/name1/name2/type)
            var ids1 = await _db.Products.AsNoTracking()
                .Where(p => p.IsPublished && !p.IsDiscontinued
                    && ((p.OemNoDisplay != null && EF.Functions.ILike(p.OemNoDisplay, $"%{f}%", "\\"))
                        || (p.Oem2 != null && EF.Functions.ILike(p.Oem2, $"%{f}%", "\\"))
                        || (p.ProductName1 != null && EF.Functions.ILike(p.ProductName1, $"%{f}%", "\\"))
                        || (p.ProductName2 != null && EF.Functions.ILike(p.ProductName2, $"%{f}%", "\\"))
                        || (p.Type != null && EF.Functions.ILike(p.Type, $"%{f}%", "\\"))))
                .Select(p => p.Id)
                .Take(5000)
                .ToListAsync(ct);
            // 段2: xrefs (oem_no_3 已有 trgm, 补齐 brand/oem_2 可后续)
            var ids2 = await _db.CrossReferences.AsNoTracking()
                .Where(x => (x.OemNo3 != null && EF.Functions.ILike(x.OemNo3, $"%{f}%", "\\"))
                    || (x.OemBrand != null && EF.Functions.ILike(x.OemBrand, $"%{f}%", "\\"))
                    || (x.Oem2 != null && EF.Functions.ILike(x.Oem2, $"%{f}%", "\\")))
                .Select(x => x.ProductId)
                .Take(5000)
                .ToListAsync(ct);
            // 段3: machine_applications
            var ids3 = await _db.MachineApplications.AsNoTracking()
                .Where(m => (m.MachineBrand != null && EF.Functions.ILike(m.MachineBrand, $"%{f}%", "\\"))
                    || (m.MachineModel != null && EF.Functions.ILike(m.MachineModel, $"%{f}%", "\\"))
                    || (m.EngineBrand != null && EF.Functions.ILike(m.EngineBrand, $"%{f}%", "\\"))
                    || (m.EngineType != null && EF.Functions.ILike(m.EngineType, $"%{f}%", "\\"))
                    || (m.ModelName != null && EF.Functions.ILike(m.ModelName, $"%{f}%", "\\")))
                .Select(m => m.ProductId)
                .Take(5000)
                .ToListAsync(ct);
            var fuzzyIds = ids1.Concat(ids2).Concat(ids3).Distinct().Take(5000).ToList();
            if (fuzzyIds.Count == 0)
            {
                // 无任何命中 — 提前返回空结果 (不继续 AND 收窄到空)
                sw.Stop();
                return Ok(new PublicEightResponse(
                    Total: 0, Page: page, PageSize: pageSize, TotalPages: 0,
                    ElapsedMs: (int)sw.ElapsedMilliseconds, CountMode: "exact",
                    Items: new List<PublicSearchHit>()));
            }
            // 用 id 集合过滤 (主键索引 IN, 快) — 后续 8 字段 AND 在此子集上收窄
            query = query.Where(p => fuzzyIds.Contains(p.Id));
        }

        // 文本字段: 单值 ILIKE (走 P0.1 EscapeLikePattern + 3 参重载)
        //   1) oem_no_2: 产品自身 OEM 2 字段
        if (!string.IsNullOrEmpty(oemNo2))
        {
            var kw = oemNo2.EscapeLikePattern();
            query = query.Where(p => p.Oem2 != null
                && EF.Functions.ILike(p.Oem2, $"%{kw}%", "\\"));
        }
        //   2) oem_brand + oem_no_3: 走 1 个合并 EXISTS (xref 5.27M, 索引覆盖)
        //      与 AdminProductService 638-655 行同样模式, 避免 2 次嵌套扫描
        var brandKw = oemBrand;
        var oem3Kw  = oemNo3;
        if (!string.IsNullOrEmpty(brandKw) || !string.IsNullOrEmpty(oem3Kw))
        {
            var brandEsc = brandKw?.EscapeLikePattern();
            var oem3Esc  = oem3Kw?.EscapeLikePattern();
            // 局部变量提升闭包捕获, EF 翻译成 p.id = ANY (SELECT product_id FROM cross_references WHERE ...)
            var bKw = brandEsc;
            var o3Kw = oem3Esc;
            query = query.Where(p => _db.CrossReferences.Any(x =>
                x.ProductId == p.Id
                && (bKw == null || (x.OemBrand != null && EF.Functions.ILike(x.OemBrand, $"%{bKw}%", "\\")))
                && (o3Kw == null || (x.OemNo3 != null && EF.Functions.ILike(x.OemNo3, $"%{o3Kw}%", "\\")))
            ));
        }
        //   3) machine 5 字段: 1 个合并 EXISTS
        //      任一字段空 → 跳过该判断; 全部空 → 整个 EXISTS 不加入 (但已被前面 400 拦掉)
        if (!string.IsNullOrEmpty(machineBrand) || !string.IsNullOrEmpty(machineModel)
            || !string.IsNullOrEmpty(modelName) || !string.IsNullOrEmpty(engineBrand)
            || !string.IsNullOrEmpty(engineType))
        {
            var mbEsc = machineBrand?.EscapeLikePattern();
            var mmEsc = machineModel?.EscapeLikePattern();
            var mnEsc = modelName?.EscapeLikePattern();
            var ebEsc = engineBrand?.EscapeLikePattern();
            var etEsc = engineType?.EscapeLikePattern();
            var mb = mbEsc; var mm = mmEsc; var mn = mnEsc; var eb = ebEsc; var et = etEsc;
            query = query.Where(p => _db.MachineApplications.Any(m =>
                m.ProductId == p.Id
                && (mb == null || (m.MachineBrand != null && EF.Functions.ILike(m.MachineBrand, $"%{mb}%", "\\")))
                && (mm == null || (m.MachineModel != null && EF.Functions.ILike(m.MachineModel, $"%{mm}%", "\\")))
                && (mn == null || (m.ModelName    != null && EF.Functions.ILike(m.ModelName,    $"%{mn}%", "\\")))
                && (eb == null || (m.EngineBrand  != null && EF.Functions.ILike(m.EngineBrand,  $"%{eb}%", "\\")))
                && (et == null || (m.EngineType   != null && EF.Functions.ILike(m.EngineType,   $"%{et}%", "\\")))
            ));
        }

        // 分页 (offset, 不走 cursor — 公开搜索结果无需书签翻页)
        //   WHY Skip 必须先于 Take: 顺序错会 page>1 时丢数据 (见 AdminProductService 754-757)
        //   P3.4 count 超时降级: 8 字段 EXISTS + 大表 (1M products + 5M xref + 1M apps) 时
        //     count 可能 5-10s (e.g. oem_brand='Mann' 命中 20% 数据, ILIKE '%Mann%' 走全表扫描).
        //     用 5s 超时降级到 estimated count
        //   注: SqlQueryRaw<long>(reltuples) 在 EF Core 8 + 含 ILike/EXISTS 的查询上下文下
        //     会生成 `t.Value` 不存在的列引用 (PG 42703 字段不存在) — EF 把 raw SQL 套进
        //     SELECT t.c1 AS "Value" 子查询, 而我们的 raw SQL 没有 Value 别名 → 报 42703
        //   兜底: 走 _db.Products.LongCountAsync() (无过滤, 但远快于 EXISTS+ILike), 误差大,
        //     仅作 "约 N 条" 提示用 — 显式 flag countMode='estimated' 让前端文案区分
        long total;
        bool countTimedOut = false;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(5000);  // 5s 超时 — 1M+5M EXISTS 通常 < 2s, 5s 留 2x 缓冲
            total = await query.LongCountAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 走无过滤 COUNT(*) 兜底 — 1M 数据下 50ms, 远小于 5s, 给前端 "约 N 条" 提示用
            //   实际与精确值的偏差会很大 (e.g. 1M 总数 vs 200k 精确), 但用户体验上 "加载快" 比 "数字准" 重要
            total = await _db.Products.LongCountAsync(ct);
            countTimedOut = true;
        }
        string countModeUsed = countTimedOut ? "estimated" : "exact";
        // 🔧 fix(2026-08-23 走查): 先查主表投影, 再批量取回 xrefs/machine 关联字段 (避免 N+1)
        var rows = await query
            .OrderByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                // WHY: 与公开详情和聚合搜索统一展示首个已发布 OEM3，主表字段仅作兼容回退。
                Oem3 = _db.CrossReferences
                    .Where(x => x.ProductId == p.Id && x.IsPublished && !x.IsDiscontinued
                        && x.OemNo3 != null && x.OemNo3 != "")
                    .OrderBy(x => _db.XrefOemBrands
                        .Where(b => b.Brand == x.OemBrand && b.DeletedAt == null)
                        .Select(b => (int?)b.SortOrder)
                        .FirstOrDefault() ?? int.MaxValue)
                    .ThenBy(x => x.SortOrder)
                    .ThenBy(x => x.OemNo3)
                    .Select(x => x.OemNo3!)
                    .FirstOrDefault() ?? p.OemNoDisplay,
                p.Oem2,
                p.ProductName1,
                p.Type,
                p.D1Mm,
                p.H1Mm
            })
            .ToListAsync(ct);

        // 批量取回关联字段 (每页 ≤100 行, 2 次批量查询, 无 N+1)
        var rowIds = rows.Select(r => r.Id).ToList();
        Dictionary<long, string?> brandMap = new();
        Dictionary<long, (string? Mb, string? Eb)> machineMap = new();
        if (rowIds.Count > 0)
        {
            var brandRows = await _db.CrossReferences.AsNoTracking()
                .Where(x => rowIds.Contains(x.ProductId) && x.OemBrand != null && x.OemBrand != "")
                .GroupBy(x => x.ProductId)
                .Select(g => new { Id = g.Key, Brand = g.Select(x => x.OemBrand!).FirstOrDefault() })
                .ToListAsync(ct);
            brandMap = brandRows.ToDictionary(x => x.Id, x => x.Brand);
            var machineRows = await _db.MachineApplications.AsNoTracking()
                .Where(m => rowIds.Contains(m.ProductId))
                .GroupBy(m => m.ProductId)
                .Select(g => new { Id = g.Key, Mb = g.Select(x => x.MachineBrand).FirstOrDefault(), Eb = g.Select(x => x.EngineBrand).FirstOrDefault() })
                .ToListAsync(ct);
            machineMap = machineRows.ToDictionary(x => x.Id, x => (x.Mb, x.Eb));
        }
        var items = rows.Select(r => new PublicSearchHit(
            r.Id,
            r.Oem3 ?? "",
            r.Oem2,
            r.ProductName1,
            r.Type,
            r.D1Mm?.ToString(),
            r.H1Mm?.ToString(),
            brandMap.GetValueOrDefault(r.Id),
            machineMap.GetValueOrDefault(r.Id).Mb,
            machineMap.GetValueOrDefault(r.Id).Eb
        )).ToList();

        sw.Stop();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        _logger.LogInformation($"eight-field search: oemBrand={oemBrand} oemNo2={oemNo2} oemNo3={oemNo3} "
            + $"mb={machineBrand} mm={machineModel} mn={modelName} eb={engineBrand} et={engineType} "
            + $"fuzzy={fuzzy} → total={total}({countModeUsed}) items={items.Count} elapsed={sw.ElapsedMilliseconds}ms");

        return Ok(new PublicEightResponse(
            Total: total,
            Page: page,
            PageSize: pageSize,
            TotalPages: totalPages,
            ElapsedMs: (int)sw.ElapsedMilliseconds,
            CountMode: countModeUsed,
            Items: items
        ));
    }

    /// <summary>
    /// V2 Task 1.2: 聚合搜索 (需求 5,修复漏洞 1/2/4/12)
    /// POST /api/public/search/aggregate
    /// - 文档级返回: mr1 + oemList 嵌套数组 (修复漏洞 1: 之前返回扁平 OEM 列表丢失 MR.1 关联)
    /// - _formatted 高亮字段 + XSS 防御 (修复漏洞 4: MeiliSearchProvider.SanitizeFormatted 递归处理)
    /// - 分页深度校验 (修复漏洞 12: page > max_page_depth 抛 SEARCH_PAGE_TOO_DEEP)
    /// - PG 兜底 (修复漏洞 2: Meili 离线时降级,返回结构一致)
    /// </summary>
    /// <remarks>
    /// 示例请求:
    ///
    ///     POST /api/public/search/aggregate
    ///     {
    ///       "q": "CAT 320D",
    ///       "page": 1,
    ///       "pageSize": 20,
    ///       "tolerance": 5,
    ///       "machineCategory": "construction"
    ///     }
    ///
    /// 成功响应 (200):
    ///
    ///     {
    ///       "total": 42,
    ///       "page": 1,
    ///       "pageSize": 20,
    ///       "totalPages": 3,
    ///       "processingTimeMs": 12,
    ///       "provider": "meilisearch",
    ///       "hits": [
    ///         {
    ///           "mr1": "ABC1234567",
    ///           "productName1": "Oil Filter",
    ///           "oemList": [{"oemBrand":"BOSCH","oemNo3":"F000000001","sortOrder":1}],
    ///           "formatted": {"productName1":"Oil <mark>Filter</mark>"},
    ///           "rankingScore": 0.95
    ///         }
    ///       ]
    ///     }
    ///
    /// 400 (分页过深):
    ///
    ///     {
    ///       "type": "https://sakurafilter.com/errors/search-page-too-deep",
    ///       "title": "Search Page Too Deep",
    ///       "status": 400,
    ///       "errorCode": "SEARCH_PAGE_TOO_DEEP",
    ///       "detail": "分页深度超过限制(最大 100 页)"
    ///     }
    /// </remarks>
    [HttpPost("aggregate")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Aggregate(
        [FromBody] AggregateSearchRequest? req,
        CancellationToken ct)
    {
        // 入参兜底
        req ??= new AggregateSearchRequest(Q: null);
        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, 100);

        // V2 Task 1.2.4: 分页深度校验 (修复漏洞 12)
        //   WHY: 深度分页 (如 page=10000) 会让 Meili/PG OFFSET 跳过大量行,性能急剧下降
        //   max_page_depth 从 system_settings 读取,默认 100 (可配)
        var maxPageDepth = await GetMaxPageDepthAsync(ct);
        if (page > maxPageDepth)
        {
            throw new ArgumentException(
                $"SEARCH_PAGE_TOO_DEEP: 分页深度超过限制 (最大 {maxPageDepth} 页, 当前 page={page})");
        }

        // V2 Task 1.2.5/1.2.6: Meili 主搜索 (含高亮 + XSS 防御)
        //   1s 超时: 与 ResilientSearchProvider 一致,避免公开搜索长耗时
        //   失败降级 PG (修复漏洞 2)
        AggregateSearchResponse response;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(1000);
            response = await _meili.AggregateSearchAsync(req with { Page = page, PageSize = pageSize }, cts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or HttpRequestException)
        {
            _logger.LogWarning(ex, "聚合搜索 Meili 失败,降级 PG 兜底 (q={Q})", req.Q);
            response = await _pg.AggregateSearchAsync(req with { Page = page, PageSize = pageSize }, ct);
        }

        _logger.LogInformation("aggregate search: q={Q} page={Page} pageSize={PageSize} → total={Total} provider={Provider} elapsed={Elapsed}ms",
            req.Q, page, pageSize, response.Total, response.Provider, response.ProcessingTimeMs);

        // 聚合查询内部以 MR1 分组，但公开响应只能交付客户字段。
        // key 用于前端展开状态，优先取公开 OEM3，不得回退或序列化 MR1。
        var publicHits = response.Hits.Select((hit, index) => new
        {
            key = hit.OemList.FirstOrDefault(x => x.IsPublished && !string.IsNullOrWhiteSpace(x.OemNo3))?.OemNo3
                  ?? hit.OemList.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.OemNo3))?.OemNo3
                  ?? hit.Oem2
                  ?? $"result-{index}",
            hit.ProductName1,
            hit.ProductName2,
            hit.Oem2,
            hit.Type,
            hit.Remark,
            hit.Media,
            OemList = hit.OemList
                .Where(x => x.IsPublished)
                .Select(x => new { x.OemBrand, x.OemNo3, x.Oem2, x.MachineType })
                .ToList(),
            hit.MachineList,
            Formatted = hit.Formatted?.Where(x => x.Key == "product_name_1")
                .ToDictionary(x => x.Key, x => x.Value),
            hit.RankingScore
        });

        return Ok(new
        {
            response.Total,
            response.Page,
            response.PageSize,
            response.TotalPages,
            Hits = publicHits
        });
    }

    /// <summary>
    /// 从 system_settings 读取 search.max_page_depth (默认 100)
    /// 改进 1.2: IMemoryCache 5 分钟缓存 (配置变更频率极低, 避免每次请求查 DB)
    /// </summary>
    private async Task<int> GetMaxPageDepthAsync(CancellationToken ct)
    {
        // 改进 1.2: 缓存命中直接返回 (5 分钟 TTL, 配置变更后最多 5 分钟生效)
        const string cacheKey = "search.max_page_depth";
        if (_cache.TryGetValue(cacheKey, out int cached) && cached > 0)
            return cached;

        var value = await _db.SystemSettings
            .AsNoTracking()
            .Where(s => s.Key == "search.max_page_depth")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        var depth = (string.IsNullOrEmpty(value) || !int.TryParse(value, out var d) || d < 1) ? 100 : d;
        // V24-F85: 用 SetWithSize 替代手写 MemoryCacheEntryOptions (避免再次遗漏 Size 声明)
        _cache.SetWithSize(cacheKey, depth, TimeSpan.FromMinutes(5));
        return depth;
    }
}

/// <summary>批量查询入参</summary>
public record BatchOemRequest(List<string> Oems);

/// <summary>单条 OEM 结果</summary>
public record BatchOemResult(
    string Oem,
    bool Hit,
    long? ProductId = null,
    string? OemBrand = null,
    string? ProductName1 = null,
    string? Oem2 = null
);

/// <summary>批量查询响应</summary>
public record BatchOemResponse(
    int Total,
    int Hits,
    int Miss,
    List<BatchOemResult> Results
);

/// <summary>P3.4 (Task 11.5): 公开搜索单条结果</summary>
public record PublicSearchHit(
    long Id,
    string OemNoDisplay,
    string? Oem2,
    string? ProductName1,
    string? Type,
    string? D1Mm,
    string? H1Mm,
    // 🔧 fix(2026-08-23 走查): 新增 3 字段 — 结果表格展示 OEM Brand / Machine Brand / Engine Brand,
    //   用户能确认搜索结果是否目标 (原 6 字段字段偏少)
    string? OemBrand,
    string? MachineBrand,
    string? EngineBrand
);

/// <summary>P3.4 (Task 11.5): 8 字段响应</summary>
public record PublicEightResponse(
    long Total,
    int Page,
    int PageSize,
    int TotalPages,
    int ElapsedMs,
    string CountMode,
    List<PublicSearchHit> Items
);
