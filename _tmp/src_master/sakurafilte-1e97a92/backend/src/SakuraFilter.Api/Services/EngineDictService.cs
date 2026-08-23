using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// DictEngine 字典服务 (Day 10+ P2.2)
/// 用途: 多字段字典 (2 字段: engine_brand + engine_type)
/// 设计:
///   - 主值字段 EngineBrand, List/Typeahead 走 2 字段 OR 匹配
/// </summary>
public class EngineDictService : BaseDictService<DictEngine>
{
    public EngineDictService(ProductDbContext db, ILogger<EngineDictService> logger)
        : base(db, logger, tableName: "dict_engine", maxLength: 200) { }

    protected override string ValueProperty => "EngineBrand";
    protected override string SortOrderProperty => "SortOrder";
    protected override string DeletedAtProperty => "DeletedAt";
    protected override DbSet<DictEngine> Set(ProductDbContext ctx) => ctx.DictEngines;

    // P2.2 多字段扩展: List/Typeahead 走 EngineBrand + EngineType OR 匹配
    protected override IReadOnlyList<string> ExtraSearchProperties => new[] { "EngineType" };

    protected override string GetValue(DictEngine item) => item.EngineBrand;
    protected override void SetValue(DictEngine item, string value) => item.EngineBrand = value;
    protected override int GetSortOrder(DictEngine item) => item.SortOrder;
    protected override void SetSortOrder(DictEngine item, int sortOrder) => item.SortOrder = sortOrder;
    protected override DateTime? GetDeletedAt(DictEngine item) => item.DeletedAt;
    protected override void SetDeletedAt(DictEngine item, DateTime? deletedAt) => item.DeletedAt = deletedAt;
    protected override long GetId(DictEngine item) => item.Id;

    // 业务: xrefCount 实时聚合 machine_applications.engine_brand
    public override async Task<long> GetXrefCountAsync(string value, CancellationToken ct = default)
        => await _db.MachineApplications.AsNoTracking()
            .CountAsync(m => m.EngineBrand == value, ct);

    public async Task<List<EngineItem>> ListEnginesAsync(
        string? q, bool includeDeleted, int? limit, CancellationToken ct = default)
    {
        var rows = await ListAsync(q, includeDeleted, limit, ct);
        return rows.Select(b => new EngineItem(
            b.Id, b.EngineBrand, b.EngineType, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0)).ToList();
    }

    public async Task<List<EngineTypeaheadItem>> TypeaheadEnginesAsync(
        string? q, int? limit, CancellationToken ct = default)
    {
        var rows = await TypeaheadAsync(q, limit ?? 20, ct);
        return rows.Select(b => new EngineTypeaheadItem(b.Id, b.EngineBrand, b.EngineType)).ToList();
    }

    public async Task<EngineItem> CreateEngineAsync(
        string brand, string? type, int? sortOrder, CancellationToken ct = default)
    {
        var b = await CreateAsync(brand, sortOrder, ct);
        b.EngineType = string.IsNullOrWhiteSpace(type) ? null : type.Trim();
        await _db.SaveChangesAsync(ct);
        return new EngineItem(b.Id, b.EngineBrand, b.EngineType, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0);
    }

    public async Task<EngineItem> UpdateEngineAsync(
        long id, string? brand, string? type, int? sortOrder, CancellationToken ct = default)
    {
        var b = await UpdateAsync(id, brand, sortOrder, ct);
        if (type != null) b.EngineType = string.IsNullOrWhiteSpace(type) ? null : type.Trim();
        await _db.SaveChangesAsync(ct);
        var cnt = await GetXrefCountAsync(b.EngineBrand, ct);
        return new EngineItem(b.Id, b.EngineBrand, b.EngineType, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task DeleteEngineAsync(long id, CancellationToken ct = default) => DeleteAsync(id, ct);

    public async Task<EngineItem> RestoreEngineAsync(long id, CancellationToken ct = default)
    {
        var b = await RestoreAsync(id, ct);
        var cnt = await GetXrefCountAsync(b.EngineBrand, ct);
        return new EngineItem(b.Id, b.EngineBrand, b.EngineType, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task ReorderEnginesAsync(List<EngineReorderItem> items, CancellationToken ct = default)
        => ReorderAsync(items.Select(i => (i.Id, i.SortOrder)).ToList(), ct);
}

public record EngineItem(
    long Id, string EngineBrand, string? EngineType, int SortOrder,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? DeletedAt, long XrefCount);
public record EngineTypeaheadItem(long Id, string EngineBrand, string? EngineType);
public record EngineReorderItem(long Id, int SortOrder);
public record EngineReorderRequest(List<EngineReorderItem> Items);
public record EngineCreateRequest(string EngineBrand, string? EngineType, int? SortOrder);
public record EngineUpdateRequest(string? EngineBrand, string? EngineType, int? SortOrder);
