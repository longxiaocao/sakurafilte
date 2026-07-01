using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Search;

/// <summary>
/// PostgreSQL 搜索提供者 (兜底实现,无 typo 容错但 100% 可靠)
/// - 复用复合索引 (type, dX_mm) 走 Index Scan
/// - ILike 模糊匹配 (PostgreSQL 原生)
/// - 任何场景都能用,作为 Meili 失败时的兜底
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
            // EF Core 8 健康检查:SELECT 1 等价
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
        var q = _db.Products.AsNoTracking().Where(p => !p.IsDiscontinued);

        // 1) 模糊关键词 (对 OEM/Remark)
        if (!string.IsNullOrWhiteSpace(req.Q))
        {
            var pattern = req.Q.Trim();
            q = q.Where(p =>
                EF.Functions.ILike(p.OemNoNormalized, $"%{pattern}%") ||
                EF.Functions.ILike(p.OemNoDisplay, $"%{pattern}%") ||
                (p.Remark != null && EF.Functions.ILike(p.Remark, $"%{pattern}%"))
            );
        }

        // 2) Type 过滤
        if (!string.IsNullOrWhiteSpace(req.Type))
        {
            q = q.Where(p => p.Type == req.Type);
        }

        // 3) ±容差范围 (走复合索引 type, dX_mm)
        if (req.D1.HasValue) { var t = req.Tolerance; q = q.Where(p => p.D1Mm >= req.D1 - t && p.D1Mm <= req.D1 + t); }
        if (req.D2.HasValue) { var t = req.Tolerance; q = q.Where(p => p.D2Mm >= req.D2 - t && p.D2Mm <= req.D2 + t); }
        if (req.D3.HasValue) { var t = req.Tolerance; q = q.Where(p => p.D3Mm >= req.D3 - t && p.D3Mm <= req.D3 + t); }
        if (req.H1.HasValue) { var t = req.Tolerance; q = q.Where(p => p.H1Mm >= req.H1 - t && p.H1Mm <= req.H1 + t); }
        if (req.H2.HasValue) { var t = req.Tolerance; q = q.Where(p => p.H2Mm >= req.H2 - t && p.H2Mm <= req.H2 + t); }
        if (req.H3.HasValue) { var t = req.Tolerance; q = q.Where(p => p.H3Mm >= req.H3 - t && p.H3Mm <= req.H3 + t); }

        if (req.IncludeDiscontinued) q = q.IgnoreQueryFilters();

        // 分页
        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, 100);
        var total = await q.LongCountAsync(ct);
        var items = await q
            .OrderBy(p => p.OemNoNormalized)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new SearchResultItem(
                p.Id, p.OemNoDisplay, p.Remark, p.Type,
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

    public async Task IndexAsync(IEnumerable<ProductIndexDoc> docs, CancellationToken ct = default)
    {
        // PG 兜底实现不需要主动索引 (直接走 SQL 查询),这里提供 no-op
        // 但保留接口以兼容 ETL 的双写调用
        await Task.CompletedTask;
        _logger.LogDebug("PostgresSearchProvider.IndexAsync: no-op (PG 数据已是 source of truth)");
    }

    public async Task DeleteAsync(IEnumerable<long> ids, CancellationToken ct = default)
    {
        // 同上,no-op
        await Task.CompletedTask;
        _logger.LogDebug("PostgresSearchProvider.DeleteAsync: no-op");
    }
}
