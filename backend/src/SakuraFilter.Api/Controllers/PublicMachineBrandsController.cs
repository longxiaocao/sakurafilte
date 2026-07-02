using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Controllers;

/// <summary>
/// P2.3 (Task 8.4): 公开机型品牌聚合端点 (无需 token)
/// 用途: 前台按 4 大类 (Agriculture/Commercial/Construction/others) 展示活跃 brand
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
        { "Agriculture", "Commercial", "Construction", "others" };

    /// <summary>
    /// 按 4 大类聚合 brand 列表
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
            var cat = r.MachineCategory ?? "others";
            // 容错: category 字段值不在 4 大类时归 'others' (防御 EF 默认值之外的脏数据)
            if (!AllCategories.Contains(cat)) cat = "others";
            if (string.IsNullOrWhiteSpace(r.MachineBrand)) continue;
            if (!seenPerCat[cat].Add(r.MachineBrand)) continue;  // 去重
            byCategory[cat].Add(r.MachineBrand);
        }
        var totalCount = byCategory.Values.Sum(v => v.Count);

        _logger.LogInformation("machine-brands/aggregated: total={Total} (A={A} C={C} K={K} O={O})",
            totalCount,
            byCategory["Agriculture"].Count,
            byCategory["Commercial"].Count,
            byCategory["Construction"].Count,
            byCategory["others"].Count);

        return Ok(new MachineBrandsAggregatedDto(byCategory, totalCount));
    }
}

/// <summary>P2.3: 按 category 聚合的 brand 响应</summary>
public record MachineBrandsAggregatedDto(
    Dictionary<string, List<string>> ByCategory,
    int TotalCount
);
