namespace SakuraFilter.Core.DTOs;

/// <summary>
/// 后台产品高级搜索 DTO (Day 8.2)
/// 用途: 支撑 规格 后台搜索统筹 17 个调取字段
/// 设计:
///   - 扁平字段 (无嵌套), ASP.NET Core query string 直接绑定, 前端表单易构造
///   - 尺寸范围用 D1Min/D1Max 平铺, 容差统一 SizeTolerance (默认 ±5mm)
///   - 批量 OEM 用逗号分隔字符串 (Excel 多行复制黏贴用)
///   - 排序白名单: sortBy 只接受预定义列名
/// </summary>
public record AdminProductSearchRequest
{
    // ===== 分页 =====
    //   Page/PageSize 标 nullable 供 [AsParameters] query string 绑定 (init 属性会变 required)
    public int? Page { get; init; }
    public int? PageSize { get; init; }

    // ===== 软删除/状态 =====
    //   所有非 nullable 字段都标 nullable 供 [AsParameters] query string 绑定 (init 属性会变 required)
    public bool? IncludeDiscontinued { get; init; }
    public bool? IsPublished { get; init; }

    // ===== 规格 R2-R8: 文本字段 (单值模糊) =====
    public string? ProductName1 { get; init; }
    public string? ProductName2 { get; init; }
    public string? Type { get; init; }              // 单 type (oil/fuel/air/cabin/others)
    public string? Mr1 { get; init; }
    public string? Oem2 { get; init; }              // 单 OEM 2 (含归一化匹配)
    public string? OemBrand { get; init; }
    public string? MediaName { get; init; }
    public string? MediaModel { get; init; }
    // Day 8.2.1 补齐: 后台搜索 + 详情同字段, 规格要求筛选支持
    public string? SealingMaterial { get; init; }
    public string? Efficiency1 { get; init; }

    // ===== 规格 R6/R8: 批量 OEM (Excel 多行复制黏贴) =====
    //   格式: "ABC-123,XYZ-456,DEF-789" 走 OR 匹配 (任一命中即返回)
    //   WHY 用 string 而非 List<string>: URL query 简单, 前端 form 一行 textarea 即可
    public string? Oem2Batch { get; init; }
    public string? Oem3Batch { get; init; }

    // ===== 规格 R9-R18: 尺寸范围 (D1-D4, H1-H4, D7, D8) =====
    //   语义: 任一 Min/Max 给出, 走 目标值 ± SizeTolerance 匹配
    //   例: D1Min=90, D1Max=100, Tolerance=5 → D1 ∈ [85, 105] 都命中
    //   WHY 同时支持 Min/Max: 运营常做 "80 ≤ D1 ≤ 100" 区间搜索
    public decimal? D1Min { get; init; }
    public decimal? D1Max { get; init; }
    public decimal? D2Min { get; init; }
    public decimal? D2Max { get; init; }
    public decimal? D3Min { get; init; }
    public decimal? D3Max { get; init; }
    public decimal? D4Min { get; init; }
    public decimal? D4Max { get; init; }
    public decimal? H1Min { get; init; }
    public decimal? H1Max { get; init; }
    public decimal? H2Min { get; init; }
    public decimal? H2Max { get; init; }
    public decimal? H3Min { get; init; }
    public decimal? H3Max { get; init; }
    public decimal? H4Min { get; init; }
    public decimal? H4Max { get; init; }

    // ===== 规格 R17/R18: D7/D8 螺纹 (文本匹配) =====
    public string? D7Thread { get; init; }
    public string? D8Thread { get; init; }

    // ===== 尺寸容差 (用户可调 ±1/±5/±10) =====
    //   WHY 统一: 规格要求 ±5mm, 但 project_memory 硬约束需支持 1/5/10 三档
    public decimal? SizeTolerance { get; init; }

