using Meilisearch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SakuraFilter.Core.DTOs;

namespace SakuraFilter.Search;

/// <summary>
/// MeiliSearch 搜索配置
/// </summary>
public class MeiliSearchOptions
{
    public string Endpoint { get; set; } = "http://localhost:7700";
    public string? ApiKey { get; set; }
    public string IndexName { get; set; } = "products";
    public int TimeoutMs { get; set; } = 1000;
}

/// <summary>
/// MeiliSearch 搜索提供者 (主,支持 typo 容错 + facet)
/// - typo 容错: 原生支持 (CATT -> CAT)
/// - facet 过滤: type, media
/// - 范围查询: d1_mm 等
/// - 失败时由 ResilientSearchProvider 切到 PG 兜底
/// </summary>
public class MeiliSearchProvider : ISearchProvider
{
    private readonly MeilisearchClient _client;
    private readonly MeiliSearchOptions _opts;
    private readonly ILogger<MeiliSearchProvider> _logger;
    private readonly Meilisearch.Index _index;

    public string Name => "meilisearch";

    public MeiliSearchProvider(IOptions<MeiliSearchOptions> opts, ILogger<MeiliSearchProvider> logger)
    {
        _opts = opts.Value;
        _logger = logger;
        _client = new MeilisearchClient(_opts.Endpoint, _opts.ApiKey);
        _index = _client.Index(_opts.IndexName);
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            // Meili /health 端点
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

        // 1) 构建 Meili 搜索参数
        var searchQuery = new SearchQuery
        {
            Limit = Math.Clamp(req.PageSize, 1, 100),
            Offset = (Math.Max(1, req.Page) - 1) * Math.Clamp(req.PageSize, 1, 100),
        };

        // 2) 过滤器: type + 范围
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(req.Type))
        {
            filters.Add($"type = \"{EscapeFilter(req.Type)}\"");
        }
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
        if (req.H1.HasValue)
        {
            var (lo, hi) = (req.H1.Value - req.Tolerance, req.H1.Value + req.Tolerance);
            filters.Add($"h1_mm >= {lo} AND h1_mm <= {hi}");
        }
        if (!req.IncludeDiscontinued)
        {
            filters.Add("is_discontinued = false");
        }
        if (filters.Count > 0)
        {
            searchQuery.Filter = string.Join(" AND ", filters);
        }

        // 3) 调用 Meili
        var query = req.Q?.Trim() ?? "";
        var result = await _index.SearchAsync<ProductIndexDoc>(query, searchQuery, ct);
        // ISearchable<T> 接口只暴露 Hits,需要转 SearchResult<T> 拿 EstimatedTotalHits
        var total = (result as SearchResult<ProductIndexDoc>)?.EstimatedTotalHits ?? result.Hits.Count;

        // 4) 映射结果
        var items = result.Hits.Select(h => new SearchResultItem(
            h.Id, h.OemNoDisplay, h.Remark, h.Type,
            h.D1Mm, h.D2Mm, h.H1Mm, null, h.IsDiscontinued  // Meili 不存 image_key,前端用另一接口
        )).ToList();

        sw.Stop();
        var pageSize = Math.Clamp(req.PageSize, 1, 100);
        return new SearchResult(
            total, Math.Max(1, req.Page), pageSize,
            (int)Math.Ceiling(total / (double)pageSize),
            (int)sw.ElapsedMilliseconds,
            items
        );
    }

    public async Task IndexAsync(IEnumerable<ProductIndexDoc> docs, CancellationToken ct = default)
    {
        var batch = docs.ToList();
        if (batch.Count == 0) return;

        var task = await _index.AddDocumentsAsync(batch, primaryKey: "id", cancellationToken: ct);
        _logger.LogInformation("Meili 索引已提交: {Count} 条, taskUid={TaskUid}", batch.Count, task.TaskUid);
    }

    public async Task DeleteAsync(IEnumerable<long> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return;
        // Meili 0.15.4 重载只支持 IEnumerable<string> 或 IEnumerable<int>
        var task = await _index.DeleteDocumentsAsync(idList.Select(i => i.ToString()), cancellationToken: ct);
        _logger.LogInformation("Meili 删除已提交: {Count} 条, taskUid={TaskUid}", idList.Count, task.TaskUid);
    }

    private static string EscapeFilter(string s) => s.Replace("\"", "\\\"");
}
