using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// DictType 字典服务 (Day 10+ P2.2)
/// 用途: 单字段字典 (Type, 5 固定值: oil/fuel/air/cabin/others)
/// </summary>
public class TypeDictService : BaseDictService<DictType>
{
    public TypeDictService(ProductDbContext db, ILogger<TypeDictService> logger)
        : base(db, logger, tableName: "dict_type", maxLength: 50) { }

    protected override string ValueProperty => "Type";
    protected override string SortOrderProperty => "SortOrder";
    protected override string DeletedAtProperty => "DeletedAt";
    protected override DbSet<DictType> Set(ProductDbContext ctx) => ctx.DictTypes;

    protected override string GetValue(DictType item) => item.Type;
    protected override void SetValue(DictType item, string value) => item.Type = value;
    protected override int GetSortOrder(DictType item) => item.SortOrder;
    protected override void SetSortOrder(DictType item, int sortOrder) => item.SortOrder = sortOrder;
    protected override DateTime? GetDeletedAt(DictType item) => item.DeletedAt;
    protected override void SetDeletedAt(DictType item, DateTime? deletedAt) => item.DeletedAt = deletedAt;
    protected override long GetId(DictType item) => item.Id;

    // 业务: xrefCount 实时聚合 products.type
    public override async Task<long> GetXrefCountAsync(string value, CancellationToken ct = default)
        => await _db.Products.AsNoTracking()
            .CountAsync(p => p.Type == value, ct);

    public async Task<List<TypeItem>> ListTypesAsync(
        string? q, bool includeDeleted, int? limit, CancellationToken ct = default)
    {
        var rows = await ListAsync(q, includeDeleted, limit, ct);
        return rows.Select(b => new TypeItem(
            b.Id, b.Type, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0)).ToList();
    }

    public async Task<List<TypeTypeaheadItem>> TypeaheadTypesAsync(
        string? q, int? limit, CancellationToken ct = default)
    {
        var rows = await TypeaheadAsync(q, limit ?? 20, ct);
        return rows.Select(b => new TypeTypeaheadItem(b.Id, b.Type)).ToList();
    }

    public async Task<TypeItem> CreateTypeAsync(string v, int? sortOrder, CancellationToken ct = default)
    {
        var b = await CreateAsync(v, sortOrder, ct);
        return new TypeItem(b.Id, b.Type, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0);
    }

    public async Task<TypeItem> UpdateTypeAsync(
        long id, string? v, int? sortOrder, CancellationToken ct = default)
    {
        var b = await UpdateAsync(id, v, sortOrder, ct);
        var cnt = await GetXrefCountAsync(b.Type, ct);
        return new TypeItem(b.Id, b.Type, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task DeleteTypeAsync(long id, CancellationToken ct = default) => DeleteAsync(id, ct);

    public async Task<TypeItem> RestoreTypeAsync(long id, CancellationToken ct = default)
    {
        var b = await RestoreAsync(id, ct);
        var cnt = await GetXrefCountAsync(b.Type, ct);
        return new TypeItem(b.Id, b.Type, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task ReorderTypesAsync(List<TypeReorderItem> items, CancellationToken ct = default)
        => ReorderAsync(items.Select(i => (i.Id, i.SortOrder)).ToList(), ct);
}

public record TypeItem(
    long Id, string Type, int SortOrder,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? DeletedAt, long XrefCount);
public record TypeTypeaheadItem(long Id, string Type);
public record TypeReorderItem(long Id, int SortOrder);
public record TypeReorderRequest(List<TypeReorderItem> Items);
public record TypeCreateRequest(string Type, int? SortOrder);
public record TypeUpdateRequest(string? Type, int? SortOrder);
