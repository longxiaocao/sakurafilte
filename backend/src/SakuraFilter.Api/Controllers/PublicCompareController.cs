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
///   - 返回: { count, items: ProductDetailDto[] }, 与 admin compare 形态一致
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
            .Where(p => idList.Contains(p.Id) && !p.IsDiscontinued)
            .ToListAsync(ct);
        var ordered = idList
            .Select(id => products.FirstOrDefault(p => p.Id == id))
            .Where(p => p != null)
            .Cast<SakuraFilter.Core.Entities.Product>()
            .ToList();

        if (ordered.Count == 0)
            return Ok(new { count = 0, items = Array.Empty<ProductDetailDto>() });

        var matchedIds = ordered.Select(p => p.Id).ToList();

        // 单次查 xref + apps (公开对比是表格视图, 不需要图片)
        //   WHY 复用 AdminProductService.CompareAsync 模式, 不引入图片预签名复杂度
        //   用户需要看图可点击任一列进入 /product/{oem} 详情页 (该页面会查图)
        var xrefs = await _db.CrossReferences.AsNoTracking()
            .Where(x => matchedIds.Contains(x.ProductId))
            .Select(x => new { x.ProductId, x.Id, x.ProductName1, x.OemBrand, x.OemNo3 })
            .ToListAsync(ct);
        var apps = await _db.MachineApplications.AsNoTracking()
            .Where(m => matchedIds.Contains(m.ProductId))
            .ToListAsync(ct);

        var result = new List<ProductDetailDto>();
        foreach (var p in ordered)
        {
            var pXrefs = xrefs.Where(x => x.ProductId == p.Id)
                .Select(x => new XrefInfo(x.Id, x.ProductName1, x.OemBrand, x.OemNo3))
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
            // 公开对比不需要 RowVersion (前台不修改数据), 传 0 即可
            result.Add(new ProductDetailDto(
                p.Id, p.OemNoDisplay, p.Oem2, p.Mr1, p.ProductName1, p.ProductName2,
                p.Type, p.IsPublished, p.Remark,
                0u,  // RowVersion: 公开端点不需要乐观锁
                p.D1Mm, p.D2Mm, p.D3Mm, p.D4Mm,
                p.H1Mm, p.H2Mm, p.H3Mm, p.H4Mm,
                p.D7Thread, p.D8Thread,
                p.NoCheckValves, p.NoBypassValves,
                p.Media, p.MediaModel,
                p.BypassValveLr, p.BypassValveHr,
                p.Efficiency1, p.Efficiency2, p.BypassPressure,
                p.CollapsePressureBar,
                p.SealingMaterial, p.TempRange,
                p.QtyPerCarton, p.WeightKgs,
                p.CartonLengthMm, p.CartonWidthMm, p.CartonHeightMm,
                p.MasterBoxQty, p.MasterBoxWeightKgs,
                p.MasterBoxLengthMm, p.MasterBoxWidthMm, p.MasterBoxHeightMm,
                p.VolumePerCartonM3,
                p.IsDiscontinued, p.CreatedAt, p.UpdatedAt,
                pXrefs, pApps, new List<ProductImageInfo>()
            ));
        }

        _logger.LogInformation("PublicCompare: ids=[{Ids}] returned={Count}",
            string.Join(",", idList), result.Count);
        return Ok(new { count = result.Count, items = result });
    }
}
