using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;
using SakuraFilter.Search;
using SakuraFilter.Etl;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// PostgreSQL
var pgConn = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=spike_test_v3;Username=postgres;Password=784533";
builder.Services.AddDbContext<ProductDbContext>(opt => opt.UseNpgsql(pgConn));

// 搜索抽象 (主 Meili + 兜底 PG + 弹性包装)
builder.Services.Configure<MeiliSearchOptions>(builder.Configuration.GetSection("MeiliSearch"));
builder.Services.AddScoped<PostgresSearchProvider>();
builder.Services.AddScoped<MeiliSearchProvider>();
builder.Services.AddScoped<ISearchProvider, ResilientSearchProvider>();

// ETL 配置 (Day 7.8): Bind 自 appsettings.json "Etl" section + 启动校验
//   WHY ValidateOnStart: 启动时失败立即可见,不必运行期才发现配置错
//   校验器注册为 Singleton<IValidateOptions<EtlOptions>>,与 Bind 配合工作
builder.Services.AddOptions<EtlOptions>()
    .Bind(builder.Configuration.GetSection("Etl"))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<EtlOptions>, EtlOptionsValidator>();

// ETL (单例,共享进度状态;内部用 IServiceProvider 创 scope 访问 scoped 服务)
builder.Services.AddSingleton(sp => new EtlImportService(
    pgConn,
    sp.GetRequiredService<ILogger<EtlImportService>>(),
    sp,
    sp.GetRequiredService<IOptions<EtlOptions>>()));

// 后台服务:产品变更历史清理 (永久保留,客户可配置)
builder.Services.AddHostedService<HistoryCleanupService>();

// 后台服务:ETL 历史清理 (Day 7.8,默认关闭,etl_log.retention_enabled=true 时启用)
builder.Services.AddHostedService<EtlLogCleanupService>();

// 后台服务:Meili 死信清理 (Day 7.9,默认 7 天保留,dead_letter.retention_enabled=true 时启用)
builder.Services.AddHostedService<DeadLetterCleanupService>();

// 后台服务:死信自动恢复 (Day 7.10 Item 4,默认关闭,dead_letter.auto_recovery_enabled=true 时启用)
builder.Services.AddHostedService<DeadLetterRecoveryService>();

// 后台服务:Meili 索引写入补偿 (Day 5;Day 7.9 配置由 EtlOptions 注入)
builder.Services.AddHostedService<IndexReplayWorker>();

// 后台服务:ETL 失败告警 (Day 7.9,默认关闭,alert.enabled=true 时启用,需配 webhook_url)
builder.Services.AddHostedService<EtlAlertService>();
builder.Services.AddHttpClient("EtlAlert", c =>
{
    c.Timeout = TimeSpan.FromSeconds(5);  // 告警推送快进快出,失败可重试
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new { name = "SakuraFilter API", version = "0.3.0", status = "running" }));

// 搜索接口 (Day 4 阶段:走 ISearchProvider 抽象,Resilient 自动主备切换)
app.MapPost("/api/search", async (SearchRequest req, ISearchProvider search, CancellationToken ct) =>
{
    var result = await search.SearchAsync(req, ct);
    return Results.Ok(new { provider = search.Name, result });
})
.WithName("SearchProducts")
.WithOpenApi();

// 搜索健康检查 (监控主备状态)
app.MapGet("/api/search/health", async (ISearchProvider search, CancellationToken ct) =>
{
    var healthy = await search.HealthCheckAsync(ct);
    return Results.Ok(new { provider = search.Name, healthy });
})
.WithName("SearchHealth")
.WithOpenApi();

// 产品详情
app.MapGet("/api/products/{oem}", async (string oem, ProductDbContext db, CancellationToken ct) =>
{
    var p = await db.Products.AsNoTracking()
        .FirstOrDefaultAsync(x => x.OemNoNormalized == oem || x.OemNoDisplay == oem, ct);
    if (p is null) return Results.NotFound();

    var xrefs = await db.CrossReferences.AsNoTracking()
        .Where(x => x.ProductId == p.Id)
        .Select(x => new CrossReferenceDto(x.OemBrand, x.OemNo3, x.ProductName1))
        .ToListAsync(ct);

    var apps = await db.MachineApplications.AsNoTracking()
        .Where(m => m.ProductId == p.Id)
        .Select(m => new MachineApplicationDto(
            m.MachineBrand, m.MachineModel, m.ModelName,
            m.EngineBrand, m.EngineType, m.EngineEnergy))
        .ToListAsync(ct);

    return Results.Ok(new ProductDetail(
        p.Id, p.OemNoDisplay, p.OemNoNormalized, p.Remark, p.Type,
        p.D1Mm, p.D2Mm, p.D3Mm, p.H1Mm, p.H2Mm, p.H3Mm,
        p.D7Thread, p.D8Thread,
        p.Media, p.SealingMaterial, p.Efficiency1, p.CollapsePressureBar, p.TempRange,
        p.QtyPerCarton, p.WeightKgs, p.CartonLengthMm, p.CartonWidthMm, p.CartonHeightMm,
        p.ImageKey, xrefs, apps
    ));
})
.WithName("GetProductByOem")
.WithOpenApi();

