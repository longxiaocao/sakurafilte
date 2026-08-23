using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// DictProductName2 字典服务 (Day 10+ P2.2)
/// 用途: 单字段字典 (Product Name 2), 继承 BaseDictService
/// </summary>
public class ProductName2DictService : BaseDictService<DictProductName2>
{
    public ProductName2DictService(ProductDbContext db, ILogger<ProductName2DictService> logger)
        : base(db, logger, tableName: "dict_product_name2", maxLength: 200) { }

    protected override string ValueProperty => "ProductName2";
    protected override string SortOrderProperty => "SortOrder";
    protected override string DeletedAtProperty => "DeletedAt";
    protected override DbSet<DictProductName2> Set(ProductDbContext ctx) => ctx.DictProductName2s;

    protected override string GetValue(DictProductName2 item) => item.ProductName2;
    protected override void SetValue(DictProductName2 item, string value) => item.ProductName2 = value;
    protected override int GetSortOrder(DictProductName2 item) => item.SortOrder;
    protected override void SetSortOrder(DictProductName2 item, int sortOrder) => item.SortOrder = sortOrder;
    protected override DateTime? GetDeletedAt(DictProductName2 item) => item.DeletedAt;
    protected override void SetDeletedAt(DictProductName2 item, DateTime? deletedAt) => item.DeletedAt = deletedAt;
    protected override long GetId(DictProductName2 item) => item.Id;

    // 业务: xrefCount 实时聚合 products.product_name_2
    public override async Task<long> GetXrefCountAsync(string value, CancellationToken ct = default)
        => await _db.Products.AsNoTracking()
            .CountAsync(p => p.ProductName2 == value, ct);

    public async Task<List<ProductName2Item>> ListProductName2sAsync(
        string? q, bool includeDeleted, int? limit, CancellationToken ct = default)
    {
        var rows = await ListAsync(q, includeDeleted, limit, ct);
        return rows.Select(b => new ProductName2Item(
            b.Id, b.ProductName2, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0)).ToList();
    }

    public async Task<List<ProductName2TypeaheadItem>> TypeaheadProductName2sAsync(
        string? q, int? limit, CancellationToken ct = default)
    {
        var rows = await TypeaheadAsync(q, limit ?? 20, ct);
        return rows.Select(b => new ProductName2TypeaheadItem(b.Id, b.ProductName2)).ToList();
    }

    public async Task<ProductName2Item> CreateProductName2Async(
        string v, int? sortOrder, CancellationToken ct = default)
    {
        var b = await CreateAsync(v, sortOrder, ct);
        return new ProductName2Item(b.Id, b.ProductName2, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0);
    }

    public async Task<ProductName2Item> UpdateProductName2Async(
        long id, string? v, int? sortOrder, CancellationToken ct = default)
    {
        var b = await UpdateAsync(id, v, sortOrder, ct);
        var cnt = await GetXrefCountAsync(b.ProductName2, ct);
        return new ProductName2Item(b.Id, b.ProductName2, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task DeleteProductName2Async(long id, CancellationToken ct = default) => DeleteAsync(id, ct);

    public async Task<ProductName2Item> RestoreProductName2Async(long id, CancellationToken ct = default)
    {
        var b = await RestoreAsync(id, ct);
        var cnt = await GetXrefCountAsync(b.ProductName2, ct);
        return new ProductName2Item(b.Id, b.ProductName2, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task ReorderProductName2sAsync(List<ProductName2ReorderItem> items, CancellationToken ct = default)
        => ReorderAsync(items.Select(i => (i.Id, i.SortOrder)).ToList(), ct);
}

public record ProductName2Item(
    long Id, string ProductName2, int SortOrder,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? DeletedAt, long XrefCount);
public record ProductName2TypeaheadItem(long Id, string ProductName2);
public record ProductName2ReorderItem(long Id, int SortOrder);
public record ProductName2ReorderRequest(List<ProductName2ReorderItem> Items);
public record ProductName2CreateRequest(string ProductName2, int? SortOrder);
public record ProductName2UpdateRequest(string? ProductName2, int? SortOrder);
