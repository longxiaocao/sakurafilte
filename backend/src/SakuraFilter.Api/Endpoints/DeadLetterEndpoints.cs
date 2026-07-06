using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Api.DTOs;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// 死信队列端点：admin 角色鉴权（DevTokenAuthMiddleware 已保护）。
/// 提供分页查询、单条恢复、批量恢复。
/// </summary>
public static class DeadLetterEndpoints
{
    public static IEndpointRouteBuilder MapDeadLetterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/dead-letter").WithTags("AdminDeadLetter");

        // 分页查询
        group.MapGet("/", async (
            [FromQuery] int? limit,
            [FromQuery] string? operation,
            [FromQuery] string? since,
            [FromQuery] string? cursor,
            [FromQuery(Name = "min_recovery_count")] int? minRecoveryCount,
            [FromQuery(Name = "max_recovery_count")] int? maxRecoveryCount,
            ProductDbContext db,
            CancellationToken ct) =>
        {
            var cap = Math.Clamp(limit ?? 50, 1, 500);
            var query = db.SearchIndexDeadLetters.AsNoTracking();
            if (!string.IsNullOrEmpty(operation))
                query = query.Where(d => d.Operation == operation);
            if (minRecoveryCount.HasValue)
                query = query.Where(d => d.RecoveryCount >= minRecoveryCount.Value);
            if (maxRecoveryCount.HasValue)
                query = query.Where(d => d.RecoveryCount <= maxRecoveryCount.Value);

            DateTime? sinceUtc = null;
            if (!string.IsNullOrEmpty(since))
            {
                if (!DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                    return Results.BadRequest(new { error = "since 必须是 ISO8601 时间 (例: 2026-07-01T00:00:00Z)", since });
                sinceUtc = parsed;
                query = query.Where(d => d.MovedAt >= sinceUtc);
            }

            DateTime? cursorMovedAt = null;
            long? cursorId = null;
            if (!string.IsNullOrEmpty(cursor))
            {
                var parts = cursor.Split('|', 2);
                if (parts.Length != 2
                    || !DateTime.TryParse(parts[0], null,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                        out var cma)
                    || !long.TryParse(parts[1], out var cid))
                    return Results.BadRequest(new { error = "cursor 格式错,期望 <ISO8601 movedAt>|<id>", cursor });
                cursorMovedAt = cma;
                cursorId = cid;
                query = query.Where(d => d.MovedAt < cursorMovedAt.Value
                                       || (d.MovedAt == cursorMovedAt.Value && d.Id < cursorId.Value));
            }

            var rows = await query
                .OrderByDescending(d => d.MovedAt)
                .ThenByDescending(d => d.Id)
                .Take(cap + 1)
                .Select(d => new DeadLetterItem(
                    d.Id, d.OriginalId, d.Operation, d.RetryCount, d.LastError,
                    d.CreatedAt, d.MovedAt,
                    d.Payload.ToString().Length > 200
                        ? d.Payload.ToString().Substring(0, 200) + "..."
                        : d.Payload.ToString(),
                    d.RecoveryCount, d.LastRecoveryAt, d.LastRecoveryError,
                    d.Status, d.RecoveredAt, d.RecoveredToPendingId))
                .ToListAsync(ct);
            var hasMore = rows.Count > cap;
            if (hasMore) rows.RemoveAt(rows.Count - 1);
            string? nextCursor = null;
            if (hasMore && rows.Count > 0)
            {
                var last = rows[^1];
                nextCursor = $"{new DateTimeOffset(DateTime.SpecifyKind(last.MovedAt, DateTimeKind.Utc), TimeSpan.Zero):yyyy-MM-ddTHH:mm:ss.fffZ}|{last.Id}";
            }
            var totalAll = await db.SearchIndexDeadLetters.CountAsync(ct);
            var totalInRange = sinceUtc.HasValue || minRecoveryCount.HasValue || maxRecoveryCount.HasValue
                ? await query.CountAsync(ct)
                : totalAll;
            return Results.Ok(new
            {
                total = totalAll,
                totalInRange = totalInRange,
                returned = rows.Count,
                limit = cap,
                since = sinceUtc,
                minRecoveryCount = minRecoveryCount,
                maxRecoveryCount = maxRecoveryCount,
                cursor = cursor,
                nextCursor = nextCursor,
                hasMore = hasMore,
                items = rows
            });
        })
        .WithSummary("死信队列分页查询 (keyset cursor, 支持 operation/since/recovery_count 过滤)").WithName("GetDeadLetter")
        .WithOpenApi();

        // 单条恢复
        group.MapPost("/{id:long}/recover", async (long id, ProductDbContext db, ILogger<Program> logger, CancellationToken ct) =>
        {
            bool gotLock = false;
            object? result = null;
            gotLock = await DeadLetterRecoveryService.TryWithAdvisoryLockAsync(db, async () =>
            {
                var dead = await db.SearchIndexDeadLetters.FirstOrDefaultAsync(d => d.Id == id, ct);
                if (dead is null)
                {
                    result = Results.NotFound(new { error = "死信条目不存在", id });
                    return;
                }
                if (dead.Status == "recovered")
                {
                    result = Results.Conflict(new { error = "死信已恢复,无需再次操作", id, recoveredAt = dead.RecoveredAt, recoveredToPendingId = dead.RecoveredToPendingId });
                    return;
                }

                var now = DateTime.UtcNow;
                var pending = new SearchIndexPending
                {
                    Operation = dead.Operation,
                    Payload = dead.Payload,
                    RetryCount = 0,
                    LastError = null,
                    CreatedAt = dead.CreatedAt,
                    NextRetryAt = now
                };
                db.SearchIndexPending.Add(pending);
                dead.Status = "recovered";
                dead.RecoveryCount += 1;
                dead.LastRecoveryAt = now;
                dead.LastRecoveryError = null;
                dead.RecoveredAt = now;
                await db.SaveChangesAsync(ct);
                dead.RecoveredToPendingId = pending.Id;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("死信 {Id} (original={OriginalId}) 恢复成功 → pending {NewId} (recovery_count={Rc})",
                    dead.Id, dead.OriginalId, pending.Id, dead.RecoveryCount);
                result = Results.Ok(new
                {
                    recovered = true,
                    newPendingId = pending.Id,
                    originalId = dead.OriginalId,
                    recoveryCount = dead.RecoveryCount,
                });
            }, ct);

            if (!gotLock)
            {
                return Results.Conflict(new { error = "advisory lock 被占用,后台 worker 正在恢复,请稍后重试" });
            }
            return result!;
        })
        .WithSummary("死信单条恢复 (移回 pending + advisory lock 串行化)").WithName("RecoverDeadLetter")
        .WithOpenApi();

        // 批量恢复
        group.MapPost("/recover-batch", async (
            [FromQuery] string? operation,
            [FromQuery] string? lastErrorContains,
            [FromQuery] int? maxRecoveryCount,
            [FromQuery] int? limit,
            ProductDbContext db,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var cap = Math.Clamp(limit ?? 100, 1, 1000);
            var maxRc = maxRecoveryCount ?? 3;
            bool gotLock = false;
            object? result = null;
            gotLock = await DeadLetterRecoveryService.TryWithAdvisoryLockAsync(db, async () =>
            {
                var query = db.SearchIndexDeadLetters
                    .Where(d => d.Status == "active")
                    .Where(d => d.RecoveryCount < maxRc);
                if (!string.IsNullOrEmpty(operation))
                    query = query.Where(d => d.Operation == operation);
                if (!string.IsNullOrEmpty(lastErrorContains))
                    query = query.Where(d => d.LastError != null && d.LastError.ToLower().Contains(lastErrorContains.ToLower()));

                var dead = await query.OrderBy(d => d.MovedAt).Take(cap).ToListAsync(ct);
                if (dead.Count == 0)
                {
                    result = Results.Ok(new { matched = 0, moved = 0 });
                    return;
                }

                var now = DateTime.UtcNow;
                int moved = 0;
                var addedPending = new Dictionary<long, SearchIndexPending>();
                foreach (var d in dead)
                {
                    var pending = new SearchIndexPending
                    {
                        Operation = d.Operation,
                        Payload = d.Payload,
                        RetryCount = 0,
                        LastError = null,
                        CreatedAt = d.CreatedAt,
                        NextRetryAt = now,
                    };
                    db.SearchIndexPending.Add(pending);
                    d.Status = "recovered";
                    d.RecoveryCount += 1;
                    d.LastRecoveryAt = now;
                    d.LastRecoveryError = null;
                    d.RecoveredAt = now;
                    addedPending[d.Id] = pending;
                    moved++;
                }
                await db.SaveChangesAsync(ct);
                foreach (var d in dead)
                {
                    if (d.RecoveredToPendingId.HasValue) continue;
                    if (addedPending.TryGetValue(d.Id, out var p))
                        d.RecoveredToPendingId = p.Id;
                }
                await db.SaveChangesAsync(ct);

                logger.LogInformation("批量恢复 {Moved}/{Matched} 条死信 → pending (operation={Op}, lastErrorContains={Lec}, maxRc={MaxRc})",
                    moved, dead.Count, operation, lastErrorContains, maxRc);
                result = Results.Ok(new
                {
                    matched = dead.Count,
                    moved = moved,
                    operation = operation,
                    lastErrorContains = lastErrorContains,
                    maxRecoveryCount = maxRc,
                });
            }, ct);

            if (!gotLock)
            {
                return Results.Conflict(new { error = "advisory lock 被占用,后台 worker 正在恢复,请稍后重试" });
            }
            return result!;
        })
        .WithName("RecoverDeadLetterBatch")
        .WithOpenApi();

        return app;
    }
}
