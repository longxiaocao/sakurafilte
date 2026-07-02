using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Controllers;

/// <summary>
/// 公开产品页端点 (无需 token)
/// 设计:
///   - P2.3 by-type: 按 dict_type.sort_order 分组聚合产品摘要
///   - P3.3 by-slug: 单产品详情 (复用了 AdminProductService.GetByIdAsync 避免重写投影)
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public")]
public class PublicProductController : ControllerBase
{
    private readonly ProductDbContext _db;
    private readonly AdminProductService _adminService;
    private readonly ILogger<PublicProductController> _logger;

    public PublicProductController(
        ProductDbContext db,
        AdminProductService adminService,
        ILogger<PublicProductController> logger)
    {
        _db = db;
        _adminService = adminService;
        _logger = logger;
    }

    /// <summary>
    /// P3.3 (Task 11): 单产品详情 (公开)
    /// URL 格式: /api/public/product/{slug}
    /// slug 格式 (按 R1 规格): {name1}-{name2}-{oemBrand}-{oemNo}
    /// 解析策略: 取 slug 最后一段作为 oem (支持 OEM 自身含 - 的场景,如 "AB-123-X")
    ///   - 1) OemNoDisplay 精确匹配
    ///   - 2) Oem2 匹配 (alt OEM)
    ///   - 3) Mr1 匹配
    /// 排除 is_discontinued=true (前台不展示下架产品)
    /// </summary>
    [HttpGet("product/{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return BadRequest(new { error = "slug 不能为空" });

        // 解析 slug: 取最后一段作为 oem (支持 OEM 含 '-')
        var oem = slug.Contains('-') ? slug[(slug.LastIndexOf('-') + 1)..] : slug;

        long? productId = null;

        // 1) OemNoDisplay 精确匹配
        productId = await _db.Products.AsNoTracking()
            .Where(p => p.OemNoDisplay == oem && !p.IsDiscontinued)
            .Select(p => (long?)p.Id)
            .FirstOrDefaultAsync(ct);

        // 2) fallback: Oem2 匹配
        if (productId == null)
        {
            productId = await _db.Products.AsNoTracking()
                .Where(p => p.Oem2 == oem && !p.IsDiscontinued)
                .Select(p => (long?)p.Id)
                .FirstOrDefaultAsync(ct);
        }

        // 3) fallback: Mr1 匹配
        if (productId == null)
        {
            productId = await _db.Products.AsNoTracking()
                .Where(p => p.Mr1 == oem && !p.IsDiscontinued)
                .Select(p => (long?)p.Id)
                .FirstOrDefaultAsync(ct);
        }

        if (productId == null)
        {
            _logger.LogInformation("GetBySlug: 404 slug={Slug} oem={Oem}", slug, oem);
            return NotFound(new { error = $"产品不存在: {slug}" });
        }

        // 复用 AdminProductService.GetByIdAsync: 投影逻辑统一
        var detail = await _adminService.GetByIdAsync(productId.Value, ct);
        _logger.LogInformation("GetBySlug: 200 slug={Slug} id={Id}", slug, productId);
        return Ok(detail);
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
