using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Minio;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Core.Entities;
using SakuraFilter.Core.Interfaces;
using SakuraFilter.Infrastructure.Data;
using SakuraFilter.Infrastructure.Storage;
using SakuraFilter.Search;
using SakuraFilter.Etl;
using System.Threading.RateLimiting;

// Npgsql 6+: 默认只接受 DateTime Kind=Utc, 反序列化 "2007-01-01" 这类无时区字符串会抛异常
//   Day 8.1: machine_application.production_date_start 等字段是 DATE 类型,
//            前端 JSON 传 "2007-01-01" 被 .NET 当 Kind=Unspecified
//   启用 legacy behavior: Npgsql 自动按 UTC 写入, 不强制 UTC
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "SakuraFilter API", Version = "0.4.0" });
    c.AddSecurityDefinition("X-Admin-Token", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "X-Admin-Token",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Day 8.4: dev 静态 token, 从 appsettings.json 的 Auth:DevStaticToken 读"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "X-Admin-Token"
                }
            },
            Array.Empty<string>()
        }
    });
});

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

// Day 8.3: cursor HMAC 签名工具 (单例, 内部从 IConfiguration 读 Search:CursorHmacKey)
builder.Services.AddSingleton<CursorHmac>();

// Day 8.1: 注册 IObjectStorage (MinIO)
//   WHY Singleton: IMinioClient 是线程安全单例, MinioStorage 无内部状态
//   WHY 不放 appsettings.Development: 默认值兜底, dev 可覆盖 endpoint/bucket
builder.Services.AddSingleton<IObjectStorage>(sp =>
{
    var config = builder.Configuration.GetSection("Minio");
    var endpoint = config["Endpoint"] ?? "localhost:9000";
    var useSSL = bool.TryParse(config["UseSSL"], out var ssl) && ssl;
    var minioClient = new MinioClient()
        .WithEndpoint(endpoint)
        .WithCredentials(config["AccessKey"] ?? "minioadmin", config["SecretKey"] ?? "minioadmin")
        .WithSSL(useSSL)
        .Build();
    return new MinioStorage(
        minioClient,
        config["BucketName"] ?? "sakurafilter",
        config["PublicEndpoint"] ?? $"http://{endpoint}"
    );
});

// Day 8.1: CORS (前端 Vite dev server localhost:5173 / 5174 + 未来其它端口)
//   WHY 显式 AllowedOrigins 而非 AllowAnyOrigin: AllowAnyOrigin + AllowCredentials
//        浏览器会拒绝, 必须白名单
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173", "http://localhost:5174", "http://localhost:3000" };
builder.Services.AddCors(o => o.AddPolicy("SakuraFilterCors", p =>
    p.WithOrigins(allowedOrigins)
     .AllowAnyMethod()
     .AllowAnyHeader()
     .AllowCredentials()));

// Day 8.4: API Rate Limiting (.NET 8 内置 System.Threading.RateLimiting)
//   设计: 三个分区独立限流
//     - "global"   全局默认 600/分钟 (后台 CRUD 等)
//     - "search"   前台搜索 300/分钟 (防爬虫)
//     - "etl"      ETL 触发 30/分钟 (防误触发 / 误重试)
//   算法: FixedWindow 1 分钟 (简单可控, 适合 MVP)
//   超限响应: 429 + Retry-After + ProblemDetails
var rateLimitConfig = builder.Configuration.GetSection("RateLimit").Get<RateLimitOptions>()
    ?? new RateLimitOptions();
if (rateLimitConfig.Enabled)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, ct) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.ContentType = "application/problem+json";
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)retryAfter.TotalSeconds).ToString();
            }
            await context.HttpContext.Response.WriteAsync(
                "{\"type\":\"https://tools.ietf.org/html/rfc6585#section-4\"," +
                "\"title\":\"Too Many Requests\"," +
                "\"status\":429," +
                "\"detail\":\"请求频率超限, 请稍后重试\"}", ct);
        };
        options.AddPolicy("global", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "global",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitConfig.GlobalPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
        options.AddPolicy("search", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "search",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitConfig.SearchPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
        options.AddPolicy("etl", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "etl",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitConfig.EtlPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
    });
}

