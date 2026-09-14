using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Controllers;

/// <summary>
/// P0 权限改造 (Day 14): 公开产品对比端点 (无需 token)
/// 设计:
///   - 复刻 AdminProductService.CompareAsync 的查询结构, 但排除 is_discontinued=true
///     (前台不应展示下架产品, 与 PublicProductController.GetBySlug 一致)
///   - 上限 6 个产品, 单次 query + InMemory 分组, 避免 N+1
///   - 返回: { count, items: PublicProductDetailDto[] }，只包含客户字段
///
/// 与 admin/compare 的差异:
///   - 路径: /api/public/compare vs /api/admin/products/compare
///   - 鉴权: 公开 (AllowAnonymous) vs 需 X-Admin-Token / JWT
///   - 过滤: 排除下架 vs 不过滤
///   - 排序: 保持传入 ids 顺序 vs 保持传入 ids 顺序
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public")]
public class PublicCompareController : ControllerBase
{
    private readonly ProductDbContext _db;
    private readonly ILogger<PublicCompareController> _logger;

    public PublicCompareController(
        ProductDbContext db,
        ILogger<PublicCompareController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 批量对比 (公开) - 1-6 个产品
    /// URL: GET /api/public/compare?ids=1,2,3
    /// </summary>
    [HttpGet("compare")]
    public async Task<IActionResult> Compare(
        [FromQuery] string? ids,
        CancellationToken ct = default)
    {
        // 解析 ids: 逗号分隔, 最多 6 个
        if (string.IsNullOrWhiteSpace(ids))
            return BadRequest(new { error = "ids 不能为空" });

        var idList = new List<long>();
        foreach (var s in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!long.TryParse(s, out var id) || id <= 0)
                return BadRequest(new { error = $"非法 id: {s}" });
            idList.Add(id);
        }
        if (idList.Count == 0)
            return BadRequest(new { error = "ids 不能为空" });
        if (idList.Count > 6)
            return BadRequest(new { error = "对比最多 6 个产品", given = idList.Count });

        // 公开版排除下架产品
        var products = await _db.Products.AsNoTracking()
            .Where(p => idList.Contains(p.Id) && p.IsPublished && !p.IsDiscontinued)
            .ToListAsync(ct);
        var ordered = idList
            .Select(id => products.FirstOrDefault(p => p.Id == id))
            .Where(p => p != null)
            .Cast<SakuraFilter.Core.Entities.Product>()
            .ToList();

        if (ordered.Count == 0)
            return Ok(new { count = 0, items = Array.Empty<PublicProductDetailDto>() });

        var matchedIds = ordered.Select(p => p.Id).ToList();

        // 单次查 xref + apps (公开对比是表格视图, 不需要图片)
        //   WHY 复用 AdminProductService.CompareAsync 模式, 不引入图片预签名复杂度
        //   用户需要看图可点击任一列进入 /product/{oem} 详情页 (该页面会查图)
        var xrefs = await (
            from x in _db.CrossReferences.AsNoTracking()
            where matchedIds.Contains(x.ProductId)
            orderby (_db.XrefOemBrands
                        .Where(b => b.Brand == x.OemBrand && b.DeletedAt == null)
                        .Select(b => (int?)b.SortOrder)
                        .FirstOrDefault() ?? int.MaxValue),
                    x.SortOrder,
                    x.OemNo3
            select new { x.ProductId, x.Id, x.ProductName1, x.OemBrand, x.OemNo3, x.Oem2, x.SortOrder, x.MachineType, x.IsPublished, x.RowVersion })
            .ToListAsync(ct);
        var apps = await _db.MachineApplications.AsNoTracking()
            .Where(m => m.ProductId.HasValue && matchedIds.Contains(m.ProductId.Value))
            .ToListAsync(ct);

        // 加载图片 (公开对比缩略图): 单次查 product_images, 每产品取其主图 (slot 升序第一张)。
        //   imageUrl 用后端代理 /api/public/images/{key}：生产 MinIO 端口不对外暴露,
        //   GetPublicUrl 返回的直连 URL 浏览器不可达; 代理端点 (StorageEndpoints) 任何存储下均可访问,
        //   与 PublicProductView 的 key→代理用法保持一致。
        var imageRows = await _db.ProductImages.AsNoTracking()
            .Where(i => matchedIds.Contains(i.ProductId))
            .OrderBy(i => i.ProductId).ThenBy(i => i.Slot)
            .ToListAsync(ct);
        var primaryImageByProduct = imageRows
            .Where(i => !string.IsNullOrEmpty(i.ImageKey))
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Slot).First());

        var result = new List<ProductDetailDto>();
        foreach (var p in ordered)
        {
            var pXrefs = xrefs.Where(x => x.ProductId == p.Id)
                .Select(x => new XrefInfo(x.Id, x.ProductName1, x.OemBrand, x.OemNo3, x.Oem2, x.SortOrder, x.MachineType, x.IsPublished, x.RowVersion))
                .ToList();
            var pApps = apps.Where(m => m.ProductId == p.Id)
                .Select(m => new MachineAppInfo(
                    m.Id, m.MachineBrand, m.MachineModel, m.ModelName,
                    m.EngineBrand, m.EngineType, m.EngineEnergy,
                    m.ProductionDateStart, m.ProductionDateEnd, m.Power,
                    m.SerialNumberFrom, m.SerialNumberTo,
                    m.CarBodyType, m.Series,
                    m.Co2EmissionStandard, m.TransmissionType,
                    m.EngineDisplacement, m.NumberOfCylinders,
                    m.Gvwr, m.Tonnage, m.GeographicArea,
                    m.ChassisType, m.EngineModel,
                    m.CabinType, m.Capacity, m.EngineSerialNumber))
                .ToList();
            // 公开对比: 只带主图 (slot 优先); 无图时空列表 (前端占位, img v-if 隐藏)
            var imgList = new List<ProductImageInfo>();
            if (primaryImageByProduct.TryGetValue(p.Id, out var pi))
            {
                imgList.Add(new ProductImageInfo(0, p.Id, 1, pi.ImageKey,
                    $"/api/public/images/{pi.ImageKey}", pi.FileSize, pi.ContentType,
                    pi.Width, pi.Height, pi.IsPrimary, pi.UploadedAt, pi.UploadedBy, pi.OemNo3, "primary"));
            }
            // 公开对比不需要 RowVersion (前台不修改数据), 传 0 即可
            result.Add(new ProductDetailDto(
                p.Id, p.OemNoDisplay, p.Oem2, p.Mr1, p.ProductName1, p.ProductName2,
                p.Type, p.IsPublished, p.Remark,
                0u,  // RowVersion: 公开端点不需要乐观锁
                p.D1Mm, p.D2Mm, p.D3Mm, p.D4Mm,
                p.H1Mm, p.H2Mm, p.H3Mm, p.H4Mm,
                p.D7Thread, p.D8Thread,
                p.NoCheckValves, p.NoBypassValves,
                p.NoCheckValvesRaw, p.NoBypassValvesRaw,
                p.Media, p.MediaModel,
                p.BypassValveLr, p.BypassValveHr,
                p.Efficiency1, p.Efficiency2, p.BypassPressure,
                p.CollapsePressureBar,
                p.BypassValveLrRaw, p.BypassValveHrRaw, p.BypassPressureRaw, p.CollapsePressureBarRaw,
                p.SealingMaterial, p.TempRange,
                p.QtyPerCarton, p.WeightKgs,
                p.CartonLengthMm, p.CartonWidthMm, p.CartonHeightMm,
                p.MasterBoxQty, p.MasterBoxWeightKgs,
                p.MasterBoxLengthMm, p.MasterBoxWidthMm, p.MasterBoxHeightMm,
                p.VolumePerCartonM3,
                p.IsDiscontinued, p.CreatedAt, p.UpdatedAt,
                pXrefs, pApps, imgList
            ));
        }

        _logger.LogInformation("PublicCompare: ids=[{Ids}] returned={Count}",
            string.Join(",", idList), result.Count);
        // 管理端 DTO 含 MR1、发布状态和审计字段；公开对比必须经过客户契约投影。
        var publicItems = result.Select(PublicProductDetailDto.From).ToList();
        return Ok(new { count = publicItems.Count, items = publicItems });
    }
}
