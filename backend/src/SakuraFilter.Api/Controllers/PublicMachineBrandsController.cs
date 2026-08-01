using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Controllers;

/// <summary>
/// P2.3 (Task 8.4): 公开机型品牌聚合端点 (无需 token)
/// 用途: 前台按 5 大类 (Agriculture/Commercial/Construction/Industrial/others) 展示活跃 brand
/// 设计:
///   - 仅含 active (deleted_at IS NULL) 的 dict_machine
///   - 去重: 同 brand 多次出现 (因 model/name 不同) 只返一次
///   - 4 大类一定有 key, 即使空列表也返 (前端不用判空)
///   - brand 按 sort_order 升序, 再按字母序
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/machine-brands")]
public class PublicMachineBrandsController : ControllerBase
{
    private readonly ProductDbContext _db;
    private readonly ILogger<PublicMachineBrandsController> _logger;

    public PublicMachineBrandsController(ProductDbContext db, ILogger<PublicMachineBrandsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private static readonly string[] AllCategories =
        { "Agriculture", "Commercial", "Construction", "Industrial", "others" };

    /// <summary>
    /// 按 5 大类聚合 brand 列表
    /// 返: MachineBrandsAggregatedDto:
    ///   { byCategory: { "Agriculture": [...], "Commercial": [...], ... }, totalCount: N }
    /// 4 大类 key 一定存在, 即使空列表也返 []
    /// </summary>
    [HttpGet("aggregated")]
    public async Task<IActionResult> Aggregated(CancellationToken ct)
    {
        // 单次 SQL 拉所有 active brand + category
        //   用 SortOrder + Brand 排序, EF Core 翻译为 ORDER BY sort_order, machine_brand
        var rows = await _db.DictMachines.AsNoTracking()
            .Where(m => m.DeletedAt == null)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.MachineBrand)
            .Select(m => new { m.MachineBrand, m.MachineCategory })
            .ToListAsync(ct);

        // 内存按 category 分组 + brand 去重
        //   不用 EF GroupBy: 翻译复杂, PG distinct on 写起来不直观, 内存分组对 < 1000 行足够
        //   去重: HashSet<string> 用 OrdinalIgnoreCase 比较 (BOSCH / bosch 视为同一 brand)
        var byCategory = AllCategories.ToDictionary(c => c, _ => new List<string>());
        var seenPerCat = AllCategories.ToDictionary(
            c => c, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        foreach (var r in rows)
        {
            var cat = NormalizeCategory(r.MachineCategory);
            if (string.IsNullOrWhiteSpace(r.MachineBrand)) continue;
            if (!seenPerCat[cat].Add(r.MachineBrand)) continue;  // 去重
            byCategory[cat].Add(r.MachineBrand);
        }
        var totalCount = byCategory.Values.Sum(v => v.Count);

        _logger.LogInformation("machine-brands/aggregated: total={Total} (A={A} C={C} K={K} I={I} O={O})",
            totalCount,
            byCategory["Agriculture"].Count,
            byCategory["Commercial"].Count,
            byCategory["Construction"].Count,
            byCategory["Industrial"].Count,
            byCategory["others"].Count);

        return Ok(new MachineBrandsAggregatedDto(byCategory, totalCount));
    }

    /// <summary>
    /// 项目规划V2公开 Catalog：Machine Type -> Machine Brand -> Machine Model。
    /// 仅返回未删除的字典项；模型为空的品牌仍保留，避免隐藏可检索品牌。
    /// </summary>
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken ct)
    {
        var rows = await _db.DictMachines.AsNoTracking()
            .Where(m => m.DeletedAt == null && m.MachineBrand != "")
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.MachineBrand)
            .ThenBy(m => m.MachineModel)
            .Select(m => new { m.MachineCategory, m.MachineBrand, m.MachineModel })
            .ToListAsync(ct);

        var categories = AllCategories.Select(category =>
        {
            var brands = rows
                .Where(row => NormalizeCategory(row.MachineCategory) == category)
                .GroupBy(row => row.MachineBrand, StringComparer.OrdinalIgnoreCase)
                .Select(group => new MachineCatalogBrandDto(
                    group.First().MachineBrand,
                    group.Select(row => row.MachineModel)
                        .Where(model => !string.IsNullOrWhiteSpace(model))
                        .Select(model => model!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()))
                .ToList();
            return new MachineCatalogCategoryDto(category, brands);
        }).ToList();

        return Ok(new MachineCatalogDto(categories));
    }

    private static string NormalizeCategory(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "agriculture" => "Agriculture",
        "commercial" or "commercial vehicle" or "commercial vehicles" => "Commercial",
        "construction" or "construction equipment" => "Construction",
        "industrial" => "Industrial",
        _ => "others"
    };
}

/// <summary>P2.3: 按 category 聚合的 brand 响应</summary>
public record MachineBrandsAggregatedDto(
    Dictionary<string, List<string>> ByCategory,
    int TotalCount
);

public record MachineCatalogDto(List<MachineCatalogCategoryDto> Categories);
public record MachineCatalogCategoryDto(string Category, List<MachineCatalogBrandDto> Brands);
public record MachineCatalogBrandDto(string Brand, List<string> Models);
