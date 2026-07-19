using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SakuraFilter.Api.Extensions;
using SakuraFilter.Api.Services;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// V2 Task 4.3: sitemap.xml 端点
///
/// 设计目标:
///   - GET /sitemap.xml: 返回 sitemap index (列出所有分片)
///   - GET /sitemaps/products-{shard}.xml: 返回分片 urlset (每分片 ≤ shardSize URL, 默认 50000)
///   - 分片策略: 按 mr_1 排序 OFFSET/LIMIT, 保证分片稳定 (不依赖 hash 避免空洞)
///   - 过滤: cross_references.is_published=true AND is_discontinued=false, 关联 products (前台展示的产品)
///   - 缓存: IMemoryCache, key=sitemap:index / sitemap:shard:{shard}, TTL 由 seo.sitemap_cache_ttl_seconds 配置 (默认 3600s)
///
/// 性能考虑:
///   - 1M 产品 × 平均 5 OEM 3 = 5M URL, 单分片 50000 → 100 分片
///   - 分片查询走 keyset pagination (mr_1 排序 + OFFSET), PG 索引 idx_products_mr_1_unique 命中
///   - 索引页查询只算 COUNT, 不拉数据, ~50ms (PG 缓存命中)
///   - 分片页查询 OFFSET 较大时性能下降, 生产可改 keyset (WHERE mr_1 > last_mr_1)
///
/// 失效策略:
///   - OEM 3 上架/下架/排序变更时由 AdminProductService 主动调 InvalidateCache() 清除相关缓存
///   - 当前简化版: 仅按 TTL 自然过期 (1 小时), 后续可加主动失效
/// </summary>
public static class SitemapEndpoints
{
    private const string SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";
    private const int DefaultShardSize = 50000;
    private const int DefaultCacheTtlSeconds = 3600;
    private const int MaxShardSize = 50000;  // sitemaps.org 协议规定单文件 ≤ 50000 URL
    private const int MaxShardCount = 1000;   // 防御性上限 (1M 产品 / 50000 = 20 分片)

    public static IEndpointRouteBuilder MapSitemapEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /sitemap.xml — 索引页
        app.MapGet("/sitemap.xml", MapSitemapIndexAsync)
            .WithSummary("sitemap 索引 (列出所有分片, 供搜索引擎发现)")
            .WithName("SitemapIndex")
            .WithOpenApi()
            .RequireRateLimiting("public");

        // GET /sitemaps/products-{shard}.xml — 单分片 urlset
        app.MapGet("/sitemaps/products-{shard:int}.xml", MapSitemapShardAsync)
            .WithSummary("sitemap 分片 urlset (每分片 ≤ 50000 URL)")
            .WithName("SitemapShard")
            .WithOpenApi()
            .RequireRateLimiting("public");

