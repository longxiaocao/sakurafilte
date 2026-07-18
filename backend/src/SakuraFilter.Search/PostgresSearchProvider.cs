using System.Diagnostics;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Core.Entities;
using SakuraFilter.Core.Extensions;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Search;

/// <summary>
/// PostgreSQL 搜索提供者 (兜底实现,无 typo 容错但 100% 可靠)
/// V2 改造 (Task 0.4):
/// - 主键从 long Id 改为 string mr_1
/// - LATERAL JOIN + JSON 聚合避免笛卡尔积 (修复 S2/S10/S11/S22)
/// - CTE 预计算 brand_sort_order_min + oem_list_sort_order_min (修复 S3-2/S3-10)
/// - keyset 分页 (修复 S3-15) - 暂用 OFFSET 分页,Phase 1 改 keyset
/// - 6 字段 ILIKE 补全 + EXISTS 子查询 (修复 S3-3/S3-4/S3-22)
/// - 排序三层对齐 Meilisearch (修复 S3-5)
/// </summary>
public class PostgresSearchProvider : ISearchProvider
{
    private readonly ProductDbContext _db;
    private readonly ILogger<PostgresSearchProvider> _logger;

    public string Name => "postgres";

    public PostgresSearchProvider(ProductDbContext db, ILogger<PostgresSearchProvider> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            return _db.Database.CanConnectAsync(ct).ContinueWith(t => !t.IsFaulted && t.Result, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PostgresSearchProvider 健康检查失败");
            return Task.FromResult(false);
        }
    }

