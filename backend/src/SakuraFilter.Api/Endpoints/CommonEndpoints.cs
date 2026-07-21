using Microsoft.EntityFrameworkCore;
using Prometheus;
using SakuraFilter.Api.DTOs;
using SakuraFilter.Api.Extensions;
using SakuraFilter.Api.Services;
using SakuraFilter.Infrastructure.Data;
using SakuraFilter.Search;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// 通用端点：根路径、健康检查、性能监控、指标、auth status。
/// </summary>
public static class CommonEndpoints
{
    public static IEndpointRouteBuilder MapCommonEndpoints(this IEndpointRouteBuilder app)
    {
        // V24-F25 (spec Task 0.7.5.1): 移除根路由 "/", 改为 "/api/info" (修复 F1)
        //   WHY: 根路由 "/" 返回 JSON 会与前端 index.html 冲突
        //        - 生产环境: nginx try_files 优先静态文件, 后端 "/" 不应被访问
        //        - 开发环境: 直接访问后端 "/" 返回 JSON, 误导开发者以为后端是前端入口
        //   spec 验证: curl -I http://localhost/ 返回 Content-Type: text/html (非 JSON, 由 nginx 提供 index.html)
        //   注: Task 0.7.5.2 (nginx try_files 配置) 不在代码修改范围, 需运维同步调整
        app.MapGet("/api/info", () => Results.Ok(new { name = "SakuraFilter API", version = "0.3.0", status = "running" }))
            .WithSummary("API 元信息 (名称/版本/状态)").WithName("ApiInfo");
        app.MapMetrics("/metrics")
            .WithSummary("Prometheus 兼容 /metrics 端点 (含 HTTP + 业务 + 进程指标)").WithName("Metrics")
            .WithOpenApi();

        app.MapPerfEndpoints();
        app.MapHealthEndpoints();
        app.MapAdminAuthStatusEndpoint();
        return app;
    }

    // -------------------- 性能监控 --------------------

    private static IEndpointRouteBuilder MapPerfEndpoints(this IEndpointRouteBuilder app)
    {
        // 性能埋点快照
        // v30-19 P0: 加 RequireAuthorization("Admin"), 原公开访问泄漏 P50/P95/P99 运维数据
        //   WHY: /api/perf 返回 PerfMetrics.GetSnapshot() 含 P50/P95/P99 + 样本数,
        //     与 v30-18 修复的 /api/admin/perf/alerts 同类敏感数据 (性能告警也基于这些指标)
        //   攻击场景: 攻击者读取性能快照推断系统负载, 选择最佳攻击时机 (DDoS/QPS 压测)
        //   前端兼容: AdminPerfView.vue L49 用 http.get('/perf') 带 Bearer (axios 拦截器自动注入), 不破坏
        app.MapGet("/api/perf", (PerfMetrics metrics) =>
            Results.Ok(metrics.GetSnapshot()))
            .WithSummary("性能埋点快照 (P50/P95/P99, 最近 1000 条样本)").WithName("PerfSnapshot")
            .RequireAuthorization("Admin")
            .WithOpenApi();

        // 性能告警列表
        // v30-18 P0: 加 RequireAuthorization("Admin"), 原脱离 group 无鉴权
        //   WHY: /api/admin/perf/alerts 在 DevTokenAuthMiddleware AdminPaths 内,
        //     但 Bearer 请求会跳过 DevToken 校验放行 (DevTokenAuthMiddleware.cs L122-127),
        //     端点无 RequireAuthorization 时 Bearer 攻击可绕过 (任意 Bearer token 即可访问)
        //   数据敏感性: 性能告警含 P95/P99 数据 + rule_name, 泄漏运维内部信息
        app.MapGet("/api/admin/perf/alerts", (PerfAlertService alerts, int? limit) =>
            Results.Ok(alerts.GetRecentAlerts(limit ?? 50)))
            .WithSummary("性能告警列表 (按时间倒序, 运维面板用)").WithName("PerfAlerts")
            .RequireAuthorization("Admin")
            .WithOpenApi();

        // 前端性能埋点批量上报
        app.MapPost("/api/perf/ingest", (
            FrontendPerfBatch body,
            ILogger<Program> logger,
            HttpContext ctx) =>
        {
            if (body?.Samples is null || body.Samples.Count == 0)
                return Results.BadRequest(new { error = "samples 不能为空" });
            if (body.Samples.Count > 100)
                return Results.BadRequest(new { error = "单次最多 100 条", given = body.Samples.Count });
            var ua = ctx.Request.Headers.UserAgent.ToString();
            foreach (var s in body.Samples)
            {
                logger.LogInformation("[PERF-FE] {Method} {Path} {Status} {Duration}ms ts={Ts} ua={UA}",
                    s.Method ?? "?", s.Path ?? "?", s.StatusCode, s.DurationMs, s.Ts, ua);
            }
            return Results.Ok(new { received = body.Samples.Count });
        })
        .WithSummary("接收前端性能埋点批量上报 (上限 100 条/批)").WithName("PerfIngest")
        .WithOpenApi();

        return app;
    }

