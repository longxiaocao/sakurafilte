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
///   原生 SQL 不受 EF Core 表达式树限制,可恢复完整功能:
///     - 分词: unnest(string_to_array(@q, ' ')) 生成 token 数组,每个 token 走 ILIKE OR
///     - 排序: CTE 预计算 brand_sort_order_min + oem_list_sort_order_min
///     - 关联: LATERAL JOIN 取每产品 50 个 OEM 3 (避免笛卡尔积)
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
    /// 构建原生 SQL WHERE 子句 + 参数 (供 SearchAsync 与 AggregateSearchAsync 复用)
    /// WHY 抽取: 两个搜索方法 WHERE 子句语义一致, 复用避免漂移 (规则 3.2 绝对复用优先)
    /// </summary>
    /// <returns>(whereSql, parameters)</returns>
    private (string whereSql, List<NpgsqlParameter> parameters) BuildWhereClause(
        string? q, string? type,
        decimal? d1, decimal? d2, decimal? d3,
        decimal? h1, decimal? h2, decimal? h3,
        string? d7Thread, string? d8Thread,
        decimal tolerance, bool includeDiscontinued)
    {
        var conditions = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        // 基础过滤: 未停产 + 已上架
        //   WHY 放在 conditions 而非硬编码: includeDiscontinued=true 时需移除 is_discontinued 过滤
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

        // 关键词搜索: 分词 OR 匹配 (恢复 V24-F76 丢失的分词能力)
        //   WHY unnest(string_to_array): PG 内置分词, 空格分隔, 与 Meili tokenizer 对齐
        //   每个 token 走 6 字段 ILIKE OR + xref 3 字段 + machine 2 字段
        //   全部 token 用 AND 连接 (类似 Meili 的 default matchingStrategy='all')
        if (!string.IsNullOrWhiteSpace(q))
        {
            var raw = q.Trim();
            var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var tokenParams = new List<string>();
            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                // LIKE 转义: \ → \\, % → \%, _ → \_ (与 V24-F76 一致, 复用 EscapeLikePattern 等价逻辑)
                var escaped = token.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                var paramName = $"@q{i}";
                // V24-F80: 用 ANY(string_to_array) 避免 SQL 注入, 单参数传整个 token
                //   WHY 不用 unnest: unnest 需要数组类型参数, ANY 更简单
                tokenParams.Add($@"(
                    p.product_name_1 ILIKE '%' || {paramName} || '%' ESCAPE '\' OR
                    p.product_name_2 ILIKE '%' || {paramName} || '%' ESCAPE '\' OR
                    p.oem_2 ILIKE '%' || {paramName} || '%' ESCAPE '\' OR
                    p.mr_1 ILIKE '%' || {paramName} || '%' ESCAPE '\' OR
                    p.remark ILIKE '%' || {paramName} || '%' ESCAPE '\' OR
                    EXISTS (
                        SELECT 1 FROM cross_references x
                        WHERE x.product_id = p.id
                          AND (x.oem_brand ILIKE '%' || {paramName} || '%' ESCAPE '\'
                            OR x.oem_no_3 ILIKE '%' || {paramName} || '%' ESCAPE '\'
                            OR x.oem_2 ILIKE '%' || {paramName} || '%' ESCAPE '\')
                    ) OR
                    EXISTS (
                        SELECT 1 FROM machine_applications m
                        WHERE m.product_id = p.id
                          AND (m.machine_brand ILIKE '%' || {paramName} || '%' ESCAPE '\'
                            OR m.machine_model ILIKE '%' || {paramName} || '%' ESCAPE '\')
                    )
                )");
                parameters.Add(new NpgsqlParameter(paramName, NpgsqlDbType.Text) { Value = escaped });
            }
            // 多 token 用 AND 连接 (类似 Meili default matchingStrategy='all')
            //   单 token 时直接用, 无 AND
            if (tokenParams.Count == 1)
            {
                conditions.Add(tokenParams[0]);
            }
            else
            {
                conditions.Add("(" + string.Join(" AND ", tokenParams) + ")");
            }
        }

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

        var whereSql = string.Join(" AND ", conditions);
        return (whereSql, parameters);
    }

    /// <summary>
    /// V24-F80: 原生 SQL 搜索 (恢复分词 OR + 三层排序)
    /// </summary>
    public async Task<SearchResult> SearchAsync(SearchRequest req, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var (whereSql, parameters) = BuildWhereClause(
            req.Q, req.Type,
            req.D1, req.D2, req.D3,
            req.H1, req.H2, req.H3,
            req.D7Thread, req.D8Thread,
            req.Tolerance, req.IncludeDiscontinued);

        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        // CTE + LATERAL JOIN 原生 SQL
        //   sort_cte: 预计算 brand_sort_order_min + oem_list_sort_order_min (修复 S3-2/S3-10)
        //   主查询: WHERE + ORDER BY 三层 (brand_sort_order_min → oem_list_sort_order_min → updated_at DESC)
        //   LIMIT/OFFSET 分页 (Phase 2 改 keyset)
        var sql = $@"
WITH sort_cte AS (
    SELECT
        p.id AS product_id,
        COALESCE(MIN(xb.sort_order), 2147483647) AS brand_sort_order_min,
        COALESCE(MIN(x.sort_order), 2147483647) AS oem_list_sort_order_min
    FROM products p
    LEFT JOIN cross_references x ON x.product_id = p.id
        AND x.is_published = true
        AND x.is_discontinued = false
    LEFT JOIN xref_oem_brand xb ON xb.brand = x.oem_brand
        AND xb.deleted_at IS NULL
    WHERE {whereSql}
    GROUP BY p.id
)
SELECT
    p.id, p.mr_1, p.oem_no_display, p.remark, p.type,
    p.d1_mm, p.d2_mm, p.h1_mm, p.image_key, p.is_discontinued,
    p.updated_at
FROM products p
JOIN sort_cte s ON s.product_id = p.id
ORDER BY
    s.brand_sort_order_min ASC,
    s.oem_list_sort_order_min ASC,
    p.updated_at DESC
LIMIT @pageSize OFFSET @offset";

        // 总数查询 (CTE 复用, 单独 COUNT)
        var countSql = $@"
SELECT COUNT(*) FROM products p
WHERE {whereSql}";

        // 参数克隆 (countSql 复用 whereSql 参数, 但 sql 还需 pageSize/offset)
        var countParams = parameters.Select(p => p.Clone()).ToList();
        var listParams = parameters.ToList();
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
    /// V24-F80: 改用原生 SQL + LATERAL JOIN, 与 SearchAsync 复用 BuildWhereClause
    /// </summary>
    public async Task<AggregateSearchResponse> AggregateSearchAsync(AggregateSearchRequest req, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // 复用 BuildWhereClause 构建 WHERE + 参数 (规则 3.2 绝对复用优先)
        var (whereSql, parameters) = BuildWhereClause(
            req.Q, req.Type,
            req.D1, req.D2, req.D3,
            req.H1, req.H2, req.H3,
            req.D7Thread, req.D8Thread,
            req.Tolerance, req.IncludeDiscontinued);

        // 机型分类过滤 (聚合搜索独有)
        if (!string.IsNullOrWhiteSpace(req.MachineCategory))
        {
            whereSql += " AND EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id AND m.machine_category = @machineCategory)";
            parameters.Add(new NpgsqlParameter("@machineCategory", NpgsqlDbType.Text) { Value = req.MachineCategory });
        }

        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        // 主查询: CTE + LATERAL JOIN 取分页产品 + 嵌套 OEM 3 / 机型列表
        var sql = $@"
WITH sort_cte AS (
    SELECT
        p.id AS product_id,
        COALESCE(MIN(xb.sort_order), 2147483647) AS brand_sort_order_min,
        COALESCE(MIN(x.sort_order), 2147483647) AS oem_list_sort_order_min
    FROM products p
    LEFT JOIN cross_references x ON x.product_id = p.id
        AND x.is_published = true
        AND x.is_discontinued = false
    LEFT JOIN xref_oem_brand xb ON xb.brand = x.oem_brand
        AND xb.deleted_at IS NULL
    WHERE {whereSql}
    GROUP BY p.id
)
SELECT
    p.id, p.mr_1, p.product_name_1, p.product_name_2, p.oem_2, p.type,
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
    ) AS machine_list_json
FROM products p
JOIN sort_cte s ON s.product_id = p.id
ORDER BY
    s.brand_sort_order_min ASC,
    s.oem_list_sort_order_min ASC,
    p.updated_at DESC
LIMIT @pageSize OFFSET @offset";

        var countSql = $@"SELECT COUNT(*) FROM products p WHERE {whereSql}";

        // 参数克隆
        var countParams = parameters.Select(p => p.Clone()).ToList();
        var listParams = parameters.ToList();
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
