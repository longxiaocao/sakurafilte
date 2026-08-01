using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Controllers;

/// <summary>
/// 公开 featured 端点 (无需 token, 走 "search" 限流分区)
/// 用途: 公开搜索页进入时, 展示"最新 20 条产品"明细表, 供用户直接浏览/对比/查看详情
/// 设计: 不带任何过滤条件, 只排除 is_discontinued=true
///   - 排序: OrderByDescending(Id) -> 最新上架优先
///   - limit: 1-50, 默认 20
///   - 性能: 主键降序扫描 + Take(20), 1M 数据下 &lt; 20ms
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("search")]
[Route("api/public/featured")]
public class PublicFeaturedController : ControllerBase
{
    private readonly ProductDbContext _db;
    private readonly ILogger<PublicFeaturedController> _logger;

    public PublicFeaturedController(ProductDbContext db, ILogger<PublicFeaturedController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 获取最新 N 条 active 产品
    /// URL: GET /api/public/featured?limit=20
    /// </summary>
    /// <remarks>
    /// 成功响应 (200):
    ///
    ///     {
    ///       "total": 20,
    ///       "items": [
    ///         { "id": 12345, "oemNoDisplay": "P00050000", "oem2": "P00050000", "productName1": "OIL FILTER", "type": "Oil", "d1Mm": "95", "h1Mm": "120" },
    ///         ...
    ///       ]
    ///     }
    /// </remarks>
    [HttpGet("")]
    [ProducesResponseType(typeof(PublicFeaturedResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Featured(
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        // 限流: limit 1-50, 防止滥用
        limit = Math.Clamp(limit, 1, 50);

        var items = await _db.Products.AsNoTracking()
            .Where(p => p.IsPublished && !p.IsDiscontinued)
            .OrderByDescending(p => p.Id)
            .Take(limit)
            .Select(p => new PublicSearchHit(
                p.Id,
                // WHY: 公开列表的主编号必须与详情、搜索聚合和对比页保持 OEM3 优先的一致口径。
                _db.CrossReferences
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
                p.D1Mm != null ? p.D1Mm.ToString() : null,
                p.H1Mm != null ? p.H1Mm.ToString() : null
            ))
            .ToListAsync(ct);

        _logger.LogDebug("featured: limit={Limit} returned={Count}", limit, items.Count);

        return Ok(new PublicFeaturedResponse(items.Count, items));
    }
}

/// <summary>公开 featured 响应 (复用 PublicSearchHit 形状, 方便前端统一处理)</summary>
public record PublicFeaturedResponse(
    int Total,
    List<PublicSearchHit> Items
);
