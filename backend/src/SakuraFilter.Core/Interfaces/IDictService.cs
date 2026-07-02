namespace SakuraFilter.Core.Interfaces;

/// <summary>
/// 通用字典管理服务接口 (Day 10+ P2.1)
/// 用途: 抽象所有字典 (OEM Brand / Product Name / Type / Media / Machine) 的
///       7 个核心 CRUD 操作,便于 Phase 2 实现 5 个新字典时直接复用
///
/// 设计:
///   - 泛型 TItem = EF Core 实体 (e.g. XrefOemBrand),非 DTO
///   - List / Typeahead / Create / Update / Restore 返 TItem entity;
///     强类型 DTO (含 xrefCount) 转换由子类包装方法负责
///   - 错误语义: ArgumentException → 400, KeyNotFoundException → 404,
///                InvalidOperationException → 409
///   - 7 方法全部 async + CancellationToken
/// </summary>
public interface IDictService<TItem> where TItem : class
{
    /// <summary>列表查询 (后台管理页)</summary>
    /// <param name="keyword">模糊匹配 (转义 LIKE 通配符)</param>
    /// <param name="includeDeleted">true 含软删 (审计场景)</param>
    /// <param name="limit">可选条数限制</param>
    Task<List<TItem>> ListAsync(
        string? keyword, bool includeDeleted, int? limit, CancellationToken ct);

    /// <summary>typeahead 自动补全 (后台表单分区字段用,字段精简)</summary>
    /// <param name="q">前缀模糊 (转义 LIKE 通配符)</param>
    /// <param name="limit">限 N 条 (内部 clamp 1-50)</param>
    Task<List<TItem>> TypeaheadAsync(
        string? q, int limit, CancellationToken ct);

    /// <summary>新增 (重名抛 InvalidOperationException,空值抛 ArgumentException)</summary>
    /// <param name="value">字典值 (已 normalize)</param>
    /// <param name="sortOrder">null = 自动 max+10 步长</param>
    Task<TItem> CreateAsync(
        string value, int? sortOrder, CancellationToken ct);

    /// <summary>更新 (改 value / sortOrder; 不存在抛 KeyNotFoundException;重名抛 InvalidOperationException)</summary>
    Task<TItem> UpdateAsync(
        long id, string? value, int? sortOrder, CancellationToken ct);

    /// <summary>软删除 (不存在 / 已删抛对应异常)</summary>
    Task DeleteAsync(long id, CancellationToken ct);

    /// <summary>恢复软删 (不存在 / 未删抛对应异常; 占用检查)</summary>
    Task<TItem> RestoreAsync(long id, CancellationToken ct);

    /// <summary>批量重排序 (前端拖拽后,部分失败整体回滚)</summary>
    /// <param name="items">(id, sortOrder) 元组列表</param>
    Task ReorderAsync(
        List<(long id, int sortOrder)> items, CancellationToken ct);
}
