using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;
using System.Text.Json;

namespace SakuraFilter.Api.Services;

/// <summary>
/// OEM 品牌字典服务 (Day 10+: P2.1 抽象子类)
/// 用途: Day 10 P1.3 实现, P2.1 重构为继承 BaseDictService&lt;XrefOemBrand&gt;
///       业务逻辑仅 override xrefCount 计数, 7 个核心方法委托给基类
///
/// 设计:
///   - List/Typeahead/Create/Update/Restore/Reorder 委托给 BaseDictService
///   - xrefCount 实时聚合 cross_references.oem_brand (避免双写不一致)
///   - API 端点签名保留原 OemBrand*Async 方法名, Day 10 E2E 10/10 不变
/// V24-F51 (spec Task 5.1.21): 字典变更触发 search_index_pending 重建信号
///   - UpdateAsync/DeleteAsync/RestoreAsync 后调用 ApplyChangeAsync
///   - ApplyChangeAsync 查找受影响产品, 写入 search_index_pending 记录
///   - IndexReplayWorker 后台消费, 重建 Meili 文档 (brand_sort_order 字段)
/// </summary>
public class OemBrandDictService : BaseDictService<XrefOemBrand>
{
    public OemBrandDictService(ProductDbContext db, ILogger<OemBrandDictService> logger)
        : base(db, logger, tableName: "xref_oem_brand", maxLength: 100) { }

    // ========== EF Property 反射名 (基类 Where 表达式用) ==========
    protected override string ValueProperty => "Brand";
    protected override string SortOrderProperty => "SortOrder";
    protected override string DeletedAtProperty => "DeletedAt";
    protected override DbSet<XrefOemBrand> Set(ProductDbContext ctx) => ctx.XrefOemBrands;

    // ========== Accessor ==========
    protected override string GetValue(XrefOemBrand item) => item.Brand;
    protected override void SetValue(XrefOemBrand item, string value) => item.Brand = value;
    protected override int GetSortOrder(XrefOemBrand item) => item.SortOrder;
    protected override void SetSortOrder(XrefOemBrand item, int sortOrder) => item.SortOrder = sortOrder;
    protected override DateTime? GetDeletedAt(XrefOemBrand item) => item.DeletedAt;
    protected override void SetDeletedAt(XrefOemBrand item, DateTime? deletedAt) => item.DeletedAt = deletedAt;
    protected override long GetId(XrefOemBrand item) => item.Id;

    // ========== 业务: xrefCount 实时聚合 cross_references ==========
    public override async Task<long> GetXrefCountAsync(string value, CancellationToken ct = default)
        => await _db.CrossReferences.AsNoTracking()
            .CountAsync(x => x.OemBrand == value, ct);

    // ========== DTO 包装 (保留原 ListOemBrandsAsync 等方法名, Day 10 API 端点不变) ==========
    public async Task<List<OemBrandItem>> ListOemBrandsAsync(
        string? q, bool includeDeleted, int? limit, CancellationToken ct = default)
    {
        var rows = await ListAsync(q, includeDeleted, limit, ct);
        // P1-1 修复: 批量查 xref 计数 (1 次 GroupBy SQL 而非 N+1 循环 COUNT)
        //   原方案: foreach 内 GetXrefCountAsync, 200 条触发 200 次 COUNT(*) on cross_references
        //   新方案: 1 次 GroupBy 聚合, 200 次 SQL 降为 1 次
        var brands = rows.Select(r => r.Brand).Distinct().ToList();
        var counts = new Dictionary<string, long>();
        if (brands.Count > 0)
        {
            var brandCounts = await _db.CrossReferences.AsNoTracking()
                .Where(x => x.OemBrand != null && brands.Contains(x.OemBrand))
                .GroupBy(x => x.OemBrand)
                .Select(g => new { Brand = g.Key, Cnt = g.LongCount() })
                .ToListAsync(ct);
            foreach (var bc in brandCounts)
                counts[bc.Brand!] = bc.Cnt;  // CS8604: bc.Brand 来自 GroupBy Key 可能 null, ! 抑制 (Where 已过滤 null)
        }
        return rows.Select(b => new OemBrandItem(
            b.Id, b.Brand, b.SortOrder, b.CreatedAt, b.UpdatedAt, b.DeletedAt,
            counts.TryGetValue(b.Brand, out var c) ? c : 0)).ToList();
    }

    public async Task<List<OemBrandTypeaheadItem>> TypeaheadOemBrandsAsync(
        string? q, int? limit, CancellationToken ct = default)
    {
        var rows = await TypeaheadAsync(q, limit ?? 20, ct);
        return rows.Select(b => new OemBrandTypeaheadItem(b.Id, b.Brand)).ToList();
    }

    public async Task<OemBrandItem> CreateOemBrandAsync(
        string brand, int? sortOrder, CancellationToken ct = default)
    {
        var b = await CreateAsync(brand, sortOrder, ct);
        // V24-F51: 新增品牌无需触发索引重建 (无 xref 引用, brand_sort_order 不影响现有产品)
        return new OemBrandItem(b.Id, b.Brand, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0);
    }

