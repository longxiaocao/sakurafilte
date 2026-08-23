// SiteContent: 站点内容维护 (about / contact / news / site_name / logo_url)
// 实现: 复用 system_settings key-value 表 (无需新表/迁移)
//   - site.name / site.logo_url  : 站点名与 logo (前台 AppHeader 显示)
//   - site.about / site.contact  : 关于我们 / 联系我们 文本 (前台 PublicInfoView)
//   - site.news                  : JSON 数组 [{id,title,body,publishedAt}] (前台 News 页, 小型发布功能)
// 安全: Admin 端点 (Admin 策略) 读写; 公开端点只读聚合
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Endpoints;

public static class SiteContentEndpoints
{
    private const string Prefix = "api/admin/site-content";
    private static readonly string[] Keys =
        ["site.name", "site.logo_url", "site.about", "site.contact", "site.news"];

    public static void MapSiteContentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Prefix)
            .RequireAuthorization("Admin")
            .WithTags("site-content");

        // GET 全部站点内容 (维护页加载)
        group.MapGet("", async (ProductDbContext db, CancellationToken ct) =>
        {
            var rows = await db.SystemSettings
                .AsNoTracking()
                .Where(s => Keys.Contains(s.Key))
                .ToListAsync(ct);
            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in Keys) data[k] = rows.FirstOrDefault(r => r.Key == k)?.Value;
            return Results.Ok(data);
        }).WithSummary("读取站点内容 (about/contact/news/site_name/logo)").WithName("SiteContentGet");

        // PUT 更新全部站点内容 (维护页保存; 缺失的 key 用空串写入)
        group.MapPut("", async (Dictionary<string, string?> req, ProductDbContext db, CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var rows = await db.SystemSettings.Where(s => Keys.Contains(s.Key)).ToListAsync(ct);
            foreach (var k in Keys)
            {
                var val = req.TryGetValue(k, out var v) ? v ?? "" : "";
                var row = rows.FirstOrDefault(r => r.Key == k);
                if (row == null)
                {
                    db.SystemSettings.Add(new SystemSetting { Key = k, Value = val, Description = $"site content: {k}", UpdatedAt = now });
                }
                else if (row.Value != val)
                {
                    row.Value = val;
                    row.UpdatedAt = now;
                }
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).WithSummary("更新站点内容").WithName("SiteContentPut");

        // 公开只读聚合 (前台 About/News/Contact 页 + AppHeader 站点名/logo)
        app.MapGet("api/public/site-content", async (ProductDbContext db, CancellationToken ct) =>
        {
            var rows = await db.SystemSettings
                .AsNoTracking()
                .Where(s => Keys.Contains(s.Key))
                .ToListAsync(ct);
            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in Keys) data[k] = rows.FirstOrDefault(r => r.Key == k)?.Value;
            return Results.Ok(data);
        }).WithSummary("公开读取站点内容").WithName("SiteContentPublicGet").WithTags("site-content");
    }
}