// Day 8.1: 路由约束 — slot 用 :int (后端内部转 short, 范围 1-6 校验)
//   WHY 不注册 :short: ASP.NET Core 8 Routing.ConstraintMap 不支持 typeof(short),
//   必须用 IParameterPolicy 实现 (如 Int16RouteConstraint 不存在, 用 IntRouteConstraint)
//   简化:用 :int 接收, 内部 short 强转 + 范围校验
// builder.Services.Configure<RouteOptions>(o => o.ConstraintMap["short"] = typeof(Int16RouteConstraint));

// Day 8.1: 后台产品服务 (Scoped, 跟 DbContext 一致)
builder.Services.AddScoped<AdminProductService>();
builder.Services.AddScoped<AdminProductImageService>();

var app = builder.Build();

// Day 8.4: 中间件 pipeline 顺序
//   1) UseExceptionHandler 统一错误 (开发环境显示堆栈, 生产隐藏)
//   2) UseCors 跨域 (要在鉴权前, 否则 preflight 失败)
//   3) UseRateLimiter 限流 (鉴权前, 防匿名 DoS)
//   4) DevTokenAuthMiddleware 自定义鉴权
//   5) UseOpenAPI 文档 (所有环境可用, 替代原 Swagger)
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
app.UseCors("SakuraFilterCors");
if (rateLimitConfig.Enabled)
{
    app.UseRateLimiter();
}
app.UseMiddleware<DevTokenAuthMiddleware>();

// Day 8.4: Scalar UI 替代 Swagger (生产环境也可访问, 配防火墙策略控制)
//   端点: /scalar (Scalar UI) + /openapi/v1.json (原始 OpenAPI 文档, 给前端生成 TS 类型用)
//   Day 8.4 实际回退: 环境无外网, 用已缓存的 Swashbuckle 6.6.2 (Swagger UI)
//   生产部署时: nuget 装 Scalar.AspNetCore → 改用 MapScalarApiReference
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SakuraFilter v1");
    c.DocumentTitle = "SakuraFilter API (Day 8.4)";
    c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
});

app.MapGet("/", () => Results.Ok(new { name = "SakuraFilter API", version = "0.3.0", status = "running" }));

// 搜索接口 (Day 4 阶段:走 ISearchProvider 抽象,Resilient 自动主备切换)
app.MapPost("/api/search", async (SearchRequest req, ISearchProvider search, CancellationToken ct) =>
{
    var result = await search.SearchAsync(req, ct);
    return Results.Ok(new { provider = search.Name, result });
})
.WithName("SearchProducts")
.WithOpenApi()
.RequireRateLimiting("search");

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

// =================== Day 8.1: 后台产品管理端点 (规格 后台新增产品格式) ===================

// 新增产品 (7 分区表单一次性提交)
app.MapPost("/api/admin/products", async (ProductFormDto form, AdminProductService svc, HttpContext ctx, CancellationToken ct) =>
{
    try
    {
        var user = ctx.Request.Headers["X-User"].FirstOrDefault() ?? "system";
        var p = await svc.CreateAsync(form, user, ct);
        return Results.Created($"/api/admin/products/{p.Id}", p);
    }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
})
.WithName("AdminCreateProduct");

// 列表分页
app.MapGet("/api/admin/products", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] int? page,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? pageSize,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? type,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? keyword,
    [Microsoft.AspNetCore.Mvc.FromQuery] bool? includeDiscontinued,
    AdminProductService svc, CancellationToken ct) =>
{
    var (items, total) = await svc.ListAsync(
        page ?? 1, pageSize ?? 50, type, keyword, includeDiscontinued ?? false, ct);
    return Results.Ok(new { total, page = page ?? 1, pageSize = pageSize ?? 50, items });
})
.WithName("AdminListProducts");

