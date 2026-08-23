using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// DictOemNo3 字典服务 (Day 10+ P2.2)
/// 用途: 单字段字典 (OEM 3, 来源 cross_references.oem_no_3)
/// </summary>
public class OemNo3DictService : BaseDictService<DictOemNo3>
{
    public OemNo3DictService(ProductDbContext db, ILogger<OemNo3DictService> logger)
        : base(db, logger, tableName: "dict_oem_no3", maxLength: 200) { }

    protected override string ValueProperty => "OemNo3";
    protected override string SortOrderProperty => "SortOrder";
    protected override string DeletedAtProperty => "DeletedAt";
    protected override DbSet<DictOemNo3> Set(ProductDbContext ctx) => ctx.DictOemNo3s;

    protected override string GetValue(DictOemNo3 item) => item.OemNo3;
    protected override void SetValue(DictOemNo3 item, string value) => item.OemNo3 = value;
    protected override int GetSortOrder(DictOemNo3 item) => item.SortOrder;
    protected override void SetSortOrder(DictOemNo3 item, int sortOrder) => item.SortOrder = sortOrder;
    protected override DateTime? GetDeletedAt(DictOemNo3 item) => item.DeletedAt;
    protected override void SetDeletedAt(DictOemNo3 item, DateTime? deletedAt) => item.DeletedAt = deletedAt;
    protected override long GetId(DictOemNo3 item) => item.Id;

    // 业务: xrefCount 实时聚合 cross_references.oem_no_3
    public override async Task<long> GetXrefCountAsync(string value, CancellationToken ct = default)
        => await _db.CrossReferences.AsNoTracking()
            .CountAsync(x => x.OemNo3 == value, ct);

    public async Task<List<OemNo3Item>> ListOemNo3sAsync(
        string? q, bool includeDeleted, int? limit, CancellationToken ct = default)
    {
        var rows = await ListAsync(q, includeDeleted, limit, ct);
        return rows.Select(b => new OemNo3Item(
            b.Id, b.OemNo3, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0)).ToList();
    }

    public async Task<List<OemNo3TypeaheadItem>> TypeaheadOemNo3sAsync(
        string? q, int? limit, CancellationToken ct = default)
    {
        var rows = await TypeaheadAsync(q, limit ?? 20, ct);
        return rows.Select(b => new OemNo3TypeaheadItem(b.Id, b.OemNo3)).ToList();
    }

    public async Task<OemNo3Item> CreateOemNo3Async(string v, int? sortOrder, CancellationToken ct = default)
    {
        var b = await CreateAsync(v, sortOrder, ct);
        return new OemNo3Item(b.Id, b.OemNo3, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0);
    }

    public async Task<OemNo3Item> UpdateOemNo3Async(
        long id, string? v, int? sortOrder, CancellationToken ct = default)
    {
        var b = await UpdateAsync(id, v, sortOrder, ct);
        var cnt = await GetXrefCountAsync(b.OemNo3, ct);
        return new OemNo3Item(b.Id, b.OemNo3, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task DeleteOemNo3Async(long id, CancellationToken ct = default) => DeleteAsync(id, ct);

    public async Task<OemNo3Item> RestoreOemNo3Async(long id, CancellationToken ct = default)
    {
        var b = await RestoreAsync(id, ct);
        var cnt = await GetXrefCountAsync(b.OemNo3, ct);
        return new OemNo3Item(b.Id, b.OemNo3, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task ReorderOemNo3sAsync(List<OemNo3ReorderItem> items, CancellationToken ct = default)
        => ReorderAsync(items.Select(i => (i.Id, i.SortOrder)).ToList(), ct);
}

public record OemNo3Item(
    long Id, string OemNo3, int SortOrder,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? DeletedAt, long XrefCount);
public record OemNo3TypeaheadItem(long Id, string OemNo3);
public record OemNo3ReorderItem(long Id, int SortOrder);
public record OemNo3ReorderRequest(List<OemNo3ReorderItem> Items);
public record OemNo3CreateRequest(string OemNo3, int? SortOrder);
public record OemNo3UpdateRequest(string? OemNo3, int? SortOrder);
