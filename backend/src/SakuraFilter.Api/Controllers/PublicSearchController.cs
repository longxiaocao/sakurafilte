using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Controllers;

/// <summary>
/// P3.2 (Task 10): 公开搜索端点 (无需 token, 走 "search" 限流分区)
/// 用途: 前台搜索页面的批量粘贴查询
/// 设计: 与 AdminProductService 共享 ProductDbContext, 走 AsNoTracking 性能最优
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/search")]
public class PublicSearchController : ControllerBase
{
    private readonly ProductDbContext _db;
    private readonly ILogger<PublicSearchController> _logger;

    public PublicSearchController(ProductDbContext db, ILogger<PublicSearchController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 批量 OEM 查询 (Excel 多行粘贴)
    /// 入参: oems (1-500 个字符串, 自动 trim + 去重)
    /// 返: 每个 OEM 一条结果
    ///   命中: {oem, hit=true, productId, oemBrand, productName1, oem2}
    ///   未命中: {oem, hit=false}
    /// 匹配字段: oem_2 (与前台搜索一致)
    /// </summary>
    [HttpPost("batch-oem")]
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

        // 单次 SQL: WHERE oem_2 = ANY(@oems)
        //   EF Core 翻译 distinctOems.Contains(p.Oem2) 为 p.oem_2 = ANY(...)
        //   排除 Oem2=null 行, 避免 "Contains('')" 误匹配
        var candidates = await _db.Products.AsNoTracking()
            .Where(p => p.Oem2 != null && distinctOems.Contains(p.Oem2))
            .Select(p => new
            {
                p.Id,
                p.Oem2,
                p.ProductName1
            })
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            var emptyResults = distinctOems
                .Select(oem => new BatchOemResult(oem, Hit: false))
                .ToList();
            return Ok(new BatchOemResponse(distinctOems.Count, 0, distinctOems.Count, emptyResults));
        }

        // 每个 product 聚合 brand (来自 cross_references)
        var productIds = candidates.Select(c => c.Id).Distinct().ToList();
        var brandGroups = await _db.CrossReferences.AsNoTracking()
            .Where(x => productIds.Contains(x.ProductId) && x.OemBrand != null)
            .GroupBy(x => x.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Brands = g.Select(x => x.OemBrand!).Distinct().ToList()
            })
            .ToListAsync(ct);
        var brandMap = brandGroups.ToDictionary(
            b => b.ProductId,
            b => b.Brands.Count == 1 ? b.Brands[0] : string.Join(", ", b.Brands));

        // 同一 OEM 可能命中多条产品, 取 Id 最小 (最早上架) 作为代表
        var byOem = candidates
            .GroupBy(c => c.Oem2)
            .ToDictionary(g => g.Key!, g => g.OrderBy(x => x.Id).First());

        // 按请求顺序 (distinct 后) 产出
        var results = distinctOems.Select(oem =>
        {
            if (byOem.TryGetValue(oem, out var hit))
            {
                return new BatchOemResult(
                    Oem: oem,
                    Hit: true,
                    ProductId: hit.Id,
                    OemBrand: brandMap.GetValueOrDefault(hit.Id),
                    ProductName1: hit.ProductName1,
                    Oem2: hit.Oem2
                );
            }
            return new BatchOemResult(oem, Hit: false);
        }).ToList();

        var hitCount = results.Count(r => r.Hit);
        _logger.LogInformation("batch-oem: distinct={Total} hit={Hit} miss={Miss}",
            distinctOems.Count, hitCount, distinctOems.Count - hitCount);

        return Ok(new BatchOemResponse(
            Total: distinctOems.Count,
            Hits: hitCount,
            Miss: distinctOems.Count - hitCount,
            Results: results
        ));
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
