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
        var group = app.MapGroup("/api/admin/etl").WithTags("AdminEtl")
            .RequireAuthorization("Admin")  // V24-F19: spec F11
            .RequireRateLimiting("etl");

        // ===== V3(2026-08-25): P0 导入向导 — 模板下载 + 文件上传 (客户自助导入) =====
        //   设计: docs/etl-import-wizard-design.md
        //   模板: GET /template?entity= → xlsx (表头=JSON key + 批注说明 + 示例行, 2 行式兼容 ConvertAsync Skip(1))
        //   上传: POST /upload (multipart) → 保存 /tmp/etl-upload/{guid}.{ext} → xlsx 转 jsonl → 返回 jsonlPath
        //   安全: 扩展名白名单 + 50MB 上限 + guid 文件名 (不信任原始名) + >24h 临时文件清理
        group.MapGet("/template", async (
            HttpContext ctx,
            [FromQuery] string? entity,
            CancellationToken ct) =>
        {
            var entityKey = (entity ?? "products").Trim().ToLowerInvariant();
            if (entityKey != "products" && entityKey != "xrefs" && entityKey != "apps")
                return Results.BadRequest(new { error = "entity 必须是 products/xrefs/apps", value = entityKey });

            try
            {
                var bytes = await Task.Run(() => EtlTemplateGenerator.Build(entityKey), ct);
                var fileName = $"sakurafilter-{entityKey}-template.xlsx";
                return Results.File(bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: $"模板生成失败: {ex.Message}", statusCode: 500, title: "Template Generation Failed");
            }
        })
        .WithName("AdminEtlTemplate");

        // 文件上传 (客户真正上传 XLSX/JSONL 到服务器)
        //   WHY: 原"拖拽"只填服务器路径 (假设文件已就位), 客户无法自助导入
        group.MapPost("/upload", async (
            [FromForm] IFormFile? file,
            [FromQuery] string? entity,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var entityKey = (entity ?? "products").Trim().ToLowerInvariant();
            if (entityKey != "products" && entityKey != "xrefs" && entityKey != "apps")
                return Results.BadRequest(new { error = "entity 必须是 products/xrefs/apps", value = entityKey });
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "请选择要上传的文件" });
            if (file.Length > 50 * 1024 * 1024)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowed = new[] { ".xlsx", ".xls", ".jsonl" };
            if (!allowed.Contains(ext))
                return Results.BadRequest(new { error = $"不支持的文件类型 {ext}, 仅支持: {string.Join(" / ", allowed)}" });

            // 临时目录 + guid 文件名 (防路径注入)
            var uploadDir = "/tmp/etl-upload";
            Directory.CreateDirectory(uploadDir);
            // 清理 >24h 旧临时文件 (防磁盘膨胀)
            try
            {
                foreach (var old in Directory.GetFiles(uploadDir, "*", SearchOption.TopDirectoryOnly))
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(old) > TimeSpan.FromHours(24))
                    {
                        try { File.Delete(old); } catch { /* 忽略清理失败 */ }
                    }
                }
            }
            catch { /* 清理失败不影响上传 */ }

            var guid = Guid.NewGuid().ToString("N");
            var savedPath = Path.Combine(uploadDir, guid + ext);
            await using (var fs = new FileStream(savedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(fs, ct);
            }

            // xlsx/xls → JSONL (ConvertAsync 只处理 .xlsx, 输出到 temp/sakurafilter-etl/)
            string jsonlPath;
            try
            {
                jsonlPath = await EtlSpreadsheetAdapter.ConvertAsync(savedPath, entityKey, ct);
            }
            catch (Exception ex)
            {
                try { File.Delete(savedPath); } catch { }
                return Results.BadRequest(new { error = $"文件解析失败: {ex.Message}" });
            }
            // 转换成功后删除原始上传文件 (JSONL 已就位)
            if (!jsonlPath.Equals(savedPath, StringComparison.Ordinal))
            {
                try { File.Delete(savedPath); } catch { }
            }

            logger.LogInformation("ETL 上传完成: entity={Entity} file={Name} jsonl={Path} size={Size}",
                entityKey, file.FileName, jsonlPath, file.Length);
            return Results.Ok(new
            {
                jsonlPath,
                entityType = entityKey,
                fileName = file.FileName,
                sizeBytes = file.Length
            });
        })
        .DisableAntiforgery()  // V3: IFormFile 端点自动附加 antiforgery 元数据, 需显式禁用 (JWT 鉴权已足够)
        .WithName("AdminEtlUpload");

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

            if (config.ValidateJsonlPath(req.JsonlPath) is { } pathErr)
                return Results.BadRequest(new { error = pathErr });

            if (req.DryRun)
            {
                if (!File.Exists(req.JsonlPath))
                    return Results.Problem(detail: $"文件不存在: {req.JsonlPath}", statusCode: 404, title: "File Not Found");

                var previewPath = Path.GetExtension(req.JsonlPath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                    ? await EtlSpreadsheetAdapter.ConvertAsync(req.JsonlPath, req.EntityType ?? "products", ct)
                    : req.JsonlPath;

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
                using (var fs = File.OpenRead(previewPath))
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
                // v30-27 P0 修复: 1M 文档重建耗时 30+ 分钟, HTTP 请求 30s 超时会取消 CancellationToken
                //   根因: ReindexAllAsync(ct) 的 ct 来自 HTTP 请求, 请求超时后 ct 被取消, 索引写入中断
                //   修复: 用 Task.Run + CancellationToken.None 触发后台任务 (与 /resume 端点 L158 同模式)
                //   返回立即响应, 进度通过 /progress/stream 或 /history 查询
                _ = Task.Run(async () =>
                {
                    try { await etl.ReindexAllAsync(CancellationToken.None); }
                    catch (Exception ex) { logger.LogError(ex, "后台 ReindexAllAsync 失败"); }
                });
                return Results.Ok(new { message = "Meilisearch 全量重建已触发 (后台执行)", hint = "通过 /api/admin/etl/progress/stream 或 /api/admin/etl/history 查询进度" });
            }
            catch (InvalidOperationException ex)
            {
                // 已有 ETL 任务在运行 (AcquireActiveCts 抛 InvalidOperationException)
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("AdminReindexAll");

        // V3(2026-08-24): 断点续传重建 (掉盘/冻结恢复后不清空续传) — 从 perf 分支移植
        //   WHY: 该机磁盘/WSL2 不稳定, 全量重建每次清空从头来, 中断白干; 续传从 fromId 继续, 已提交文档保留
        //   用法: fromId = Meili 当前文档数 - 2000 (留余量覆盖边界缺口); limit 可选 (分批触发, 防硬件过载)
        group.MapPost("/reindex-resume", async (
            EtlImportService etl,
            ILogger<Program> logger,
            [FromQuery] long fromId,
            [FromQuery] int? limit,
            CancellationToken ct) =>
        {
            logger.LogInformation("触发 Meilisearch 续传重建 fromId={FromId} limit={Limit}", fromId, limit);
            try
            {
                // 与 /reindex-all 同模式: 后台 fire-and-forget, 避免 HTTP 超时取消
                _ = Task.Run(async () =>
                {
                    try { await etl.ReindexFromIdAsync(fromId, limit, CancellationToken.None); }
                    catch (Exception ex) { logger.LogError(ex, "后台 ReindexFromIdAsync 失败"); }
                });
                return Results.Ok(new { message = $"Meilisearch 续传重建已触发 (fromId={fromId}, limit={limit}, 后台执行)", hint = "通过 /api/admin/etl/progress 查询进度" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("AdminReindexResume");

        // 进度 SSE 流
        // v30-17 P0 安全修复: SSE 端点脱离 group 鉴权, 未认证用户可获取 ETL 进度
        //   WHY 脱离 group: V24-F78 时期为兼容 EventSource (不能带 header) 故意脱离, ADR #1 已改用 fetch + Bearer, 后端鉴权可恢复
        //   修复: 加 RequireAuthorization("Admin"), 前端 useEtlProgress.ts L201-209 已用 fetch + buildAuthHeaders() 带 Bearer
        //   限流暂不加: SSE 长连接限流策略需单独评估 (QPS vs 并发连接), 留 P2
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
        }).RequireAuthorization("Admin");  // v30-17 P0: SSE 端点鉴权 (原脱离 group, 未认证可访问)

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
