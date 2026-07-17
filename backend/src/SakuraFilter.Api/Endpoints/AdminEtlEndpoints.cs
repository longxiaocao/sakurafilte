using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Api.DTOs;
using SakuraFilter.Api.Extensions;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Core.Entities;
using SakuraFilter.Etl;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// 后台 ETL 管理端点：admin 角色鉴权 + etl 限流。
/// 包含触发、取消、暂停、恢复、进度 SSE、历史、聚合。
/// </summary>
public static class AdminEtlEndpoints
{
    public static IEndpointRouteBuilder MapAdminEtlEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/etl").WithTags("AdminEtl").RequireRateLimiting("etl");

        // 手动触发（含 dry-run）
        group.MapPost("/trigger", async (
            [FromBody] EtlTriggerRequest req,
            EtlImportService etl,
            ILogger<Program> logger,
            IConfiguration config,
            CancellationToken ct) =>
        {
            logger.LogInformation("手动 ETL 触发 entity={Entity} mode={Mode} file={File} dryRun={Dry}",
                req.EntityType ?? "products", req.Mode, req.JsonlPath, req.DryRun);

            if (config.ValidateJsonlPath(req.JsonlPath, requireJsonlExtension: req.DryRun) is { } pathErr)
                return Results.BadRequest(new { error = pathErr });

            if (req.DryRun)
            {
                if (!File.Exists(req.JsonlPath))
                    return Results.Problem(detail: $"文件不存在: {req.JsonlPath}", statusCode: 404, title: "File Not Found");

                var lines = 0;
                var samples = new List<string>();
                var sampleSchemas = new List<LineSchemaReport>();
                var missingFieldTotal = new Dictionary<string, int>();
                var typeMismatchTotal = new Dictionary<string, int>();
                const int SampleSizeForSchema = 50;
                const int SampleSizeForMissing = 1000;
                var requiredFields = (req.EntityType?.ToLowerInvariant() ?? "products") switch
                {
                    "products" or "product" => new[] { "oem_no_normalized", "oem_no_display" },
                    "xrefs" or "xref" or "cross_references" => new[] { "oem_no_normalized", "oem_brand", "oem_no_3" },
                    "apps" or "machine_applications" => new[] { "oem_no_normalized", "machine_brand", "machine_model" },
                    _ => new[] { "oem_no_normalized" }
                };
                using (var fs = File.OpenRead(req.JsonlPath))
                using (var sr = new StreamReader(fs))
                {
                    string? line;
                    while ((line = await sr.ReadLineAsync(ct)) != null)
                    {
                        lines++;
                        if (samples.Count < SampleSizeForSchema) samples.Add(line);
                        if (lines <= SampleSizeForMissing)
                        {
                            var report = ValidateLineSchema(line, requiredFields);
                            if (report != null)
                            {
                                report = report with { LineNo = lines };
                                sampleSchemas.Add(report);
                                foreach (var f in report.MissingFields)
                                {
                                    missingFieldTotal.TryGetValue(f, out var c);
                                    missingFieldTotal[f] = c + 1;
                                }
                                foreach (var f in report.TypeMismatches)
                                {
                                    typeMismatchTotal.TryGetValue(f, out var c);
                                    typeMismatchTotal[f] = c + 1;
                                }
                            }
                        }
                    }
                }
                return Results.Ok(new
                {
                    dryRun = true,
                    file = req.JsonlPath,
                    entity = req.EntityType ?? "products",
                    mode = req.Mode ?? "upsert",
                    requiredFields,
                    lines,
                    sizeBytes = new FileInfo(req.JsonlPath).Length,
                    samples,
                    sampleSchemas,
                    missingFieldTotal,
                    typeMismatchTotal,
                    schemaCheckedLines = Math.Min(lines, SampleSizeForMissing)
                });
            }

            var entityType = (req.EntityType ?? "products").Trim().ToLowerInvariant();
            if (entityType != "products" && entityType != "xrefs" && entityType != "apps")
                return Results.BadRequest(new { error = "EntityType 必须是 products/xrefs/apps", value = entityType });
            var cascade = req.Cascade ?? true;
            var p = await etl.TriggerAsync(entityType, req.JsonlPath, req.Mode ?? "upsert", 0, ct, cascade);
            return Results.Ok(p.ToJson());
        })
        .WithName("AdminTriggerEtl");

