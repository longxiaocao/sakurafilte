namespace SakuraFilter.Core.DTOs;

/// <summary>
/// 搜索请求 (用户最常见查询)
/// </summary>
public record SearchRequest(
    string? Q,                  // 模糊关键词 (OEM / brand / model)
    string? Type,               // 分类筛选 (AIR FILTER / OIL FILTER ...)
    decimal? D1,                // 目标外径
    decimal? D2,
    decimal? D3,
    decimal? H1,
    decimal? H2,
    decimal? H3,
    string? D7Thread,           // v24 修复: 螺纹规格 1 (文本精确匹配, 与 Product.D7Thread 对齐)
    string? D8Thread,           // v24 修复: 螺纹规格 2
    decimal Tolerance = 5,      // ±容差 (用户可调 1/5/10)
    bool IncludeDiscontinued = false,
    int Page = 1,
    int PageSize = 20
);

/// <summary>
/// 搜索结果 (产品摘要)
/// </summary>
public record SearchResultItem(
    long Id,
    string OemNoDisplay,
    string? Remark,
    string Type,
    decimal? D1Mm,
    decimal? D2Mm,
    decimal? H1Mm,
    string? ImageKey,
    bool IsDiscontinued
);

public record SearchResult(
    long Total,
    int Page,
    int PageSize,
    int TotalPages,
    int ElapsedMs,
    IEnumerable<SearchResultItem> Items
);

/// <summary>
/// 产品详情 (含交叉引用 + 机型)
/// </summary>
public record ProductDetail(
    long Id,
    string OemNoDisplay,
    string OemNoNormalized,
    string? Remark,
    string Type,
    decimal? D1Mm,
    decimal? D2Mm,
    decimal? D3Mm,
    decimal? H1Mm,
    decimal? H2Mm,
    decimal? H3Mm,
    string? D7Thread,
    string? D8Thread,
    string? Media,
    string? SealingMaterial,
    string? Efficiency1,
    decimal? CollapsePressureBar,
    string? TempRange,
    int? QtyPerCarton,
    decimal? WeightKgs,
    decimal? CartonLengthMm,
    decimal? CartonWidthMm,
    decimal? CartonHeightMm,
    string? ImageKey,
    IEnumerable<CrossReferenceDto> CrossReferences,
    IEnumerable<MachineApplicationDto> MachineApplications
);

public record CrossReferenceDto(string? Brand, string? OemNo, string? ProductName1);
public record MachineApplicationDto(
    string? MachineBrand,
    string? MachineModel,
    string? ModelName,
    string? EngineBrand,
    string? EngineType,
    string? EngineEnergy
);
