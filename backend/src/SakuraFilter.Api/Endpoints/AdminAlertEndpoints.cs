using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Api.Extensions;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// 后台告警查询端点 (P2-1)
/// - GET /api/admin/alerts/history         告警历史 (按 type/severity/status 过滤)
/// - GET /api/admin/alerts/history/{id}    单条告警详情
/// - GET /api/admin/alerts/stats           7 日 KPI 统计
/// - GET /api/admin/alerts/rules           告警规则列表
/// - PUT /api/admin/alerts/rules/{id}      更新告警规则
/// - POST /api/admin/alerts/test           测试告警推送 (运维调试用)
/// 鉴权: admin 角色 (与 EtlEndpoints 一致)
/// </summary>
public static class AdminAlertEndpoints
{
    public static IEndpointRouteBuilder MapAdminAlertEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/alerts")
            .WithTags("AdminAlerts")
            .RequireAuthorization("Admin")
            .RequireRateLimiting("global");

        // 告警历史 (分页 + 过滤)
        group.MapGet("/history", async (
            [FromQuery] string? type,
            [FromQuery] string? severity,
            [FromQuery] string? status,
            [FromQuery] int limit,
            [FromQuery] int offset,
            ProductDbContext db,
            CancellationToken ct) =>
        {
            limit = Math.Clamp(limit > 0 ? limit : 50, 1, 200);
            offset = Math.Max(0, offset);

            var q = db.AlertHistories.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(type)) q = q.Where(a => a.Type == type);
            if (!string.IsNullOrWhiteSpace(severity)) q = q.Where(a => a.Severity == severity);
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(a => a.Status == status);

            var total = await q.CountAsync(ct);
            var items = await q
                .OrderByDescending(a => a.SentAt)
                .Skip(offset)
                .Take(limit)
                .Select(a => new
                {
                    a.Id,
                    a.Type,
                    a.Severity,
                    a.Title,
                    a.Channel,
                    a.Status,
                    a.SentAt,
                    a.CorrelationId,
                    a.Error
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                total,
                limit,
                offset,
                items
            });
        });

        // 单条详情
        group.MapGet("/history/{id:long}", async (long id, ProductDbContext db, CancellationToken ct) =>
        {
            var a = await db.AlertHistories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a == null) return Results.NotFound();
            return Results.Ok(new
            {
                a.Id,
                a.Type,
                a.Severity,
                a.Title,
                a.Channel,
                a.Status,
                a.SentAt,
                a.CorrelationId,
                content = a.ContentJson.RootElement.GetRawText(),
                recipients = a.Recipients?.RootElement.GetRawText(),
                response = a.Response,
                error = a.Error
            });
        });

        // 7 日 KPI 统计
        group.MapGet("/stats", async (ProductDbContext db, CancellationToken ct) =>
        {
            var since = DateTimeOffset.UtcNow.AddDays(-7);
            var stats = await db.AlertHistories.AsNoTracking()
                .Where(a => a.SentAt >= since)
                .GroupBy(a => 1)
                .Select(g => new
                {
                    total = g.Count(),
                    sent = g.Count(a => a.Status == "sent"),
                    failed = g.Count(a => a.Status == "failed"),
                    suppressed = g.Count(a => a.Status == "suppressed"),
                    p0 = g.Count(a => a.Severity == "P0"),
                    p1 = g.Count(a => a.Severity == "P1"),
                    p2 = g.Count(a => a.Severity == "P2"),
                    warn = g.Count(a => a.Severity == "WARN"),
                    info = g.Count(a => a.Severity == "INFO")
                })
                .FirstOrDefaultAsync(ct);
            return Results.Ok(stats ?? new
            {
                total = 0,
                sent = 0,
                failed = 0,
                suppressed = 0,
                p0 = 0,
                p1 = 0,
                p2 = 0,
                warn = 0,
                info = 0
            });
        });

        // 规则列表
        group.MapGet("/rules", async (ProductDbContext db, CancellationToken ct) =>
        {
            var rules = await db.AlertRules.AsNoTracking()
                .OrderBy(r => r.Id)
                .ToListAsync(ct);
            return Results.Ok(rules.Select(r => new
            {
                r.Id,
                r.Type,
                r.Enabled,
                r.Severity,
                channels = r.Channels.RootElement.GetRawText(),
                conditions = r.Conditions?.RootElement.GetRawText(),
                recipients = r.Recipients?.RootElement.GetRawText(),
                r.Description,
                r.CreatedAt,
                r.UpdatedAt
            }));
        });

        // 规则更新
        group.MapPut("/rules/{id:long}", async (
            long id,
            [FromBody] AlertRuleUpdateRequest req,
            ProductDbContext db,
            CancellationToken ct) =>
        {
            var r = await db.AlertRules.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r == null) return Results.NotFound();
            if (req.Enabled.HasValue) r.Enabled = req.Enabled.Value;
            if (!string.IsNullOrEmpty(req.Severity)) r.Severity = req.Severity;
            if (req.Channels != null) r.Channels = JsonDocument.Parse(JsonSerializer.Serialize(req.Channels));
            if (req.Recipients != null) r.Recipients = JsonDocument.Parse(JsonSerializer.Serialize(req.Recipients));
            if (!string.IsNullOrEmpty(req.Description)) r.Description = req.Description;
            r.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { success = true });
        });

        // 测试告警 (运维调试 webhook 配置)
        group.MapPost("/test", async (
            [FromBody] AlertTestRequest req,
            Services.Alerts.AlertCenter center,
            CancellationToken ct) =>
        {
            var r = await center.EmitAsync(
                type: req.Type ?? "test.manual",
                severity: req.Severity ?? "INFO",
                title: req.Title ?? "[Test] 告警测试",
                markdown: req.Markdown ?? "**告警测试消息**\n\n这是一条来自 `/api/admin/alerts/test` 的测试告警。",
                context: new Dictionary<string, object?>
                {
                    ["test"] = true,
                    ["triggeredBy"] = "manual"
                },
                suppressKey: null,  // 测试不抑制
                ct: ct);
            return Results.Ok(new
            {
                success = r.Success,
                disabled = r.Disabled,
                noChannel = r.NoChannel,
                suppressed = r.Suppressed,
                correlationId = r.CorrelationId,
                sentCount = r.SentCount,
                failedCount = r.FailedCount,
                results = r.Results
            });
        });

        return app;
    }
}

public class AlertRuleUpdateRequest
{
    public bool? Enabled { get; set; }
    public string? Severity { get; set; }
    public List<string>? Channels { get; set; }
    public List<string>? Recipients { get; set; }
    public string? Description { get; set; }
}

public class AlertTestRequest
{
    public string? Type { get; set; }
    public string? Severity { get; set; }
    public string? Title { get; set; }
    public string? Markdown { get; set; }
}