        // 取消
        group.MapDelete("/task", (EtlImportService etl, [FromBody] CancelRequest? body) =>
        {
            var reason = string.IsNullOrWhiteSpace(body?.Reason) ? "用户取消" : body!.Reason!.Trim();
            var reasonCode = string.IsNullOrWhiteSpace(body?.ReasonCode) ? "USER_REQUEST" : body!.ReasonCode!.Trim();
            var normalizedCode = EtlProgress.NormalizeReasonCode(reasonCode);
            var cancelled = etl.CancelActiveTask(reason, reasonCode);
            if (!cancelled)
                return Results.Ok(new { cancelled = false, reason = "无活跃任务", reasonCode, normalizedCode });
            return Results.Ok(new
            {
                cancelled = true,
                reason,
                reasonCode,
                normalizedCode
            });
        })
        .WithName("AdminCancelEtl");

        // 暂停
        group.MapPost("/pause", (EtlImportService etl, ILogger<Program> logger) =>
        {
            var paused = etl.PauseActiveTask();
            if (!paused)
                return Results.Ok(new { paused = false, reason = "无活跃任务或任务已被取消" });
            logger.LogInformation("ETL 暂停信号已发送 (admin 手动暂停)");
            return Results.Ok(new
            {
                paused = true,
                checkpointId = etl.Progress.Read,
                entity = etl.Progress.CurrentFile
            });
        })
        .WithName("AdminPauseEtl");

