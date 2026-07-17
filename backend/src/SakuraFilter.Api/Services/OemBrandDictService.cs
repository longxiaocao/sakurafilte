using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

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
        return new OemBrandItem(b.Id, b.Brand, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0);
    }

    public async Task<OemBrandItem> UpdateOemBrandAsync(
        long id, string? brand, int? sortOrder, CancellationToken ct = default)
    {
        var b = await UpdateAsync(id, brand, sortOrder, ct);
        var cnt = await GetXrefCountAsync(b.Brand, ct);
        return new OemBrandItem(b.Id, b.Brand, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task DeleteOemBrandAsync(long id, CancellationToken ct = default)
        => DeleteAsync(id, ct);

    public async Task<OemBrandItem> RestoreOemBrandAsync(
        long id, CancellationToken ct = default)
    {
        var b = await RestoreAsync(id, ct);
        var cnt = await GetXrefCountAsync(b.Brand, ct);
        return new OemBrandItem(b.Id, b.Brand, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task ReorderOemBrandsAsync(
        List<OemBrandReorderItem> items, CancellationToken ct = default)
        => ReorderAsync(items.Select(i => (i.Id, i.SortOrder)).ToList(), ct);
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
