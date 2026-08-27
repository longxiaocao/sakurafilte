using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SakuraFilter.Api.Extensions;
using SakuraFilter.Core.Entities;
using SakuraFilter.Core.Validation;
using SakuraFilter.Infrastructure.Data;
using System.Text.Json.Serialization;

namespace SakuraFilter.Api.Services;

/// <summary>
/// DictMachine 字典服务 (Day 10+ P2.2)
/// 用途: 多字段字典 (3 字段: machine_brand + machine_model + machine_name)
/// 设计:
///   - 主值字段 MachineBrand, List/Typeahead 走 3 字段 OR 匹配
/// </summary>
public class MachineDictService : BaseDictService<DictMachine>
{
    private readonly IMemoryCache _cache;

    public MachineDictService(ProductDbContext db, ILogger<MachineDictService> logger, IMemoryCache cache)
        : base(db, logger, tableName: "dict_machine", maxLength: 200)
    {
        _cache = cache;
    }

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

    /// <summary>
    /// 查询机型三级树: MachineCategory → MachineBrand → MachineModel
    /// 用途: 前端机型选择级联组件的数据源 (后台管理 + 前台筛选)
    /// 缓存: IMemoryCache 5 分钟, key = "machine_tree" (字典变更频率低, 避免每次聚合查询)
    /// 排序: 每级按字母序 (category asc, brand asc, model asc)
    /// 去重: DISTINCT ON 按 (category, brand, model) 去重, 避免 196 万行全量加载导致卡死
    /// 空数据: 返回空 List, 不返回 null
    /// </summary>
    public async Task<List<MachineTreeNode>> GetTreeAsync(CancellationToken ct = default)
    {
        // 缓存命中直接返回 ( WHY: 字典数据变更频率极低, 5 分钟 TTL 足够; 写操作不会失效缓存,
        //   前端可接受最长 5 分钟延迟, 与 PublicTypeaheadService 缓存策略一致 )
        if (_cache.TryGetValue("machine_tree", out List<MachineTreeNode>? cached) && cached != null)
            return cached;

        // 查询所有未删除的 machine 记录, 数据库层预排序减少内存排序开销
        // WHY DISTINCT: dict_machine 有 196 万行但仅 39.6 万唯一 (category, brand, model) 组合,
        //   全量加载会导致前端 el-tree 卡死 (V24-F105 客户反馈)。
        //   先 GroupBy → Select 在数据库层去重, 避免把 196 万行拉到内存。
        var rows = await _db.DictMachines.AsNoTracking()
            .Where(m => m.DeletedAt == null)
            .GroupBy(m => new { m.MachineCategory, m.MachineBrand, m.MachineModel })
            .Select(g => new {
                MachineCategory = g.Key.MachineCategory,
                MachineBrand = g.Key.MachineBrand,
                MachineModel = g.Key.MachineModel,
                MachineName = g.Select(x => x.MachineName).FirstOrDefault()
            })
            .OrderBy(m => m.MachineCategory)
            .ThenBy(m => m.MachineBrand)
            .ThenBy(m => m.MachineModel)
            .ToListAsync(ct);

        // 分组聚合: category (一级) → brand (二级) → model (三级, 每行一个节点)
        // WHY 内存分组而非 SQL GROUP BY: 三级嵌套结构在 SQL 中需多次 JOIN, 内存 LINQ 更清晰
        var tree = rows
            .GroupBy(m => m.MachineCategory)
            .Select(catGroup => new MachineTreeNode(
                catGroup.Key,
                catGroup
                    .GroupBy(m => m.MachineBrand)
                    .Select(brandGroup => new MachineBrandNode(
                        brandGroup.Key,
                        brandGroup
                            .Select(m => new MachineModelNode(
                                0,  // id 占位, 树节点不需要真实 ID
                                // WHY: machine_model 可能为 null, fallback 到 machine_name 保证前端展示有值
                                m.MachineModel ?? m.MachineName ?? ""))
                            .OrderBy(m => m.ModelName)
                            .ToList()))
                    .OrderBy(b => b.Brand)
                    .ToList()))
            .OrderBy(c => c.Category)
            .ToList();

        // 写缓存: 使用 SetWithSize 扩展方法 (SizeLimit=10000 要求每个 entry 必须指定 Size)
        _cache.SetWithSize("machine_tree", tree, TimeSpan.FromMinutes(5), 100);
        return tree;
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

    // ========== Task 2: 批量绑定 MR.1 到机型 ==========
    // WHY 独立方法: 现有 machine_applications 表按 product_id 关联且含丰富机型字段,
    //   不支持按 dict_machine.id + mr_1 批量绑定; 新增 MachineMr1Binding 表承载此关系
    // 设计:
    //   - 单事务: 查询 + 删除 + 插入原子完成, 避免部分写入
    //   - 幂等: 先查已存在 (machine_id, mr_1) 绑定, 跳过 (等价 ON CONFLICT DO NOTHING)
    //   - replace=true: 先删除该机型所有绑定再插入, 保证最终状态 = mr1List
    //   - replace=false: 增量追加, 已存在的跳过
    public async Task<BatchBindResponse> BatchBindAsync(BatchBindRequest req, string @operator, CancellationToken ct)
    {
        // 校验 1: machineId 必须存在于 dict_machines
        var machineExists = await _db.DictMachines.AsNoTracking()
            .AnyAsync(m => m.Id == req.MachineId, ct);
        if (!machineExists)
            throw new KeyNotFoundException($"MACHINE_NOT_FOUND: 机型 id={req.MachineId} 不存在");

        // 校验 2: mr1List 数量 1-200
        if (req.Mr1List == null || req.Mr1List.Count == 0)
            throw new ArgumentException("BATCH_BIND_LIMIT_EXCEEDED: mr1_list 不能为空");
        if (req.Mr1List.Count > 200)
            throw new ArgumentException(
                $"BATCH_BIND_LIMIT_EXCEEDED: mr1_list 数量不能超过 200 (实际: {req.Mr1List.Count})");

        // 校验 3: 每个 MR.1 格式 ^[A-Za-z0-9]{1,10}$ (复用 Mr1Validator, 空字符串也视为格式无效)
        //   WHY 统一抛 MR1_FORMAT_INVALID: 批量绑定场景中空字符串的 MR.1 是格式问题而非"必填缺失"
        var normalizedMr1List = new List<string>(req.Mr1List.Count);
        foreach (var raw in req.Mr1List)
        {
            var trimmed = raw?.Trim() ?? "";
            if (!Mr1Validator.TryNormalize(trimmed, out var normalized, out _))
                throw new ArgumentException(
                    $"MR1_FORMAT_INVALID: MR.1 必须为 1-10 位字母数字 (实际: '{trimmed}')");
            normalizedMr1List.Add(normalized!);
        }

        // 开启单事务 (参考 AdminProductService.CreateAsync)
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // 查询 mr1List 中实际存在于 products 表的 mr_1
            var existingMr1s = await _db.Products.AsNoTracking()
                .Where(p => p.Mr1 != null && normalizedMr1List.Contains(p.Mr1))
                .Select(p => p.Mr1!)
                .ToListAsync(ct);
            var existingMr1Set = existingMr1s.ToHashSet();
            var notFound = normalizedMr1List.Where(m => !existingMr1Set.Contains(m)).ToList();

            // 若 Replace=true: 先删除该机型的所有绑定 (记录 removed 数)
            int removed = 0;
            if (req.Replace)
            {
                var existingBindings = await _db.MachineMr1Bindings
                    .Where(b => b.MachineId == req.MachineId)
                    .ToListAsync(ct);
                removed = existingBindings.Count;
                if (removed > 0)
                    _db.MachineMr1Bindings.RemoveRange(existingBindings);
            }

            // 幂等插入: 查询已存在的 (machine_id, mr_1) 绑定, 跳过 (等价 ON CONFLICT DO NOTHING)
            //   WHY 先查再插而非依赖 DB 唯一约束: InMemory 测试不支持 ON CONFLICT raw SQL,
            //        先查后插在事务内保证幂等 (READ COMMITTED 下并发场景由唯一约束兜底)
            var alreadyBoundMr1s = req.Replace
                ? new HashSet<string>()  // replace 模式已清空, 无需跳过
                : (await _db.MachineMr1Bindings
                    .Where(b => b.MachineId == req.MachineId && existingMr1s.Contains(b.Mr1))
                    .Select(b => b.Mr1)
                    .ToListAsync(ct)).ToHashSet();

            int bound = 0;
            int skipped = 0;
            foreach (var mr1 in existingMr1s)
            {
                if (alreadyBoundMr1s.Contains(mr1))
                {
                    skipped++;
                }
                else
                {
                    _db.MachineMr1Bindings.Add(new MachineMr1Binding
                    {
                        MachineId = req.MachineId,
                        Mr1 = mr1,
                        CreatedAt = DateTime.UtcNow
                    });
                    bound++;
                }
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "batch-bind operator={Op} machineId={Id} bound={Bound} skipped={Skipped} removed={Removed} notFound={NotFound}",
                @operator, req.MachineId, bound, skipped, removed, notFound.Count);

            return new BatchBindResponse(bound, skipped, removed, notFound);
        }
        catch (Exception ex) when (ex is not KeyNotFoundException && ex is not ArgumentException)
        {
            // 业务异常 (机型不存在 / 校验失败) 直接抛出: await using 会自动 rollback 未 commit 的事务
            // 其他异常显式回滚 + 记日志 + 重抛
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "batch-bind 事务回滚 machineId={Id}", req.MachineId);
            throw;
        }
    }
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

// Task 2: 批量绑定 MR.1 到机型 — 请求/响应 DTO
//   JSON 字段用 snake_case (项目约定), 错误码无 ERR_ 前缀
public record BatchBindRequest(
    [property: JsonPropertyName("machine_id")] long MachineId,
    [property: JsonPropertyName("mr1_list")] List<string> Mr1List,
    [property: JsonPropertyName("replace")] bool Replace);

public record BatchBindResponse(
    [property: JsonPropertyName("bound")] int Bound,
    [property: JsonPropertyName("skipped")] int Skipped,
    [property: JsonPropertyName("removed")] int Removed,
    [property: JsonPropertyName("not_found")] List<string> NotFound);

// Task 1: 机型三级树查询返回类型 (MachineCategory → MachineBrand → MachineModel)
//   JSON 字段用 snake_case (项目约定)
public record MachineTreeNode(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("brands")] List<MachineBrandNode> Brands);

public record MachineBrandNode(
    [property: JsonPropertyName("brand")] string Brand,
    [property: JsonPropertyName("models")] List<MachineModelNode> Models);

public record MachineModelNode(
    [property: JsonPropertyName("machine_id")] long MachineId,
    [property: JsonPropertyName("model_name")] string ModelName);
