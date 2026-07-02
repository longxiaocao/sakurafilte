using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Controllers;

/// <summary>
/// P2.3 (Task 8.3): 公开产品页端点 (无需 token)
/// 用途: 前台产品页按 dict_type.sort_order 排序展示分组
/// 设计:
///   - JOIN dict_type t ON t.type = p.type, 按 t.sort_order 升序
///   - 仅含 active (deleted_at IS NULL) 的 dict_type
///   - 仅含 active (is_discontinued = false) 的 products
///   - 4 大类: oil(1)/fuel(2)/air(3)/cabin(4)/others(99), others 永远排最后
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/products")]
public class PublicProductController : ControllerBase
{
    private readonly ProductDbContext _db;
    private readonly ILogger<PublicProductController> _logger;

    public PublicProductController(ProductDbContext db, ILogger<PublicProductController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 按 type 分组聚合, 顺序按 dict_type.sort_order 升序
    /// 返: List&lt;TypeGroupDto&gt;:
    ///   { type, sortOrder, productCount, products: [ProductSummaryDto...] }
    /// 限制:
    ///   - 每个 type 至多 50 个 product (避免单 type 撑爆响应)
    ///   - 仅返回 dict_type 已定义的 5 类 (oil/fuel/air/cabin/others), 未定义 type 的产品不展示
    /// </summary>
    [HttpGet("by-type")]
    public async Task<IActionResult> ByType(CancellationToken ct)
    {
        // 1) 拉 active dict_type, 按 sort_order 升序
        var types = await _db.DictTypes.AsNoTracking()
            .Where(t => t.DeletedAt == null)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Type)
            .Select(t => new { t.Type, t.SortOrder })
            .ToListAsync(ct);
        if (types.Count == 0)
        {
            return Ok(new ByTypeResponse(0, new List<TypeGroupDto>()));
        }

        // 2) 对每个 type 拉 active product 摘要 (至多 50)
        const int perTypeLimit = 50;
        var typeNames = types.Select(t => t.Type).ToList();
        // 单次 SQL 拉所有 type 的 active product 摘要, 内存分组, 避免 N+1
        var productRows = await _db.Products.AsNoTracking()
            .Where(p => !p.IsDiscontinued && p.Type != null && typeNames.Contains(p.Type))
            .OrderBy(p => p.Id)
            .Select(p => new
            {
                p.Id,
                p.Type,
                p.OemNoDisplay,
                p.ProductName1,
                p.D1Mm,
                p.D2Mm,
                p.H1Mm,
                p.ImageKey,
                p.ImageStatus
            })
            .Take(perTypeLimit * typeNames.Count)  // 防止极端情况下拖全表
            .ToListAsync(ct);

        // 3) 按 type 分组, 取前 50
        var byType = productRows
            .GroupBy(p => p.Type!)
            .ToDictionary(g => g.Key, g => g.Take(perTypeLimit).ToList());

        // 4) 组装 DTO (按 dict_type 顺序)
        var groups = types.Select(t =>
        {
            var products = byType.TryGetValue(t.Type, out var list)
                ? list.Select(p => new ProductSummaryDto(
                    p.Id, p.OemNoDisplay, p.ProductName1,
                    p.D1Mm, p.D2Mm, p.H1Mm, p.ImageKey, p.ImageStatus)).ToList()
                : new List<ProductSummaryDto>();
            return new TypeGroupDto(t.Type, t.SortOrder, products.Count, products);
        }).ToList();

        _logger.LogInformation("by-type: types={Types} products={Products}",
            groups.Count, groups.Sum(g => g.ProductCount));
        return Ok(new ByTypeResponse(groups.Count, groups));
    }
}

/// <summary>P2.3: 单个 type 分组</summary>
public record TypeGroupDto(
    string Type,
    int SortOrder,
    int ProductCount,
    List<ProductSummaryDto> Products
);

/// <summary>P2.3: 产品摘要 (前台 type 分组展示用, 字段精简)</summary>
public record ProductSummaryDto(
    long Id,
    string OemNoDisplay,
    string? ProductName1,
    decimal? D1Mm,
    decimal? D2Mm,
    decimal? H1Mm,
    string? ImageKey,
    string ImageStatus
);

/// <summary>P2.3: by-type 响应</summary>
public record ByTypeResponse(
    int TotalTypes,
    List<TypeGroupDto> Groups
);