        // 恢复
        group.MapPost("/resume", async (EtlImportService etl, ILogger<Program> logger, CancellationToken ct) =>
        {
            try
            {
                var (checkpointId, entity, mode, filePath) = await etl.GetLastPausedCheckpointAsync();
                if (!File.Exists(filePath))
                    return Results.BadRequest(new { error = "暂停时记录的 JSONL 文件不存在, 无法 Resume", filePath });
                logger.LogInformation("ETL Resume 触发 entity={Entity} mode={Mode} checkpointId={Cp} file={File}",
                    entity, mode, checkpointId, filePath);
                _ = Task.Run(async () => await etl.TriggerAsync(entity, filePath, mode, checkpointId, CancellationToken.None));
                return Results.Ok(new
                {
                    resumed = true,
                    entity,
                    mode,
                    checkpointId,
                    batchSize = 1000,
                    nextLineNo = checkpointId + 1
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        })
        .WithName("AdminResumeEtl");

        // 进度查询
        group.MapGet("/progress", (EtlImportService etl) =>
        {
            return Results.Ok(etl.GetActiveTaskInfo());
        })
        .WithName("AdminEtlProgress");

        // V2 Task V17-3.2: 全量重建 Meilisearch 索引
        //   WHY 必要: 索引损坏/字段变更/schema 升级后需清空重建
        //   限流: 复用 "etl" 策略 (30/min),避免高频调用
        //   鉴权: group 已通过 RequireAuthorization (X-Admin-Token/JWT)
        //   互斥: ReindexAllAsync 内部 AcquireActiveCts 防止与 ImportXxxAsync 并发
        group.MapPost("/reindex-all", async (
            EtlImportService etl,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            logger.LogInformation("手动触发 Meilisearch 全量重建");
            try
            {
                var result = await etl.ReindexAllAsync(ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                // 已有 ETL 任务在运行 (AcquireActiveCts 抛 InvalidOperationException)
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("AdminReindexAll");

        // 进度 SSE 流
        app.MapGet("/api/admin/etl/progress/stream", async (HttpContext ctx, EtlImportService etl, IEtlProgressBroadcaster broadcaster) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";
            var first = etl.GetActiveTaskInfo();
            var firstJson = JsonSerializer.Serialize(first);
            await ctx.Response.WriteAsync($"data: {firstJson}\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

            IDisposable? subscription = null;
            if (broadcaster.IsListening)
            {
                subscription = broadcaster.Subscribe(async (payload) =>
                {
                    try
                    {
                        if (ctx.RequestAborted.IsCancellationRequested) return;
                        await ctx.Response.WriteAsync($"data: {payload}\n\n", ctx.RequestAborted);
                        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                    }
                    catch
                    {
                        // 客户端断开
                    }
                });
            }

            try
            {
                var lastLocalJson = firstJson;
                while (!ctx.RequestAborted.IsCancellationRequested)
                {
                    await Task.Delay(15000, ctx.RequestAborted);
                    if (!broadcaster.IsListening)
                    {
                        var localJson = JsonSerializer.Serialize(etl.GetActiveTaskInfo());
                        if (localJson != lastLocalJson)
                        {
                            lastLocalJson = localJson;
                            await ctx.Response.WriteAsync($"data: {localJson}\n\n", ctx.RequestAborted);
                            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                        }
                    }
                    await ctx.Response.WriteAsync(": keepalive\n\n", ctx.RequestAborted);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                subscription?.Dispose();
            }
            return Results.Empty;
        });

        // 历史查询
        group.MapGet("/history", async (
            [FromQuery] int? limit,
            [FromQuery] string? status,
            ProductDbContext db,
            CancellationToken ct) =>
        {
            var cap = Math.Clamp(limit ?? 50, 1, 500);
            var query = db.EtlProgressLogs.AsNoTracking().OrderByDescending(l => l.Id);
            if (!string.IsNullOrEmpty(status))
                query = (IOrderedQueryable<EtlProgressLog>)query.Where(l => l.Status == status);
            var rows = await query.Take(cap).Select(l => new
            {
                l.Id,
                l.EntityType,
                l.Mode,
                l.Status,
                l.ReasonCode,
                l.CancelReason,
                l.CancelledAt,
                l.ReadCount,
                l.InsertedCount,
                l.UpdatedCount,
                l.SkippedCount,
                l.SkippedMissingOem,
                // V2 改进 1: 暴露 mr_1 关联失败计数 (前端 Dashboard 可展示 V2 关键指标)
                l.SkippedMissingMr1,
                l.SkippedNullField,
                l.SkippedDuplicate,
                l.ErrorCount,
                l.IndexedCount,
                l.IndexPendingCount,
                l.LastError,
                l.StartedAt,
                l.FinishedAt,
                l.DurationSec
            }).ToListAsync(ct);
            return Results.Ok(new { count = rows.Count, items = rows });
        })
        .WithName("AdminEtlHistory");

        // reason_code 聚合
        group.MapGet("/history/aggregate", async (ProductDbContext db, CancellationToken ct) =>
        {
            var sql = @"
                SELECT
                    COALESCE(reason_code, 'LEGACY') AS code,
                    COUNT(*) AS n
                FROM etl_progress_log
                WHERE status = 'cancelled'
                GROUP BY COALESCE(reason_code, 'LEGACY')
                ORDER BY n DESC";
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var breakdown = new List<(string Code, long Count)>();
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    breakdown.Add((reader.GetString(0), reader.GetInt64(1)));
                }
            }
            var total = breakdown.Sum(x => x.Count);
            return Results.Ok(new
            {
                total,
                breakdown = breakdown.Select(x => new
                {
                    code = x.Code,
                    count = x.Count,
                    pct = total > 0 ? Math.Round(x.Count * 100.0 / total, 1) : 0
                }).ToArray()
            });
        })
        .WithName("AdminEtlHistoryAggregate");

        return app;
    }

    // 本地函数: 解析单行 JSON, 列出必填字段缺失
    private static LineSchemaReport? ValidateLineSchema(string? line, string[] requiredFields)
    {
        if (line is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var fields = new Dictionary<string, string>();
            var missing = new List<string>();
            foreach (var req in requiredFields)
            {
                if (root.TryGetProperty(req, out var prop))
                    fields[req] = prop.ValueKind.ToString().ToLowerInvariant();
                else
                {
                    fields[req] = "missing";
                    missing.Add(req);
                }
            }
            return new LineSchemaReport(0, fields, missing, new List<string>(), null);
        }
        catch (Exception ex)
        {
            return new LineSchemaReport(0, new Dictionary<string, string>(), new List<string>(), new List<string>(), ex.Message);
        }
    }
}
