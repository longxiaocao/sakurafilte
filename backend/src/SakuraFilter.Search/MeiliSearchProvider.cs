using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meilisearch;
using Npgsql;
using NpgsqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Search;

/// <summary>
/// MeiliSearch 搜索配置
/// V2 改造 (Task 0.4):
/// - WriteTargets: 写入目标索引列表 (双索引灰度期间可配置 ["products", "products_v2"])
/// - IndexName: 读取索引名 (灰度切换后改为 products_v2)
/// </summary>
public class MeiliSearchOptions
{
    public string Endpoint { get; set; } = "http://localhost:7700";
    public string? ApiKey { get; set; }
    public string IndexName { get; set; } = "products";
    /// <summary>V2 (S4-9/D4-6): 写入目标列表,默认 ["products"],灰度期 ["products", "products_v2"]</summary>
    public List<string> WriteTargets { get; set; } = new() { "products" };
    public int TimeoutMs { get; set; } = 1000;
}

/// <summary>
/// MeiliSearch 搜索提供者 (主,支持 typo 容错 + facet)
/// V2 改造 (Task 0.4):
/// - 主键从 id (long) 改为 mr_1 (string)
/// - 索引文档从 ProductIndexDoc 改为 Mr1IndexDoc (嵌套结构)
/// - DeleteAsync 签名从 IEnumerable&lt;long&gt; 改为 IEnumerable&lt;string&gt; mr1s
/// - 新增 BuildMr1DocumentAsync 方法构建嵌套文档
/// - XSS 防御: BMP 私用区占位符 + 递归 SanitizeFormatted
/// - 双索引灰度: WriteTargets 列表 + DeleteAsync 遍历全部删除
/// </summary>
public class MeiliSearchProvider : ISearchProvider
{
    private readonly MeilisearchClient _client;
    private readonly MeiliSearchOptions _opts;
    private readonly ILogger<MeiliSearchProvider> _logger;
    private readonly ProductDbContext _db;
    /// <summary>2026-08 性能优化: 富化结果缓存 (可空, 测试/降级场景不注入则走原 PG 查询)</summary>
    private readonly EnrichmentCache? _enrichCache;
    /// <summary>2026-08 性能优化: 搜索响应缓存 (热点查询跳过 Meili+富化+Sanitize, 可空降级)</summary>
    private readonly SearchResponseCache? _searchCache;
    /// <summary>V2 (S4-21): volatile 保证多线程可见性,RefreshWriteTargets 重建时同步</summary>
    private volatile Meilisearch.Index _index;
    /// <summary>V2 (S4-21): volatile 写入目标列表,支持运行时热切换</summary>
    private volatile List<Meilisearch.Index> _writeTargets;

    // V2 (S4-16/S4-17): BMP 私用区单字符占位符 (非 C0 控制字符,避免 HtmlEncode 不转义问题)
    //   WHY \uE000/\uE001: BMP 私用区不会被 WebUtility.HtmlEncode 转义,也不在 C0 控制字符范围
    //   WHY 单字符: Replace 性能优于多字符,且不会被分词器切分
    private const string MarkOpen = "\uE000";
    private const string MarkClose = "\uE001";
    // V2 (S4-17): 暂存用非字符 (Noncharacter),SanitizeFormatted 步骤 1 暂存 Meilisearch 标签
    //   WHY \uFDD0/\uFDD1: 非字符不会被 HtmlEncode 转义,步骤 3 移除时不会被误伤
    private const string MarkOpenStash = "\uFDD0";
    private const string MarkCloseStash = "\uFDD1";

    // 2026-08 性能优化: 高亮字段从 "*"(全 20+ 字段) 收窄为前端实际展示字段
    //   WHY: Meili 1M 文档 × 全字段高亮在并发下是最大开销 (直连实测: hl=* 比 hl=展示字段慢 ~70-100ms P95)
    //   SearchAsync 实际只消费 product_name_1/type/remark 等字段的高亮 (ExtractFieldValue),
    //   收窄后 Meili 高亮计算量从 ~20 字段降到 6 字段, 且不影响展示 (字段全集覆盖)
    private static readonly string[] SearchHighlightFields =
    {
        "product_name_1", "product_name_2", "oem_2", "remark", "type", "media"
    };

    public string Name => "meilisearch";

    public MeiliSearchProvider(
        IOptions<MeiliSearchOptions> opts,
        ILogger<MeiliSearchProvider> logger,
        ProductDbContext db,
        EnrichmentCache? enrichmentCache = null,
        SearchResponseCache? searchCache = null)
    {
        _opts = opts.Value;
        _logger = logger;
        _db = db;
        _enrichCache = enrichmentCache;
        _searchCache = searchCache;
        _client = new MeilisearchClient(_opts.Endpoint, _opts.ApiKey);
        _index = _client.Index(_opts.IndexName);
        _writeTargets = _opts.WriteTargets.Select(name => _client.Index(name)).ToList();
    }

