using SakuraFilter.Core.DTOs;

namespace SakuraFilter.Search;

/// <summary>
/// 搜索提供者抽象 (Strategy Pattern)
/// - MeiliSearchProvider: 主,支持 typo 容错 + facet
/// - PostgresSearchProvider: 兜底,ILike + 范围 (无 typo 容错)
/// - ResilientSearchProvider: 包装,Polly 熔断,主失败自动切兜底
///
/// V2 改造 (Task 0.4):
/// - 索引文档主键从 long Id 改为 string Mr1 (1-10 位字母数字)
/// - ProductIndexDoc record 重写为 Mr1IndexDoc 嵌套结构 (oem_list + machine_list 数组)
/// - DeleteAsync 签名从 IEnumerable&lt;long&gt; ids 改为 IEnumerable&lt;string&gt; mr1s (修复 S19)
/// - 新增扁平化冗余字段 (OemListPublishedBrands/OemBrandsStr 等) 修复 S3-7/S3-8/S3-21
/// </summary>
public interface ISearchProvider
{
    /// <summary>提供者名 (用于日志和 health check)</summary>
    string Name { get; }

    /// <summary>健康检查 (用于 Resilient 包装判断主备切换)</summary>
    Task<bool> HealthCheckAsync(CancellationToken ct = default);

    /// <summary>搜索 (主入口)</summary>
    Task<SearchResult> SearchAsync(SearchRequest req, CancellationToken ct = default);

    /// <summary>
    /// 批量索引 (ETL 调用,失败抛异常由 caller 决定是否重试)
    /// V2: 文档类型从 ProductIndexDoc 改为 Mr1IndexDoc (嵌套结构)
    /// </summary>
    Task IndexAsync(IEnumerable<Mr1IndexDoc> docs, CancellationToken ct = default);

    /// <summary>
    /// 按 MR.1 删除 (后台编辑产品时同步)
    /// V2: 签名从 IEnumerable&lt;long&gt; ids 改为 IEnumerable&lt;string&gt; mr1s (修复 S19)
    /// WHY: V2 主键改为 mr_1 (字符串),删除按主键更准确,避免 id 映射错误
    /// </summary>
    Task DeleteAsync(IEnumerable<string> mr1s, CancellationToken ct = default);
}

/// <summary>
/// V2 索引文档 (Meili/PG 共用,主键 mr_1)
/// 嵌套结构: oem_list + machine_list 数组,支持 Brand 优先级排序和 OEM 3 定位
///
/// 扁平化冗余字段 (修复 S3-7/S3-8/S3-21):
/// - OemListPublishedBrands: 仅含上架 OEM 3 的 brand 去重列表 (修复 S3-7 数组 AND filter 语义)
/// - OemListPublishedNo3s: 仅含上架 OEM 3 的 oem_no_3 去重列表
/// - OemBrandsStr: 所有 OEM brand 空格拼接 (扁平化高亮,修复 S3-21 嵌套数组 _formatted 高亮不完整)
/// - OemNo3sStr: 所有 OEM 3 空格拼接
/// - BrandSortOrderMin: 未软删除 brand 的 sort_order MIN (修复 S3-8 软删除 brand 不参与排序)
/// - OemListSortOrderMin: 上架 OEM 3 的 sort_order MIN (修复 S4-16 Meilisearch 数组排序取首元素非 MIN)
/// </summary>
public record Mr1IndexDoc(
    // ===== 主键 + 顶层字段 =====
    string Mr1,                              // V2 主键 (1-10 位字母数字)
    string? ProductName1,
    string? ProductName2,
    string? Oem2,
    string Type,
    string? Remark,
    string? Media,

    // ===== 尺寸字段 (filterable) =====
    decimal? D1Mm, decimal? D2Mm, decimal? D3Mm, decimal? D4Mm,
    decimal? H1Mm, decimal? H2Mm, decimal? H3Mm, decimal? H4Mm,

    // ===== 状态字段 (filterable) =====
    bool IsPublished,                        // 顶层 MR.1 上架
    bool IsDiscontinued,                     // 顶层 MR.1 下架

    // ===== 嵌套数组 =====
    List<OemListItem> OemList,               // OEM 3 列表 (含已下架,但不含已删除 brand)
    List<MachineListItem> MachineList,       // 机型列表

    // ===== 扁平化冗余字段 (修复 S3-7/S3-8/S3-21/S4-16) =====
    List<string> OemListPublishedBrands,     // 仅上架 OEM 3 的 brand 去重列表
    List<string> OemListPublishedNo3s,       // 仅上架 OEM 3 的 oem_no_3 去重列表
    string OemBrandsStr,                     // "BOSCH MANN NTN" 空格拼接 (S4-13: 分隔符改空格)
    string OemNo3sStr,                       // "F000000001 F000000002" 空格拼接
    int? BrandSortOrderMin,                  // 未软删除 brand 的 sort_order MIN (S4-25: 改 long? NULL)
    int? OemListSortOrderMin,                // 上架 OEM 3 的 sort_order MIN

    // ===== 时间戳 =====
    long UpdatedAtUnix
);

/// <summary>
/// OEM 3 列表项 (嵌套数组元素)
/// WHY 保留软删除 brand 的 OEM 3 (S4-11): D21 决策"cross_references.oem_brand 不加外键,字典软删除后历史数据保留"
/// </summary>
public record OemListItem(
    string? OemBrand,
    string? OemNo3,
    string? Oem2,
    int SortOrder,
    string? MachineType,
    bool IsPublished,
    int? BrandSortOrder                     // 软删除 brand 时为 null (S4-11: CASE WHEN 语义)
);

/// <summary>
/// 机型列表项 (嵌套数组元素)
/// </summary>
public record MachineListItem(
    string? MachineBrand,
    string? MachineModel,
    string? MachineCategory
);