// 高级搜索 (Day 8.2, 17 字段 + 尺寸范围 + 批量 OEM)
//   接受 AdminProductSearchRequest 全部 query string 字段 (扁平 DTO)
//   例: GET /api/admin/products/search?type=oil&d1Min=90&d1Max=100&sizeTolerance=5&oem2Batch=ABC,XYZ
//   Day 8.2.1: countMode=exact|estimated|none, 默认 exact (向后兼容)
app.MapGet("/api/admin/products/search", async (
    [AsParameters] AdminProductSearchRequest req,
    AdminProductService svc, HttpContext ctx, CancellationToken ct) =>
{
    try
    {
        var (items, total, nextCursor, countModeUsed) = await svc.SearchAsync(req, ct);
        var page = req.Page ?? 1;
        var pageSize = req.PageSize ?? 50;
        // countMode 归一化 (与 Service 内部一致, 共享 DTO 扩展方法)
        var countMode = req.NormalizeCountMode();
        // pagingMode 归一化
        var pagingMode = req.NormalizePagingMode();
        // hasMore 判断:
        //   - countMode=exact/estimated: total - (page*pageSize) > 0
        //   - countMode=none: items.Count < pageSize 表示末页 (拿不满 = 没了)
        //   WHY 不论 mode 都用 items.Count < pageSize 兜底:
        //     翻到末页时 exact/estimated 的 total 计算也可能漏 (并发新增数据), 实际条数永远最准
        bool hasMore = items.Count >= pageSize
            && (countMode == "none" || total > (long)page * pageSize);
        return Results.Ok(new
        {
            total,
            countMode,        // 客户端请求的 count 模式
            countModeUsed,    // 实际使用的模式 (exact 模式超时可能降级到 estimated)
            pagingMode,
            hasMore,
            nextCursor,    // cursor 模式下为下一页起点, 末页为 null
            page,
            pageSize,
            sizeTolerance = req.SizeTolerance ?? 5m,
            items
        });
    }
    catch (ArgumentException ex)
    {
        // Day 8.4: cursor HMAC 验签失败 / 格式错统一转 ProblemDetails
        return ProblemDetailsFactory.FromException(ctx, ex);
    }
})
.WithName("AdminSearchProducts");

// 批量对比 (Day 8.2, 1-6 个产品)
//   WHY POST 而非 GET: URL 长度限制 (RFC 7230 推荐 8000 字符), 6 个 id 不会爆但 POST 语义更清晰
//   WHY body 是 { ids: [1,2,3] }: 数组 query string 多语言绑定不稳定, JSON body 通用
app.MapPost("/api/admin/products/compare", async (
    CompareRequest body,
    AdminProductService svc, CancellationToken ct) =>
{
    if (body?.Ids is null || body.Ids.Count == 0)
        return Results.BadRequest(new { error = "ids 不能为空" });
    if (body.Ids.Count > 6)
        return Results.BadRequest(new { error = "对比最多 6 个产品", given = body.Ids.Count });
    var items = await svc.CompareAsync(body.Ids, null, ct);
    return Results.Ok(new { count = items.Count, items });
})
.WithName("AdminCompareProducts");

// 详情
app.MapGet("/api/admin/products/{id:long}", async (long id, AdminProductService svc, AdminProductImageService imgSvc, HttpContext ctx, CancellationToken ct) =>
{
    try
    {
        var p = await svc.GetByIdAsync(id, ct);
        var imgs = await imgSvc.ListAsync(id, ct);
        return Results.Ok(p with { Images = imgs });
    }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
})
.WithName("AdminGetProduct");

// 更新
app.MapPut("/api/admin/products/{id:long}", async (long id, ProductFormDto form, AdminProductService svc, HttpContext ctx, CancellationToken ct) =>
{
    try
    {
        var user = ctx.Request.Headers["X-User"].FirstOrDefault() ?? "system";
        var p = await svc.UpdateAsync(id, form, user, ct);
        return Results.Ok(p);
    }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
})
.WithName("AdminUpdateProduct");