        return app;
    }

    // ============================================================================
    // GET /sitemap.xml — 索引页
    // ============================================================================

    private static async Task<IResult> MapSitemapIndexAsync(
        ProductDbContext db,
        IProductDetailService detailService,
        IMemoryCache cache,
        IConfiguration config,
        HttpContext ctx,
        CancellationToken ct)
    {
        var cacheKey = "sitemap:index";
        var ttl = GetCacheTtl(config);

        // V2 Task 4.3: 索引页缓存 (TTL 1 小时, Size=8KB 估算)
        if (cache.TryGetValue(cacheKey, out (string Xml, DateTime Generated) cached)
            && cached.Generated > DateTime.UtcNow.AddSeconds(-ttl))
        {
            return Results.Text(cached.Xml, "application/xml", Encoding.UTF8);
        }

        // 查询分片数: COUNT(DISTINCT mr_1) / shard_size
        //   WHY DISTINCT mr_1: 同 mr_1 多 OEM 3 在同一详情页, sitemap 只列一次
        var shardSize = GetShardSize(config);
        var totalProducts = await db.Products.AsNoTracking()
            .Where(p => !p.IsDiscontinued && p.IsPublished && p.Mr1 != null)
            .CountAsync(ct);
        var shardCount = Math.Min(MaxShardCount, (int)Math.Ceiling((double)totalProducts / shardSize));

        // 生成索引 XML
        var baseUrl = GetBaseUrl(ctx);
        var sb = new StringBuilder(8 * 1024);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append($"<sitemapindex xmlns=\"{SitemapNamespace}\">");
        for (var i = 1; i <= shardCount; i++)
        {
            sb.Append("<sitemap>");
            sb.Append($"<loc>{baseUrl}/sitemaps/products-{i}.xml</loc>");
            sb.Append($"<lastmod>{DateTime.UtcNow:yyyy-MM-dd}</lastmod>");
            sb.Append("</sitemap>");
        }
        sb.Append("</sitemapindex>");

        var xml = sb.ToString();
        // Size 估算: 8KB / 100 = 80 bytes per entry; 上限 1000 分片 ≈ 80KB, 取 100 安全
        cache.Set(cacheKey, (xml, DateTime.UtcNow), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(ttl),
            Size = 100
        });

        return Results.Text(xml, "application/xml", Encoding.UTF8);
    }

    // ============================================================================
    // GET /sitemaps/products-{shard}.xml — 单分片 urlset
    // ============================================================================

    private static async Task<IResult> MapSitemapShardAsync(
        int shard,
        ProductDbContext db,
        IProductDetailService detailService,
        IMemoryCache cache,
        IConfiguration config,
        HttpContext ctx,
        CancellationToken ct)
    {
        if (shard < 1)
            return Results.BadRequest(new { error = "shard 必须 >= 1" });

        var cacheKey = $"sitemap:shard:{shard}";
        var ttl = GetCacheTtl(config);

        if (cache.TryGetValue(cacheKey, out (string Xml, DateTime Generated) cached)
            && cached.Generated > DateTime.UtcNow.AddSeconds(-ttl))
        {
            return Results.Text(cached.Xml, "application/xml", Encoding.UTF8);
        }

        var shardSize = GetShardSize(config);
        var offset = (shard - 1) * shardSize;

        // V2 Task 4.3: 查询当前分片的产品 (mr_1 排序保证分片稳定)
        //   WHY DISTINCT ON mr_1: 同 mr_1 多 OEM 3 只列一次 (URL 含 pn1/pn2/brand/oem3, 取第一个 OEM 3 即可)
        //   WHY 取第一个 xref: BuildProductUrl 需要 pn1/pn2/brand/oem3, 取 SortOrder 最小的 xref (主图口径一致)
        var rows = await (
            from p in db.Products.AsNoTracking()
            where !p.IsDiscontinued && p.IsPublished && p.Mr1 != null
            orderby p.Mr1
            select new
            {
                p.Mr1,
                p.ProductName1,
                p.ProductName2,
                p.OemNoDisplay,
                p.UpdatedAt,
                FirstXref = (
                    from x in db.CrossReferences.AsNoTracking()
                    where x.ProductId == p.Id
                          && !x.IsDiscontinued
                          && x.IsPublished
                    orderby (db.XrefOemBrands
                                .Where(b => b.Brand == x.OemBrand && b.DeletedAt == null)
                                .Select(b => (int?)b.SortOrder)
                                .FirstOrDefault() ?? int.MaxValue),
                            x.SortOrder,
                            x.OemNo3
                    select new { x.OemBrand, x.OemNo3, x.ProductName1 }
                ).FirstOrDefault()
            }
        ).Skip(offset).Take(shardSize).ToListAsync(ct);

        // 生成 urlset XML
        var baseUrl = GetBaseUrl(ctx);
        var sb = new StringBuilder(shardSize * 200);  // 每条 URL 约 200 字节估算
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append($"<urlset xmlns=\"{SitemapNamespace}\">");

        foreach (var r in rows)
        {
            // V2 Task 4.3: 用 IProductDetailService.BuildSlug 拼新 SEO URL
            //   WHY 复用 BuildSlug: 保证与详情页 canonical URL 一致 (避免 SEO 重复内容)
            var pn1Slug = detailService.BuildSlug(r.FirstXref?.ProductName1 ?? r.ProductName1);
            var pn2Slug = detailService.BuildSlug(r.ProductName2);
            var brandSlug = detailService.BuildSlug(r.FirstXref?.OemBrand);
            var oem3Slug = detailService.BuildSlug(r.FirstXref?.OemNo3 ?? r.OemNoDisplay);

            // mr1 末 6 位防 slug 冲突 (与 BuildProductUrl 一致)
            var mr1Suffix = (r.Mr1?.Length ?? 0) > 6 ? r.Mr1![^6..] : (r.Mr1 ?? "nomr1");
            var loc = $"{baseUrl}/products/{pn1Slug}-{mr1Suffix}/{pn2Slug}/{brandSlug}/{oem3Slug}".ToLowerInvariant();

            sb.Append("<url>");
            sb.Append($"<loc>{System.Security.SecurityElement.Escape(loc)}</loc>");
            sb.Append($"<lastmod>{r.UpdatedAt:yyyy-MM-dd}</lastmod>");
            sb.Append("<changefreq>weekly</changefreq>");
            sb.Append("<priority>0.8</priority>");
            sb.Append("</url>");
        }
        sb.Append("</urlset>");

        var xml = sb.ToString();
        // Size 估算: 50000 URL × 200 bytes = 10MB; 但 IMemoryCache SizeLimit=10000, 单条 10MB 会撑爆
        //   折中: 单分片缓存 Size=2000 (占容量 20%), 实际缓存命中显著降低 PG 查询压力
        // V24-F85: 用 SetWithSize 替代手写 MemoryCacheEntryOptions (size=2000 显式传参)
        cache.SetWithSize(cacheKey, (xml, DateTime.UtcNow), TimeSpan.FromSeconds(ttl), size: 2000);

        return Results.Text(xml, "application/xml", Encoding.UTF8);
    }

    // ============================================================================
    // 辅助方法
    // ============================================================================

    private static int GetShardSize(IConfiguration config)
    {
        var size = config.GetValue<int?>("Seo:SitemapShardSize") ?? DefaultShardSize;
        return Math.Min(Math.Max(size, 1000), MaxShardSize);  // 范围 [1000, 50000]
    }

    private static int GetCacheTtl(IConfiguration config)
    {
        var ttl = config.GetValue<int?>("Seo:SitemapCacheTtlSeconds") ?? DefaultCacheTtlSeconds;
        return Math.Min(Math.Max(ttl, 60), 86400);  // 范围 [60s, 86400s]
    }

    private static string GetBaseUrl(HttpContext ctx)
    {
        // V2 Task 4.3: 优先用 ForwardedHeaders 中的 X-Forwarded-Proto/Host (nginx 反代场景)
        var scheme = ctx.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? ctx.Request.Scheme;
        var host = ctx.Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? ctx.Request.Host.Value;
        return $"{scheme}://{host}".TrimEnd('/');
    }

    // V2 Task 4.3: 主动失效缓存 (供 AdminProductService 在 OEM 3 变更时调用)
    //   WHY 单独暴露: 端点是 static, AdminProductService 注入 IMemoryCache 后可直接调
    public static void InvalidateCache(IMemoryCache cache, int? shard = null)
    {
        if (shard.HasValue)
        {
            cache.Remove($"sitemap:shard:{shard.Value}");
        }
        else
        {
            // 全量失效 (供"清空缓存"按钮用)
            cache.Remove("sitemap:index");
            // shards 无法精确枚举 (依赖运行时状态), 简化为清除索引缓存 + 等 TTL 自然过期
        }
    }
}
