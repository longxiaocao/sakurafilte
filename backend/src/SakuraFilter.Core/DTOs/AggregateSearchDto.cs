namespace SakuraFilter.Core.DTOs;

// V2 Task 1.2: 聚合搜索 DTO (POST /api/public/search/aggregate)
//   与 spec.md "POST /api/public/search/aggregate" 契约一致
//   设计: 文档级返回 (mr1 + oemList 数组 + _formatted 高亮 + _rankingScore)

/// <summary>
/// 聚合搜索请求
/// </summary>
public record AggregateSearchRequest(
    /// <summary>模糊关键词 (OEM / brand / model / 产品名)</summary>
    string? Q,
    int Page = 1,
    int PageSize = 20,
    /// <summary>尺寸容差 (±mm),用户可调 1/5/10</summary>
    decimal Tolerance = 5,
    bool IncludeDiscontinued = false,
    /// <summary>机型分类筛选 (agriculture/commercial/construction/industrial/others)</summary>
    string? MachineCategory = null,
    /// <summary>分类筛选 (oil/fuel/air/cabin/others)</summary>
    string? Type = null,
    decimal? D1 = null,
    decimal? D2 = null,
    decimal? D3 = null,
    decimal? H1 = null,
    decimal? H2 = null,
    decimal? H3 = null,
    // v24 修复: 螺纹规格 (与 SearchRequest.D7Thread/D8Thread 对齐, 文本精确匹配)
    string? D7Thread = null,
    string? D8Thread = null,
    // W4 (2026-10-01 走查): 8 字段多框搜索并入聚合搜索页
    //   WHY: 原 8 字段搜索在独立页 /public/search (GET /api/public/search), 与聚合搜索页
    //     条件面板割裂; 合并为"高级搜索与筛选"后统一走本端点。
    //   语义与 EightField 完全一致: 各字段 AND 收窄, 空字段不参与;
    //   oemBrand/oemNo3 走 cross_references, machine* 5 字段走 machine_applications, oemNo2 走产品主表。
    //   注意: 这 8 字段不在 Meili filterableAttributes 中 → 任一非空时控制器改走 PG 精确过滤。
    string? OemBrand = null,
    string? OemNo2 = null,
    string? OemNo3 = null,
    string? MachineBrand = null,
    string? MachineModel = null,
    string? ModelName = null,
    string? EngineBrand = null,
    string? EngineType = null
);

/// <summary>
/// 聚合搜索单条结果 (文档级,含 oemList 嵌套数组)
/// </summary>
public record AggregateSearchHit(
    /// <summary>MR.1 主键 (V2 文档级主键)</summary>
    string Mr1,
    string? ProductName1,
    string? ProductName2,
    string? Oem2,
    string Type,
    string? Remark,
    string? Media,
    bool IsPublished,
    bool IsDiscontinued,
    /// <summary>OEM 3 列表 (已按 brand_sort_order → sort_order 排序)</summary>
    List<AggregateOemItem> OemList,
    /// <summary>机型列表 (去重,最多 50)</summary>
    List<AggregateMachineItem> MachineList,
    /// <summary>Meilisearch 高亮字段 (XSS 已防御,只允许 &lt;mark&gt; 标签)</summary>
    Dictionary<string, object?>? Formatted,
    /// <summary>相关性评分 (Meilisearch 0-1,PG 兜底固定 0.5)</summary>
    double? RankingScore
);

/// <summary>OEM 3 嵌套项 (聚合搜索响应)</summary>
public record AggregateOemItem(
    string? OemBrand,
    string? OemNo3,
    string? Oem2,
    int SortOrder,
    string? MachineType,
    bool IsPublished,
    int? BrandSortOrder
);

/// <summary>机型嵌套项 (聚合搜索响应)</summary>
public record AggregateMachineItem(
    string? MachineBrand,
    string? MachineModel,
    string? MachineCategory,
    string? EngineBrand
);

/// <summary>
/// 聚合搜索响应
/// </summary>
public record AggregateSearchResponse(
    long Total,
    int Page,
    int PageSize,
    int TotalPages,
    int ProcessingTimeMs,
    /// <summary>"meilisearch" / "postgres" (标识实际命中哪个 provider)</summary>
    string Provider,
    List<AggregateSearchHit> Hits
);