// 软删除 (下架)
app.MapDelete("/api/admin/products/{id:long}", async (long id, AdminProductService svc, HttpContext ctx, CancellationToken ct) =>
{
    try
    {
        var user = ctx.Request.Headers["X-User"].FirstOrDefault() ?? "system";
        await svc.DeleteAsync(id, user, ct);
        return Results.Ok(new { id, discontinued = true });
    }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
})
.WithName("AdminDeleteProduct");

// 恢复 (从下架恢复)
app.MapPost("/api/admin/products/{id:long}/restore", async (long id, AdminProductService svc, HttpContext ctx, CancellationToken ct) =>
{
    try
    {
        var user = ctx.Request.Headers["X-User"].FirstOrDefault() ?? "system";
        await svc.RestoreAsync(id, user, ct);
        return Results.Ok(new { id, restored = true });
    }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
})
.WithName("AdminRestoreProduct");

// 上传产品图 (slot 1-6)
app.MapPost("/api/admin/products/{id:long}/images/{slot:int}", async (
    long id, int slot, HttpRequest req, AdminProductImageService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (slot < 1 || slot > 6) return Results.BadRequest(new { error = "slot 必须在 1-6 之间" });
    if (!req.HasFormContentType) return Results.BadRequest(new { error = "需 multipart/form-data" });
    var form = await req.ReadFormAsync(ct);
    var file = form.Files.GetFile("file");
    if (file == null) return Results.BadRequest(new { error = "缺 file 字段" });
    var user = req.Headers["X-User"].FirstOrDefault() ?? "system";
    try
    {
        using var stream = file.OpenReadStream();
        var img = await svc.UploadAsync(id, (short)slot, stream, file.ContentType ?? "image/jpeg", user, ct);
        return Results.Ok(img);
    }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
})
.WithName("AdminUploadProductImage")
.DisableAntiforgery();  // 后台上传不强制 CSRF (内部 API)

// 删除产品图
app.MapDelete("/api/admin/products/{id:long}/images/{slot:int}", async (long id, int slot, AdminProductImageService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (slot < 1 || slot > 6) return Results.BadRequest(new { error = "slot 必须在 1-6 之间" });
    try
    {
        await svc.DeleteAsync(id, (short)slot, ct);
        return Results.Ok(new { productId = id, slot, deleted = true });
    }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
})
.WithName("AdminDeleteProductImage");

// 列出产品 6 张图
app.MapGet("/api/admin/products/{id:long}/images", async (long id, AdminProductImageService svc, CancellationToken ct) =>
    Results.Ok(await svc.ListAsync(id, ct)))
.WithName("AdminListProductImages");

// Day 8.4: 产品变更历史查询 (后台详情页"变更记录"tab 用)
//   Day 9.2: 加可选筛选 (changeType/since/until) 用于 history 抽屉顶部筛选
//     - changeType: create/update/discontinue/restore
//     - since/until: ISO8601 (例: 2026-07-01T00:00:00Z),DateTime 隐式绑定
app.MapGet("/api/admin/products/{id:long}/history", async (
    long id,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? changeType,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? since,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? until,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    AdminProductService svc,
    HttpContext ctx,
    CancellationToken ct) =>
{
    try
    {
        DateTime? sinceUtc = null, untilUtc = null;
        if (!string.IsNullOrEmpty(since))
        {
            if (!DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var ps))
                return Results.BadRequest(new { error = "since 必须是 ISO8601", since });
            sinceUtc = ps;
        }
        if (!string.IsNullOrEmpty(until))
        {
            if (!DateTime.TryParse(until, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var pu))
                return Results.BadRequest(new { error = "until 必须是 ISO8601", until });
            untilUtc = pu;
        }
        var cap = Math.Clamp(limit ?? 50, 1, 200);
        var page = await svc.GetHistoryAsync(id, cap, changeType, sinceUtc, untilUtc, ct);
        return Results.Ok(page);
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
})
.WithName("AdminGetProductHistory")
.RequireRateLimiting("global");

