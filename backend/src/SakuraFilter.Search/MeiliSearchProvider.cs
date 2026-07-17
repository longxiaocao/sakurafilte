using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meilisearch;
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

    public string Name => "meilisearch";

    public MeiliSearchProvider(
        IOptions<MeiliSearchOptions> opts,
        ILogger<MeiliSearchProvider> logger,
        ProductDbContext db)
    {
        _opts = opts.Value;
        _logger = logger;
        _db = db;
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
            AttributesToHighlight = new[] { "*" },
            HighlightPreTag = MarkOpen,
            HighlightPostTag = MarkClose,
            ShowRankingScore = true,
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
        return new SearchResult(
            total, Math.Max(1, req.Page), pageSize,
            (int)Math.Ceiling(total / (double)pageSize),
            (int)sw.ElapsedMilliseconds,
            items
        );
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

        // V2: 默认 filter 排除下架 + 要求至少一个上架 OEM 3 (与 SearchAsync 一致)
        var filters = new List<string>
        {
            "is_published = true",
            "is_discontinued = false",
            "oem_list.is_published = true"  // 文档级: 至少一个 OEM 3 上架
        };

        if (!string.IsNullOrWhiteSpace(req.Type))
            filters.Add($"type = \"{EscapeFilter(req.Type)}\"");
        if (!string.IsNullOrWhiteSpace(req.MachineCategory))
            filters.Add($"machine_list.machine_category = \"{EscapeFilter(req.MachineCategory)}\"");

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
            AttributesToHighlight = new[] { "*" },
            HighlightPreTag = MarkOpen,
            HighlightPostTag = MarkClose,
            ShowRankingScore = true,
        };

        var query = req.Q?.Trim() ?? "";
        var rawResult = await _index.SearchAsync<JsonObject>(query, searchQuery, ct);
        var total = (rawResult as SearchResult<JsonObject>)?.EstimatedTotalHits ?? rawResult.Hits.Count;

        // 映射 hits → AggregateSearchHit (含完整 oem_list + machine_list + _formatted + _rankingScore)
        var hits = new List<AggregateSearchHit>(rawResult.Hits.Count);
        foreach (var hit in rawResult.Hits)
        {
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

            // 嵌套数组 oem_list
            var oemList = new List<AggregateOemItem>();
            if (hit.TryGetPropertyValue("oem_list", out var oemListNode) && oemListNode is JsonArray oemArr)
            {
                foreach (var item in oemArr)
                {
                    if (item is JsonObject oemObj)
                    {
                        oemList.Add(new AggregateOemItem(
                            OemBrand: oemObj.TryGetPropertyValue("oem_brand", out var b) ? b?.GetValue<string>() : null,
                            OemNo3: oemObj.TryGetPropertyValue("oem_no_3", out var n) ? n?.GetValue<string>() : null,
                            Oem2: oemObj.TryGetPropertyValue("oem_2", out var o2) ? o2?.GetValue<string>() : null,
                            SortOrder: oemObj.TryGetPropertyValue("sort_order", out var so) && so != null ? so.GetValue<int>() : 0,
                            MachineType: oemObj.TryGetPropertyValue("machine_type", out var mt) ? mt?.GetValue<string>() : null,
                            IsPublished: oemObj.TryGetPropertyValue("is_published", out var ip) && ip?.GetValue<bool>() == true,
                            BrandSortOrder: oemObj.TryGetPropertyValue("brand_sort_order", out var bso) && bso != null ? bso.GetValue<int?>() : null
                        ));
                    }
                }
            }

            // 嵌套数组 machine_list
            var machineList = new List<AggregateMachineItem>();
            if (hit.TryGetPropertyValue("machine_list", out var mlNode) && mlNode is JsonArray mlArr)
            {
                foreach (var item in mlArr)
                {
                    if (item is JsonObject mlObj)
                    {
                        machineList.Add(new AggregateMachineItem(
                            MachineBrand: mlObj.TryGetPropertyValue("machine_brand", out var mb) ? mb?.GetValue<string>() : null,
                            MachineModel: mlObj.TryGetPropertyValue("machine_model", out var mm) ? mm?.GetValue<string>() : null,
                            MachineCategory: mlObj.TryGetPropertyValue("machine_category", out var mc) ? mc?.GetValue<string>() : null
                        ));
                    }
                }
            }

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

            hits.Add(new AggregateSearchHit(
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
            ));
        }

        sw.Stop();
        return new AggregateSearchResponse(
            Total: total,
            Page: page,
            PageSize: pageSize,
            TotalPages: (int)Math.Ceiling(total / (double)pageSize),
            ProcessingTimeMs: (int)sw.ElapsedMilliseconds,
            Provider: "meilisearch",
            Hits: hits
        );
    }

    /// <summary>
    /// 从 hit 或 _formatted 中提取字段值 (优先 _formatted 高亮版本)
    /// </summary>
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

    public async Task IndexAsync(IEnumerable<Mr1IndexDoc> docs, CancellationToken ct = default)
    {
        var batch = docs.ToList();
        if (batch.Count == 0) return;

        // V2 (S4-21): 遍历所有 WriteTargets 双写 (灰度期间同时写 products + products_v2)
        foreach (var target in _writeTargets)
        {
            try
            {
                // V2: 主键改为 mr_1 (字符串)
                var task = await target.AddDocumentsAsync(batch, primaryKey: "mr_1", cancellationToken: ct);
                _logger.LogInformation("Meili 索引已提交: target={Target}, count={Count}, taskUid={TaskUid}",
                    target.Uid, batch.Count, task.TaskUid);
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

        // S4-11: oem_list 数组按 (brand_sort_order, sort_order, oem_brand, oem_no_3) 排序
        //   软删除 brand 排末尾 (BrandSortOrder 为 null 时用 int.MaxValue)
        var oemList = oemListRaw
            .OrderBy(x => x.BrandDeletedAt == null ? (x.BrandSortOrder ?? int.MaxValue) : int.MaxValue)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.OemBrand)
            .ThenBy(x => x.OemNo3)
            .Select(x => new OemListItem(
                x.OemBrand, x.OemNo3, x.Oem2, x.SortOrder,
                x.MachineType, x.IsPublished,
                x.BrandDeletedAt == null ? x.BrandSortOrder : null  // 软删除 brand 排序为 null
            ))
            .ToList();

        // 机型列表 (去重 + 排序)
        var machineList = await _db.MachineApplications
            .AsNoTracking()
            .Where(m => m.ProductId == p.Id)
            .Select(m => new { m.MachineBrand, m.MachineModel, m.MachineCategory })
            .Distinct()
            .OrderBy(m => m.MachineBrand)
            .ThenBy(m => m.MachineModel)
            .Take(50)
            .Select(m => new MachineListItem(m.MachineBrand, m.MachineModel, m.MachineCategory))
            .ToListAsync(ct);

        // ===== 扁平化冗余字段计算 =====
        // S3-7: 仅含上架 OEM 3 的 brand/oem_no_3 去重列表
        var publishedOemList = oemList.Where(x => x.IsPublished).ToList();
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
        var oemBrandsStr = string.Join(" ", oemList.Select(x => x.OemBrand).Where(b => !string.IsNullOrEmpty(b)).Distinct());
        var oemNo3sStr = string.Join(" ", oemList.Select(x => x.OemNo3).Where(n => !string.IsNullOrEmpty(n)).Distinct());

        // S3-8/S4-25: brand_sort_order_min 只取未软删除 brand 的 sort_order MIN,全软删除时为 null
        int? brandSortOrderMin = oemListRaw
            .Where(x => x.BrandDeletedAt == null && x.BrandSortOrder.HasValue)
            .Select(x => x.BrandSortOrder!.Value)
            .DefaultIfEmpty()
            .Cast<int?>()
            .Min();

        // S4-16: oem_list_sort_order_min 取上架 OEM 3 的 sort_order MIN
        int? oemListSortOrderMin = publishedOemList
            .Select(x => (int?)x.SortOrder)
            .DefaultIfEmpty()
            .Cast<int?>()
            .Min();

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
            OemList: oemList,
            MachineList: machineList,
            OemListPublishedBrands: publishedBrands,
            OemListPublishedNo3s: publishedNo3s,
            OemBrandsStr: oemBrandsStr,
            OemNo3sStr: oemNo3sStr,
            BrandSortOrderMin: brandSortOrderMin,
            OemListSortOrderMin: oemListSortOrderMin,
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
                obj[prop.Key] = SanitizeToken(prop.Value);
            }
            return obj;
        }
        if (node is JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                arr[i] = SanitizeToken(arr[i]);
            }
            return arr;
        }
        if (node is JsonValue val && val.TryGetValue<string>(out var s))
        {
            return JsonValue.Create(SanitizeString(s));
        }
        return node;
    }

    private static string SanitizeString(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

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
        // V2 (S4-9): 遍历所有 WriteTargets 配置 schema (灰度期间两个索引都需配置)
        foreach (var target in _writeTargets)
        {
            try
            {
                // FilterableAttributes: 支持范围/等值过滤的字段 (与 SearchAsync.BuildFilter 一致)
                var filterable = new[]
                {
                    "mr_1", "type", "is_published", "is_discontinued",
                    "d1_mm", "d2_mm", "d3_mm", "d4_mm",
                    "h1_mm", "h2_mm", "h3_mm", "h4_mm",
                    // v24 修复: 螺纹规格 (文本精确匹配)
                    "d7_thread", "d8_thread",
                    // 嵌套数组字段 (V2 新增)
                    "oem_list.oem_brand", "oem_list.oem_no_3", "oem_list.is_published", "oem_list.machine_type",
                    "machine_list.machine_brand", "machine_list.machine_model", "machine_list.machine_category",
                    // 扁平化冗余字段 (S3-7/S3-8)
                    "oem_list_published_brands", "oem_list_published_no3s",
                    "brand_sort_order_min", "oem_list_sort_order_min"
                };
                // SDK 0.15.4: WaitForTaskAsync(int taskUid, double timeoutMs, int intervalMs = 500)
                var filterTask = await target.UpdateFilterableAttributesAsync(filterable, ct);
                await target.WaitForTaskAsync(filterTask.TaskUid, 30000);

                // SortableAttributes: 支持排序的字段 (Brand 优先级 + 更新时间)
                var sortable = new[]
                {
                    "brand_sort_order_min",       // S3-8: Brand 优先级排序
                    "oem_list_sort_order_min",    // S4-16: OEM 3 排序
                    "updated_at_unix",            // 按更新时间排序
                    "d1_mm", "d2_mm", "d3_mm", "h1_mm", "h2_mm", "h3_mm"  // 尺寸排序
                };
                var sortTask = await target.UpdateSortableAttributesAsync(sortable, ct);
                await target.WaitForTaskAsync(sortTask.TaskUid, 30000);

                // SearchableAttributes: 全文检索字段 (顺序=相关性权重)
                //   WHY 显式配置: 默认所有字符串字段都参与搜索,但嵌套数组字段会干扰相关性
                var searchable = new[]
                {
                    "mr_1",                       // MR.1 主键搜索
                    "product_name_1", "product_name_2", "oem_2", "type", "remark", "media",
                    // 扁平化冗余字段 (S4-13: 空格分隔,可被分词器切分)
                    "oem_brands_str", "oem_no3s_str",
                    // 嵌套数组字段 (支持 OEM 3 / 机型搜索)
                    "oem_list.oem_brand", "oem_list.oem_no_3",
                    "machine_list.machine_brand", "machine_list.machine_model"
                };
                var searchTask = await target.UpdateSearchableAttributesAsync(searchable, ct);
                await target.WaitForTaskAsync(searchTask.TaskUid, 30000);

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
