using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// DictMachine 字典服务 (Day 10+ P2.2)
/// 用途: 多字段字典 (3 字段: machine_brand + machine_model + machine_name)
/// 设计:
///   - 主值字段 MachineBrand, List/Typeahead 走 3 字段 OR 匹配
/// </summary>
public class MachineDictService : BaseDictService<DictMachine>
{
    public MachineDictService(ProductDbContext db, ILogger<MachineDictService> logger)
        : base(db, logger, tableName: "dict_machine", maxLength: 200) { }

    protected override string ValueProperty => "MachineBrand";
    protected override string SortOrderProperty => "SortOrder";
    protected override string DeletedAtProperty => "DeletedAt";
    protected override DbSet<DictMachine> Set(ProductDbContext ctx) => ctx.DictMachines;

    // P2.2 多字段扩展: List/Typeahead 走 3 字段 OR 匹配
    protected override IReadOnlyList<string> ExtraSearchProperties => new[] { "MachineModel", "MachineName" };

    protected override string GetValue(DictMachine item) => item.MachineBrand;
    protected override void SetValue(DictMachine item, string value) => item.MachineBrand = value;
    protected override int GetSortOrder(DictMachine item) => item.SortOrder;
    protected override void SetSortOrder(DictMachine item, int sortOrder) => item.SortOrder = sortOrder;
    protected override DateTime? GetDeletedAt(DictMachine item) => item.DeletedAt;
    protected override void SetDeletedAt(DictMachine item, DateTime? deletedAt) => item.DeletedAt = deletedAt;
    protected override long GetId(DictMachine item) => item.Id;

    // 业务: xrefCount 实时聚合 machine_applications.machine_brand
    public override async Task<long> GetXrefCountAsync(string value, CancellationToken ct = default)
        => await _db.MachineApplications.AsNoTracking()
            .CountAsync(m => m.MachineBrand == value, ct);

    public async Task<List<MachineItem>> ListMachinesAsync(
        string? q, bool includeDeleted, int? limit, CancellationToken ct = default)
    {
        var rows = await ListAsync(q, includeDeleted, limit, ct);
        return rows.Select(b => new MachineItem(
            b.Id, b.MachineBrand, b.MachineModel, b.MachineName, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0)).ToList();
    }

    public async Task<List<MachineTypeaheadItem>> TypeaheadMachinesAsync(
        string? q, int? limit, CancellationToken ct = default)
    {
        var rows = await TypeaheadAsync(q, limit ?? 20, ct);
        return rows.Select(b => new MachineTypeaheadItem(b.Id, b.MachineBrand, b.MachineModel, b.MachineName)).ToList();
    }

    public async Task<MachineItem> CreateMachineAsync(
        string brand, string? model, string? name, int? sortOrder, CancellationToken ct = default)
    {
        var b = await CreateAsync(brand, sortOrder, ct);
        b.MachineModel = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        b.MachineName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        await _db.SaveChangesAsync(ct);
        return new MachineItem(b.Id, b.MachineBrand, b.MachineModel, b.MachineName, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0);
    }

    public async Task<MachineItem> UpdateMachineAsync(
        long id, string? brand, string? model, string? name, int? sortOrder, CancellationToken ct = default)
    {
        var b = await UpdateAsync(id, brand, sortOrder, ct);
        if (model != null) b.MachineModel = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if (name != null) b.MachineName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        await _db.SaveChangesAsync(ct);
        var cnt = await GetXrefCountAsync(b.MachineBrand, ct);
        return new MachineItem(b.Id, b.MachineBrand, b.MachineModel, b.MachineName, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task DeleteMachineAsync(long id, CancellationToken ct = default) => DeleteAsync(id, ct);

    public async Task<MachineItem> RestoreMachineAsync(long id, CancellationToken ct = default)
    {
        var b = await RestoreAsync(id, ct);
        var cnt = await GetXrefCountAsync(b.MachineBrand, ct);
        return new MachineItem(b.Id, b.MachineBrand, b.MachineModel, b.MachineName, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task ReorderMachinesAsync(List<MachineReorderItem> items, CancellationToken ct = default)
        => ReorderAsync(items.Select(i => (i.Id, i.SortOrder)).ToList(), ct);
}

public record MachineItem(
    long Id, string MachineBrand, string? MachineModel, string? MachineName, int SortOrder,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? DeletedAt, long XrefCount);
public record MachineTypeaheadItem(long Id, string MachineBrand, string? MachineModel, string? MachineName);
public record MachineReorderItem(long Id, int SortOrder);
public record MachineReorderRequest(List<MachineReorderItem> Items);
public record MachineCreateRequest(string MachineBrand, string? MachineModel, string? MachineName, int? SortOrder);
public record MachineUpdateRequest(string? MachineBrand, string? MachineModel, string? MachineName, int? SortOrder);