// Day 8.4: 后台手动 ETL 触发 (后台 ETL 页面 "立即导入" 按钮)
app.MapPost("/api/admin/etl/trigger", async (
    [FromBody] EtlTriggerRequest req,
    EtlImportService etl,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    logger.LogInformation("手动 ETL 触发 entity={Entity} mode={Mode} file={File} dryRun={Dry}",
        req.JsonlPath, req.Mode, req.JsonlPath, req.DryRun);

    if (req.DryRun)
    {
        // Day 9.1: dry-run 校验 + 前 5 行 JSON 样本, 避免大文件等待
        // Day 9.2: 加 JSON Schema 校验 — 解析 samples 字段, 列出缺失/类型错
        if (!File.Exists(req.JsonlPath))
            return Results.Problem(detail: $"文件不存在: {req.JsonlPath}", statusCode: 404, title: "File Not Found");

        // Day 9.2: local function — 解析单行 JSON, 列出必填字段缺失
        //   WHY 用 local function: 顶级语句文件不允许 public static method (CS8803)
        //   WHY 不可变 record: 跨方法访问 .MissingFields (元组字段名不可见)
        //   WHY 不加 type mismatch 检查: 当前 ETL 字段类型都是 string/number/nullable, 强校验收效小
        LineSchemaReport? ValidateLineSchema(string? line, string[] requiredFields)
        {
            if (line is null) return null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(line);
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

        var lines = 0;
        var samples = new List<string>();
        var sampleSchemas = new List<LineSchemaReport>();
        var missingFieldTotal = new Dictionary<string, int>();
        var typeMismatchTotal = new Dictionary<string, int>();
        const int SampleSizeForSchema = 5;     // 前端展示
        const int SampleSizeForMissing = 1000;  // 字段缺失统计抽样
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

    var p = await etl.TriggerAsync("products", req.JsonlPath, req.Mode ?? "upsert", ct);
    return Results.Ok(p.ToJson());
})
.WithName("AdminTriggerEtl")
.RequireRateLimiting("etl");

// Day 9.1: 后台取消 ETL 任务 (后台 ETL 页面 "取消" 按钮)
app.MapDelete("/api/admin/etl/task", (EtlImportService etl) =>
{
    var cancelled = etl.CancelActiveTask();
    if (!cancelled)
        return Results.Ok(new { cancelled = false, reason = "无活跃任务" });
    return Results.Ok(new { cancelled = true });
})
.WithName("AdminCancelEtl")
.RequireRateLimiting("etl");

// Day 8.4: 后台 ETL 进度查询 (后台 ETL 页面 3s 轮询)
app.MapGet("/api/admin/etl/progress", (EtlImportService etl) =>
{
    return Results.Ok(etl.GetActiveTaskInfo());
})
.WithName("AdminEtlProgress")
.RequireRateLimiting("etl");

app.Run();

// Day 9.2: dry-run JSON Schema 校验结果
//   显式 record 而非 tuple: 跨 lambda 调用 .MissingFields/.TypeMismatches (元组字段名跨方法不可见)
public record LineSchemaReport(
    int LineNo,
    Dictionary<string, string> Fields,
    List<string> MissingFields,
    List<string> TypeMismatches,
    string? Error);

public record ImportRequest(string JsonlPath, string? Mode);

// Day 8.2: 批量对比请求体
public record CompareRequest(List<long> Ids);

// Day 7.5: 死信查询参数 (运维可见性)
// Day 7.10: 增加 recovery_count / last_recovery_at / last_recovery_error 字段
// Day 7.10.1: 增加 status / recovered_at / recovered_to_pending_id 字段
public record DeadLetterItem(long Id, long OriginalId, string Operation, int RetryCount,
    string? LastError, DateTime CreatedAt, DateTime MovedAt, string PayloadPreview,
    int RecoveryCount, DateTime? LastRecoveryAt, string? LastRecoveryError,
    string Status, DateTime? RecoveredAt, long? RecoveredToPendingId);