    // -------------------- 健康检查 --------------------

    private static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }))
            .WithSummary("Liveness 探活 (进程是否存活, K8s/Docker 用)").WithName("HealthLive");

        app.MapGet("/health/ready", async (
            ProductDbContext db,
            ISearchProvider search,
            IHostedServiceStatus hostedStatus,
            CancellationToken ct) =>
        {
            var checks = new List<object>();

            var pgOk = false;
            try { pgOk = await db.Database.CanConnectAsync(ct); }
            catch { pgOk = false; }
            checks.Add(new { name = "postgres", healthy = pgOk });

            var resilient = search as ResilientSearchProvider;
            var meiliOk = false;
            var fallbackOk = false;
            if (resilient is not null)
            {
                try { meiliOk = await resilient.IsPrimaryHealthyAsync(ct); }
                catch { meiliOk = false; }
                try { fallbackOk = await resilient.IsFallbackHealthyAsync(ct); }
                catch { fallbackOk = false; }
            }
            else
            {
                try { meiliOk = await search.HealthCheckAsync(ct); }
                catch { meiliOk = false; }
                fallbackOk = meiliOk;
            }
            checks.Add(new { name = "meili", healthy = meiliOk });
            checks.Add(new { name = "fallback", healthy = fallbackOk });

            var staleServices = hostedStatus.GetStaleServices(TimeSpan.FromMinutes(5));
            var bgHealthy = staleServices.Count == 0;
            checks.Add(new { name = "backgroundServices", healthy = bgHealthy, stale = staleServices });

            var allOk = pgOk && (meiliOk || fallbackOk);
            var degraded = allOk && !meiliOk;
            var status = !allOk ? "unhealthy" : (degraded ? "degraded" : "healthy");
            var statusCode = allOk ? 200 : 503;

            return Results.Json(new { status, checks }, statusCode: statusCode);
        })
        .WithSummary("Readiness 探活 (PG/Meili/BackgroundService 整体健康度)").WithName("HealthReady");

        return app;
    }

    // -------------------- Auth Token 状态 --------------------

    private static IEndpointRouteBuilder MapAdminAuthStatusEndpoint(this IEndpointRouteBuilder app)
    {
        // v30-18 P0: 加 RequireAuthorization("Admin"), 原脱离 group 无鉴权
        //   WHY: /api/admin/auth/status 在 DevTokenAuthMiddleware AdminPaths 内,
        //     但 Bearer 请求会跳过 DevToken 校验放行 (DevTokenAuthMiddleware.cs L122-127),
        //     端点无 RequireAuthorization 时 Bearer 攻击可绕过
        //   数据敏感性: token 轮转信息 (current/previous prefix + 轮转时间), 虽不暴露完整 token,
        //     但 prefix 泄漏 + 轮转时间可辅助攻击者推测 token 模式
        app.MapGet("/api/admin/auth/status", (IAuthTokenStore store) =>
        {
            var current = store.Current;
            var previous = store.Previous;
            return Results.Ok(new
            {
                currentLen = current?.Length ?? 0,
                currentPrefix = current is { Length: >= 4 } ? current[..4] : null,
                previousLen = previous?.Length ?? 0,
                previousPrefix = previous is { Length: >= 4 } ? previous[..4] : null,
                lastRotatedAt = store.LastRotatedAt,
                lastRotatedBy = store.LastRotatedBy,
                loadedFromDb = store.LoadedFromDb,
                hasPrevious = !string.IsNullOrEmpty(previous)
            });
        })
        .WithSummary("Auth Token 轮转状态查询 (current/previous 长度 + 轮转时间, 不暴露完整 token)").WithName("AdminAuthStatus")
        .RequireAuthorization("Admin")
        .RequireRateLimiting("global");
        return app;
    }
}
