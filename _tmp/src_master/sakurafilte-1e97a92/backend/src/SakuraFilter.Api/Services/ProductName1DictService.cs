using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// DictProductName1 字典服务 (Day 10+ P2.2)
/// 用途: 单字段字典 (Product Name 1), 继承 BaseDictService
///       业务逻辑仅 override xrefCount 计数 (来自 products.product_name_1)
/// </summary>
public class ProductName1DictService : BaseDictService<DictProductName1>
{
    public ProductName1DictService(ProductDbContext db, ILogger<ProductName1DictService> logger)
        : base(db, logger, tableName: "dict_product_name1", maxLength: 200) { }

    protected override string ValueProperty => "ProductName1";
    protected override string SortOrderProperty => "SortOrder";
    protected override string DeletedAtProperty => "DeletedAt";
    protected override DbSet<DictProductName1> Set(ProductDbContext ctx) => ctx.DictProductName1s;

    protected override string GetValue(DictProductName1 item) => item.ProductName1;
    protected override void SetValue(DictProductName1 item, string value) => item.ProductName1 = value;
    protected override int GetSortOrder(DictProductName1 item) => item.SortOrder;
    protected override void SetSortOrder(DictProductName1 item, int sortOrder) => item.SortOrder = sortOrder;
    protected override DateTime? GetDeletedAt(DictProductName1 item) => item.DeletedAt;
    protected override void SetDeletedAt(DictProductName1 item, DateTime? deletedAt) => item.DeletedAt = deletedAt;
    protected override long GetId(DictProductName1 item) => item.Id;

    // 业务: xrefCount 实时聚合 products.product_name_1
    public override async Task<long> GetXrefCountAsync(string value, CancellationToken ct = default)
        => await _db.Products.AsNoTracking()
            .CountAsync(p => p.ProductName1 == value, ct);

    // ========== DTO 包装 (与 OemBrandDictService 风格一致) ==========
    public async Task<List<ProductName1Item>> ListProductName1sAsync(
        string? q, bool includeDeleted, int? limit, CancellationToken ct = default)
    {
        var rows = await ListAsync(q, includeDeleted, limit, ct);
        return rows.Select(b => new ProductName1Item(
            b.Id, b.ProductName1, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0)).ToList();
    }

    public async Task<List<ProductName1TypeaheadItem>> TypeaheadProductName1sAsync(
        string? q, int? limit, CancellationToken ct = default)
    {
        var rows = await TypeaheadAsync(q, limit ?? 20, ct);
        return rows.Select(b => new ProductName1TypeaheadItem(b.Id, b.ProductName1)).ToList();
    }

    public async Task<ProductName1Item> CreateProductName1Async(
        string v, int? sortOrder, CancellationToken ct = default)
    {
        var b = await CreateAsync(v, sortOrder, ct);
        return new ProductName1Item(b.Id, b.ProductName1, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0);
    }

    public async Task<ProductName1Item> UpdateProductName1Async(
        long id, string? v, int? sortOrder, CancellationToken ct = default)
    {
        var b = await UpdateAsync(id, v, sortOrder, ct);
        var cnt = await GetXrefCountAsync(b.ProductName1, ct);
        return new ProductName1Item(b.Id, b.ProductName1, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task DeleteProductName1Async(long id, CancellationToken ct = default) => DeleteAsync(id, ct);

    public async Task<ProductName1Item> RestoreProductName1Async(long id, CancellationToken ct = default)
    {
        var b = await RestoreAsync(id, ct);
        var cnt = await GetXrefCountAsync(b.ProductName1, ct);
        return new ProductName1Item(b.Id, b.ProductName1, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task ReorderProductName1sAsync(List<ProductName1ReorderItem> items, CancellationToken ct = default)
        => ReorderAsync(items.Select(i => (i.Id, i.SortOrder)).ToList(), ct);
}

public record ProductName1Item(
    long Id, string ProductName1, int SortOrder,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? DeletedAt, long XrefCount);
public record ProductName1TypeaheadItem(long Id, string ProductName1);
public record ProductName1ReorderItem(long Id, int SortOrder);
public record ProductName1ReorderRequest(List<ProductName1ReorderItem> Items);
public record ProductName1CreateRequest(string ProductName1, int? SortOrder);
public record ProductName1UpdateRequest(string? ProductName1, int? SortOrder);
