using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Core.Entities;
using SakuraFilter.Core.Extensions;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Search;

/// <summary>
/// PostgreSQL 搜索提供者 (兜底实现,无 typo 容错但 100% 可靠)
/// V2 改造 (Task 0.4):
/// - 主键从 long Id 改为 string mr_1
/// - LATERAL JOIN + JSON 聚合避免笛卡尔积 (修复 S2/S10/S11/S22)
/// - CTE 预计算 brand_sort_order_min + oem_list_sort_order_min (修复 S3-2/S3-10)
/// - keyset 分页 (修复 S3-15) - 暂用 OFFSET 分页,Phase 1 改 keyset
/// - 6 字段 ILIKE 补全 + EXISTS 子查询 (修复 S3-3/S3-4/S3-22)
/// - 排序三层对齐 Meilisearch (修复 S3-5)
/// V24-F80 (2026-07-18, P1-2, spec Task 1.2.9-1.2.11): 改用原生 SQL + CTE + LATERAL JOIN
///   WHY 重写: V24-F76 为绕过 EF Core 8 NavigationExpandingExpressionVisitor bug 牺牲了:
///     1. 关键词分词 OR 匹配 (原 patterns.Any 多 token OR)
///     2. 三层排序 (brand_sort_order_min → oem_list_sort_order_min → updated_at)
///   原生 SQL 不受 EF Core 表达式树限制,可恢复完整功能
/// V24-F94 (2026-07-19, v28-2, spec 28.2): CTE UNION 拆分 + 三表 GIN trgm 索引
///   WHY 重写: v28-1 验证显示 baseline SQL (OR + EXISTS xref/machine) 让 PG 优化器不选 GIN trgm 索引
///     baseline P95=1827ms (含 2 个 EXISTS 子查询, 49989 产品 × 37459 次循环)
///   v28-2 改造:
///     - q_match CTE 用 UNION 拆分: products 5 字段 + cross_references 3 字段 + machine_applications 2 字段
///     - 三表都加 GIN trgm 索引 (见 migration AddGinTrgmIndexesForSearch + 017_add_trgm_indexes.sql)
///     - PG 优化器对每个 UNION 分支独立选 GIN trgm Bitmap Index Scan
///     - P95 从 1827ms → 305ms (6x, spike_test_v3 50K 验证)
///   多 token 处理: 每个 token 独立 CTE (q_match_0, q_match_1, ...), 最终 INTERSECT 取交集
///     语义对齐 Meili default matchingStrategy='all'
/// </summary>
public class PostgresSearchProvider : ISearchProvider
{
    private readonly ProductDbContext _db;
    private readonly ILogger<PostgresSearchProvider> _logger;

    public string Name => "postgres";

