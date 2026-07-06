using SakuraFilter.Api.DTOs;
using SakuraFilter.Api.Extensions;
using SakuraFilter.Etl;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// 公开 ETL 端点：/api/etl/import 触发导入、/api/etl/status 进度查询、
/// 旧入口 /import-xrefs /import-apps 保留（向后兼容）。
/// </summary>
public static class EtlEndpoints
{
    public static IEndpointRouteBuilder MapEtlEndpoints(this IEndpointRouteBuilder app)
    {
        // 统一入口
        app.MapPost("/api/etl/import", (ImportRequest req, EtlImportService etl, ILogger<Program> logger, IConfiguration config, CancellationToken ct) =>
        {
            if (config.ValidateJsonlPath(req.JsonlPath) is { } pathErr)
                return Results.BadRequest(new { error = pathErr });
            if (!File.Exists(req.JsonlPath))
                return Results.BadRequest(new { error = "文件不存在", path = req.JsonlPath });

            if (etl.Progress.Status == "running")
                return Results.Conflict(new { error = "已有导入任务在运行,请等待完成", progress = etl.Progress.ToJson() });

            var mode = (req.Mode ?? "upsert").ToLowerInvariant();
            var entityType = (req.EntityType ?? "products").ToLowerInvariant();
            var cascade = req.Cascade ?? true;

            if (entityType != "products" && entityType != "xrefs" && entityType != "apps")
                return Results.BadRequest(new { error = "EntityType 必须是 products/xrefs/apps", value = entityType });

            logger.LogInformation("触发 ETL 导入: {Entity} {Path} (mode={Mode}, cascade={Cascade})", entityType, req.JsonlPath, mode, cascade);

            var cascadeFlag = entityType == "products" ? cascade : true;
            _ = Task.Run(async () => await etl.TriggerAsync(entityType, req.JsonlPath, mode, 0, CancellationToken.None, cascadeFlag));
            return Results.Accepted(value: etl.Progress.ToJson());
        })
        .WithSummary("ETL 导入触发 (products/xrefs/apps, 统一入口, 路径白名单校验)").WithName("EtlImport")
        .WithOpenApi();

        // 进度查询
        app.MapGet("/api/etl/status", (EtlImportService etl) =>
            Results.Ok(etl.Progress.ToJson()))
        .WithSummary("ETL 导入进度查询 (实时 JSON, 含 current/total/elapsed/eta)").WithName("EtlStatus")
        .WithOpenApi();

        // 旧入口: xrefs
        app.MapPost("/api/etl/import-xrefs", (ImportRequest req, EtlImportService etl, ILogger<Program> logger, IConfiguration config, CancellationToken ct) =>
        {
            if (config.ValidateJsonlPath(req.JsonlPath) is { } pathErr)
                return Results.BadRequest(new { error = pathErr });
            if (!File.Exists(req.JsonlPath))
                return Results.BadRequest(new { error = "文件不存在", path = req.JsonlPath });
            if (etl.Progress.Status == "running")
                return Results.Conflict(new { error = "已有导入任务在运行,请等待完成", progress = etl.Progress.ToJson() });
            var mode = (req.Mode ?? "upsert").ToLowerInvariant();
            logger.LogInformation("触发 xrefs 导入: {Path} (mode={Mode})", req.JsonlPath, mode);
            _ = Task.Run(async () => await etl.TriggerAsync("xrefs", req.JsonlPath, mode, 0, CancellationToken.None));
            return Results.Accepted(value: etl.Progress.ToJson());
        })
        .WithSummary("ETL 导入 xrefs (兼容旧入口, 新调用走 /api/etl/import + entityType)").WithName("EtlImportXrefs")
        .WithOpenApi();

        // 旧入口: apps
        app.MapPost("/api/etl/import-apps", (ImportRequest req, EtlImportService etl, ILogger<Program> logger, IConfiguration config, CancellationToken ct) =>
        {
            if (config.ValidateJsonlPath(req.JsonlPath) is { } pathErr)
                return Results.BadRequest(new { error = pathErr });
            if (!File.Exists(req.JsonlPath))
                return Results.BadRequest(new { error = "文件不存在", path = req.JsonlPath });
            if (etl.Progress.Status == "running")
                return Results.Conflict(new { error = "已有导入任务在运行,请等待完成", progress = etl.Progress.ToJson() });
            var mode = (req.Mode ?? "upsert").ToLowerInvariant();
            logger.LogInformation("触发 apps 导入: {Path} (mode={Mode})", req.JsonlPath, mode);
            _ = Task.Run(async () => await etl.TriggerAsync("apps", req.JsonlPath, mode, 0, CancellationToken.None));
            return Results.Accepted(value: etl.Progress.ToJson());
        })
        .WithSummary("ETL 导入 apps (兼容旧入口, 新调用走 /api/etl/import + entityType)").WithName("EtlImportApps")
        .WithOpenApi();

        return app;
    }
}
