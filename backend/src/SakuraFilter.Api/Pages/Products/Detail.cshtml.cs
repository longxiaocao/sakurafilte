using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Pages.Products;

/// <summary>
/// V2 Task 4.1: 产品详情页 Razor PageModel (SSR)
///
/// 设计目标:
///   - 服务端渲染产品详情 HTML, 搜索引擎可直接索引 (修复漏洞 3/11/14/15)
///   - Vue client mount 在浏览器端接管交互层 (非 hydration 模式)
///   - 复用 IProductDetailService 公共服务 (修复 F19), 与 PublicProductController 查询逻辑统一
///
/// 路由: /products/{pn1}/{pn2}/{brand}/{oem3}
///   - oem3 段为核心匹配键 (调 IProductDetailService.GetBySlugSegmentsAsync)
///   - pn1/pn2/brand 段用于二次校验 (防止 SEO URL 误导), 不参与主查询
///
/// 安全:
///   - 404 渲染友好页 + 站内搜索入口 (修复 F2-16)
///   - JSON 数据岛用 @Json.Serialize (Razor @ 自动 HTML 编码, 修复 F2-1)
///   - 禁用 @Html.Raw, 防止 Product.remark/productName1 中 &lt;/script&gt; 截断攻击
/// </summary>
public class DetailModel : PageModel
{
    private readonly IProductDetailService _detailService;
    private readonly ProductDbContext _db;
    private readonly ILogger<DetailModel> _logger;

    public DetailModel(
        IProductDetailService detailService,
        ProductDbContext db,
        ILogger<DetailModel> logger)
    {
        _detailService = detailService;
        _db = db;
        _logger = logger;
    }

    /// <summary>产品详情 DTO; 查不到时为 null (404 分支渲染)</summary>
    public ProductDetailDto? Product { get; private set; }

    /// <summary>同 MR.1 其他 OEM 3 推荐列表 (前台"同 MR.1 其他品牌"区块)</summary>
    public List<SiblingOem3Item> SiblingOem3List { get; private set; } = new();

    /// <summary>SSR 兜底主图 (取 Product.Images 中 IsPrimary=true 的第一张; 无则 null)</summary>
    public ProductImageInfo? PrimaryImage { get; private set; }

    /// <summary>SEO 标题</summary>
    public string SeoTitle => Product == null
        ? "产品不存在 - SakuraFilter"
        : $"{Product.OemNoDisplay} {Product.ProductName1} {Product.ProductName2}".Trim();

    /// <summary>SEO 描述 (前 160 字符, 超出截断)</summary>
    public string SeoDescription
    {
        get
        {
            if (Product == null) return "产品不存在";
            var parts = new[]
            {
                Product.ProductName1,
                Product.ProductName2,
                $"OEM: {Product.OemNoDisplay}",
                Product.Type,
                Product.D1Mm.HasValue ? $"D1={Product.D1Mm}mm" : null,
                Product.H1Mm.HasValue ? $"H1={Product.H1Mm}mm" : null
            }.Where(x => !string.IsNullOrWhiteSpace(x));
            var desc = string.Join(" | ", parts);
            return desc.Length > 160 ? desc[..160] + "..." : desc;
        }
    }

    /// <summary>规范 URL (用于 canonical link)</summary>
    public string CanonicalUrl => Product == null
        ? $"{Request.Scheme}://{Request.Host}/products/404"
        : $"{Request.Scheme}://{Request.Host}{_detailService.BuildProductUrl(Product)}";

    /// <summary>OG 图像 URL (取主图; 无则站点默认图)</summary>
    public string OgImage => PrimaryImage?.ImageUrl ?? $"{Request.Scheme}://{Request.Host}/static/og-default.png";

    /// <summary>
    /// V2 Task 4.1.1: 详情页入口
    ///   - 调 IProductDetailService.GetBySlugSegmentsAsync (三级 fallback + 二次校验)
    ///   - 查 sibling OEM 3 (同 MR.1 其他上架 OEM 3)
    ///   - 404 时设置状态码 + 渲染友好页 (修复 F2-16)
    /// </summary>
    public async Task<IActionResult> OnGetAsync(
        string pn1, string pn2, string brand, string oem3,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(oem3))
        {
            Response.StatusCode = 404;
            return Page();
        }