// ETL 导入接口 (Day 5: 触发导入 + 查询进度)
app.MapPost("/api/etl/import", async (ImportRequest req, EtlImportService etl, ILogger<Program> logger, CancellationToken ct) =>
{
    if (!File.Exists(req.JsonlPath))
        return Results.BadRequest(new { error = "文件不存在", path = req.JsonlPath });

    if (etl.Progress.Status == "running")
        return Results.Conflict(new { error = "已有导入任务在运行,请等待完成", progress = etl.Progress.ToJson() });

    var mode = (req.Mode ?? "upsert").ToLowerInvariant();
    logger.LogInformation("触发 ETL 导入: {Path} (mode={Mode})", req.JsonlPath, mode);
    // 后台跑,不阻塞 HTTP 响应
    _ = Task.Run(async () => await etl.ImportProductsAsync(req.JsonlPath, mode, CancellationToken.None));
    return Results.Accepted(value: etl.Progress.ToJson());
})
.WithName("EtlImport")
.WithOpenApi();

// ETL 进度查询
app.MapGet("/api/etl/status", (EtlImportService etl) =>
    Results.Ok(etl.Progress.ToJson()))
.WithName("EtlStatus")
.WithOpenApi();

// ETL 导入 xrefs
app.MapPost("/api/etl/import-xrefs", async (ImportRequest req, EtlImportService etl, ILogger<Program> logger, CancellationToken ct) =>
{
    if (!File.Exists(req.JsonlPath))
        return Results.BadRequest(new { error = "文件不存在", path = req.JsonlPath });
    if (etl.Progress.Status == "running")
        return Results.Conflict(new { error = "已有导入任务在运行,请等待完成", progress = etl.Progress.ToJson() });
    var mode = (req.Mode ?? "upsert").ToLowerInvariant();
    logger.LogInformation("触发 xrefs 导入: {Path} (mode={Mode})", req.JsonlPath, mode);
    _ = Task.Run(async () => await etl.ImportXrefsAsync(req.JsonlPath, mode, CancellationToken.None));
    return Results.Accepted(value: etl.Progress.ToJson());
})
.WithName("EtlImportXrefs")
.WithOpenApi();

// ETL 导入 apps
app.MapPost("/api/etl/import-apps", async (ImportRequest req, EtlImportService etl, ILogger<Program> logger, CancellationToken ct) =>
{
    if (!File.Exists(req.JsonlPath))
        return Results.BadRequest(new { error = "文件不存在", path = req.JsonlPath });
    if (etl.Progress.Status == "running")
        return Results.Conflict(new { error = "已有导入任务在运行,请等待完成", progress = etl.Progress.ToJson() });
    var mode = (req.Mode ?? "upsert").ToLowerInvariant();
    logger.LogInformation("触发 apps 导入: {Path} (mode={Mode})", req.JsonlPath, mode);
    _ = Task.Run(async () => await etl.ImportAppsAsync(req.JsonlPath, mode, CancellationToken.None));
    return Results.Accepted(value: etl.Progress.ToJson());
})
.WithName("EtlImportApps")
.WithOpenApi();

