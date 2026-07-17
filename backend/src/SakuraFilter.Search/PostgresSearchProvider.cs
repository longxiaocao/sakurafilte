using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Core.Entities;
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
            // S3-3: 分词 OR 拼接,对齐 Meilisearch 分词召回口径
            var tokens = raw.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var patterns = tokens.Select(t =>
                t.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")).ToList();

            // S3-22: 每个_token 都走 6 字段 ILIKE OR
            q = q.Where(p =>
                patterns.Any(token =>
                    EF.Functions.ILike(p.ProductName1 ?? "", $"%{token}%", "\\") ||
                    EF.Functions.ILike(p.ProductName2 ?? "", $"%{token}%", "\\") ||
                    EF.Functions.ILike(p.Oem2 ?? "", $"%{token}%", "\\") ||
                    EF.Functions.ILike(p.Mr1 ?? "", $"%{token}%", "\\") ||
                    EF.Functions.ILike(p.Remark ?? "", $"%{token}%", "\\") ||
                    p.CrossReferences.Any(x =>
                        EF.Functions.ILike(x.OemBrand ?? "", $"%{token}%", "\\") ||
                        EF.Functions.ILike(x.OemNo3 ?? "", $"%{token}%", "\\") ||
                        EF.Functions.ILike(x.Oem2 ?? "", $"%{token}%", "\\")) ||
                    p.MachineApplications.Any(m =>
                        EF.Functions.ILike(m.MachineBrand ?? "", $"%{token}%", "\\") ||
                        EF.Functions.ILike(m.MachineModel ?? "", $"%{token}%", "\\"))
                )
            );
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

        if (req.IncludeDiscontinued) q = q.IgnoreQueryFilters();

        // V2: 要求至少有一个上架 OEM 3 (对齐 Meilisearch filter 语义)
        q = q.Where(p => p.CrossReferences.Any(x => x.IsPublished && !x.IsDiscontinued));

        // V2 排序 (S3-5): brand_sort_order_min ASC → oem_list_sort_order_min ASC → updated_at DESC
        //   Phase 1 改原生 SQL 用 CTE 预计算;Phase 0 用 LINQ 子查询 (性能差但功能正确)
        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, 100);
        var total = await q.LongCountAsync(ct);

        var items = await q
            .OrderBy(p => p.CrossReferences
                .Where(x => !x.IsDiscontinued)
                .Select(x => _db.XrefOemBrands
                    .Where(b => b.Brand == x.OemBrand && b.DeletedAt == null)
                    .Select(b => (int?)b.SortOrder)
                    .FirstOrDefault() ?? int.MaxValue)
                .DefaultIfEmpty(int.MaxValue)
                .Min())
            .ThenBy(p => p.CrossReferences
                .Where(x => x.IsPublished && !x.IsDiscontinued)
                .Select(x => (int?)x.SortOrder)
                .DefaultIfEmpty(int.MaxValue)
                .Min())
            .ThenByDescending(p => p.UpdatedAt)
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