    /// <summary>
    /// V2 (S4-21): 重建写入目标列表 (配置热切换时调用)
    /// </summary>
    public void RefreshWriteTargets(List<string> targetNames)
    {
        _writeTargets = targetNames.Select(name => _client.Index(name)).ToList();
        _logger.LogInformation("Meili 写入目标已刷新: {Targets}", string.Join(", ", targetNames));
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_opts.TimeoutMs);
            var health = await _client.HealthAsync(cts.Token);
            return health is not null && string.Equals(health.Status, "available", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MeiliSearch 健康检查失败: {Endpoint}", _opts.Endpoint);
            return false;
        }
    }

    public async Task<SearchResult> SearchAsync(SearchRequest req, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 2026-08 优化: 响应缓存 — 热点查询直接命中, 跳过 Meili+Sanitize (目录数据低频变化, TTL 30s)
        var cacheKey = BuildSearchCacheKey(req);
        if (_searchCache != null && _searchCache.TryGet<SearchResult>(cacheKey, out var cachedResult) && cachedResult != null)
        {
            return cachedResult;
        }

        // V2: 默认 filter 排除下架 + 要求至少一个上架 OEM 3
        var filters = new List<string>
        {
            "is_published = true",
            "is_discontinued = false"
        };

        if (!string.IsNullOrWhiteSpace(req.Type))
        {
            filters.Add($"type = \"{EscapeFilter(req.Type)}\"");
        }

        // V2: 尺寸范围 filter (d1_mm ~ h4_mm)
        if (req.D1.HasValue)
        {
            var (lo, hi) = (req.D1.Value - req.Tolerance, req.D1.Value + req.Tolerance);
            filters.Add($"d1_mm >= {lo} AND d1_mm <= {hi}");
        }
        if (req.D2.HasValue)
        {
            var (lo, hi) = (req.D2.Value - req.Tolerance, req.D2.Value + req.Tolerance);
            filters.Add($"d2_mm >= {lo} AND d2_mm <= {hi}");
        }
        if (req.D3.HasValue)
        {
            var (lo, hi) = (req.D3.Value - req.Tolerance, req.D3.Value + req.Tolerance);
            filters.Add($"d3_mm >= {lo} AND d3_mm <= {hi}");
        }
        if (req.H1.HasValue)
        {
            var (lo, hi) = (req.H1.Value - req.Tolerance, req.H1.Value + req.Tolerance);
            filters.Add($"h1_mm >= {lo} AND h1_mm <= {hi}");
        }
        if (req.H2.HasValue)
        {
            var (lo, hi) = (req.H2.Value - req.Tolerance, req.H2.Value + req.Tolerance);
            filters.Add($"h2_mm >= {lo} AND h2_mm <= {hi}");
        }
        if (req.H3.HasValue)
        {
            var (lo, hi) = (req.H3.Value - req.Tolerance, req.H3.Value + req.Tolerance);
            filters.Add($"h3_mm >= {lo} AND h3_mm <= {hi}");
        }

        // v24 修复: D7/D8 螺纹规格文本精确匹配 (修复 v18 起已知 bug)
        //   WHY 文本匹配: 螺纹规格如 "M14×1.5" 无法用数值范围表达,与 Product.D7Thread/D8Thread 类型对齐
        if (!string.IsNullOrWhiteSpace(req.D7Thread))
        {
            filters.Add($"d7_thread = \"{EscapeFilter(req.D7Thread)}\"");
        }
        if (!string.IsNullOrWhiteSpace(req.D8Thread))
        {
            filters.Add($"d8_thread = \"{EscapeFilter(req.D8Thread)}\"");
        }

        if (req.IncludeDiscontinued)
        {
            // 用户显式要求含下架,移除 is_discontinued filter
            filters.RemoveAll(f => f.StartsWith("is_discontinued"));
        }

        var searchQuery = new SearchQuery
        {
            Limit = Math.Clamp(req.PageSize, 1, 100),
            Offset = (Math.Max(1, req.Page) - 1) * Math.Clamp(req.PageSize, 1, 100),
            Filter = string.Join(" AND ", filters),
            // V2 (S4-16): 高亮标签用 BMP 私用区占位符,后端 SanitizeFormatted 还原
            // 2026-08 优化: 高亮字段收窄为展示字段 (原 "*" 全字段高亮是 Meili 并发最大开销)
            AttributesToHighlight = SearchHighlightFields,
            HighlightPreTag = MarkOpen,
            HighlightPostTag = MarkClose,
            // 2026-08 优化: SearchAsync 响应不含 _rankingScore (SearchResultItem 无此字段),
            // 关掉让 Meili 少算一步 (AggregateSearchAsync 有独立 query 仍开)
            ShowRankingScore = false,
            // 三层排序 (spec L953):
            //   1. brand_sort_order_min ASC (品牌优先级, null 排末尾)
            //   2. oem_list_sort_order_min ASC (品牌内 OEM 3 优先级, null 排末尾, sort_order=0 视为未维护=null)
            //   3. _ranking_score DESC (MeiliSearch 默认 ranking rules, sort 值相同时自动按相关性排序)
            // WHY: Sort 参数只放前两层, 第三层 _ranking_score 是搜索时计算值非文档属性,
            //      MeiliSearch 在 sort 值相同时自动回退到默认 ranking rules (words/typo/proximity...) 排序
            Sort = new[] { "brand_sort_order_min:asc", "oem_list_sort_order_min:asc" },
        };

        var query = req.Q?.Trim() ?? "";
        // V2: 用 JsonNode 接收原始响应,手动解析 _formatted 字段做 XSS 防御
        var rawResult = await _index.SearchAsync<JsonObject>(query, searchQuery, ct);
        var total = (rawResult as SearchResult<JsonObject>)?.EstimatedTotalHits ?? rawResult.Hits.Count;

        // V2: 映射结果 + SanitizeFormatted 递归处理 _formatted
        var items = new List<SearchResultItem>(rawResult.Hits.Count);
        foreach (var hit in rawResult.Hits)
        {
            var formatted = hit.ContainsKey("_formatted") ? hit["_formatted"] : null;
            if (formatted is JsonObject formattedObj)
            {
                SanitizeFormatted(formattedObj);
            }
            // 提取展示字段 (优先从 _formatted 取高亮版本,降级用原始字段)
            var mr1 = hit.TryGetPropertyValue("mr_1", out var mr1Node) ? mr1Node?.GetValue<string>() : null;
            var productName1 = ExtractFieldValue(hit, formatted, "product_name_1");
            var type = ExtractFieldValue(hit, formatted, "type") ?? "UNKNOWN";
            var remark = ExtractFieldValue(hit, formatted, "remark");
            var d1Mm = hit.TryGetPropertyValue("d1_mm", out var d1Node) ? d1Node?.GetValue<decimal?>() : null;
            var d2Mm = hit.TryGetPropertyValue("d2_mm", out var d2Node) ? d2Node?.GetValue<decimal?>() : null;
            var h1Mm = hit.TryGetPropertyValue("h1_mm", out var h1Node) ? h1Node?.GetValue<decimal?>() : null;
            var isDiscontinued = hit.TryGetPropertyValue("is_discontinued", out var discNode) && discNode?.GetValue<bool>() == true;

            items.Add(new SearchResultItem(
                0,  // V2: Id 不再使用,前端用 mr_1 定位;此处保留 0 占位 (SearchResultItem.Id 字段后续 Phase 1 改 mr1)
                productName1 ?? mr1 ?? "",  // 展示用 product_name_1,降级 mr_1
                remark, type, d1Mm, d2Mm, h1Mm, null, isDiscontinued
            ));
        }

        sw.Stop();
        var pageSize = Math.Clamp(req.PageSize, 1, 100);
        var result = new SearchResult(
            total, Math.Max(1, req.Page), pageSize,
            (int)Math.Ceiling(total / (double)pageSize),
            (int)sw.ElapsedMilliseconds,
            items
        );
        _searchCache?.Set(cacheKey, result);
        return result;
    }

    /// <summary>
    /// V2 Task 1.2: 聚合搜索 (文档级返回 + _formatted 高亮 + _rankingScore)
    /// 与 SearchAsync 区别:
    ///   - 返回完整 oem_list + machine_list 嵌套数组 (SearchAsync 仅返回摘要)
    ///   - 透传 _formatted 字段 (XSS 防御后,前端 v-html 渲染高亮)
    ///   - 透传 _rankingScore (相关性评分)
    ///   - 响应含 Provider="meilisearch" 标识
    /// </summary>
    public async Task<AggregateSearchResponse> AggregateSearchAsync(AggregateSearchRequest req, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 2026-08 优化: 响应缓存 — 前台主路径热点查询直接命中, 跳过 Meili+富化+Sanitize (TTL 30s)
        var cacheKey = BuildAggregateCacheKey(req);
        if (_searchCache != null && _searchCache.TryGet<AggregateSearchResponse>(cacheKey, out var cachedResponse) && cachedResponse != null)
        {
            return cachedResponse;
        }

        // V2: 默认 filter 排除下架 + 要求至少一个上架 OEM 3 (与 SearchAsync 一致)
        var filters = new List<string>
        {
            "is_published = true",
            "is_discontinued = false",
            "oem_list_published_brands IS NOT EMPTY"  // ① P0 缩索引: 文档级至少一个上架 OEM 3 (原 oem_list.is_published 嵌套字段已移除)
        };

        if (!string.IsNullOrWhiteSpace(req.Type))
            filters.Add($"type = \"{EscapeFilter(req.Type)}\"");
        if (!string.IsNullOrWhiteSpace(req.MachineCategory))
        {
            // 🔧 fix(审查): others = 未分类 → 兼容空值 — 导入数据 machine_category 常为 NULL/'',
            //   catalog 归一化 null→others 但过滤只精确匹配 others → 点击目录 0 结果 (用户实测)
            if (req.MachineCategory == "others")
                // 🔧 fix(实证): 导入数据 machine_category 字段缺失(值为 null) — 用 IS NULL 而非 = ""
                filters.Add("(machine_categories IS EMPTY OR machine_categories = \"others\")");  // ① P0 缩索引: 原 machine_list.machine_category 嵌套字段已移除
            else
                filters.Add($"machine_categories = \"{EscapeFilter(req.MachineCategory)}\"");  // ① P0 缩索引: 标量 machine_categories
        }

        // 尺寸范围 filter
        if (req.D1.HasValue) { var (lo, hi) = (req.D1.Value - req.Tolerance, req.D1.Value + req.Tolerance); filters.Add($"d1_mm >= {lo} AND d1_mm <= {hi}"); }
        if (req.D2.HasValue) { var (lo, hi) = (req.D2.Value - req.Tolerance, req.D2.Value + req.Tolerance); filters.Add($"d2_mm >= {lo} AND d2_mm <= {hi}"); }
        if (req.D3.HasValue) { var (lo, hi) = (req.D3.Value - req.Tolerance, req.D3.Value + req.Tolerance); filters.Add($"d3_mm >= {lo} AND d3_mm <= {hi}"); }
        if (req.H1.HasValue) { var (lo, hi) = (req.H1.Value - req.Tolerance, req.H1.Value + req.Tolerance); filters.Add($"h1_mm >= {lo} AND h1_mm <= {hi}"); }
        if (req.H2.HasValue) { var (lo, hi) = (req.H2.Value - req.Tolerance, req.H2.Value + req.Tolerance); filters.Add($"h2_mm >= {lo} AND h2_mm <= {hi}"); }
        if (req.H3.HasValue) { var (lo, hi) = (req.H3.Value - req.Tolerance, req.H3.Value + req.Tolerance); filters.Add($"h3_mm >= {lo} AND h3_mm <= {hi}"); }

        // v24 修复: D7/D8 螺纹规格文本精确匹配 (与 SearchAsync 一致)
        if (!string.IsNullOrWhiteSpace(req.D7Thread))
            filters.Add($"d7_thread = \"{EscapeFilter(req.D7Thread)}\"");
        if (!string.IsNullOrWhiteSpace(req.D8Thread))
            filters.Add($"d8_thread = \"{EscapeFilter(req.D8Thread)}\"");

        if (req.IncludeDiscontinued)
            filters.RemoveAll(f => f.StartsWith("is_discontinued"));

        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, 100);

        var searchQuery = new SearchQuery
        {
            Limit = pageSize,
            Offset = (page - 1) * pageSize,
            Filter = string.Join(" AND ", filters),
            // V2 (S4-16): 高亮标签用 BMP 私用区占位符, SanitizeFormatted 还原
            // 2026-08 优化: 高亮字段收窄为展示字段 (前端仅消费 product_name_1 等, 原 "*" 全字段高亮是并发最大开销)
            AttributesToHighlight = SearchHighlightFields,
            HighlightPreTag = MarkOpen,
            HighlightPostTag = MarkClose,
            ShowRankingScore = true,
            // 三层排序 (spec L953): 同 SearchAsync, brand → oem3 → _ranking_score
            // WHY: Sort 参数只放前两层, 第三层 _ranking_score 是搜索时计算值非文档属性,
            //      MeiliSearch 在 sort 值相同时自动回退到默认 ranking rules (words/typo/proximity...) 排序
            Sort = new[] { "brand_sort_order_min:asc", "oem_list_sort_order_min:asc" },
        };

        var query = req.Q?.Trim() ?? "";
        var rawResult = await _index.SearchAsync<JsonObject>(query, searchQuery, ct);
        var total = (rawResult as SearchResult<JsonObject>)?.EstimatedTotalHits ?? rawResult.Hits.Count;

        // V3(2026-08-24): Hybrid 白名单补位 — Meili 命中集上限 1000, 白名单产品(竞价排名)
        //   可能被 ranking 挤出命中集 (如搜品牌缩写时 oem_brands_str 单 token 匹配分数低).
        //   二次重排只能排"命中集内"顺序, 集外产品必须从 PG 单独取并强制排最前.
        //   补位条件: 搜索词非空 且 白名单产品的 品牌/OEM3/OEM2/产品名 含搜索词 (大小写不敏感)
        //   白名单数量级 < 100, 构造成本可忽略; 查询失败降级为空 (不阻塞主搜索)
        // V3(2026-08-25): OEM 精确子串补位 — 搜 OEM 号 (含数字) 时 Meili typo 数字容错
        //   产生大量误匹配 (如 '1002390' 71 命中, 精确产品 MR00839317 排 71 位). 
        //   从 PG 按 oem_no_3 ILIKE %q% 精确子串匹配, 强制排最前 (精确 OEM 优先于 typo)
        var boostHits = new List<AggregateSearchHit>();
        var boostMr1s = new HashSet<string>();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var qLower = query.ToLowerInvariant();
            boostHits = await LoadWhitelistBoostHitsAsync(qLower, ct);
            if (ContainsDigit(qLower))
            {
                // 含数字 → OEM 号特征, 精确子串补位 (精确匹配优先)
                var oemExact = await LoadOemExactBoostHitsAsync(qLower, ct);
                boostHits = boostHits.Concat(oemExact).ToList();
            }
            boostMr1s = boostHits.Select(h => h.Mr1).ToHashSet();
        }

        // ① P0 缩索引: oem_list/machine_list 已移出 Meili 索引体, 检索后按 mr_1 从 PG 批量回填 (仅当前页)
        var pageMr1s = rawResult.Hits
            .Select(h => h.TryGetPropertyValue("mr_1", out var n) ? n?.GetValue<string>() : null)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m!)
            .Where(m => !boostMr1s.Contains(m))  // 白名单补位产品不重复回填
            .Distinct()
            .ToList();
        // ① P0 缩索引: oem/machine 列表由 PG 回填。PG 不可用时降级为空列表,
        //   不阻断 Meili 检索主流程 (保持 ResilientSearchProvider 的韧性设计: 搜索不依赖 PG 在线)。
        Dictionary<string, (List<AggregateOemItem> OemList, List<AggregateMachineItem> MachineList)> enrichMap = new();
        try
        {
            enrichMap = await EnrichFromPgAsync(pageMr1s, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PG 回填 oem/machine 列表失败, 降级为空 (搜索结果仍可用)");
        }

        // 映射 hits → AggregateSearchHit (含完整 oem_list + machine_list + _formatted + _rankingScore)
        // V3(2026-08-24): 暂存白名单/品牌 sort 值, 用于二次重排 (白名单优先, 不依赖 Meili inverted index 索引时延)
        var indexed = new List<(AggregateSearchHit Hit, int? OemSort, int? BrandSort, int OriginalIdx)>(rawResult.Hits.Count);
        foreach (var hit in rawResult.Hits)
        {
            // V3: 跳过已由白名单补位覆盖的产品 (避免重复)
            if (hit.TryGetPropertyValue("mr_1", out var hitMr1) && hitMr1?.GetValue<string>() is string hm1 && boostMr1s.Contains(hm1))
                continue;

            // XSS 防御: 递归处理 _formatted
            var formatted = hit.ContainsKey("_formatted") ? hit["_formatted"] : null;
            if (formatted is JsonObject formattedObj)
            {
                SanitizeFormatted(formattedObj);
            }

            // 提取顶层字段 (优先 _formatted 高亮版本)
            var mr1 = hit.TryGetPropertyValue("mr_1", out var mr1Node) ? mr1Node?.GetValue<string>() : null;
            var productName1 = ExtractFieldValue(hit, formatted, "product_name_1");
            var productName2 = ExtractFieldValue(hit, formatted, "product_name_2");
            var oem2 = ExtractFieldValue(hit, formatted, "oem_2");
            var type = ExtractFieldValue(hit, formatted, "type") ?? "UNKNOWN";
            var remark = ExtractFieldValue(hit, formatted, "remark");
            var media = ExtractFieldValue(hit, formatted, "media");
            var isPublished = hit.TryGetPropertyValue("is_published", out var pubNode) && pubNode?.GetValue<bool>() == true;
            var isDiscontinued = hit.TryGetPropertyValue("is_discontinued", out var discNode) && discNode?.GetValue<bool>() == true;

            // 提取白名单/品牌 sort 值 (用于二次重排)
            int? oemSort = TryGetInt(hit, "oem_list_sort_order_min");
            int? brandSort = TryGetInt(hit, "brand_sort_order_min");

            // WHY: 兼容 P0 缩索引上线前已存在的旧文档。旧文档仍带嵌套列表，PG 暂时不可用时不能丢失公开 OEM/机型。
            enrichMap.TryGetValue(mr1 ?? "", out var enrichPair);
            var oemList = enrichPair.OemList ?? new List<AggregateOemItem>();
            var machineList = enrichPair.MachineList ?? new List<AggregateMachineItem>();
            if (oemList.Count == 0 && hit.TryGetPropertyValue("oem_list", out var legacyOem) && legacyOem != null)
                oemList = ParseLegacyOemList(legacyOem);
            if (machineList.Count == 0 && hit.TryGetPropertyValue("machine_list", out var legacyMachine) && legacyMachine != null)
                machineList = ParseLegacyMachineList(legacyMachine);

            // 产品名 1 允许为空；公开目录仍需有可读标题，类型是稳定的业务兜底字段。
            productName1 ??= type;

            // _rankingScore (Meilisearch 0-1)
            double? rankingScore = null;
            if (hit.TryGetPropertyValue("_rankingScore", out var rsNode) && rsNode != null)
            {
                try { rankingScore = rsNode.GetValue<double>(); } catch { /* 兜底: null */ }
            }

            // _formatted 转 Dictionary (前端 v-html 渲染用)
            Dictionary<string, object?>? formattedDict = null;
            if (formatted is JsonObject fObj)
            {
                formattedDict = new Dictionary<string, object?>();
                foreach (var prop in fObj)
                {
                    formattedDict[prop.Key] = prop.Value?.Deserialize<object>();
                }
            }

            indexed.Add((
                new AggregateSearchHit(
                    Mr1: mr1 ?? "",
                    ProductName1: productName1,
                    ProductName2: productName2,
                    Oem2: oem2,
                    Type: type,
                    Remark: remark,
                    Media: media,
                    IsPublished: isPublished,
                    IsDiscontinued: isDiscontinued,
                    OemList: oemList,
                    MachineList: machineList,
                    Formatted: formattedDict,
                    RankingScore: rankingScore
                ),
                oemSort, brandSort, indexed.Count
            ));
        }

        // V3(2026-08-24): Hybrid 补位 + 二次重排
        //   1. 白名单补位产品 (boostHits) 无条件最前 — 竞价排名语义 (Meili 命中集外也保证可见)
        //   2. Meili 命中集内: 白名单 (oemSort != null) 排前, 非白名单保持 Meili 原相对位置
        var finalHits = boostHits
            .Concat(indexed
                .OrderBy(t => t.OemSort.HasValue ? 0 : 1)
                .ThenBy(t => t.OemSort ?? int.MaxValue)
                .ThenBy(t => t.BrandSort ?? int.MaxValue)
                .ThenBy(t => t.OriginalIdx)
                .Select(t => t.Hit))
            .ToList();

        sw.Stop();
        var response = new AggregateSearchResponse(
            Total: total,
            Page: page,
            PageSize: pageSize,
            TotalPages: (int)Math.Ceiling(total / (double)pageSize),
            ProcessingTimeMs: (int)sw.ElapsedMilliseconds,
            Provider: "meilisearch",
            Hits: finalHits
        );
        _searchCache?.Set(cacheKey, response);
        return response;
    }

    /// <summary>2026-08 优化: 摘要搜索缓存键 (全参数规范化, 命中即同结果)</summary>
    private static string BuildSearchCacheKey(SearchRequest req) => "srch:" + string.Join('|',
        req.Q?.Trim() ?? "", req.Type ?? "",
        req.D1, req.D2, req.D3, req.H1, req.H2, req.H3,
        req.Tolerance, req.IncludeDiscontinued, req.Page, req.PageSize,
        req.D7Thread ?? "", req.D8Thread ?? "");

    /// <summary>2026-08 优化: 聚合搜索缓存键</summary>
    private static string BuildAggregateCacheKey(AggregateSearchRequest req) => "aggr:" + string.Join('|',
        req.Q?.Trim() ?? "", req.MachineCategory ?? "", req.Type ?? "",
        req.D1, req.D2, req.D3, req.H1, req.H2, req.H3,
        req.Tolerance, req.IncludeDiscontinued, req.Page, req.PageSize,
        req.D7Thread ?? "", req.D8Thread ?? "");

    /// <summary>
    /// ① P0 缩索引: Meili 索引体已移出 oem_list/machine_list 嵌套数组 (27GB 主因),
    ///   聚合检索响应所需的完整 oem_list/machine_list 改在检索后按 mr_1 批量从 PG 回填。
    ///   仅回填当前页 hits 对应的 mr_1,避免在全量结果上展开大数组。
    ///   聚合口径与 PostgresSearchProvider 的 LATERAL JOIN 一致 (oem_list_json / machine_list_json)。
    /// </summary>
    private async Task<Dictionary<string, (List<AggregateOemItem> OemList, List<AggregateMachineItem> MachineList)>> EnrichFromPgAsync(
        IEnumerable<string> mr1s, CancellationToken ct)
    {
        var result = new Dictionary<string, (List<AggregateOemItem>, List<AggregateMachineItem>)>();
        var list = mr1s.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToList();
        if (list.Count == 0) return result;

        // 2026-08 性能优化: 先查富化缓存, 命中直取 (高并发 c50/c100 瓶颈 = 每次搜索回 PG 富化)
        //   miss 的 mr1 才走 PG 批量查询; 缓存永不作为正确性依赖 (miss → 原查询逻辑)
        var missList = new List<string>();
        if (_enrichCache != null)
        {
            foreach (var mr1 in list)
            {
                if (_enrichCache.TryGet(mr1, out var cached) && cached != null)
                    result[mr1] = (cached.OemList, cached.MachineList);
                else
                    missList.Add(mr1);
            }
        }
        else
        {
            missList.AddRange(list);
        }
        if (missList.Count == 0) return result;
        list = missList;

        const string sql = @"
            SELECT p.mr_1,
                COALESCE((SELECT json_agg(row_to_json(t)) FROM (
                    SELECT x.oem_brand, x.oem_no_3, x.oem_2, x.sort_order, x.machine_type, x.is_published,
                           (SELECT xb.sort_order FROM xref_oem_brand xb WHERE xb.brand = x.oem_brand AND xb.deleted_at IS NULL LIMIT 1) AS brand_sort_order
                    FROM cross_references x
                    WHERE x.product_id = p.id AND x.is_discontinued = false
                    ORDER BY (SELECT xb.sort_order FROM xref_oem_brand xb WHERE xb.brand = x.oem_brand AND xb.deleted_at IS NULL LIMIT 1) NULLS LAST, x.sort_order ASC, x.oem_brand, x.oem_no_3
                    LIMIT 50) t), '[]'::json) AS oem_list_json,
                COALESCE((SELECT json_agg(row_to_json(t)) FROM (
                SELECT DISTINCT m.machine_brand, m.machine_model, m.machine_category, m.engine_brand
                FROM machine_applications m
                WHERE m.product_id = p.id
                ORDER BY m.machine_brand, m.machine_model   -- ① P0 缩索引: 对齐旧版 machine_list 展示顺序
                LIMIT 50) t), '[]'::json) AS machine_list_json
            FROM products p
            WHERE p.mr_1 = ANY(@mr1s)";

        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        var opened = false;
        try
        {
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync(ct);
                opened = true;
            }
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.CommandTimeout = 30;
            cmd.Parameters.Add(new NpgsqlParameter("@mr1s", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = list.ToArray() });
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var mr1 = reader.IsDBNull(reader.GetOrdinal("mr_1")) ? "" : reader.GetString(reader.GetOrdinal("mr_1"));
                var oemListJson = reader.IsDBNull(reader.GetOrdinal("oem_list_json")) ? "[]" : reader.GetFieldValue<string>(reader.GetOrdinal("oem_list_json"));
                var machineListJson = reader.IsDBNull(reader.GetOrdinal("machine_list_json")) ? "[]" : reader.GetFieldValue<string>(reader.GetOrdinal("machine_list_json"));
                var oemList = ParseEnrichedOemList(oemListJson);
                var machineList = ParseEnrichedMachineList(machineListJson);
                result[mr1] = (oemList, machineList);
                // 回填缓存 (仅本批 miss 的 mr1; 缓存内 List 视为只读)
                _enrichCache?.Set(mr1, oemList, machineList);
            }
        }
        finally
        {
            if (opened && conn.State == System.Data.ConnectionState.Open)
                await conn.CloseAsync();
        }
        return result;
    }

    /// <summary>① P0 缩索引: 解析 PG 回填的 oem_list_json (对齐 AggregateOemItem 形状)</summary>
    private static List<AggregateOemItem> ParseEnrichedOemList(string json)
    {
        var list = new List<AggregateOemItem>();
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            list.Add(new AggregateOemItem(
                OemBrand: item.TryGetProperty("oem_brand", out var b) ? b.GetString() : null,
                OemNo3: item.TryGetProperty("oem_no_3", out var n) ? n.GetString() : null,
                Oem2: item.TryGetProperty("oem_2", out var o2) ? o2.GetString() : null,
                SortOrder: item.TryGetProperty("sort_order", out var so) && so.ValueKind == JsonValueKind.Number ? so.GetInt32() : 0,
                MachineType: item.TryGetProperty("machine_type", out var mt) ? mt.GetString() : null,
                IsPublished: item.TryGetProperty("is_published", out var ip) && ip.ValueKind == JsonValueKind.True,
                BrandSortOrder: item.TryGetProperty("brand_sort_order", out var bso) && bso.ValueKind == JsonValueKind.Number ? bso.GetInt32() : null
            ));
        }
        return list;

    }

    /// <summary>① P0 缩索引: 解析 PG 回填的 machine_list_json (对齐 AggregateMachineItem 形状)</summary>
    private static List<AggregateMachineItem> ParseEnrichedMachineList(string json)
    {
        var list = new List<AggregateMachineItem>();
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            list.Add(new AggregateMachineItem(
                MachineBrand: item.TryGetProperty("machine_brand", out var mb) ? mb.GetString() : null,
                MachineModel: item.TryGetProperty("machine_model", out var mm) ? mm.GetString() : null,
                MachineCategory: item.TryGetProperty("machine_category", out var mc) ? mc.GetString() : null,
                EngineBrand: item.TryGetProperty("engine_brand", out var eb) ? eb.GetString() : null
            ));
        }
        return list;
    }

    private static List<AggregateOemItem> ParseLegacyOemList(JsonNode node)
    {
        try { return ParseEnrichedOemList(node.ToJsonString()); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return new List<AggregateOemItem>(); }
    }

    private static List<AggregateMachineItem> ParseLegacyMachineList(JsonNode node)
    {
        try { return ParseEnrichedMachineList(node.ToJsonString()); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return new List<AggregateMachineItem>(); }
    }

    private static string? ExtractFieldValue(JsonObject hit, JsonNode? formatted, string fieldName)
    {
        if (formatted is JsonObject formattedObj &&
            formattedObj.TryGetPropertyValue(fieldName, out var fNode) &&
            fNode is JsonValue fVal && fVal.TryGetValue<string>(out var s))
        {
            return s;
        }
        if (hit.TryGetPropertyValue(fieldName, out var node) && node is JsonValue val && val.TryGetValue<string>(out var s2))
        {
            return s2;
        }
        return null;
    }

    /// <summary>V3(2026-08-24): 从 Meili hit 安全读取 int 字段 (sort 字段), 失败返回 null</summary>
    private static int? TryGetInt(JsonObject hit, string fieldName)
    {
        if (!hit.TryGetPropertyValue(fieldName, out var node) || node == null) return null;
        if (node is JsonValue val)
        {
            if (val.TryGetValue<int>(out var i)) return i;
            if (val.TryGetValue<long>(out var l)) return (int)l;
            if (val.TryGetValue<double>(out var d)) return (int)d;
        }
        return null;
    }

    /// <summary>
    /// V3(2026-08-24): Hybrid 搜索 — 白名单(竞价排名)产品强制补位
    ///   WHY: Meili 命中集上限 1000 + ranking 规则可能把白名单产品挤出命中集
    ///        (搜品牌缩写时 oem_brands_str 单 token 匹配分数低于 typo 匹配),
    ///        二次重排只能排命中集内顺序. 本方法从 PG 直接取白名单产品,
    ///        搜索词匹配 (品牌/OEM3/OEM2/产品名 含 q) 时构造成结果排最前.
    ///   白名单数量级 &lt; 100, 构造成本可忽略; 查询失败降级为空 (不阻塞主搜索).
    /// </summary>
    private async Task<List<AggregateSearchHit>> LoadWhitelistBoostHitsAsync(string queryLower, CancellationToken ct)
    {
        try
        {
            // V3(2026-08-25): Join Products 取真实 ProductName1/Type/Mr1 — xrefs.product_name_1
            //   是 ETL 冗余列, 与 products 不一致 (白名单量少暂未暴露, 但同 OEM 精确补位一并发修)
            var rows = await _db.CrossReferences.AsNoTracking()
                .Where(x => x.IsWhitelisted && !x.IsDiscontinued && x.IsPublished)
                .OrderBy(x => x.SortOrder)
                .Join(_db.Products.AsNoTracking(), x => x.ProductId, p => p.Id, (x, p) => new
                {
                    x.ProductId, x.SortOrder, x.OemBrand, x.OemNo3, x.Oem2, x.MachineType,
                    p.ProductName1, p.Mr1, p.Type
                })
                .ToListAsync(ct);
            if (rows.Count == 0) return new List<AggregateSearchHit>();

            // 同一产品多品牌白名单: 取最小 sortOrder 的一条 (避免重复展示); ProductName1/Mr1 已是真实值
            var best = rows.GroupBy(r => r.ProductId).Select(g => g.First()).ToList();

            var result = new List<AggregateSearchHit>(best.Count);
            foreach (var r in best)
            {
                // 搜索词匹配判断: 品牌/OEM3/OEM2/产品名 任一包含 (大小写不敏感)
                var haystacks = new[] { r.OemBrand, r.OemNo3, r.Oem2, r.ProductName1 };
                if (!haystacks.Any(h => h != null && h.ToLowerInvariant().Contains(queryLower, StringComparison.OrdinalIgnoreCase)))
                    continue;  // 与搜索词不相关, 不补位 (避免污染无关搜索)

                if (string.IsNullOrWhiteSpace(r.Mr1))
                    continue;

                var oemList = new List<AggregateOemItem>
                {
                    new(r.OemBrand, r.OemNo3, r.Oem2, r.SortOrder, r.MachineType, true, null)
                };
                result.Add(new AggregateSearchHit(
                    Mr1: r.Mr1,
                    ProductName1: r.ProductName1,
                    ProductName2: null,
                    Oem2: r.Oem2,
                    Type: r.Type ?? r.ProductName1 ?? "UNKNOWN",
                    Remark: null,
                    Media: null,
                    IsPublished: true,
                    IsDiscontinued: false,
                    OemList: oemList,
                    MachineList: new List<AggregateMachineItem>(),
                    Formatted: null,
                    RankingScore: null
                ));
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "白名单补位查询失败, 降级为空 (主搜索不受影响)");
            return new List<AggregateSearchHit>();
        }
    }

    /// <summary>
    /// V3(2026-08-25): OEM 精确子串补位 — 搜含数字的 OEM 号时, Meili typo 数字容错产生大量
    ///   误匹配 (如 '1002390' 71 命中, 精确产品 MR00839317 排第 71 位).
    ///   本方法从 PG 按 oem_no_3 ILIKE %q% 精确子串匹配, 强制排最前 (精确 OEM 优先于 typo 匹配).
    ///   与白名单补位同模式: 数量级小, 构造成本可忽略; 失败降级为空 (不阻塞主搜索).
    /// </summary>
    private async Task<List<AggregateSearchHit>> LoadOemExactBoostHitsAsync(string queryLower, CancellationToken ct)
    {
        try
        {
            // ILIKE 通配符转义 (用户输入含 %/_ 时按字面匹配)
            string likePattern = "%" + EscapeLikePattern(queryLower) + "%";
            // V3(2026-08-25): Join Products 取真实 ProductName1/Type/Mr1 — 之前误用 xrefs 冗余字段
            //   (xrefs.product_name_1 是 ETL 导入时的冗余列, 与 products 不一致, 如 MR00839317 真实是 OIL FILTER
            //    但 xrefs 里出现 FUEL/AIR/PETROL/WATER SEPARATOR 等多种值)
            var rows = await _db.CrossReferences.AsNoTracking()
                .Where(x => !x.IsDiscontinued && EF.Functions.ILike(x.OemNo3, likePattern))
                .OrderBy(x => x.SortOrder)
                .Take(30)  // 精确子串匹配上限 30, 防极端输入扫全表
                .Join(_db.Products.AsNoTracking(), x => x.ProductId, p => p.Id, (x, p) => new
                {
                    x.ProductId, x.SortOrder, x.OemBrand, x.OemNo3, x.Oem2, x.MachineType,
                    p.ProductName1, p.Mr1, p.Type
                })
                .ToListAsync(ct);
            if (rows.Count == 0) return new List<AggregateSearchHit>();

            // 同一产品多个 OEM 号命中: 去重 (取最小 sortOrder); 此时 ProductName1/Mr1 已是真实值
            var best = rows.GroupBy(r => r.ProductId).Select(g => g.First()).ToList();

            var result = new List<AggregateSearchHit>(best.Count);
            foreach (var r in best)
            {
                if (string.IsNullOrWhiteSpace(r.Mr1))
                    continue;
                var oemList = new List<AggregateOemItem>
                {
                    new(r.OemBrand, r.OemNo3, r.Oem2, r.SortOrder, r.MachineType, true, null)
                };
                result.Add(new AggregateSearchHit(
                    Mr1: r.Mr1,
                    ProductName1: r.ProductName1,
                    ProductName2: null,
                    Oem2: r.Oem2,
                    Type: r.Type ?? r.ProductName1 ?? "UNKNOWN",
                    Remark: null,
                    Media: null,
                    IsPublished: true,
                    IsDiscontinued: false,
                    OemList: oemList,
                    MachineList: new List<AggregateMachineItem>(),
                    Formatted: null,
                    RankingScore: null
                ));
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OEM 精确子串补位查询失败, 降级为空 (主搜索不受影响)");
            return new List<AggregateSearchHit>();
        }
    }

    /// <summary>判断字符串是否含数字 (OEM 号特征, 触发精确子串补位)</summary>
    private static bool ContainsDigit(string s)
    {
        foreach (var c in s)
        {
            if (c >= '0' && c <= '9') return true;
        }
        return false;
    }

    /// <summary>ILIKE 模式转义 (%, _ 按字面匹配)</summary>
    private static string EscapeLikePattern(string s)
        => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public async Task IndexAsync(IEnumerable<Mr1IndexDoc> docs, CancellationToken ct = default)
    {
        var batch = docs.ToList();
        if (batch.Count == 0) return;

        // v30-23 P0 修复: 过滤空主键文档, 避免 Meili 拒绝整个批次 (Document identifier "" is invalid)
        //   根因: 测试数据或旧数据中 mr_1 可能为空, 导致整个 1000 条批次被 Meili 拒绝
        var validBatch = batch.Where(d => !string.IsNullOrWhiteSpace(d.Mr1)).ToList();
        if (validBatch.Count < batch.Count)
        {
            _logger.LogWarning("过滤空主键文档: 输入={Total}, 有效={Valid}, 跳过={Skipped}",
                batch.Count, validBatch.Count, batch.Count - validBatch.Count);
        }
        if (validBatch.Count == 0) return;

        // V2 (S4-21): 遍历所有 WriteTargets 双写 (灰度期间同时写 products + products_v2)
        foreach (var target in _writeTargets)
        {
            try
            {
                // V2: 主键改为 mr_1 (字符串)
                var task = await target.AddDocumentsAsync(validBatch, primaryKey: "mr_1", cancellationToken: ct);
                _logger.LogInformation("Meili 索引已提交: target={Target}, count={Count}, taskUid={TaskUid}",
                    target.Uid, validBatch.Count, task.TaskUid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Meili 索引写入失败 target={Target} (将由 IndexReplayWorker 补偿)", target.Uid);
                // 不抛出: 单个 target 失败不影响其他 target,失败任务由 search_index_pending 补偿
            }
        }
    }

    public async Task DeleteAsync(IEnumerable<string> mr1s, CancellationToken ct = default)
    {
        var mr1List = mr1s.ToList();
        if (mr1List.Count == 0) return;

        // V2 (S4-21): 遍历所有 WriteTargets 双删 (灰度期间两个索引都需删除)
        foreach (var target in _writeTargets)
        {
            try
            {
                // Meili 0.15.4: DeleteDocumentsAsync 接受 IEnumerable<string>
                var task = await target.DeleteDocumentsAsync(mr1List, cancellationToken: ct);
                _logger.LogInformation("Meili 删除已提交: target={Target}, count={Count}, taskUid={TaskUid}",
                    target.Uid, mr1List.Count, task.TaskUid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Meili 删除失败 target={Target} (将由 IndexReplayWorker 补偿)", target.Uid);
            }
        }
    }

    /// <summary>
    /// V2 (Task 0.4.2a/0.4.18): 构建 Mr1IndexDoc 文档
    /// - 查询 cross_references + xref_oem_brand + machine_applications
    /// - 软删除 brand 的 OEM 3 仍保留可搜索 (S4-11: D21 决策),但 brand_sort_order 为 null
    /// - 预计算扁平化冗余字段 (OemListPublishedBrands/OemBrandsStr 等)
    /// </summary>
    public async Task<Mr1IndexDoc> BuildMr1DocumentAsync(Product p, CancellationToken ct = default)
    {
        // S4-11: 查询不过滤 b.DeletedAt IS NULL,保留软删除 brand 的 OEM 3
        var oemListRaw = await _db.CrossReferences
            .AsNoTracking()
            .Where(x => x.ProductId == p.Id && !x.IsDiscontinued)
            .Select(x => new
            {
                x.OemBrand,
                x.OemNo3,
                x.Oem2,
                x.SortOrder,
                x.MachineType,
                x.IsPublished,
                x.IsWhitelisted,
                BrandSortOrder = _db.XrefOemBrands
                    .Where(b => b.Brand == x.OemBrand && b.DeletedAt == null)
                    .Select(b => (int?)b.SortOrder)
                    .FirstOrDefault(),
                BrandDeletedAt = _db.XrefOemBrands
                    .Where(b => b.Brand == x.OemBrand)
                    .Select(b => b.DeletedAt)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        // ① P0 缩索引: 不再构建完整 OemListItem/MachineListItem 嵌套数组 (随交叉引用数膨胀, 是 27GB 主因)。
        //   仅基于原始查询计算标量冗余字段 (machine_categories / machine_brands_str), 供 Meili 过滤/检索;
        //   完整 oem_list/machine_list 由检索后从 PG 按 mr_1 回填 (EnrichFromPgAsync)。

        // 机型标量 (去重): machine_categories 过滤 + machine_brands_str 检索
        var machineRows = await _db.MachineApplications
            .AsNoTracking()
            .Where(m => m.ProductId == p.Id)
            .Select(m => new { m.MachineBrand, m.MachineModel, m.MachineCategory, m.EngineBrand })
            .Distinct()
            .ToListAsync(ct);
        var machineCategories = machineRows
            .Select(m => m.MachineCategory)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .Distinct()
            .ToList();
        var machineBrandsStr = string.Join(" ", machineRows
            .Select(m => m.MachineBrand)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Distinct());
        var machineModelsStr = string.Join(" ", machineRows
            .Select(m => m.MachineModel)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct());
        var engineBrandsStr = string.Join(" ", machineRows
            .Select(m => m.EngineBrand)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Distinct());

        // ===== 扁平化冗余字段计算 =====
        // S3-7: 仅含上架 OEM 3 的 brand/oem_no_3 去重列表
        var publishedOemList = oemListRaw.Where(x => x.IsPublished).ToList();
        var publishedBrands = publishedOemList
            .Select(x => x.OemBrand)
            .Where(b => !string.IsNullOrEmpty(b))
            .Distinct()
            .Select(b => b!)  // CS8620: Where 已过滤 null/空, ! 抑制可空性差异
            .ToList();
        var publishedNo3s = publishedOemList
            .Select(x => x.OemNo3)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .Select(n => n!)  // CS8620: 同上
            .ToList();

        // S4-13: 分隔符改空格 (对齐 separatorTokens 配置)
        var oemBrandsStr = string.Join(" ", oemListRaw.Select(x => x.OemBrand).Where(b => !string.IsNullOrEmpty(b)).Distinct());
        var oemNo3sStr = string.Join(" ", oemListRaw.Select(x => x.OemNo3).Where(n => !string.IsNullOrEmpty(n)).Distinct());

        // S3-8/S4-25: brand_sort_order_min 只取未软删除 brand 的 sort_order MIN,无品牌时为 null (排末尾)
        // 修复: 原 DefaultIfEmpty() 对 IEnumerable<int> 返回 0,导致无品牌产品排在最前 (违背品牌优先设计)
        int? brandSortOrderMin = oemListRaw
            .Where(x => x.BrandDeletedAt == null && x.BrandSortOrder.HasValue)
            .Select(x => (int?)x.BrandSortOrder!.Value)
            .Min();

        // S4-16: oem_list_sort_order_min 取白名单内上架 OEM 3 的 sort_order MIN
        // V3(2026-08-24): 白名单判定改 is_whitelisted — 源数据 92% 记录 sort_order>0,
        //   原"sort_order > 0 视为管理员维护过优先级"被源数据淹没 (所有产品都有值, 排序无区分);
        //   现仅 is_whitelisted=true (管理员手动添加到白名单) 的 OEM 参与优先排序, 语义与白名单页一致
        //   修复: sort_order=0 或未入白名单 → 不参与 (视为 null 排末尾)
        int? oemListSortOrderMin = publishedOemList
            .Where(x => x.IsWhitelisted && x.SortOrder > 0)
            .Select(x => (int?)x.SortOrder)
            .Min();

        // P2-3: 图片 key 列表 (主图按 OEM 3 命名, 详情图按 MR.1 命名)
        //   主图: image_role='primary' AND oem_no_3 IS NOT NULL (与 uq_product_images_primary 索引口径一致)
        //   详情图: image_role='detail'
        //   默认空列表: 无图片数据时 ToListAsync 返回空 List
        var imagePrimaryKeys = await _db.ProductImages
            .AsNoTracking()
            .Where(i => i.ProductId == p.Id && i.ImageRole == "primary" && i.OemNo3 != null)
            .Select(i => i.ImageKey)
            .ToListAsync(ct);
        var imageDetailKeys = await _db.ProductImages
            .AsNoTracking()
            .Where(i => i.ProductId == p.Id && i.ImageRole == "detail")
            .Select(i => i.ImageKey)
            .ToListAsync(ct);

        return new Mr1IndexDoc(
            Mr1: p.Mr1 ?? "",
            ProductName1: p.ProductName1,
            ProductName2: p.ProductName2,
            Oem2: p.Oem2,
            Type: p.Type ?? "UNKNOWN",
            Remark: p.Remark,
            Media: p.Media,
            D1Mm: p.D1Mm, D2Mm: p.D2Mm, D3Mm: p.D3Mm, D4Mm: p.D4Mm,
            H1Mm: p.H1Mm, H2Mm: p.H2Mm, H3Mm: p.H3Mm, H4Mm: p.H4Mm,
            // v24 修复: 螺纹规格填充 (与 Product.D7Thread/D8Thread 对齐)
            D7Thread: p.D7Thread,
            D8Thread: p.D8Thread,
            IsPublished: p.IsPublished,
            IsDiscontinued: p.IsDiscontinued,
            MachineCategories: machineCategories,
            MachineBrandsStr: machineBrandsStr,
            MachineModelsStr: machineModelsStr,
            EngineBrandsStr: engineBrandsStr,
            OemListPublishedBrands: publishedBrands,
            OemListPublishedNo3s: publishedNo3s,
            OemBrandsStr: oemBrandsStr,
            OemNo3sStr: oemNo3sStr,
            BrandSortOrderMin: brandSortOrderMin,
            OemListSortOrderMin: oemListSortOrderMin,
            ImagePrimaryKeys: imagePrimaryKeys,
            ImageDetailKeys: imageDetailKeys,
            UpdatedAtUnix: new DateTimeOffset(DateTime.SpecifyKind(p.UpdatedAt, DateTimeKind.Utc), TimeSpan.Zero).ToUnixTimeSeconds()
        );
    }

    // ===== XSS 防御 (S4-16/S4-17): 递归 SanitizeFormatted =====

    /// <summary>
    /// V2 (S4-17): 递归处理 _formatted JSON,防御 XSS
    /// 步骤:
    /// 1. 把 Meilisearch 高亮标签 (MarkOpen/MarkClose) 暂存为非字符 (MarkOpenStash/MarkCloseStash)
    /// 2. WebUtility.HtmlEncode 转义所有 HTML 实体 (用户输入的 &lt;mark&gt; 字面量也被转义)
    /// 3. 移除 C0 控制字符 (U+0000-U+001F,保留 \t \n \r) + BMP 私用区 (U+E000-U+F8FF) + 非字符 (U+FDD0-U+FDEF, U+FFFE/U+FFFF)
    /// 4. 还原非字符暂存为真实 &lt;mark&gt;/&lt;/mark&gt; 标签
    /// </summary>
    private static void SanitizeFormatted(JsonObject obj)
    {
        foreach (var prop in obj.ToList())
        {
            obj[prop.Key] = SanitizeToken(prop.Value);
        }
    }

    private static JsonNode? SanitizeToken(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var prop in obj.ToList())
            {
                var sanitized = SanitizeToken(prop.Value);
                // v30-27 P0 修复: 节点已有 parent 时重新赋值会抛 InvalidOperationException
                //   根因: SanitizeToken 递归返回原节点时, obj[key] = node 会尝试重新设置 parent
                //   修复: 只在值变化时赋值
                if (sanitized != prop.Value)
                {
                    obj[prop.Key] = sanitized;
                }
            }
            return obj;
        }
        if (node is JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                var sanitized = SanitizeToken(arr[i]);
                if (sanitized != arr[i])
                {
                    arr[i] = sanitized;
                }
            }
            return arr;
        }
        if (node is JsonValue val && val.TryGetValue<string>(out var s))
        {
            var sanitized = SanitizeString(s);
            // 只在字符串变化时创建新 JsonValue (避免不必要的新对象 + parent 问题)
            return sanitized != s ? JsonValue.Create(sanitized) : node;
        }
        return node;
    }

    /// <summary>
    /// 2026-08 性能优化: 单次扫描判断字符串是否需要慢路径处理
    /// 覆盖慢路径全部变更点 (与 SanitizeString 步骤 0-4 语义一致):
    ///   - 高亮标记 \uE000/\uE001 (步骤 1 暂存 → 步骤 4 还原为 &lt;mark&gt;)
    ///   - 暂存字符 \uFDD0/\uFDD1 (步骤 0 移除字面量)
    ///   - HTML 特殊字符 &lt;&gt;&amp;&quot;' (步骤 2 HtmlEncode)
    ///   - C0 控制字符 / BMP 私用区 / 非字符 / U+FFFE·U+FFFF (步骤 3 移除)
    /// </summary>
    private static bool NeedsSanitize(string s)
    {
        foreach (var c in s)
        {
            if (c == '\uE000' || c == '\uE001' || c == '\uFDD0' || c == '\uFDD1') return true;
            if (c == '<' || c == '>' || c == '&' || c == '"' || c == '\'') return true;
            if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') return true;
            if (c >= 0xE000 && c <= 0xF8FF) return true;
            if (c >= 0xFDD0 && c <= 0xFDEF) return true;
            if (c == 0xFFFE || c == 0xFFFF) return true;
        }
        return false;
    }

    private static string SanitizeString(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        // 2026-08 性能优化: 快速路径 — 单次扫描无任何需处理字符时直接返回
        //   WHY: 绝大多数字段是纯 OEM 号/型号/品牌 (字母数字), 慢路径(3×Replace + HtmlEncode + 逐字符 StringBuilder)
        //        在并发下是 backend CPU 大头 (直连实测 backend 比 Meili 慢 80-190ms P95)
        //   等价性: 慢路径对无特殊字符的字符串输出原串 (HtmlEncode 不改变纯字母数字, 扫描不移除, Replace 无匹配)
        //   注意: 覆盖慢路径所有变更点 — 高亮标记/暂存字符/HTML 特殊字符(含 ') 均触发慢路径
        if (!NeedsSanitize(s)) return s;

        // V24-F20 步骤 0: 过滤用户输入字面量中的 U+E000/U+E001 (spec S6-1, 修复 XSS 绕过)
        //   WHY: 用户在产品名/Remark 中输入字面量 \uE000, 会被步骤 1 误识别为 <mark> 起始标签暂存
        //        最终步骤 4 还原为 <mark> 标签, 导致 XSS 绕过
        //   修复: 在步骤 1 之前先移除字面量 \uE000/\uE001, 防止与暂存字符冲突
        s = s.Replace(MarkOpenStash, "").Replace(MarkCloseStash, "");

        // 步骤 1: 暂存 Meilisearch 高亮标签为非字符
        var stashed = s.Replace(MarkOpen, MarkOpenStash).Replace(MarkClose, MarkCloseStash);

        // 步骤 2: HtmlEncode 转义所有 HTML 实体
        var encoded = WebUtility.HtmlEncode(stashed);

        // 步骤 3: 移除 C0 控制字符 + BMP 私用区 + 非字符
        var sb = new System.Text.StringBuilder(encoded.Length);
        foreach (var c in encoded)
        {
            // 保留非字符暂存 (步骤 4 还原) + 制表符/换行/回车
            if (c == MarkOpenStash[0] || c == MarkCloseStash[0] ||
                c == '\t' || c == '\n' || c == '\r')
            {
                sb.Append(c);
                continue;
            }
            // 移除 C0 控制字符 (U+0000-U+001F)
            if (c < 0x20) continue;
            // 移除 BMP 私用区 (U+E000-U+F8FF) - 防止攻击者注入私用区字符绕过
            if (c >= 0xE000 && c <= 0xF8FF) continue;
            // 移除非字符 (U+FDD0-U+FDEF, U+FFFE, U+FFFF) - 但保留我们的暂存字符 \uFDD0/\uFDD1
            if (c >= 0xFDD0 && c <= 0xFDEF && c != MarkOpenStash[0] && c != MarkCloseStash[0]) continue;
            if (c == 0xFFFE || c == 0xFFFF) continue;
            sb.Append(c);
        }

        // 步骤 4: 还原非字符暂存为真实 <mark></mark> 标签
        return sb.ToString()
            .Replace(MarkOpenStash, "<mark>")
            .Replace(MarkCloseStash, "</mark>");
    }

    // V2 (S3-23): filter 注入防御改为移除 " 和 \ 策略
    private static string EscapeFilter(string s) => s.Replace("\"", "").Replace("\\", "");

    // V2 (S4-6): Brand filter 构建 (单值/多值/AND/OR)
    private static string BuildBrandFilter(List<string> oemBrands, string matchMode)
    {
        if (oemBrands.Count == 0) return "";
        var safeBrands = oemBrands.Select(b => EscapeFilter(b)).Where(b => !string.IsNullOrEmpty(b)).ToList();
        if (safeBrands.Count == 0) return "";

        if (safeBrands.Count == 1)
            return $"oem_list_published_brands IN [{safeBrands[0]}]";

        if (matchMode.Equals("AND", StringComparison.OrdinalIgnoreCase))
            // 多值 AND (同时包含所有 brand)
            return string.Join(" AND ", safeBrands.Select(b => $"oem_list_published_brands IN [{b}]"));
        else
            // 多值 OR (任一包含)
            return $"oem_list_published_brands IN [{string.Join(", ", safeBrands)}]";
    }

    // ===== V2 Task V17-2.2: Meilisearch schema 初始化 + 全量清空 =====

    /// <summary>
    /// V2 Task V17-2.2: 配置 Meilisearch 索引 schema (FilterableAttributes / SortableAttributes / SearchableAttributes)
    ///   WHY 必要: Meilisearch 启动时需显式配置 filterable/sortable 字段,否则 SearchAsync 的 Filter 参数会被忽略
    ///   字段命名: snake_case (与 Mr1IndexDoc 的 JSON 序列化默认一致, Meilisearch SDK 0.15.4 不做 PascalCase 转换)
    ///   幂等: 可重复执行,Meilisearch 内部覆盖旧配置
    ///   注意: 主键 mr_1 在首次 IndexAsync 时自动设置 (SDK 0.15.4 无独立 UpdatePrimaryKeyAsync 方法)
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // 首次为数万条存量文档创建 filterable 属性时，Meilisearch 需要重建倒排索引，
        // 实测可能超过 30 秒；初始化在后台执行，因此应等待任务完成而非留下半配置索引。
        const int schemaTaskTimeoutMs = 120_000;

        // V2 (S4-9): 遍历所有 WriteTargets 配置 schema (灰度期间两个索引都需配置)
        foreach (var target in _writeTargets)
        {
            try
            {
                // 新部署的 Meilisearch 尚未拥有索引时，直接更新 settings 会返回
                // index_not_found 并被下面的容错逻辑吞掉，后续首次写入虽会自动建索引，
                // 但筛选/排序字段不会被配置，公开搜索因此降级到 PostgreSQL。
                await EnsureIndexExistsAsync(target, ct);

                // FilterableAttributes: 支持范围/等值过滤的字段 (与 SearchAsync.BuildFilter 一致)
                var filterable = new[]
                {
                    "mr_1", "type", "is_published", "is_discontinued",
                    "d1_mm", "d2_mm", "d3_mm", "d4_mm",
                    "h1_mm", "h2_mm", "h3_mm", "h4_mm",
                    // v24 修复: 螺纹规格 (文本精确匹配)
                    "d7_thread", "d8_thread",
                    // ① P0 缩索引: 原嵌套 oem_list/machine_list 数组已移出索引体, 改标量过滤
                    //   oem 品牌/编号过滤走 oem_list_published_brands/no3s; 机型分类走 machine_categories
                    "machine_categories", "machine_brands_str",
                    // 扁平化冗余字段 (S3-7/S3-8)
                    "oem_list_published_brands", "oem_list_published_no3s",
                    "brand_sort_order_min", "oem_list_sort_order_min"
                };
                // SDK 0.15.4: WaitForTaskAsync(int taskUid, double timeoutMs, int intervalMs = 500)
                var filterTask = await target.UpdateFilterableAttributesAsync(filterable, ct);
                await target.WaitForTaskAsync(filterTask.TaskUid, schemaTaskTimeoutMs);

                // SortableAttributes: 支持排序的字段 (Brand 优先级 + 更新时间)
                var sortable = new[]
                {
                    "brand_sort_order_min",       // S3-8: Brand 优先级排序
                    "oem_list_sort_order_min",    // S4-16: OEM 3 排序
                    "updated_at_unix",            // 按更新时间排序
                    "d1_mm", "d2_mm", "d3_mm", "h1_mm", "h2_mm", "h3_mm"  // 尺寸排序
                };
                var sortTask = await target.UpdateSortableAttributesAsync(sortable, ct);
                await target.WaitForTaskAsync(sortTask.TaskUid, schemaTaskTimeoutMs);

                // SearchableAttributes: 全文检索字段 (顺序=相关性权重)
                //   WHY 显式配置: 默认所有字符串字段都参与搜索,但嵌套数组字段会干扰相关性
                var searchable = new[]
                {
                    "mr_1",                       // MR.1 主键搜索
                    "product_name_1", "product_name_2", "oem_2", "type", "remark", "media",
                    // 扁平化冗余字段 (S4-13: 空格分隔,可被分词器切分)
                    "oem_brands_str", "oem_no3s_str",
                    // ① P0 缩索引: 标量字符串保留原机型/发动机全文搜索能力。
                    "machine_brands_str", "machine_models_str", "engine_brands_str"
                };
                var searchTask = await target.UpdateSearchableAttributesAsync(searchable, ct);
                await target.WaitForTaskAsync(searchTask.TaskUid, schemaTaskTimeoutMs);

                // S6/S3-19: stopWords (移除 of/for/and/a, 防止型号 OF-100 误删 + "A Brand" 首词误删)
                //   WHY: spec L1533 + L1881 要求, "a" 会导致 "A Brand"/"A Filter" 品牌名首词被删
                var stopWords = new[] { "the", "an" };
                var stopTask = await target.UpdateStopWordsAsync(stopWords, ct);
                await target.WaitForTaskAsync(stopTask.TaskUid, schemaTaskTimeoutMs);

                // S4 修复: typoTolerance (OneTypo=3/TwoTypos=5, 3 字品牌缩写容错)
                //   WHY: spec L1526 + L1685 要求, 默认 5/9, 先改 4/8, 再改为 3/5 让 "BOS" 匹配 "BOSCH"
                //   V3(2026-08-25): 数字误匹配由 LoadOemExactBoostHitsAsync (OEM 精确子串补位) 兜底,
                //   不改 typo 配置 (Meili 1.12 无 disableOnNumbers, 且字母容错是产品特性)
                var typoTolerance = new TypoTolerance
                {
                    Enabled = true,
                    MinWordSizeForTypos = new TypoTolerance.TypoSize
                    {
                        OneTypo = 3,
                        TwoTypos = 5
                    }
                };
                var typoTask = await target.UpdateTypoToleranceAsync(typoTolerance, ct);
                await target.WaitForTaskAsync(typoTask.TaskUid, schemaTaskTimeoutMs);

                // S5/S3-20: separatorTokens (移除 "-", 防 OEM 号 F-000000001 被错误分割)
                //   WHY: spec L1527 + L1882 要求, "-" 是 OEM 号常见组成部分, 作为分隔符会破坏号码完整性
                var separatorTokens = new[] { " ", "/", ",", "." };
                var sepTask = await target.UpdateSeparatorTokensAsync(separatorTokens, ct);
                await target.WaitForTaskAsync(sepTask.TaskUid, schemaTaskTimeoutMs);

                _logger.LogInformation("Meili schema 已配置: target={Target}, filterable={FilterCount}, sortable={SortCount}, searchable={SearchCount}",
                    target.Uid, filterable.Length, sortable.Length, searchable.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Meili schema 配置失败 target={Target} (搜索功能可能降级)", target.Uid);
                // 不抛出: 单个 target 失败不影响其他 target,启动不应阻塞
            }
        }
    }

    private async Task EnsureIndexExistsAsync(Meilisearch.Index target, CancellationToken ct)
    {
        try
        {
            await _client.GetIndexAsync(target.Uid, ct);
        }
        catch (MeilisearchApiError ex) when (string.Equals(ex.Code, "index_not_found", StringComparison.OrdinalIgnoreCase))
        {
            var task = await _client.CreateIndexAsync(target.Uid, "mr_1", ct);
            await target.WaitForTaskAsync(task.TaskUid, 30000);
            _logger.LogInformation("Meili 索引已创建: target={Target}, taskUid={TaskUid}", target.Uid, task.TaskUid);
        }
    }

    /// <summary>
    /// V2 Task V17-2.2: 清空所有文档 (全量重建前调用)
    ///   WHY 必要: 全量重建需先清空旧文档,避免脏数据残留
    ///   注意: 仅删除文档,保留 schema 配置 (FilterableAttributes 等不变)
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task DeleteAllDocumentsAsync(CancellationToken ct = default)
    {
        foreach (var target in _writeTargets)
        {
            try
            {
                var task = await target.DeleteAllDocumentsAsync(ct);
                await target.WaitForTaskAsync(task.TaskUid, 60000);
                _logger.LogInformation("Meili 文档已全量清空: target={Target}, taskUid={TaskUid}", target.Uid, task.TaskUid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Meili 全量清空失败 target={Target} (继续后续重建,可能残留脏数据)", target.Uid);
                // 不抛出: 单个 target 失败不影响其他 target
            }
        }
    }
}