    public PostgresSearchProvider(ProductDbContext db, ILogger<PostgresSearchProvider> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            return _db.Database.CanConnectAsync(ct).ContinueWith(t => !t.IsFaulted && t.Result, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PostgresSearchProvider 健康检查失败");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// V24-F94 (v28-2): 构建基础过滤 WHERE 子句 + 参数 (供 SearchAsync 与 AggregateSearchAsync 复用)
    /// WHY 拆分: v28-2 把关键词 q 的 ILIKE 匹配从 WHERE 子句剥离到独立 CTE (BuildQMatchCte),
    ///   让 PG 优化器能对三表 ILIKE 选 GIN trgm Bitmap Index Scan
    /// 基础过滤包含:
    ///   - is_published / is_discontinued
    ///   - EXISTS 上架 OEM 3 (对齐 Meilisearch filter 语义)
    ///   - type / d1-h3 尺寸范围 / d7-d8 螺纹规格
    /// </summary>
    /// <returns>(baseWhereSql, baseParams)</returns>
    private (string baseWhereSql, List<NpgsqlParameter> baseParams) BuildBaseFilter(
        string? type,
        decimal? d1, decimal? d2, decimal? d3,
        decimal? h1, decimal? h2, decimal? h3,
        string? d7Thread, string? d8Thread,
        decimal tolerance, bool includeDiscontinued)
    {
        var conditions = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        // 基础过滤: 未停产 + 已上架
        if (!includeDiscontinued)
        {
            conditions.Add("p.is_discontinued = false");
        }
        conditions.Add("p.is_published = true");

        // 要求至少有一个上架 OEM 3 (对齐 Meilisearch filter 语义)
        conditions.Add(@"EXISTS (
            SELECT 1 FROM cross_references x
            WHERE x.product_id = p.id
              AND x.is_published = true
              AND x.is_discontinued = false
        )");

        // Type 过滤
        if (!string.IsNullOrWhiteSpace(type))
        {
            conditions.Add("p.type = @type");
            parameters.Add(new NpgsqlParameter("@type", NpgsqlDbType.Text) { Value = type });
        }

        // 尺寸范围 filter (d1_mm ~ h3_mm, ±tolerance)
        if (d1.HasValue)
        {
            conditions.Add("p.d1_mm BETWEEN @d1min AND @d1max");
            parameters.Add(new NpgsqlParameter("@d1min", NpgsqlDbType.Numeric) { Value = d1.Value - tolerance });
            parameters.Add(new NpgsqlParameter("@d1max", NpgsqlDbType.Numeric) { Value = d1.Value + tolerance });
        }
        if (d2.HasValue)
        {
            conditions.Add("p.d2_mm BETWEEN @d2min AND @d2max");
            parameters.Add(new NpgsqlParameter("@d2min", NpgsqlDbType.Numeric) { Value = d2.Value - tolerance });
            parameters.Add(new NpgsqlParameter("@d2max", NpgsqlDbType.Numeric) { Value = d2.Value + tolerance });
        }
        if (d3.HasValue)
        {
            conditions.Add("p.d3_mm BETWEEN @d3min AND @d3max");
            parameters.Add(new NpgsqlParameter("@d3min", NpgsqlDbType.Numeric) { Value = d3.Value - tolerance });
            parameters.Add(new NpgsqlParameter("@d3max", NpgsqlDbType.Numeric) { Value = d3.Value + tolerance });
        }
        if (h1.HasValue)
        {
            conditions.Add("p.h1_mm BETWEEN @h1min AND @h1max");
            parameters.Add(new NpgsqlParameter("@h1min", NpgsqlDbType.Numeric) { Value = h1.Value - tolerance });
            parameters.Add(new NpgsqlParameter("@h1max", NpgsqlDbType.Numeric) { Value = h1.Value + tolerance });
        }
        if (h2.HasValue)
        {
            conditions.Add("p.h2_mm BETWEEN @h2min AND @h2max");
            parameters.Add(new NpgsqlParameter("@h2min", NpgsqlDbType.Numeric) { Value = h2.Value - tolerance });
            parameters.Add(new NpgsqlParameter("@h2max", NpgsqlDbType.Numeric) { Value = h2.Value + tolerance });
        }
        if (h3.HasValue)
        {
            conditions.Add("p.h3_mm BETWEEN @h3min AND @h3max");
            parameters.Add(new NpgsqlParameter("@h3min", NpgsqlDbType.Numeric) { Value = h3.Value - tolerance });
            parameters.Add(new NpgsqlParameter("@h3max", NpgsqlDbType.Numeric) { Value = h3.Value + tolerance });
        }

        // D7/D8 螺纹规格文本匹配 (与 MeiliSearchProvider.SearchAsync 对齐)
        if (!string.IsNullOrWhiteSpace(d7Thread))
        {
            var escaped = d7Thread.EscapeLikePattern();
            conditions.Add("p.d7_thread IS NOT NULL AND p.d7_thread ILIKE '%' || @d7 || '%' ESCAPE '\\'");
            parameters.Add(new NpgsqlParameter("@d7", NpgsqlDbType.Text) { Value = escaped });
        }
        if (!string.IsNullOrWhiteSpace(d8Thread))
        {
            var escaped = d8Thread.EscapeLikePattern();
            conditions.Add("p.d8_thread IS NOT NULL AND p.d8_thread ILIKE '%' || @d8 || '%' ESCAPE '\\'");
            parameters.Add(new NpgsqlParameter("@d8", NpgsqlDbType.Text) { Value = escaped });
        }

        return (string.Join(" AND ", conditions), parameters);
    }

    /// <summary>
    /// V24-F94 (v28-2): 构建关键词 q 的 CTE UNION SQL (三表 GIN trgm 索引加速)
    /// WHY CTE UNION: v28-1 验证显示 baseline SQL (OR + EXISTS xref/machine) 让 PG 优化器不选 GIN trgm
    ///   改用 UNION 拆分: products 5 字段 + cross_references 3 字段 + machine_applications 2 字段
    ///   每个分支独立走 GIN trgm Bitmap Index Scan, P95 从 1827ms → 305ms (6x)
    /// 多 token 处理: 每个 token 独立 CTE (q_match_0, q_match_1, ...), 最终 INTERSECT 取交集
    ///   语义对齐 Meili default matchingStrategy='all'
    /// V24-F97 (v29-1, spec 28.3 P2 候选 1): 限制最大 token 数量为 8 (防御性兜底)
    ///   WHY: v28-5 (50K) + v28-3 (1M) 验证 1-5 token 退化可控 (50K 2.64x / 1M 1.49x), PG 优化器仍选 GIN trgm
    ///     但 6+ token 缺乏压测数据, 极端场景 (如 20+ token 恶意搜索) 可能触发 INTERSECT HashSetOp Append 退化
    ///     8 作为保守上限, 超出时截断 + LogWarning (实际电商搜索 5+ token 罕见)
    /// </summary>
    /// <returns>(ctePrefixSql, qParams) ctePrefixSql = null 当无 q</returns>
    private (string? ctePrefixSql, List<NpgsqlParameter> qParams) BuildQMatchCte(string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return (null, new List<NpgsqlParameter>());
        }

        // V24-F97 (v29-1): 限制最大 token 数量为 8, 超出截断 (防御性兜底, 见 spec 28.3 P2 候选 1)
        const int maxTokens = 8;
        var raw = q.Trim();
        var allTokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (allTokens.Length > maxTokens)
        {
            _logger.LogWarning("PostgresSearchProvider.BuildQMatchCte: q token 数量 {ActualCount} 超过上限 {MaxTokens}, 截断为前 {MaxTokens} 个 (q={RawQ})",
                allTokens.Length, maxTokens, maxTokens, raw);
            allTokens = allTokens.Take(maxTokens).ToArray();
        }
        var tokens = allTokens;
        var qParams = new List<NpgsqlParameter>();

        // 单 token: 单个 q_match CTE
        // 多 token: 多个 q_match_{i} CTE, 最终 q_match = INTERSECT
        var cteParts = new List<string>();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            // LIKE 转义: \ → \\, % → \%, _ → \_ (与 V24-F76 一致, 复用 EscapeLikePattern 等价逻辑)
            var escaped = token.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var paramName = $"@q{i}";
            qParams.Add(new NpgsqlParameter(paramName, NpgsqlDbType.Text) { Value = escaped });

            var cteName = tokens.Length == 1 ? "q_match" : $"q_match_{i}";
            // 单 token 用 q_match, 多 token 用 q_match_{i} (最终 INTERSECT 到 q_match)
            cteParts.Add($@"{cteName} AS (
    SELECT p.id AS product_id
    FROM products p
    WHERE p.is_discontinued = false AND p.is_published = true AND (
        p.product_name_1 ILIKE '%' || {paramName} || '%' ESCAPE '\' OR
        p.product_name_2 ILIKE '%' || {paramName} || '%' ESCAPE '\' OR
        p.oem_2 ILIKE '%' || {paramName} || '%' ESCAPE '\' OR
        p.mr_1 ILIKE '%' || {paramName} || '%' ESCAPE '\' OR
        p.remark ILIKE '%' || {paramName} || '%' ESCAPE '\'
    )
    UNION
    SELECT DISTINCT x.product_id
    FROM cross_references x
    WHERE x.is_published = true AND x.is_discontinued = false AND (
        x.oem_brand ILIKE '%' || {paramName} || '%' ESCAPE '\' OR
        x.oem_no_3 ILIKE '%' || {paramName} || '%' ESCAPE '\' OR
        x.oem_2 ILIKE '%' || {paramName} || '%' ESCAPE '\'
    )
    UNION
    SELECT DISTINCT m.product_id
    FROM machine_applications m
    WHERE m.machine_brand ILIKE '%' || {paramName} || '%' ESCAPE '\' OR
          m.machine_model ILIKE '%' || {paramName} || '%' ESCAPE '\'
)");
        }

        // 多 token: 最终 q_match = INTERSECT 所有 q_match_{i} (类似 Meili matchingStrategy='all')
        if (tokens.Length > 1)
        {
            var intersectParts = new List<string>();
            for (var i = 0; i < tokens.Length; i++)
            {
                intersectParts.Add($"SELECT product_id FROM q_match_{i}");
            }
            cteParts.Add($@"q_match AS ({string.Join(" INTERSECT ", intersectParts)})");
        }

        var ctePrefixSql = "WITH " + string.Join(", ", cteParts);
        return (ctePrefixSql, qParams);
    }