// Day 7.5: 死信队列查询 (运维入口,免 psql)
// Day 7.8: 加 keyset cursor 分页 — 解决 3000+ 行时 offset 性能差的问题
//   cursor 格式: "<ISO8601 movedAt>|<id>" (例: 2026-07-01T00:00:00Z|12345)
//   返回: nextCursor (下一页起点,末页为 null) + hasMore (是否还有更多)
// Day 7.10: 增加 ?min_recovery_count=N / ?max_recovery_count=N 过滤,便于排查"自动恢复多次仍失败"
app.MapGet("/api/admin/dead-letter", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? operation,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? since,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? cursor,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "min_recovery_count")] int? minRecoveryCount,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "max_recovery_count")] int? maxRecoveryCount,
    ProductDbContext db,
    CancellationToken ct) =>
{
    var cap = Math.Clamp(limit ?? 50, 1, 500);
    var query = db.SearchIndexDeadLetters.AsNoTracking();
    if (!string.IsNullOrEmpty(operation))
        query = query.Where(d => d.Operation == operation);
    // Day 7.10: 恢复次数过滤 — 排查"自动恢复 N 次仍失败"用
    if (minRecoveryCount.HasValue)
        query = query.Where(d => d.RecoveryCount >= minRecoveryCount.Value);
    if (maxRecoveryCount.HasValue)
        query = query.Where(d => d.RecoveryCount <= maxRecoveryCount.Value);
    // Day 7.6: 时间过滤 — 排查"今天又累积了多少"用,不必每次 limit 翻页
    //   since 接受 ISO8601 (如 2026-07-01T00:00:00Z),未传则不过滤
    DateTime? sinceUtc = null;
    if (!string.IsNullOrEmpty(since))
    {
        if (!DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
            return Results.BadRequest(new { error = "since 必须是 ISO8601 时间 (例: 2026-07-01T00:00:00Z)", since });
        sinceUtc = parsed;
        query = query.Where(d => d.MovedAt >= sinceUtc);
    }
    // Day 7.8: cursor 解析 — 走 keyset 分页 (比 OFFSET 快,4w 行实测 < 50ms)
    //   格式: "<movedAt ISO8601>|<id>",例如 2026-07-01T00:00:00Z|12345
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
        // keyset: 严格小于 (DESC 排序下 "更早或同时但 id 更小" 的位置)
        query = query.Where(d => d.MovedAt < cursorMovedAt.Value
                              || (d.MovedAt == cursorMovedAt.Value && d.Id < cursorId.Value));
    }
    // WHY: PostgreSQL jsonb 不支持直接 substring(jsonb, int, int),
    //      必须先 ::text 转换 (DBA 反馈,Day 7.5)
    // Day 7.8: 多取一条用于判断 hasMore (避免额外 COUNT)
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
    if (hasMore) rows.RemoveAt(rows.Count - 1);  // 弹出探针行
    string? nextCursor = null;
    if (hasMore && rows.Count > 0)
    {
        var last = rows[^1];
        nextCursor = $"{new DateTimeOffset(last.MovedAt, TimeSpan.Zero):yyyy-MM-ddTHH:mm:ss.fffZ}|{last.Id}";
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
.WithName("GetDeadLetter")
.WithOpenApi();

// Day 7.5: 死信恢复 (单条) — 移回 pending,retry_count 重置
//   流程: dead_letter → pending (retry=0, next_retry_at=now) → IndexReplayWorker 自动重试
//   WHY: 死信只移不删,排查后通过此端点重新激活
//   Day 7.10.1 BUG FIX: 不删除 dead_letter 行,改 status='recovered' + 留痕
//   Day 7.10.1: 用 advisory lock 与后台 worker 串行化
app.MapPost("/api/admin/dead-letter/{id:long}/recover", async (long id, ProductDbContext db, ILogger<Program> logger, CancellationToken ct) =>
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
            // 已恢复: 仅回填 pending_id 提示信息, 不重复恢复
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

        // BUG FIX: 死信行不删,改 status + 留痕
        dead.Status = "recovered";
        dead.RecoveryCount += 1;
        dead.LastRecoveryAt = now;
        dead.LastRecoveryError = null;
        dead.RecoveredAt = now;
        await db.SaveChangesAsync(ct);
        // 直接从跟踪的 entity 读 Id (避免重查错配)
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
.WithName("RecoverDeadLetter")
.WithOpenApi();

// Day 7.10: 死信批量恢复 — 按过滤条件一次性移回 pending
//   WHY: 凌晨某时段批量产生 200+ 死信 (Meili 短暂 5xx), 逐条 /recover 太累
//   参数: operation=index|delete, lastErrorContains=关键词, maxRecoveryCount=N (只恢复未达上限的)
//   限位: 严格按 recovery_count < maxRecoveryCount 过滤,与后台 worker 策略一致
//   Day 7.10.1 BUG FIX: 不删除行,改 status + 留痕 + 用 advisory lock
app.MapPost("/api/admin/dead-letter/recover-batch", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? operation,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? lastErrorContains,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? maxRecoveryCount,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
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
        // Day 7.10.1: 加 status='active' 过滤,跳过已恢复的
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
        // Day 7.10.1 PATCH: 跟踪新增的 pending entity 关联 dead, 直接从 instance 读 Id
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
        // 直接从跟踪的 entity 读 Id 回填
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

app.Run();

public record ImportRequest(string JsonlPath, string? Mode);

// Day 7.5: 死信查询参数 (运维可见性)
// Day 7.10: 增加 recovery_count / last_recovery_at / last_recovery_error 字段
// Day 7.10.1: 增加 status / recovered_at / recovered_to_pending_id 字段
public record DeadLetterItem(long Id, long OriginalId, string Operation, int RetryCount,
    string? LastError, DateTime CreatedAt, DateTime MovedAt, string PayloadPreview,
    int RecoveryCount, DateTime? LastRecoveryAt, string? LastRecoveryError,
    string Status, DateTime? RecoveredAt, long? RecoveredToPendingId);