    // ===== 规格 R21-R25: 机型适配字段 (走 cross-table filter, 性能敏感) =====
    public string? MachineBrand { get; init; }
    public string? MachineModel { get; init; }
    public string? ModelName { get; init; }
    public string? EngineBrand { get; init; }
    public string? EngineType { get; init; }

    // ===== 排序 (白名单) =====
    public string? SortBy { get; init; }            // 允许: updated_at / oem / type / id
    public bool? SortDesc { get; init; }

    // Day 8.2.1: 计数模式 (1M+ 大表性能)
    //   - exact: 走 COUNT(*), 准确但慢 (1M 数据 + 17 字段 EXISTS 要 2-5s)
    //   - estimated: 走 PG reltuples 统计, O(1), 误差 ±20% (适合翻页)
    //   - none: 不返回 total, 前端用 hasMore 标记 (推荐, 零成本)
    public string? CountMode { get; init; }        // 允许: exact | estimated | none, 默认 exact

    // Day 8.2.2: 分页模式
    //   - offset: 传统 Page + PageSize 翻页 (前几页 OK, 深度分页慢)
    //   - cursor: keyset 二元组 (updated_at, id) 翻页, 深度分页 O(1)
    //     配套 cursor 参数: 格式 "<ISO8601 updatedAt>|<id>", 末页传 null
    //     限制: cursor 模式强制 sortBy=updated_at DESC (keyset 要求有序键)
    public string? PagingMode { get; init; }       // 允许: offset | cursor, 默认 offset
    public string? Cursor { get; init; }           // cursor 模式下必传 (首页可空)

    // Day 8.3: count 超时阈值 (毫秒, 仅 exact 模式生效)
    //   exact 模式 LongCountAsync 走 17 字段 EXISTS 嵌套, 慢查询可能 2-5s
    //   默认 500ms 超时后降级 estimated, 0 = 不超时
    public int? CountTimeoutMs { get; init; }
}

/// <summary>
/// Day 8.2.1: AdminProductSearchRequest 工具方法, Service + Endpoint 共享逻辑
/// </summary>
public static class AdminProductSearchRequestExtensions
{
    private static readonly HashSet<string> ValidCountModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "exact", "estimated", "none"
    };

    /// <summary>
    /// countMode 归一化: 默认 exact, 非法值降级 exact
    /// </summary>
    public static string NormalizeCountMode(this AdminProductSearchRequest req)
    {
        var mode = req.CountMode ?? "exact";
        return ValidCountModes.Contains(mode) ? mode.ToLowerInvariant() : "exact";
    }

    private static readonly HashSet<string> ValidPagingModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "offset", "cursor"
    };

    /// <summary>
    /// pagingMode 归一化: 默认 offset, 非法值降级 offset
    /// </summary>
    public static string NormalizePagingMode(this AdminProductSearchRequest req)
    {
        var mode = req.PagingMode ?? "offset";
        return ValidPagingModes.Contains(mode) ? mode.ToLowerInvariant() : "offset";
    }
}

/// <summary>
/// 规格 R27 显示字段顺序 (产品搜索结果列)
/// 顺序: MR.1 | OEM 2 | OEM 3 | H1-H4 | D1-D4 | D7 | D8 | Media | MediaModel | remark | 包装 | 体积
/// </summary>
public static class ProductListColumns
{
    public static readonly string[] Default =
    {
        "id", "oem_no_display", "mr1", "oem2", "type",
        "d1_mm", "d2_mm", "d3_mm", "d4_mm",
        "h1_mm", "h2_mm", "h3_mm", "h4_mm",
        "d7_thread", "d8_thread",
        "media", "media_model", "remark",
        "qty_per_carton", "weight_kgs",
        "carton_length_mm", "carton_width_mm", "carton_height_mm",
        "volume_per_carton_m3", "is_published", "is_discontinued", "updated_at"
    };

    public static readonly HashSet<string> SortWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "oem_no_display", "type", "updated_at", "mr1"
    };
}
