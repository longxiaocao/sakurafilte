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
            b.Id, b.MachineBrand, b.MachineModel, b.MachineName, b.MachineCategory, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0)).ToList();
    }

    // P2.3: 按 category 过滤的 active machine 列表 (4 大类: Agriculture/Commercial/Construction/others)
    public async Task<List<MachineItem>> ListMachinesByCategoryAsync(
        string category, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(category))
            return new List<MachineItem>();
        var rows = await _db.DictMachines.AsNoTracking()
            .Where(m => m.DeletedAt == null && m.MachineCategory == category)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.MachineBrand)
            .ToListAsync(ct);
        return rows.Select(b => new MachineItem(
            b.Id, b.MachineBrand, b.MachineModel, b.MachineName, b.MachineCategory, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0)).ToList();
    }

    // P2.3: 更新指定 machine 的 category 字段
    public async Task UpdateMachineCategoryAsync(long id, string category, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("category 不能为空");
        if (category != "Agriculture" && category != "Commercial"
            && category != "Construction" && category != "others")
            throw new ArgumentException(
                $"category 必须是 Agriculture/Commercial/Construction/others 之一, 实际: {category}");
        var entity = await _db.DictMachines.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new KeyNotFoundException($"dict_machine id={id} 不存在");
        if (entity.MachineCategory == category)
            return;  // 幂等
        entity.MachineCategory = category;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[dict_machine] 更新 category id={Id} brand={Brand} -> {Category}",
            entity.Id, entity.MachineBrand, category);
    }

    public async Task<List<MachineTypeaheadItem>> TypeaheadMachinesAsync(
        string? q, int? limit, CancellationToken ct = default)
    {
        var rows = await TypeaheadAsync(q, limit ?? 20, ct);
        return rows.Select(b => new MachineTypeaheadItem(b.Id, b.MachineBrand, b.MachineModel, b.MachineName)).ToList();
    }

    // Day 11 Phase 1 BUG FIX B: 加 category 参数 (之前 create 漏传, 与 update 不对称)
    public async Task<MachineItem> CreateMachineAsync(
        string brand, string? model, string? name, int? sortOrder, string? category = null, CancellationToken ct = default)
    {
        var b = await CreateAsync(brand, sortOrder, ct);
        b.MachineModel = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        b.MachineName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        // Day 11 Phase 1 BUG FIX B: create 时也写 category (默认 "others")
        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = category.Trim();
            if (cat != "automobile" && cat != "engineering" && cat != "others")
                throw new ArgumentException($"MachineCategory 必须是 automobile/engineering/others, 实际: {cat}");
            b.MachineCategory = cat;
        }
        await _db.SaveChangesAsync(ct);
        return new MachineItem(b.Id, b.MachineBrand, b.MachineModel, b.MachineName, b.MachineCategory, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0);
    }

    public async Task<MachineItem> UpdateMachineAsync(
        long id, string? brand, string? model, string? name, int? sortOrder, string? category, CancellationToken ct = default)
    {
        var b = await UpdateAsync(id, brand, sortOrder, ct);
        if (model != null) b.MachineModel = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if (name != null) b.MachineName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        // P2.3: category 单独更新, 校验 4 大类
        if (category != null)
        {
            if (category != "Agriculture" && category != "Commercial"
                && category != "Construction" && category != "others")
                throw new ArgumentException(
                    $"category 必须是 Agriculture/Commercial/Construction/others 之一, 实际: {category}");
            if (b.MachineCategory != category)
            {
                b.MachineCategory = category;
                b.UpdatedAt = DateTime.UtcNow;
            }
        }
        await _db.SaveChangesAsync(ct);
        var cnt = await GetXrefCountAsync(b.MachineBrand, ct);
        return new MachineItem(b.Id, b.MachineBrand, b.MachineModel, b.MachineName, b.MachineCategory, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task DeleteMachineAsync(long id, CancellationToken ct = default) => DeleteAsync(id, ct);

    public async Task<MachineItem> RestoreMachineAsync(long id, CancellationToken ct = default)
    {
        var b = await RestoreAsync(id, ct);
        var cnt = await GetXrefCountAsync(b.MachineBrand, ct);
        return new MachineItem(b.Id, b.MachineBrand, b.MachineModel, b.MachineName, b.MachineCategory, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task ReorderMachinesAsync(List<MachineReorderItem> items, CancellationToken ct = default)
        => ReorderAsync(items.Select(i => (i.Id, i.SortOrder)).ToList(), ct);
}

public record MachineItem(
    long Id, string MachineBrand, string? MachineModel, string? MachineName, string MachineCategory, int SortOrder,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? DeletedAt, long XrefCount);
public record MachineTypeaheadItem(long Id, string MachineBrand, string? MachineModel, string? MachineName);
public record MachineReorderItem(long Id, int SortOrder);
public record MachineReorderRequest(List<MachineReorderItem> Items);
// Day 11 Phase 1 BUG FIX B: 补 MachineCategory 字段 (之前 create 漏传, update 有, 不对称)
public record MachineCreateRequest(string MachineBrand, string? MachineModel, string? MachineName, int? SortOrder, string? MachineCategory = null);
// P2.3: 加 MachineCategory 字段, 允许前端在 update 时一并改 category
public record MachineUpdateRequest(
    string? MachineBrand, string? MachineModel, string? MachineName, int? SortOrder, string? MachineCategory = null);