        // V2 Task 4.1.1: 调公共 IProductDetailService.GetBySlugSegmentsAsync
        //   WHY 不直接查 DB: 与 PublicProductController 共用查询逻辑, 避免 F19 重复
        Product = await _detailService.GetBySlugSegmentsAsync(pn1, pn2, brand, oem3, ct);

        if (Product == null)
        {
            // F2-16: 404 渲染友好页 + 站内搜索入口 (非裸 404 空响应)
            _logger.LogInformation("Detail OnGetAsync: 404 pn1={Pn1} pn2={Pn2} brand={Brand} oem3={Oem3}",
                pn1, pn2, brand, oem3);
            Response.StatusCode = 404;
            return Page();
        }

        // 取主图 (SSR 兜底 + og:image)
        PrimaryImage = Product.Images?.FirstOrDefault(i => i.IsPrimary)
                       ?? Product.Images?.FirstOrDefault();

        // 查 sibling OEM 3 (同 MR.1 其他上架 OEM 3)
        //   WHY 直接查 DB 而非调 API: SSR 页内避免 HTTP 自调用, 减少网络往返
        if (!string.IsNullOrEmpty(Product.Mr1))
        {
            SiblingOem3List = await LoadSiblingOem3Async(Product.Mr1, Product.OemNoDisplay, ct);
        }

        return Page();
    }

    /// <summary>
    /// 查询同 MR.1 其他上架 OEM 3 (排除当前产品自身)
    ///   - 排序: brand_sort_order → sort_order → oem_no_3 (与 PublicProductController.GetSiblingOem3 一致)
    ///   - 过滤: is_published=true AND is_discontinued=false (前台不展示下架/未发布)
    ///   - 限制: 最多 50 条 (防止极端情况下拖慢 SSR)
    /// </summary>
    private async Task<List<SiblingOem3Item>> LoadSiblingOem3Async(
        string mr1, string currentOemDisplay, CancellationToken ct)
    {
        // V2 Task 4.1.1: LEFT JOIN xref_oem_brands 取 brand_sort_order (软删除 brand 排末尾)
        //   WHY int.MaxValue 兜底: brand 软删除时 brand_sort_order 为 null, 排序时按最大值兜底
        var items = await (
            from x in _db.CrossReferences.AsNoTracking()
            join p in _db.Products.AsNoTracking() on x.ProductId equals p.Id
            where p.Mr1 == mr1
                  && !p.IsDiscontinued
                  && p.IsPublished
                  && !x.IsDiscontinued
                  && x.IsPublished
                  && x.OemNo3 != currentOemDisplay  // 排除当前产品自身
            orderby (_db.XrefOemBrands
                        .Where(b => b.Brand == x.OemBrand && b.DeletedAt == null)
                        .Select(b => (int?)b.SortOrder)
                        .FirstOrDefault() ?? int.MaxValue),
                    x.SortOrder,
                    x.OemNo3
            select new SiblingOem3Item
            {
                OemBrand = x.OemBrand,
                OemNo3 = x.OemNo3,
                Oem2 = x.Oem2,
                SortOrder = x.SortOrder,
                MachineType = x.MachineType,
                ProductName1 = p.ProductName1,
                ProductName2 = p.ProductName2
            }).Take(50).ToListAsync(ct);

        return items;
    }
}

/// <summary>
/// 同 MR.1 其他 OEM 3 推荐项 (SSR 渲染用)
///   - 与 PublicProductController.GetSiblingOem3 返回结构对齐 (字段名 camelCase → PascalCase 适配 C#)
///   - 新增 ProductName1/ProductName2: SSR 拼接 SEO URL 用 (BuildProductUrl 需要 pn1/pn2)
/// </summary>
public class SiblingOem3Item
{
    public string? OemBrand { get; set; }
    public string? OemNo3 { get; set; }
    public string? Oem2 { get; set; }
    public int SortOrder { get; set; }
    public string? MachineType { get; set; }
    public string? ProductName1 { get; set; }
    public string? ProductName2 { get; set; }
}
