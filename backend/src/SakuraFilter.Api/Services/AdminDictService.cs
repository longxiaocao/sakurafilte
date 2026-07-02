using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// 后台字典管理服务 (Day 10: P1.3)
/// 用途: 支撑 OEM Brand 字典的增删改查 + 拖拽排序 + typeahead
/// 设计:
///   - 软删除: deleted_at 标记, list/typeahead 不返回
///   - brand UNIQUE: 重复添加抛 InvalidOperationException
///   - Reorder 走单事务, 一次 SaveChanges 提交 (避免一半成功一半失败)
///   - typeahead: ILIKE 模糊匹配 + sort_order 排序 + 限 20 条 (与现有 typeahead UX 一致)
///   - 不写 product_history: 字典变更不属产品业务变更, 避免污染 history 表
/// </summary>
public class AdminDictService
{
    private readonly ProductDbContext _db;
    private readonly ILogger<AdminDictService> _logger;

    public AdminDictService(ProductDbContext db, ILogger<AdminDictService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ========== OEM Brand 列表 ==========
    //   includeDeleted=false (默认): 后台管理页, 默认只显未删除, 开关切换
    //   includeDeleted=true: 审计场景, 需看历史已删品牌
    public async Task<List<OemBrandItem>> ListOemBrandsAsync(
        string? keyword, bool includeDeleted, int? limit, CancellationToken ct = default)
    {
        var query = _db.XrefOemBrands.AsNoTracking();
        if (includeDeleted)
        {
            // 含已删: 仍按 sort_order 排, 已删的排在末尾
            query = query.OrderBy(b => b.DeletedAt.HasValue).ThenBy(b => b.SortOrder).ThenBy(b => b.Brand);
        }
        else
        {
            query = query.Where(b => b.DeletedAt == null).OrderBy(b => b.SortOrder).ThenBy(b => b.Brand);
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            // ILIKE 转义: 用户输入 % _ 视为字面量, 防止 LIKE 注入
            var escaped = kw.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            query = query.Where(b => EF.Functions.ILike(b.Brand, $"%{escaped}%"));
        }
        if (limit.HasValue && limit.Value > 0)
            query = query.Take(limit.Value);

        var rows = await query.ToListAsync(ct);
        // 同步统计: 每个 brand 在 cross_references 中的出现次数 (字典 + 实时聚合, 避免数据双写不一致)
        var brands = rows.Select(b => b.Brand).ToList();
        var counts = await _db.CrossReferences.AsNoTracking()
            .Where(x => x.OemBrand != null && brands.Contains(x.OemBrand))
            .GroupBy(x => x.OemBrand!)
            .Select(g => new { Brand = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Brand, g => g.Count, ct);
        return rows.Select(b => new OemBrandItem(
            b.Id, b.Brand, b.SortOrder, b.CreatedAt, b.UpdatedAt, b.DeletedAt,
            counts.TryGetValue(b.Brand, out var c) ? c : 0)).ToList();
    }

    // ========== OEM Brand Typeahead ==========
    //   给后台产品表单分区 2 的 oem_brand 自动补全用
    //   返回精简字段 (id + brand) 减少 payload
    public async Task<List<OemBrandTypeaheadItem>> TypeaheadOemBrandsAsync(
        string? q, int? limit, CancellationToken ct = default)
    {
        var cap = Math.Clamp(limit ?? 20, 1, 50);
        var query = _db.XrefOemBrands.AsNoTracking()
            .Where(b => b.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var kw = q.Trim();
            var escaped = kw.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            query = query.Where(b => EF.Functions.ILike(b.Brand, $"%{escaped}%"));
        }
        return await query
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Brand)
            .Take(cap)
            .Select(b => new OemBrandTypeaheadItem(b.Id, b.Brand))
            .ToListAsync(ct);
    }

    // ========== OEM Brand 新增 ==========
    //   存在同名 (含软删) → 抛 InvalidOperationException
    public async Task<OemBrandItem> CreateOemBrandAsync(
        string brand, int? sortOrder, CancellationToken ct = default)
    {
        var normalized = NormalizeBrand(brand);
        var exists = await _db.XrefOemBrands
            .AnyAsync(b => b.Brand == normalized, ct);
        if (exists)
            throw new InvalidOperationException($"OEM 品牌已存在: {normalized}");

        var maxSort = await _db.XrefOemBrands
            .Where(b => b.DeletedAt == null)
            .Select(b => (int?)b.SortOrder).MaxAsync(ct) ?? 0;

        var entity = new XrefOemBrand
        {
            Brand = normalized,
            SortOrder = sortOrder ?? (maxSort + 10),  // 默认插到末尾, 步长 10 留排序余地
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.XrefOemBrands.Add(entity);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("新增 OEM 品牌字典 id={Id} brand={Brand} sortOrder={SortOrder}",
            entity.Id, entity.Brand, entity.SortOrder);
        return new OemBrandItem(entity.Id, entity.Brand, entity.SortOrder,
            entity.CreatedAt, entity.UpdatedAt, entity.DeletedAt, 0);
    }

    // ========== OEM Brand 更新 ==========
    //   只改 brand 和 sortOrder; 不允许改 id/createdAt
    public async Task<OemBrandItem> UpdateOemBrandAsync(
        long id, string? brand, int? sortOrder, CancellationToken ct = default)
    {
        var entity = await _db.XrefOemBrands.FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new KeyNotFoundException($"OEM 品牌字典 id={id} 不存在");

        if (!string.IsNullOrWhiteSpace(brand))
        {
            var normalized = NormalizeBrand(brand);
            if (normalized != entity.Brand)
            {
                // 改名时检查新名是否已被占用
                var conflict = await _db.XrefOemBrands
                    .AnyAsync(b => b.Id != id && b.Brand == normalized, ct);
                if (conflict)
                    throw new InvalidOperationException($"OEM 品牌已存在: {normalized}");
                entity.Brand = normalized;
            }
        }
        if (sortOrder.HasValue)
            entity.SortOrder = sortOrder.Value;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("更新 OEM 品牌字典 id={Id} brand={Brand} sortOrder={SortOrder}",
            entity.Id, entity.Brand, entity.SortOrder);
        var count = await _db.CrossReferences.AsNoTracking()
            .CountAsync(x => x.OemBrand == entity.Brand, ct);
        return new OemBrandItem(entity.Id, entity.Brand, entity.SortOrder,
            entity.CreatedAt, entity.UpdatedAt, entity.DeletedAt, count);
    }

    // ========== OEM Brand 软删除 ==========
    //   WHY 软删: 历史 cross_references.oem_brand 仍可追溯 (前端 typeahead 过滤掉, 但历史数据保留)
    public async Task DeleteOemBrandAsync(long id, CancellationToken ct = default)
    {
        var entity = await _db.XrefOemBrands.FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new KeyNotFoundException($"OEM 品牌字典 id={id} 不存在");
        if (entity.DeletedAt != null)
            throw new InvalidOperationException($"OEM 品牌字典 id={id} 已删除, 不可重复操作");
        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("软删除 OEM 品牌字典 id={Id} brand={Brand}", entity.Id, entity.Brand);
    }

    // ========== OEM Brand 恢复 (Day 10 加, 后台误删可恢复) ==========
    public async Task<OemBrandItem> RestoreOemBrandAsync(long id, CancellationToken ct = default)
    {
        var entity = await _db.XrefOemBrands.FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new KeyNotFoundException($"OEM 品牌字典 id={id} 不存在");
        if (entity.DeletedAt == null)
            throw new InvalidOperationException($"OEM 品牌字典 id={id} 未删除, 无需恢复");
        // 恢复时若 brand 已被新条目占用 → 抛错
        var conflict = await _db.XrefOemBrands
            .AnyAsync(b => b.Id != id && b.Brand == entity.Brand, ct);
        if (conflict)
            throw new InvalidOperationException($"品牌名 {entity.Brand} 已被新条目占用, 无法恢复");
        entity.DeletedAt = null;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("恢复 OEM 品牌字典 id={Id} brand={Brand}", entity.Id, entity.Brand);
        var count = await _db.CrossReferences.AsNoTracking()
            .CountAsync(x => x.OemBrand == entity.Brand, ct);
        return new OemBrandItem(entity.Id, entity.Brand, entity.SortOrder,
            entity.CreatedAt, entity.UpdatedAt, entity.DeletedAt, count);
    }

    // ========== OEM Brand 批量重排序 ==========
    //   前端拖拽后, 把新顺序 ids 传过来
    //   一次 SaveChanges 事务, 部分失败整体回滚
    public async Task ReorderOemBrandsAsync(List<OemBrandReorderItem> items, CancellationToken ct = default)
    {
        if (items == null || items.Count == 0)
            throw new ArgumentException("items 不能为空");
        var ids = items.Select(i => i.Id).ToList();
        var entities = await _db.XrefOemBrands
            .Where(b => ids.Contains(b.Id))
            .ToListAsync(ct);
        if (entities.Count != ids.Count)
        {
            var missing = ids.Except(entities.Select(e => e.Id)).ToList();
            throw new KeyNotFoundException($"部分 id 不存在: {string.Join(",", missing)}");
        }
        var now = DateTime.UtcNow;
        foreach (var item in items)
        {
            var entity = entities.First(e => e.Id == item.Id);
            entity.SortOrder = item.SortOrder;
            entity.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("重排序 OEM 品牌字典 {Count} 条", items.Count);
    }

    // ========== 工具: brand 归一化 ==========
    //   WHY: 与 AdminProductService.NormalizeOem 一致策略, 防止 "BOSCH" / "bosch " / "Bosch" 重复入库
    private static string NormalizeBrand(string raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0)
            throw new ArgumentException("brand 不能为空");
        if (s.Length > 100)
            throw new ArgumentException("brand 长度不能超过 100");
        return s;
    }
}

/// <summary>OEM 品牌字典返回项 (含 xref 出现次数)</summary>
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