    public async Task<OemBrandItem> UpdateOemBrandAsync(
        long id, string? brand, int? sortOrder, CancellationToken ct = default)
    {
        var b = await UpdateAsync(id, brand, sortOrder, ct);
        var cnt = await GetXrefCountAsync(b.Brand, ct);
        // V24-F51 (spec Task 5.1.21): sort_order 变更影响搜索结果排序, 触发索引重建
        //   WHY: MeiliSearchProvider.BuildMr1DocumentAsync L470 实时查 XrefOemBrands.SortOrder,
        //        字典 sort_order 变更后, 受影响产品的 Meili 文档需重建才能反映新排序
        //   brand 重命名也触发 (xref.oem_brand 未变, 但旧 brand 的 BrandSortOrder 已变)
        await ApplyChangeAsync(b.Brand, isDeleted: false, ct);
        return new OemBrandItem(b.Id, b.Brand, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public async Task DeleteOemBrandAsync(long id, CancellationToken ct = default)
    {
        // V24-F51: 软删前先查 brand 值 (软删后仍可读, 但受影响产品需重建索引)
        //   WHY: 软删后 BuildMr1DocumentAsync L471 过滤 b.DeletedAt == null,
        //        受影响产品的 BrandSortOrder 变 null, 排序变化需重建 Meili 文档
        var entity = await _db.XrefOemBrands.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        var brandValue = entity?.Brand;

        await DeleteAsync(id, ct);

        if (!string.IsNullOrEmpty(brandValue))
        {
            await ApplyChangeAsync(brandValue, isDeleted: true, ct);
        }
    }

    public async Task<OemBrandItem> RestoreOemBrandAsync(
        long id, CancellationToken ct = default)
    {
        var b = await RestoreAsync(id, ct);
        var cnt = await GetXrefCountAsync(b.Brand, ct);
        // V24-F51: 恢复后 BrandSortOrder 从 null 恢复为实际值, 受影响产品需重建索引
        await ApplyChangeAsync(b.Brand, isDeleted: false, ct);
        return new OemBrandItem(b.Id, b.Brand, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task ReorderOemBrandsAsync(
        List<OemBrandReorderItem> items, CancellationToken ct = default)
        => ReorderAsync(items.Select(i => (i.Id, i.SortOrder)).ToList(), ct);

    // ========== V24-F51 (spec Task 5.1.21): 字典变更触发索引重建 ==========
    //
    /// <summary>
    /// 字典变更后, 为所有引用该 brand 的产品写入 search_index_pending 重建信号
    ///   - IndexReplayWorker 后台消费, 调 BuildMr1DocumentAsync 重建 Meili 文档
    ///   - brand_sort_order 字段变更 (sort_order 修改 / 软删 / 恢复) 都会触发
    ///   - 批量写入 (一次 SaveChanges), 避免逐条 INSERT 的 N+1 问题
    /// </summary>
    /// <param name="brand">OEM 品牌名 (cross_references.oem_brand)</param>
    /// <param name="isDeleted">字典是否已软删 (日志用, 不影响 payload)</param>
    /// <param name="ct">取消令牌</param>
    public async Task ApplyChangeAsync(string brand, bool isDeleted, CancellationToken ct = default)
    {
        // 1. 查找所有引用该 brand 的产品 ID (distinct, 避免 1 产品多 xref 重复)
        //   WHY distinct: 1 个产品可能有多个 cross_references 行 oem_brand 相同 (不同 oem_no_3)
        var productIds = await _db.CrossReferences
            .AsNoTracking()
            .Where(x => x.OemBrand == brand)
            .Select(x => x.ProductId)
            .Distinct()
            .ToListAsync(ct);

        if (productIds.Count == 0)
        {
            _logger.LogInformation("[{Table}] brand={Brand} 变更 (isDeleted={IsDeleted}) 无受影响产品",
                _tableName, brand, isDeleted);
            return;
        }

        // 2. 查询受影响产品的 Mr1 (用于 payload, IndexReplayWorker 重建时用)
        //   WHY 查 Mr1: search_index_pending.payload 是 Mr1IndexDoc JSON, 需预先构建
        //   优化: 仅查 Mr1 + Id, payload 由 IndexReplayWorker 调 BuildMr1DocumentAsync 构建
        //        (避免此处重复构建 payload, 且 BuildMr1DocumentAsync 内部查 XrefOemBrands 取最新 sort_order)
        var products = await _db.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Mr1 })
            .ToListAsync(ct);

        // 3. 批量写入 search_index_pending (一次 SaveChanges)
        //   WHY Operation="index" 而非 "delete": 字典变更不删除产品, 仅重建文档
        //   payload: 简化为 { "product_id": id, "mr1": "..." }, IndexReplayWorker 内部调 BuildMr1DocumentAsync
        //   注: 当前 IndexReplayWorker 的 payload 格式是完整的 Mr1IndexDoc JSON,
        //       此处写入简化 payload, 由 IndexReplayWorker 检测 product_id 字段后调 BuildMr1DocumentAsync 重建
        var now = DateTime.UtcNow;
        var pendingRecords = products.Select(p => new SearchIndexPending
        {
            Operation = "index",
            Payload = JsonSerializer.Serialize(new { product_id = p.Id, mr1 = p.Mr1, trigger = "oem_brand_dict_change" }),
            CreatedAt = now,
            NextRetryAt = now,
            RetryCount = 0
        }).ToList();

        _db.SearchIndexPending.AddRange(pendingRecords);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[{Table}] brand={Brand} 变更 (isDeleted={IsDeleted}) 触发 {Count} 个产品索引重建",
            _tableName, brand, isDeleted, productIds.Count);
    }
}

/// <summary>OEM 品牌字典返回项 (含 xref 出现次数) — 与 Day 10 保持一致</summary>
public record OemBrandItem(
    long Id,
    string Brand,
    int SortOrder,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? DeletedAt,
    long XrefCount
);

/// <summary>OEM 品牌 typeahead 精简项</summary>
public record OemBrandTypeaheadItem(long Id, string Brand);

/// <summary>OEM 品牌重排序输入项</summary>
public record OemBrandReorderItem(long Id, int SortOrder);

/// <summary>OEM 品牌重排序请求体</summary>
public record OemBrandReorderRequest(List<OemBrandReorderItem> Items);