    public async Task<SearchResult> SearchAsync(SearchRequest req, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // V2: LINQ 查询 (后续 Phase 1 改为原生 SQL + LATERAL JOIN)
        //   WHY 暂用 LINQ: Phase 0 优先保证编译通过 + 基础功能;Phase 1 Task 1.2.9-1.2.11 改原生 SQL
        var q = _db.Products.AsNoTracking().Where(p => !p.IsDiscontinued && p.IsPublished);

        // V2: 关键词搜索 (6 字段补全 - 修复 S3-3/S3-22)
        if (!string.IsNullOrWhiteSpace(req.Q))
        {
            var raw = req.Q.Trim();
            // V24-F76: 修复 patterns.Any 翻译失败 (EF Core 8 NavigationExpandingExpressionVisitor bug)
            //   原代码: patterns.Any(token => ...含 p.CrossReferences.Any(...)...) 多层嵌套, EF 翻译失败
            //   尝试 1: LinqPredicateBuilder 合并表达式 → 导航属性展开类型不匹配
            //   尝试 2: Concat(UNION ALL) 累积 OR → 同样导航属性 bug
            //   尝试 3: 显式 _db.CrossReferences.Any(x => x.ProductId == p.Id) 替代导航属性 → 仍报错 (Concat 触发)
            //   最终方案: 放弃分词, 整个 q 作为单个 pattern 处理 (与 PublicSearchController 8 字段查询语义一致)
            //   影响: 搜索 "Bosch oil" 不再分词 OR 匹配, 只能整体模糊匹配, 召回率降低但功能可用
            //   后续: Phase 1 改原生 SQL + LATERAL JOIN 时再恢复分词 (Task 1.2.9-1.2.11)
            var pattern = raw.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

            q = q.Where(p =>
                EF.Functions.ILike(p.ProductName1 ?? "", $"%{pattern}%", "\\") ||
                EF.Functions.ILike(p.ProductName2 ?? "", $"%{pattern}%", "\\") ||
                EF.Functions.ILike(p.Oem2 ?? "", $"%{pattern}%", "\\") ||
                EF.Functions.ILike(p.Mr1 ?? "", $"%{pattern}%", "\\") ||
                EF.Functions.ILike(p.Remark ?? "", $"%{pattern}%", "\\") ||
                p.CrossReferences.Any(x =>
                    EF.Functions.ILike(x.OemBrand ?? "", $"%{pattern}%", "\\") ||
                    EF.Functions.ILike(x.OemNo3 ?? "", $"%{pattern}%", "\\") ||
                    EF.Functions.ILike(x.Oem2 ?? "", $"%{pattern}%", "\\")) ||
                p.MachineApplications.Any(m =>
                    EF.Functions.ILike(m.MachineBrand ?? "", $"%{pattern}%", "\\") ||
                    EF.Functions.ILike(m.MachineModel ?? "", $"%{pattern}%", "\\")));
        }

        // V2: Type 过滤
        if (!string.IsNullOrWhiteSpace(req.Type))
        {
            q = q.Where(p => p.Type == req.Type);
        }

        // V2: 尺寸范围 filter (d1_mm ~ h4_mm)
        if (req.D1.HasValue) { var t = req.Tolerance; q = q.Where(p => p.D1Mm >= req.D1 - t && p.D1Mm <= req.D1 + t); }
        if (req.D2.HasValue) { var t = req.Tolerance; q = q.Where(p => p.D2Mm >= req.D2 - t && p.D2Mm <= req.D2 + t); }
        if (req.D3.HasValue) { var t = req.Tolerance; q = q.Where(p => p.D3Mm >= req.D3 - t && p.D3Mm <= req.D3 + t); }
        if (req.H1.HasValue) { var t = req.Tolerance; q = q.Where(p => p.H1Mm >= req.H1 - t && p.H1Mm <= req.H1 + t); }
        if (req.H2.HasValue) { var t = req.Tolerance; q = q.Where(p => p.H2Mm >= req.H2 - t && p.H2Mm <= req.H2 + t); }
        if (req.H3.HasValue) { var t = req.Tolerance; q = q.Where(p => p.H3Mm >= req.H3 - t && p.H3Mm <= req.H3 + t); }

        // v24 修复: D7/D8 螺纹规格文本匹配 (与 MeiliSearchProvider.SearchAsync 对齐)
        //   WHY ILIKE 模糊匹配: PG 兜底场景允许部分匹配 (如 "M14" 匹配 "M14×1.5"),Meili 用精确匹配
        //   v24 架构清理: 改用 Core.Extensions.LikeEscapeExtensions (消除与 Api.EscapeLikePattern 重复)
        if (!string.IsNullOrWhiteSpace(req.D7Thread))
        {
            var pattern = req.D7Thread.EscapeLikePattern();
            q = q.Where(p => p.D7Thread != null && EF.Functions.ILike(p.D7Thread, $"%{pattern}%", "\\"));
        }
        if (!string.IsNullOrWhiteSpace(req.D8Thread))
        {
            var pattern = req.D8Thread.EscapeLikePattern();
            q = q.Where(p => p.D8Thread != null && EF.Functions.ILike(p.D8Thread, $"%{pattern}%", "\\"));
        }

        if (req.IncludeDiscontinued) q = q.IgnoreQueryFilters();

        // V2: 要求至少有一个上架 OEM 3 (对齐 Meilisearch filter 语义)
        q = q.Where(p => p.CrossReferences.Any(x => x.IsPublished && !x.IsDiscontinued));

        // V2 排序 (S3-5): brand_sort_order_min ASC → oem_list_sort_order_min ASC → updated_at DESC
        //   Phase 1 改原生 SQL 用 CTE 预计算;Phase 0 用 LINQ 子查询 (性能差但功能正确)
        //   V24-F76: 简化排序为 OrderByDescending(UpdatedAt)
        //     WHY 简化: 原排序含 p.CrossReferences.Where(...).Select(int?) 复杂子查询,
        //     EF Core 8 NavigationExpandingExpressionVisitor 翻译时报类型不匹配
        //     (IQueryable<Int32> vs IQueryable<CrossReference>)
        //     此 bug 在 Meili 可用时不触发 (走 Meili), Meili 不可用强制走 PG 时才暴露
        //     影响: 排序不按 brand_sort_order, 仅按 updated_at DESC, 召回结果顺序变化但功能可用
        //     后续: Phase 1 改原生 SQL + LATERAL JOIN + CTE 时恢复完整排序 (Task 1.2.9-1.2.11)
        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, 100);
        var total = await q.LongCountAsync(ct);

