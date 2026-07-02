using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Interfaces;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// 字典管理抽象基类 (Day 10+ P2.1)
/// 用途: 统一实现所有字典的软删/排序/UNIQUE 验证/批量重排序 7 个核心方法,
///       子类只需 override xrefCount 业务逻辑 (Day 10: OemBrandDictService < 50 行)
///
/// 设计:
///   - TItem 必须是 EF Core 实体 (有 Id / Value / SortOrder / DeletedAt 字段)
///   - Where 表达式走 EF.Property&lt;T&gt;(x, "PropName") 反射,避免 LINQ 表达式树不支持实例方法
///   - 子类通过 abstract accessor (GetValue/SetValue 等) 暴露属性,基类在 setter 场景用
///   - 7 方法全部 async + CancellationToken, 与 IDictService&lt;TItem&gt; 一致
///   - 不写 product_history: 字典变更不属产品业务变更, 避免污染 history 表
/// </summary>
public abstract class BaseDictService<TItem> : IDictService<TItem>
    where TItem : class, new()
{
    protected readonly ProductDbContext _db;
    protected readonly ILogger _logger;

    /// <summary>子类传入表名,用于日志标识 (e.g. "xref_oem_brand")</summary>
    protected readonly string _tableName;

    /// <summary>字段 Value 的最大长度,子类在构造时传入 (e.g. 100)</summary>
    protected readonly int _maxLength;

    /// <summary>EF Property 反射名: 值字段 (e.g. "Brand" / "Name")</summary>
    protected abstract string ValueProperty { get; }
    /// <summary>EF Property 反射名: 排序字段 (固定 "SortOrder")</summary>
    protected abstract string SortOrderProperty { get; }
    /// <summary>EF Property 反射名: 软删字段 (固定 "DeletedAt")</summary>
    protected abstract string DeletedAtProperty { get; }
    /// <summary>子类暴露 DbSet (e.g. ctx => ctx.XrefOemBrands)</summary>
    protected abstract DbSet<TItem> Set(ProductDbContext ctx);

    // ========== Accessor (子类实现,因为属性名不同) ==========
    protected abstract string GetValue(TItem item);
    protected abstract void SetValue(TItem item, string value);
    protected abstract int GetSortOrder(TItem item);
    protected abstract void SetSortOrder(TItem item, int sortOrder);
    protected abstract DateTime? GetDeletedAt(TItem item);
    protected abstract void SetDeletedAt(TItem item, DateTime? deletedAt);
    protected abstract long GetId(TItem item);

    /// <summary>
    /// P2.2 扩展: 多字段字典 (Media/Machine/Engine) override 此属性
    /// 追加额外的搜索字段, ListAsync/TypeaheadAsync 会用 OR 匹配所有字段
    /// 默认空数组 = 仅匹配主 ValueProperty (单字段字典行为不变,Day 10 E2E 仍 10/10)
    /// </summary>
    protected virtual IReadOnlyList<string> ExtraSearchProperties => Array.Empty<string>();

    protected BaseDictService(
        ProductDbContext db,
        ILogger logger,
        string tableName,
        int maxLength)
    {
        _db = db;
        _logger = logger;
        _tableName = tableName;
        _maxLength = maxLength;
    }

    /// <summary>统计 value 在 xref 来源表的引用次数,默认 0,子类按需 override</summary>
    public virtual Task<long> GetXrefCountAsync(string value, CancellationToken ct)
        => Task.FromResult(0L);

    // ========== 1. ListAsync ==========
    //   includeDeleted=false (默认): 后台管理页, 只看未删除
    //   includeDeleted=true: 审计场景, 已删的排末尾
    public virtual async Task<List<TItem>> ListAsync(
        string? keyword, bool includeDeleted, int? limit, CancellationToken ct = default)
    {
        var query = Set(_db).AsNoTracking();
        if (includeDeleted)
        {
            // 含已删: 已删的排末尾 (按 DeletedAt 是否有值: 0 = 未删, 1 = 已删)
            query = query.OrderBy(b => EF.Property<DateTime?>(b, DeletedAtProperty) != null)
                         .ThenBy(b => EF.Property<int>(b, SortOrderProperty))
                         .ThenBy(b => EF.Property<string>(b, ValueProperty));
        }
        else
        {
            query = query.Where(b => EF.Property<DateTime?>(b, DeletedAtProperty) == null)
                         .OrderBy(b => EF.Property<int>(b, SortOrderProperty))
                         .ThenBy(b => EF.Property<string>(b, ValueProperty));
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            // Day 10+ P0.1: ILIKE 必须用 3 参重载 + ESCAPE '\\', 否则下划线/百分号被当通配符
            // P2.2: 多字段字典 OR 匹配 (主 ValueProperty + ExtraSearchProperties)
            var pattern = $"%{kw.EscapeLikePattern()}%";
            query = query.Where(BuildSearchPredicate(pattern));
        }
        if (limit.HasValue && limit.Value > 0)
            query = query.Take(limit.Value);
        return await query.ToListAsync(ct);
    }

    // ========== 2. TypeaheadAsync ==========
    //   只返未删除, 按 sort_order 升序
    public virtual async Task<List<TItem>> TypeaheadAsync(
        string? q, int limit, CancellationToken ct = default)
    {
        var cap = Math.Clamp(limit, 1, 50);
        var query = Set(_db).AsNoTracking()
            .Where(b => EF.Property<DateTime?>(b, DeletedAtProperty) == null);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var kw = q.Trim();
            // P2.2: 多字段字典 OR 匹配 (与 ListAsync 一致)
            var pattern = $"%{kw.EscapeLikePattern()}%";
            query = query.Where(BuildSearchPredicate(pattern));
        }
        return await query
            .OrderBy(b => EF.Property<int>(b, SortOrderProperty))
            .ThenBy(b => EF.Property<string>(b, ValueProperty))
            .Take(cap)
            .ToListAsync(ct);
    }

    // ========== 3. CreateAsync ==========
    public virtual async Task<TItem> CreateAsync(
        string value, int? sortOrder, CancellationToken ct = default)
    {
        var normalized = NormalizeValue(value);

        // UNIQUE 检查 (含已软删: 同名占用即抛)
        var exists = await AnyByValueAsync(normalized, ct: ct);
        if (exists)
            throw new InvalidOperationException($"字典值已存在: {normalized}");

        var maxSort = await Set(_db)
            .Where(b => EF.Property<DateTime?>(b, DeletedAtProperty) == null)
            .Select(b => (int?)EF.Property<int>(b, SortOrderProperty))
            .MaxAsync(ct) ?? 0;

        var entity = new TItem();
        SetValue(entity, normalized);
        SetSortOrder(entity, sortOrder ?? (maxSort + 10));
        // created_at / updated_at 在 EF Config 用 HasDefaultValueSql("now()") 兜底, 这里不写

        Set(_db).Add(entity);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[{Table}] 新增字典 id={Id} value={Value} sortOrder={SortOrder}",
            _tableName, GetId(entity), GetValue(entity), GetSortOrder(entity));
        return entity;
    }

    // ========== 4. UpdateAsync ==========
    public virtual async Task<TItem> UpdateAsync(
        long id, string? value, int? sortOrder, CancellationToken ct = default)
    {
        // EF Core 不能翻译实例方法 GetId(b) 到 SQL, 必须用 EF.Property<long>(b, "Id")
        var entity = await Set(_db).FirstOrDefaultAsync(
            b => EF.Property<long>(b, "Id") == id, ct)
            ?? throw new KeyNotFoundException($"字典 id={id} 不存在 ({_tableName})");

        if (!string.IsNullOrWhiteSpace(value))
        {
            var normalized = NormalizeValue(value);
            if (normalized != GetValue(entity))
            {
                var conflict = await AnyByValueAsync(normalized, excludeId: id, ct: ct);
                if (conflict)
                    throw new InvalidOperationException($"字典值已存在: {normalized}");
                SetValue(entity, normalized);
            }
        }
        if (sortOrder.HasValue)
            SetSortOrder(entity, sortOrder.Value);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[{Table}] 更新字典 id={Id} value={Value} sortOrder={SortOrder}",
            _tableName, GetId(entity), GetValue(entity), GetSortOrder(entity));
        return entity;
    }

    // ========== 5. DeleteAsync (软删) ==========
    public virtual async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        // EF Core 不能翻译实例方法 GetId(b) 到 SQL, 必须用 EF.Property<long>(b, "Id")
        var entity = await Set(_db).FirstOrDefaultAsync(
            b => EF.Property<long>(b, "Id") == id, ct)
            ?? throw new KeyNotFoundException($"字典 id={id} 不存在 ({_tableName})");
        if (GetDeletedAt(entity) != null)
            throw new InvalidOperationException($"字典 id={id} 已删除, 不可重复操作");
        SetDeletedAt(entity, DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[{Table}] 软删除字典 id={Id} value={Value}",
            _tableName, GetId(entity), GetValue(entity));
    }

    // ========== 6. RestoreAsync ==========
    public virtual async Task<TItem> RestoreAsync(long id, CancellationToken ct = default)
    {
        // EF Core 不能翻译实例方法 GetId(b) 到 SQL, 必须用 EF.Property<long>(b, "Id")
        var entity = await Set(_db).FirstOrDefaultAsync(
            b => EF.Property<long>(b, "Id") == id, ct)
            ?? throw new KeyNotFoundException($"字典 id={id} 不存在 ({_tableName})");
        if (GetDeletedAt(entity) == null)
            throw new InvalidOperationException($"字典 id={id} 未删除, 无需恢复");
        // 恢复时若 value 已被新条目占用 → 抛错
        var conflict = await AnyByValueAsync(GetValue(entity), excludeId: id, ct: ct);
        if (conflict)
            throw new InvalidOperationException($"字典值 {GetValue(entity)} 已被新条目占用, 无法恢复");
        SetDeletedAt(entity, null);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[{Table}] 恢复字典 id={Id} value={Value}",
            _tableName, GetId(entity), GetValue(entity));
        return entity;
    }

    // ========== 7. ReorderAsync ==========
    //   一次 SaveChanges 事务,部分失败整体回滚
    public virtual async Task ReorderAsync(
        List<(long id, int sortOrder)> items, CancellationToken ct = default)
    {
        if (items == null || items.Count == 0)
            throw new ArgumentException("items 不能为空");
        var ids = items.Select(i => i.id).ToList();
        // 用 Expression 构造主键 IN 查询,避免依赖具体 PK 字段名
        var param = Expression.Parameter(typeof(TItem), "x");
        var idProp = typeof(TItem).GetProperty("Id")
            ?? throw new InvalidOperationException($"{typeof(TItem).Name} 必须有 Id 属性");
        var idAccess = Expression.Property(param, idProp);
        var containsCall = Expression.Call(
            typeof(Enumerable), nameof(Enumerable.Contains),
            new[] { typeof(long) },
            Expression.Constant(ids),
            idAccess);
        var lambda = Expression.Lambda<Func<TItem, bool>>(containsCall, param);
        var entities = await Set(_db).Where(lambda).ToListAsync(ct);
        if (entities.Count != ids.Count)
        {
            var missing = ids.Except(entities.Select(GetId)).ToList();
            throw new KeyNotFoundException($"部分 id 不存在: {string.Join(",", missing)}");
        }
        foreach (var item in items)
        {
            var entity = entities.First(e => GetId(e) == item.id);
            SetSortOrder(entity, item.sortOrder);
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[{Table}] 重排序字典 {Count} 条", _tableName, items.Count);
    }

    // ========== 内部工具 ==========
    //   检查 value 是否已存在 (含软删); excludeId 用作 update 时的"排除自己"
    private async Task<bool> AnyByValueAsync(
        string value, long? excludeId = null, CancellationToken ct = default)
    {
        var query = Set(_db).AsNoTracking()
            .Where(b => EF.Property<string>(b, ValueProperty) == value);
        if (excludeId.HasValue)
            // EF Core 不能翻译实例方法 GetId(b) 到 SQL, 用 EF.Property<long>(b, "Id")
            query = query.Where(b => EF.Property<long>(b, "Id") != excludeId.Value);
        return await query.AnyAsync(ct);
    }

    /// <summary>归一化 + 校验: trim + 非空 + 长度限制</summary>
    protected virtual string NormalizeValue(string raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0)
            throw new ArgumentException("value 不能为空");
        if (s.Length > _maxLength)
            throw new ArgumentException($"value 长度不能超过 {_maxLength}");
        return s;
    }

    // P2.2: 多字段 OR 匹配 (主 ValueProperty + ExtraSearchProperties 全部 ILIKE)
    //   单字段字典 (OemBrand) ExtraSearchProperties 为空, 走单 EF.Functions.ILike, 等同原行为
    //   多字段字典 (Media/Machine/Engine) 返回 EF.Or(...) 表达式, EF Core 翻译为 SQL OR
    //
    // P0.1 修复教训: 必须用 3 参重载 EF.Functions.ILike(prop, pattern, "\\")
    //   - 2 参重载 ILike(prop, pattern) 无 escape 参数, 下划线/百分号被当通配符
    //   - 3 参重载 ILike(prop, pattern, escapeCharacter) 是非泛型 string 重载, 不可 MakeGenericMethod
    //   早期实现用反射取 "ILike" 3 参重载再 MakeGenericMethod(typeof(string)) 抛 "is not a GenericMethodDefinition"
    //   现改为直接引用方法组, 编译期保证类型正确
    private System.Linq.Expressions.Expression<Func<TItem, bool>> BuildSearchPredicate(string pattern)
    {
        // 直接引用 NpgsqlDbFunctionsExtensions.ILike 3 参重载 (非泛型 string 重载)
        //   签名: bool ILike(this DbFunctions _, string matchExpression, string pattern, string escapeCharacter)
        //   位于 Microsoft.EntityFrameworkCore.NpgsqlDbFunctionsExtensions (Npgsql.EntityFrameworkCore.PostgreSQL 8.0+)
        var ilikeMethod = ((Func<Microsoft.EntityFrameworkCore.DbFunctions, string, string, string, bool>)
            Microsoft.EntityFrameworkCore.NpgsqlDbFunctionsExtensions.ILike).Method;
        var efFunctionsConst = System.Linq.Expressions.Expression.Constant(
            Microsoft.EntityFrameworkCore.EF.Functions, typeof(Microsoft.EntityFrameworkCore.DbFunctions));
        var patternConst = System.Linq.Expressions.Expression.Constant(pattern);
        var escapeConst = System.Linq.Expressions.Expression.Constant("\\");
        var param = System.Linq.Expressions.Expression.Parameter(typeof(TItem), "b");
        System.Linq.Expressions.Expression? combined = null;
        foreach (var propName in new[] { ValueProperty }.Concat(ExtraSearchProperties).Distinct())
        {
            var propAccess = System.Linq.Expressions.Expression.Property(param, propName);
            var matchExpr = System.Linq.Expressions.Expression.Call(
                ilikeMethod, efFunctionsConst, propAccess, patternConst, escapeConst);
            combined = combined == null
                ? (System.Linq.Expressions.Expression)matchExpr
                : System.Linq.Expressions.Expression.OrElse(combined, matchExpr);
        }
        return System.Linq.Expressions.Expression.Lambda<Func<TItem, bool>>(combined!, param);
    }
}