    /// <summary>
    /// V24-F94 (v28-2): 组装完整 SQL (q_match CTE + sort_cte + 主查询)
    /// WHY 抽取: SearchAsync 与 AggregateSearchAsync 复用 SQL 组装逻辑
    /// 无 q 时: 直接用 base filter, 无 q_match CTE
    /// 有 q 时: q_match CTE 走 GIN trgm 索引, sort_cte JOIN q_match 过滤候选
    /// </summary>
    private string BuildFullSql(
        string? ctePrefixSql,
        string baseWhereSql,
        string selectColumns,
        string orderBySql,
        bool hasQ)
    {
        // sort_cte: 预计算 brand_sort_order_min + oem_list_sort_order_min (修复 S3-2/S3-10)
        // 有 q 时: JOIN q_match 过滤候选 (走 GIN trgm 索引)
        // 无 q 时: 仅 base filter
        var sortCteJoin = hasQ ? "JOIN q_match ON q_match.product_id = p.id" : "";
        var cteSeparator = ctePrefixSql != null ? ", " : "WITH ";

        var sql = $@"{(ctePrefixSql ?? "")}{(ctePrefixSql != null ? cteSeparator : cteSeparator)}sort_cte AS (
    SELECT
        p.id AS product_id,
        COALESCE(MIN(xb.sort_order), 2147483647) AS brand_sort_order_min,
        COALESCE(MIN(x.sort_order), 2147483647) AS oem_list_sort_order_min
    FROM products p
    {sortCteJoin}
    LEFT JOIN cross_references x ON x.product_id = p.id
        AND x.is_published = true
        AND x.is_discontinued = false
    LEFT JOIN xref_oem_brand xb ON xb.brand = x.oem_brand
        AND xb.deleted_at IS NULL
    WHERE {baseWhereSql}
    GROUP BY p.id
)
SELECT
    {selectColumns}
FROM products p
JOIN sort_cte s ON s.product_id = p.id
ORDER BY
    {orderBySql}
LIMIT @pageSize OFFSET @offset";
        return sql;
    }

    /// <summary>
    /// V24-F94 (v28-2): 组装 COUNT SQL (与 BuildFullSql 对应, 但无 LIMIT/OFFSET 和 ORDER BY)
    /// </summary>
    private string BuildCountSql(string? ctePrefixSql, string baseWhereSql, bool hasQ)
    {
        var sortCteJoin = hasQ ? "JOIN q_match ON q_match.product_id = p.id" : "";
        var cteSeparator = ctePrefixSql != null ? ", " : "WITH ";

        var sql = $@"{(ctePrefixSql ?? "")}{(ctePrefixSql != null ? cteSeparator : cteSeparator)}sort_cte AS (
    SELECT p.id AS product_id
    FROM products p
    {sortCteJoin}
    LEFT JOIN cross_references x ON x.product_id = p.id
        AND x.is_published = true
        AND x.is_discontinued = false
    LEFT JOIN xref_oem_brand xb ON xb.brand = x.oem_brand
        AND xb.deleted_at IS NULL
    WHERE {baseWhereSql}
    GROUP BY p.id
)
SELECT COUNT(*) FROM sort_cte";
        return sql;
    }

    /// <summary>
    /// V24-F80 + V24-F94: 原生 SQL 搜索 (恢复分词 OR + 三层排序 + CTE UNION GIN trgm 加速)
    /// </summary>
    public async Task<SearchResult> SearchAsync(SearchRequest req, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var (baseWhereSql, baseParams) = BuildBaseFilter(
            req.Type,
            req.D1, req.D2, req.D3,
            req.H1, req.H2, req.H3,
            req.D7Thread, req.D8Thread,
            req.Tolerance, req.IncludeDiscontinued);

        var (ctePrefixSql, qParams) = BuildQMatchCte(req.Q);
        var hasQ = ctePrefixSql != null;

        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        // 三层排序: brand_sort_order_min → oem_list_sort_order_min → updated_at DESC
        const string orderBySql = "s.brand_sort_order_min ASC, s.oem_list_sort_order_min ASC, p.updated_at DESC";
        const string selectColumns = @"p.id, p.mr_1, p.oem_no_display, p.remark, p.type,
    p.d1_mm, p.d2_mm, p.h1_mm, p.image_key, p.is_discontinued,
    p.updated_at";

        var sql = BuildFullSql(ctePrefixSql, baseWhereSql, selectColumns, orderBySql, hasQ);
        var countSql = BuildCountSql(ctePrefixSql, baseWhereSql, hasQ);

        // 参数克隆 (countSql 复用 base+q 参数, sql 还需 pageSize/offset)
        var allParams = baseParams.Concat(qParams).ToList();
        var countParams = allParams.Select(p => p.Clone()).ToList();
        var listParams = allParams.ToList();
        listParams.Add(new NpgsqlParameter("@pageSize", NpgsqlDbType.Integer) { Value = pageSize });
        listParams.Add(new NpgsqlParameter("@offset", NpgsqlDbType.Integer) { Value = offset });

        long total;
        var items = new List<SearchResultItem>();
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        try
        {
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct);

            // 1. 总数
            await using (var countCmd = new NpgsqlCommand(countSql, conn))
            {
                foreach (var p in countParams) countCmd.Parameters.Add(p);
                countCmd.CommandTimeout = 30;
                var result = await countCmd.ExecuteScalarAsync(ct);
                total = result == null || result == DBNull.Value ? 0 : Convert.ToInt64(result);
            }

            // 2. 分页结果
            await using (var listCmd = new NpgsqlCommand(sql, conn))
            {
                foreach (var p in listParams) listCmd.Parameters.Add(p);
                listCmd.CommandTimeout = 30;
                await using var reader = await listCmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    // V24-F80: mr_1 可能为 null, 用 IsDBNull 检查 (与 Product.Mr1 nullable 一致)
                    var mr1Idx = reader.GetOrdinal("mr_1");
                    var oemNoDisplayIdx = reader.GetOrdinal("oem_no_display");
                    string oemNoDisplay;
                    if (!reader.IsDBNull(mr1Idx))
                    {
                        oemNoDisplay = reader.GetString(mr1Idx);
                    }
                    else if (!reader.IsDBNull(oemNoDisplayIdx))
                    {
                        oemNoDisplay = reader.GetString(oemNoDisplayIdx);
                    }
                    else
                    {
                        oemNoDisplay = "";
                    }
                    items.Add(new SearchResultItem(
                        reader.GetInt64(reader.GetOrdinal("id")),
                        oemNoDisplay,
                        reader.IsDBNull(reader.GetOrdinal("remark")) ? null : reader.GetString(reader.GetOrdinal("remark")),
                        reader.GetString(reader.GetOrdinal("type")) ?? "UNKNOWN",
                        reader.IsDBNull(reader.GetOrdinal("d1_mm")) ? null : reader.GetDecimal(reader.GetOrdinal("d1_mm")),
                        reader.IsDBNull(reader.GetOrdinal("d2_mm")) ? null : reader.GetDecimal(reader.GetOrdinal("d2_mm")),
                        reader.IsDBNull(reader.GetOrdinal("h1_mm")) ? null : reader.GetDecimal(reader.GetOrdinal("h1_mm")),
                        reader.IsDBNull(reader.GetOrdinal("image_key")) ? null : reader.GetString(reader.GetOrdinal("image_key")),
                        reader.GetBoolean(reader.GetOrdinal("is_discontinued"))
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgresSearchProvider.SearchAsync SQL 执行失败, SQL: {Sql}", sql);
            throw;
        }

        sw.Stop();
        return new SearchResult(
            total, page, pageSize,
            (int)Math.Ceiling(total / (double)pageSize),
            (int)sw.ElapsedMilliseconds,
            items
        );
    }

    /// <summary>
    /// V2 Task 1.2.7: 聚合搜索 PG 兜底 (Meili 离线时降级使用)
    /// V24-F80 + V24-F94: 改用原生 SQL + CTE UNION + LATERAL JOIN, 复用 BuildBaseFilter + BuildQMatchCte
    /// </summary>
    public async Task<AggregateSearchResponse> AggregateSearchAsync(AggregateSearchRequest req, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // 复用 BuildBaseFilter + BuildQMatchCte (规则 3.2 绝对复用优先)
        var (baseWhereSql, baseParams) = BuildBaseFilter(
            req.Type,
            req.D1, req.D2, req.D3,
            req.H1, req.H2, req.H3,
            req.D7Thread, req.D8Thread,
            req.Tolerance, req.IncludeDiscontinued);

        var (ctePrefixSql, qParams) = BuildQMatchCte(req.Q);
        var hasQ = ctePrefixSql != null;

        // 机型分类过滤 (聚合搜索独有)
        if (!string.IsNullOrWhiteSpace(req.MachineCategory))
        {
            baseWhereSql += " AND EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id AND m.machine_category = @machineCategory)";
            baseParams.Add(new NpgsqlParameter("@machineCategory", NpgsqlDbType.Text) { Value = req.MachineCategory });
        }

        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        // 主查询: CTE + LATERAL JOIN 取分页产品 + 嵌套 OEM 3 / 机型列表
        const string orderBySql = "s.brand_sort_order_min ASC, s.oem_list_sort_order_min ASC, p.updated_at DESC";
        const string selectColumns = @"p.id, p.mr_1, p.product_name_1, p.product_name_2, p.oem_2, p.type,
    p.remark, p.media, p.is_published, p.is_discontinued, p.updated_at,
    -- LATERAL JOIN 聚合 OEM 3 列表 (JSON, 每产品最多 50, 避免笛卡尔积)
    COALESCE(
        (SELECT json_agg(row_to_json(t))
         FROM (
             SELECT x.oem_brand, x.oem_no_3, x.oem_2, x.sort_order, x.machine_type,
                    x.is_published,
                    (SELECT xb.sort_order FROM xref_oem_brand xb
                     WHERE xb.brand = x.oem_brand AND xb.deleted_at IS NULL LIMIT 1) AS brand_sort_order
             FROM cross_references x
             WHERE x.product_id = p.id
               AND x.is_discontinued = false
             ORDER BY (SELECT xb.sort_order FROM xref_oem_brand xb
                       WHERE xb.brand = x.oem_brand AND xb.deleted_at IS NULL LIMIT 1) NULLS LAST,
                      x.sort_order ASC
             LIMIT 50
         ) t),
        '[]'::json
    ) AS oem_list_json,
    -- LATERAL JOIN 聚合机型列表 (JSON, 去重, 每产品最多 50)
    COALESCE(
        (SELECT json_agg(row_to_json(t))
         FROM (
             SELECT DISTINCT m.machine_brand, m.machine_model, m.machine_category
             FROM machine_applications m
             WHERE m.product_id = p.id
             LIMIT 50
         ) t),
        '[]'::json
    ) AS machine_list_json";

        var sql = BuildFullSql(ctePrefixSql, baseWhereSql, selectColumns, orderBySql, hasQ);
        var countSql = BuildCountSql(ctePrefixSql, baseWhereSql, hasQ);

        // 参数克隆
        var allParams = baseParams.Concat(qParams).ToList();
        var countParams = allParams.Select(p => p.Clone()).ToList();
        var listParams = allParams.ToList();
        listParams.Add(new NpgsqlParameter("@pageSize", NpgsqlDbType.Integer) { Value = pageSize });
        listParams.Add(new NpgsqlParameter("@offset", NpgsqlDbType.Integer) { Value = offset });

        long total;
        var hits = new List<AggregateSearchHit>();
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        try
        {
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct);

            // 1. 总数
            await using (var countCmd = new NpgsqlCommand(countSql, conn))
            {
                foreach (var p in countParams) countCmd.Parameters.Add(p);
                countCmd.CommandTimeout = 30;
                var result = await countCmd.ExecuteScalarAsync(ct);
                total = result == null || result == DBNull.Value ? 0 : Convert.ToInt64(result);
            }

            // 2. 分页结果
            await using (var listCmd = new NpgsqlCommand(sql, conn))
            {
                foreach (var p in listParams) listCmd.Parameters.Add(p);
                listCmd.CommandTimeout = 30;
                await using var reader = await listCmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var oemList = ParseOemListJson(reader, "oem_list_json");
                    var machineList = ParseMachineListJson(reader, "machine_list_json");

                    // V24-F80: mr_1 可能为 null, 用 IsDBNull 检查
                    var mr1Idx = reader.GetOrdinal("mr_1");
                    var mr1 = reader.IsDBNull(mr1Idx) ? "" : reader.GetString(mr1Idx);

                    hits.Add(new AggregateSearchHit(
                        Mr1: mr1,
                        ProductName1: reader.IsDBNull(reader.GetOrdinal("product_name_1")) ? null : reader.GetString(reader.GetOrdinal("product_name_1")),
                        ProductName2: reader.IsDBNull(reader.GetOrdinal("product_name_2")) ? null : reader.GetString(reader.GetOrdinal("product_name_2")),
                        Oem2: reader.IsDBNull(reader.GetOrdinal("oem_2")) ? null : reader.GetString(reader.GetOrdinal("oem_2")),
                        Type: reader.GetString(reader.GetOrdinal("type")) ?? "UNKNOWN",
                        Remark: reader.IsDBNull(reader.GetOrdinal("remark")) ? null : reader.GetString(reader.GetOrdinal("remark")),
                        Media: reader.IsDBNull(reader.GetOrdinal("media")) ? null : reader.GetString(reader.GetOrdinal("media")),
                        IsPublished: reader.GetBoolean(reader.GetOrdinal("is_published")),
                        IsDiscontinued: reader.GetBoolean(reader.GetOrdinal("is_discontinued")),
                        OemList: oemList,
                        MachineList: machineList,
                        Formatted: null,  // PG 无原生高亮
                        RankingScore: 0.5  // PG 兜底固定评分
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgresSearchProvider.AggregateSearchAsync SQL 执行失败, SQL: {Sql}", sql);
            throw;
        }

        sw.Stop();
        return new AggregateSearchResponse(
            Total: total,
            Page: page,
            PageSize: pageSize,
            TotalPages: (int)Math.Ceiling(total / (double)pageSize),
            ProcessingTimeMs: (int)sw.ElapsedMilliseconds,
            Provider: "postgres",
            Hits: hits
        );
    }

    private static List<AggregateOemItem> ParseOemListJson(NpgsqlDataReader reader, string columnName)
    {
        var colIdx = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(colIdx)) return new List<AggregateOemItem>();
        var json = reader.GetString(colIdx);
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return new List<AggregateOemItem>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var result = new List<AggregateOemItem>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                result.Add(new AggregateOemItem(
                    OemBrand: el.TryGetProperty("oem_brand", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null,
                    OemNo3: el.TryGetProperty("oem_no_3", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null,
                    Oem2: el.TryGetProperty("oem_2", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : null,
                    SortOrder: el.TryGetProperty("sort_order", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0,
                    MachineType: el.TryGetProperty("machine_type", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null,
                    IsPublished: el.TryGetProperty("is_published", out var p) && p.ValueKind == JsonValueKind.True,
                    BrandSortOrder: el.TryGetProperty("brand_sort_order", out var bs) && bs.ValueKind == JsonValueKind.Number ? bs.GetInt32() : (int?)null
                ));
            }
            return result;
        }
        catch
        {
            return new List<AggregateOemItem>();
        }
    }

    private static List<AggregateMachineItem> ParseMachineListJson(NpgsqlDataReader reader, string columnName)
    {
        var colIdx = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(colIdx)) return new List<AggregateMachineItem>();
        var json = reader.GetString(colIdx);
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return new List<AggregateMachineItem>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var result = new List<AggregateMachineItem>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                result.Add(new AggregateMachineItem(
                    MachineBrand: el.TryGetProperty("machine_brand", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null,
                    MachineModel: el.TryGetProperty("machine_model", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null,
                    MachineCategory: el.TryGetProperty("machine_category", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null
                ));
            }
            return result;
        }
        catch
        {
            return new List<AggregateMachineItem>();
        }
    }

    /// <summary>
    /// V2: PG 兜底 no-op (数据已是 source of truth)
    /// </summary>
    public async Task IndexAsync(IEnumerable<Mr1IndexDoc> docs, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        _logger.LogDebug("PostgresSearchProvider.IndexAsync: no-op (PG 数据已是 source of truth)");
    }

    /// <summary>
    /// V2: PG 兜底 no-op (产品删除由 AdminProductService 处理,PG 自动同步)
    /// </summary>
    public async Task DeleteAsync(IEnumerable<string> mr1s, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        _logger.LogDebug("PostgresSearchProvider.DeleteAsync: no-op");
    }
}