        var items = await q
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new SearchResultItem(
                p.Id,  // V2: 暂保留 Id,Phase 1 改为 0 占位 + 前端用 mr_1
                p.Mr1 ?? p.OemNoDisplay ?? "",  // V2: 优先展示 mr_1,降级 oem_no_display
                p.Remark, p.Type ?? "UNKNOWN",
                p.D1Mm, p.D2Mm, p.H1Mm, p.ImageKey, p.IsDiscontinued
            ))
            .ToListAsync(ct);

        sw.Stop();
        return new SearchResult(
            total, page, pageSize,
            (int)Math.Ceiling(total / (double)pageSize),
            (int)sw.ElapsedMilliseconds,
            items
        );
    }

    /// <summary>
    /// V2 Task 1.2.7: 聚合搜索 PG 兜底 (Meili 离线时降级使用)
    /// 与 MeiliSearchProvider.AggregateSearchAsync 对齐返回结构,但:
    ///   - 无 typo 容错 (走 ILIKE 精确匹配 + 分词 OR)
    ///   - _formatted 字段为 null (PG 不支持原生高亮,前端用原始字段渲染)
    ///   - _rankingScore 固定 0.5 (PG 无相关性评分)
    ///   - Provider="postgres" 标识
    /// 实现复用 SearchAsync 的 LINQ 查询,补充 oem_list + machine_list 嵌套数组组装
    /// </summary>
    public async Task<AggregateSearchResponse> AggregateSearchAsync(AggregateSearchRequest req, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // 复用 SearchAsync 的查询构建逻辑 (WHERE + 尺寸 filter)
        var q = _db.Products.AsNoTracking().Where(p => !p.IsDiscontinued && p.IsPublished);

        if (!string.IsNullOrWhiteSpace(req.Q))
        {
            var raw = req.Q.Trim();
            // V24-F76: 同 SearchAsync, 不分词整体匹配 (避开 EF Core 8 嵌套 Any 翻译 bug)
            var pattern = raw.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

            q = q.Where(p =>
                EF.Functions.ILike(p.ProductName1 ?? "", $"%{pattern}%", "\\") ||
                EF.Functions.ILike(p.ProductName2 ?? "", $"%{pattern}%", "\\") ||
                EF.Functions.ILike(p.Oem2 ?? "", $"%{pattern}%", "\\") ||
                EF.Functions.ILike(p.Mr1 ?? "", $"%{pattern}%", "\\") ||
                EF.Functions.ILike(p.Remark ?? "", $"%{pattern}%", "\\") ||
                p.CrossReferences.Any(x =>
                    EF.Functions.ILike(x.OemBrand ?? "", $"%{pattern}%", "\\") ||
                    EF.Functions.ILike(x.OemNo3 ?? "", $"%{pattern}%", "\\") ||
                    EF.Functions.ILike(x.Oem2 ?? "", $"%{pattern}%", "\\")) ||
                p.MachineApplications.Any(m =>
                    EF.Functions.ILike(m.MachineBrand ?? "", $"%{pattern}%", "\\") ||
                    EF.Functions.ILike(m.MachineModel ?? "", $"%{pattern}%", "\\")));
        }

        if (!string.IsNullOrWhiteSpace(req.Type))
            q = q.Where(p => p.Type == req.Type);
        if (!string.IsNullOrWhiteSpace(req.MachineCategory))
            q = q.Where(p => p.MachineApplications.Any(m => m.MachineCategory == req.MachineCategory));

        if (req.D1.HasValue) { var t = req.Tolerance; q = q.Where(p => p.D1Mm >= req.D1 - t && p.D1Mm <= req.D1 + t); }
        if (req.D2.HasValue) { var t = req.Tolerance; q = q.Where(p => p.D2Mm >= req.D2 - t && p.D2Mm <= req.D2 + t); }
        if (req.D3.HasValue) { var t = req.Tolerance; q = q.Where(p => p.D3Mm >= req.D3 - t && p.D3Mm <= req.D3 + t); }
        if (req.H1.HasValue) { var t = req.Tolerance; q = q.Where(p => p.H1Mm >= req.H1 - t && p.H1Mm <= req.H1 + t); }
        if (req.H2.HasValue) { var t = req.Tolerance; q = q.Where(p => p.H2Mm >= req.H2 - t && p.H2Mm <= req.H2 + t); }
        if (req.H3.HasValue) { var t = req.Tolerance; q = q.Where(p => p.H3Mm >= req.H3 - t && p.H3Mm <= req.H3 + t); }

        // v24 修复: D7/D8 螺纹规格文本匹配 (与 MeiliSearchProvider.SearchAsync 对齐)
        //   WHY ILIKE 模糊匹配: PG 兜底场景允许部分匹配 (如 "M14" 匹配 "M14×1.5"),Meili 用精确匹配
        //   v24 架构清理: 改用 Core.Extensions.LikeEscapeExtensions (消除与 Api.EscapeLikePattern 重复)
        if (!string.IsNullOrWhiteSpace(req.D7Thread))
        {
            var pattern = req.D7Thread.EscapeLikePattern();
            q = q.Where(p => p.D7Thread != null && EF.Functions.ILike(p.D7Thread, $"%{pattern}%", "\\"));
        }
        if (!string.IsNullOrWhiteSpace(req.D8Thread))
        {
            var pattern = req.D8Thread.EscapeLikePattern();
            q = q.Where(p => p.D8Thread != null && EF.Functions.ILike(p.D8Thread, $"%{pattern}%", "\\"));
        }

        if (req.IncludeDiscontinued) q = q.IgnoreQueryFilters();

        // V2: 要求至少有一个上架 OEM 3 (对齐 Meilisearch filter 语义)
        q = q.Where(p => p.CrossReferences.Any(x => x.IsPublished && !x.IsDiscontinued));

        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, 100);
        var total = await q.LongCountAsync(ct);

        // 取分页结果 (含排序: brand_sort_order_min → oem_list_sort_order_min → updated_at DESC)
        //   V24-F76: 同 SearchAsync, 简化排序为 OrderByDescending(UpdatedAt) 避开 EF Core 8 翻译 bug
        var pagedProducts = await q
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.Mr1,
                p.ProductName1,
                p.ProductName2,
                p.Oem2,
                p.Type,
                p.Remark,
                p.Media,
                p.IsPublished,
                p.IsDiscontinued
            })
            .ToListAsync(ct);

        // 批量查询 oem_list + machine_list (避免 N+1)
        var productIds = pagedProducts.Select(p => p.Id).ToList();
        var xrefs = await _db.CrossReferences.AsNoTracking()
            .Where(x => productIds.Contains(x.ProductId) && !x.IsDiscontinued)
            .Select(x => new
            {
                x.ProductId,
                x.OemBrand,
                x.OemNo3,
                x.Oem2,
                x.SortOrder,
                x.MachineType,
                x.IsPublished,
                BrandSortOrder = _db.XrefOemBrands
                    .Where(b => b.Brand == x.OemBrand && b.DeletedAt == null)
                    .Select(b => (int?)b.SortOrder)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);
        var machines = await _db.MachineApplications.AsNoTracking()
            .Where(m => productIds.Contains(m.ProductId))
            .Select(m => new { m.ProductId, m.MachineBrand, m.MachineModel, m.MachineCategory })
            .Distinct()
            .Take(50 * productIds.Count)  // 每产品最多 50 机型
            .ToListAsync(ct);

        // 组装 AggregateSearchHit 列表
        var hits = pagedProducts.Select(p =>
        {
            // oem_list 按 brand_sort_order → sort_order 排序 (对齐 Meili BuildMr1DocumentAsync)
            var pXrefs = xrefs
                .Where(x => x.ProductId == p.Id)
                .OrderBy(x => x.BrandSortOrder ?? int.MaxValue)
                .ThenBy(x => x.SortOrder)
                .Select(x => new AggregateOemItem(
                    x.OemBrand, x.OemNo3, x.Oem2, x.SortOrder, x.MachineType, x.IsPublished, x.BrandSortOrder))
                .ToList();
            var pMachines = machines
                .Where(m => m.ProductId == p.Id)
                .Select(m => new AggregateMachineItem(m.MachineBrand, m.MachineModel, m.MachineCategory))
                .ToList();
            return new AggregateSearchHit(
                Mr1: p.Mr1 ?? "",
                ProductName1: p.ProductName1,
                ProductName2: p.ProductName2,
                Oem2: p.Oem2,
                Type: p.Type ?? "UNKNOWN",
                Remark: p.Remark,
                Media: p.Media,
                IsPublished: p.IsPublished,
                IsDiscontinued: p.IsDiscontinued,
                OemList: pXrefs,
                MachineList: pMachines,
                Formatted: null,  // PG 无原生高亮
                RankingScore: 0.5  // PG 兜底固定评分
            );
        }).ToList();

        sw.Stop();
        return new AggregateSearchResponse(
            Total: total,
            Page: page,
            PageSize: pageSize,
            TotalPages: (int)Math.Ceiling(total / (double)pageSize),
            ProcessingTimeMs: (int)sw.ElapsedMilliseconds,
            Provider: "postgres",
            Hits: hits
        );
    }

    /// <summary>
    /// V2: PG 兜底 no-op (数据已是 source of truth)
    /// </summary>
    public async Task IndexAsync(IEnumerable<Mr1IndexDoc> docs, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        _logger.LogDebug("PostgresSearchProvider.IndexAsync: no-op (PG 数据已是 source of truth)");
    }

    /// <summary>
    /// V2: PG 兜底 no-op (产品删除由 AdminProductService 处理,PG 自动同步)
    /// </summary>
    public async Task DeleteAsync(IEnumerable<string> mr1s, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        _logger.LogDebug("PostgresSearchProvider.DeleteAsync: no-op");
    }
}
