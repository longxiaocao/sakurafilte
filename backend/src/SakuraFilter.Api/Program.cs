using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.AspNetCore.HttpOverrides;        // P1-3: ForwardedHeaders
using Microsoft.AspNetCore.Diagnostics;           // P1-5: IExceptionHandlerFeature
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Minio;
using Aliyun.OSS;
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
// P3.2 (Task 10): 注册 MVC 控制器 (PublicSearchController 等)
builder.Services.AddControllers();
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
// P0-3 修复: 移除硬编码密码兜底, 配置缺失直接抛异常 (生产环境用环境变量 ConnectionStrings__Postgres 覆盖)
var pgConn = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres 未配置 (检查 appsettings.json 或环境变量 ConnectionStrings__Postgres)");
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
    sp.GetRequiredService<IOptions<EtlOptions>>(),
    // Day 9.6: 跨实例 SSE 广播器 (PG NOTIFY/LISTEN,零新依赖)
    sp.GetRequiredService<IEtlProgressBroadcaster>()));

// Day 9.6: ETL 跨实例广播器 (PG NOTIFY/LISTEN 实现,零新依赖)
//   - 多实例部署时, A 实例 ETL 进度变化 → 所有实例的 SSE 客户端都收到
//   - 单实例部署时降级为本地轮询 (无影响)
builder.Services.AddSingleton<IEtlProgressBroadcaster, EtlProgressBroadcaster>();

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
// P6: 性能阈值告警 (基于 P5.5 PerfMetrics, 默认开启, 阈值通过 system_settings 在线调整)
//   - 监控 P95/错误率/最大耗时, 超阈值时记日志 + 内存 FIFO 最近 100 条
//   - 5min 抑制窗口防刷屏, /api/admin/perf/alerts 端点查询当前告警
builder.Services.AddSingleton<PerfAlertService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PerfAlertService>());
builder.Services.AddHttpClient("EtlAlert", c =>
{
    c.Timeout = TimeSpan.FromSeconds(5);  // 告警推送快进快出,失败可重试
});

// Day 8.3: cursor HMAC 签名工具 (单例, 内部从 IConfiguration 读 Search:CursorHmacKey)
builder.Services.AddSingleton<CursorHmac>();

// P5.5: 性能埋点指标聚合 (Singleton, 中间件和端点共享)
builder.Services.AddSingleton<PerfMetrics>();

// P7.1: Auth Token 轮转存储 (Singleton, 内存缓存 + DB 覆盖 + PG NOTIFY 重载)
//   - DevTokenAuthMiddleware 当前仍直读 IConfiguration; 后续 PR 改造为注入 IAuthTokenStore
//   - 现阶段 AuthTokenStore 启动时建表 + 从 DB 加载, /api/admin/auth/status 端点可查
builder.Services.AddSingleton<IAuthTokenStore, AuthTokenStore>();
builder.Services.AddSingleton<AuthTokenBroadcaster>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AuthTokenBroadcaster>());

// P1.2 (Task 4): 注册 IObjectStorage, 按 Storage:Provider 配置切换 (minio / aliyun-oss)
//   WHY Singleton: IMinioClient / OssClient 都是线程安全单例, *Storage 无内部状态
//   WHY 不放 appsettings.Development: 默认值兜底, dev 可覆盖 endpoint/bucket
//   Provider 切换说明:
//     - "minio" (默认, 向后兼容 Day 8.1): 用本地 MinIO bucket
//     - "aliyun-oss": 用阿里云 OSS + 可选 CDN 域名
//   切换流程见 docs/cdn-switch.md (重启即生效, 无需 DB 迁移)
var storageProvider = builder.Configuration["Storage:Provider"]?.ToLowerInvariant() ?? "minio";
builder.Services.AddSingleton<IObjectStorage>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    if (storageProvider == "aliyun-oss")
    {
        var config = builder.Configuration.GetSection("Aliyun");
        var endpoint = config["Endpoint"] ?? "oss-cn-hangzhou.aliyuncs.com";
        var accessKeyId = config["AccessKeyId"] ?? "";
        var accessKeySecret = config["AccessKeySecret"] ?? "";
        // WHY 校验非空: 阿里云 SDK 空 AccessKey 启动时不报错, 第一次 PutObject 才抛 InvalidArgument
        //   启动期校验可立即暴露配置错, 避免运行时才发现
        if (string.IsNullOrEmpty(accessKeyId) || string.IsNullOrEmpty(accessKeySecret))
        {
            throw new InvalidOperationException(
                "Aliyun:AccessKeyId / AccessKeySecret 不能为空, 配置 appsettings.json 或环境变量 Aliyun__AccessKeyId / Aliyun__AccessKeySecret");
        }
        var ossClient = new OssClient(endpoint, accessKeyId, accessKeySecret);
        logger.LogInformation("[Storage] Provider=aliyun-oss, Endpoint={Endpoint}, Bucket={Bucket}, Cdn={Cdn}",
            endpoint, config["BucketName"], config["CdnEndpoint"]);
        return new AliyunOssStorage(
            ossClient,
            config["BucketName"] ?? "sakurafilter-prod",
            config["PublicEndpoint"] ?? $"https://{config["BucketName"]}.{endpoint}",
            config["CdnEndpoint"]);
    }
    // 默认 minio (Day 8.1 实现)
    var minioConfig = builder.Configuration.GetSection("Minio");
    var minioEndpoint = minioConfig["Endpoint"] ?? "localhost:9000";
    var useSSL = bool.TryParse(minioConfig["UseSSL"], out var ssl) && ssl;
    var minioClient = new MinioClient()
        .WithEndpoint(minioEndpoint)
        .WithCredentials(minioConfig["AccessKey"] ?? "minioadmin", minioConfig["SecretKey"] ?? "minioadmin")
        .WithSSL(useSSL)
        .Build();
    logger.LogInformation("[Storage] Provider=minio, Endpoint={Endpoint}, Bucket={Bucket}", minioEndpoint, minioConfig["BucketName"]);
    return new MinioStorage(
        minioClient,
        minioConfig["BucketName"] ?? "sakurafilter",
        minioConfig["PublicEndpoint"] ?? $"http://{minioEndpoint}"
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
// Day 10+: 后台字典服务 (P1.3 OEM 品牌字典, P2.1 重构为 OemBrandDictService 继承 BaseDictService)
builder.Services.AddScoped<OemBrandDictService>();
// Day 10+ P2.2: 6 个新字典 (复用 P2.1 IDictService + BaseDictService 抽象)
builder.Services.AddScoped<ProductName1DictService>();
builder.Services.AddScoped<ProductName2DictService>();
builder.Services.AddScoped<TypeDictService>();
builder.Services.AddScoped<OemNo3DictService>();
builder.Services.AddScoped<MediaDictService>();
builder.Services.AddScoped<MachineDictService>();
builder.Services.AddScoped<EngineDictService>();

var app = builder.Build();

// Day 9.11: EF Core Migrations 自动应用
//   WHY: 启动时自动应用待执行迁移,无需手动 SQL,获得 __EFMigrationsHistory 版本追踪
//   现有数据库: InitialCreate 已手动标记为已应用,Migrate 跳过执行,不会 ALTER 现有 schema
//   全新部署: InitialCreate 执行 Up 创建所有表
//   并发安全: Migrate 内部用 PostgreSQL advisory lock 保护,多实例并发安全
//   异常处理: 失败立即抛出终止启动,避免带病运行
try
{
    using var migrateScope = app.Services.CreateScope();
    var migrateDb = migrateScope.ServiceProvider.GetRequiredService<ProductDbContext>();
    migrateDb.Database.Migrate();
    app.Logger.LogInformation("数据库迁移检查完成");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "数据库迁移失败,应用启动终止");
    throw;
}

// Day 9.6: 启动 ETL 跨实例广播器 (PG LISTEN 后台 task)
_ = Task.Run(async () =>
{
    try
    {
        await app.Services.GetRequiredService<IEtlProgressBroadcaster>().InitAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "EtlProgressBroadcaster 启动失败, SSE 将退化为本地轮询");
    }
});

// P7.1: 启动 AuthTokenStore 初始化 (从 DB 加载, 覆盖 IConfiguration 兜底值)
_ = Task.Run(async () =>
{
    try
    {
        await app.Services.GetRequiredService<IAuthTokenStore>().InitAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "AuthTokenStore 启动失败, 使用 IConfiguration 兜底");
    }
});

// Day 8.4: 中间件 pipeline 顺序
//   1) UseForwardedHeaders 反向代理 IP 透传 (P1-3: 限流才有效)
//   2) UseExceptionHandler 统一错误 (P1-5: 生产隐藏堆栈, 开发显示)
//   3) UseCors 跨域 (要在鉴权前, 否则 preflight 失败)
//   4) UseRateLimiter 限流 (鉴权前, 防匿名 DoS)
//   5) P5.5: ResponseTimeMiddleware 响应时间埋点 (在鉴权前,记录 401 等异常耗时)
//   6) DevTokenAuthMiddleware 自定义鉴权
//   7) UseSwagger/UseSwaggerUI (P1-6: 仅开发环境暴露)
// P1-3 修复: ForwardedHeaders 透传反向代理的客户端 IP, 否则限流把所有请求当代理 IP
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// P1-5 修复: 生产环境用 UseExceptionHandler + ProblemDetailsFactory, 不泄露堆栈
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler(handler =>
    {
        handler.Run(async ctx =>
        {
            var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
            var logger = ctx.RequestServices.GetService<ILogger<Program>>();
            // ProblemDetailsFactory 内部会记日志 (P0-2 修复) + 返回通用 500 提示
            var result = ProblemDetailsFactory.FromException(ctx, ex ?? new Exception("未知异常"), logger);
            await result.ExecuteAsync(ctx);
        });
    });
}
app.UseCors("SakuraFilterCors");
if (rateLimitConfig.Enabled)
{
    app.UseRateLimiter();
}
// P5.5: 响应时间埋点 (在鉴权前,记录 401 等异常耗时;中间件自身排除 /api/perf 防止递归)
app.UseMiddleware<ResponseTimeMiddleware>();
app.UseMiddleware<DevTokenAuthMiddleware>();

// P1-6 修复: Swagger 仅开发环境暴露, 生产环境关闭防止接口泄露
//   WHY: Swagger UI 暴露所有端点签名, 攻击者可枚举 API 进行精准攻击
//   生产环境用 OpenAPI.json + 防火墙白名单替代 (如需文档)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "SakuraFilter v1");
        c.DocumentTitle = "SakuraFilter API (Day 8.4)";
        c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    });
}

app.MapGet("/", () => Results.Ok(new { name = "SakuraFilter API", version = "0.3.0", status = "running" }));

// P5.5: 性能埋点查询端点 (返回最近 1000 条请求样本的 P50/P95/P99)
//   暴露给前端 perf 监控 + 运维自查, 不需要鉴权 (只读统计)
//   WHY 不放 /api/admin: 监控指标应公开, 监控代理(如 uptime robot)能直接访问
//   端点本身被 ResponseTimeMiddleware 排除, 不会污染统计样本
app.MapGet("/api/perf", (PerfMetrics metrics) =>
    Results.Ok(metrics.GetSnapshot()))
.WithName("PerfSnapshot")
.WithOpenApi();

// P6: 性能告警查询 (需 X-Admin-Token, 走 DevTokenAuthMiddleware 鉴权)
//   返回最近 100 条告警 (按时间倒序), 运维面板用
//   WHY 放 /api/admin: 告警含系统健康信号, 不应公开 (与 /api/perf 区分)
app.MapGet("/api/admin/perf/alerts", (PerfAlertService alerts, int? limit) =>
    Results.Ok(alerts.GetRecentAlerts(limit ?? 50)))
.WithName("PerfAlerts")
.WithOpenApi();

// P5.5: 接收前端性能埋点批量上报
//   格式: { samples: [{ path, method, statusCode, durationMs, ts }] }
//   上限 100 条/批, 防恶意大批量上报
//   WHY 接收前端埋点: 真实用户体验 (含网络/RTT/渲染) 比服务端时间更全
//   写入 server 日志, 不入库 (高频写, 无业务价值); 运维可从日志聚合
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
        // WHY structured log: 让日志聚合工具能直接查询慢请求
        logger.LogInformation("[PERF-FE] {Method} {Path} {Status} {Duration}ms ts={Ts} ua={UA}",
            s.Method ?? "?", s.Path ?? "?", s.StatusCode, s.DurationMs, s.Ts, ua);
    }
    return Results.Ok(new { received = body.Samples.Count });
})
.WithName("PerfIngest")
.WithOpenApi();

// P5.5: 健康检查分级
//   /health/live   永远 200 (liveness probe — 进程是否存活, Docker K8s 用)
//   /health/ready  检查 PG + Meili (readiness probe — 进程是否可服务流量)
//   WHY 分级: liveness 失败 → 杀进程重启; readiness 失败 → 临时剔除 LB 流量池
//   中间件排除这两个路径, 不会污染 perf 统计
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }))
    .WithName("HealthLive");
app.MapGet("/health/ready", async (
    ProductDbContext db,
    SakuraFilter.Search.ISearchProvider search,
    CancellationToken ct) =>
{
    var checks = new List<object>();
    var pgOk = false;
    try
    {
        pgOk = await db.Database.CanConnectAsync(ct);
    }
    catch { pgOk = false; }
    checks.Add(new { name = "postgres", healthy = pgOk });
    var meiliOk = false;
    try
    {
        meiliOk = await search.HealthCheckAsync(ct);
    }
    catch { meiliOk = false; }
    checks.Add(new { name = "search", healthy = meiliOk });
    var allOk = pgOk && meiliOk;
    return Results.Json(
        new { status = allOk ? "ready" : "degraded", checks },
        statusCode: allOk ? 200 : 503);
})
.WithName("HealthReady");

// P7.1: Auth Token 状态查询端点
//   暴露 current/previous 长度 + 轮转时间 + 操作人 (不暴露完整 token)
//   用于: 运维验证 rotate 是否生效 + CI E2E 验证
app.MapGet("/api/admin/auth/status", (IAuthTokenStore store) =>
{
    var current = store.Current;
    var previous = store.Previous;
    return Results.Ok(new
    {
        // WHY 只暴露长度: token 不应进日志/响应, 即便是 admin 接口
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
.WithName("AdminAuthStatus")
.RequireRateLimiting("global");

// P3.2 (Task 10): 启用 MVC 控制器路由
//   - PublicSearchController (/api/public/search/*) 由 Controller 内的 [Route] 自动注册
app.MapControllers();

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
// Day 11 改进 1: 统一入口, EntityType 参数路由到 products/xrefs/apps
//   - 兼容: 不传 EntityType = products (旧调用)
//   - 新调用: POST /api/etl/import { entityType: "xrefs" } = 触发 xrefs
//   - cascade: 仅 products full-load 时生效 (改进 2: CASCADE 安全锁)
app.MapPost("/api/etl/import", (ImportRequest req, EtlImportService etl, ILogger<Program> logger, CancellationToken ct) =>
{
    if (!File.Exists(req.JsonlPath))
        return Results.BadRequest(new { error = "文件不存在", path = req.JsonlPath });

    if (etl.Progress.Status == "running")
        return Results.Conflict(new { error = "已有导入任务在运行,请等待完成", progress = etl.Progress.ToJson() });

    var mode = (req.Mode ?? "upsert").ToLowerInvariant();
    var entityType = (req.EntityType ?? "products").ToLowerInvariant();
    // 改进 2: cascade 安全锁, 仅 products full-load 默认 cascade=true (兼容旧行为)
    //   显式传 cascade=false 可防止 TRUNCATE CASCADE 清空 xrefs/apps
    var cascade = req.Cascade ?? true;

    if (entityType != "products" && entityType != "xrefs" && entityType != "apps")
        return Results.BadRequest(new { error = "EntityType 必须是 products/xrefs/apps", value = entityType });

    logger.LogInformation("触发 ETL 导入: {Entity} {Path} (mode={Mode}, cascade={Cascade})", entityType, req.JsonlPath, mode, cascade);

    // 改进 2: 仅 products 传 cascade 标志, xrefs/apps 忽略
    var cascadeFlag = entityType == "products" ? cascade : true;
    _ = Task.Run(async () => await etl.TriggerAsync(entityType, req.JsonlPath, mode, 0, CancellationToken.None, cascadeFlag));
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
app.MapPost("/api/etl/import-xrefs", (ImportRequest req, EtlImportService etl, ILogger<Program> logger, CancellationToken ct) =>
{
    if (!File.Exists(req.JsonlPath))
        return Results.BadRequest(new { error = "文件不存在", path = req.JsonlPath });
    if (etl.Progress.Status == "running")
        return Results.Conflict(new { error = "已有导入任务在运行,请等待完成", progress = etl.Progress.ToJson() });
    var mode = (req.Mode ?? "upsert").ToLowerInvariant();
    logger.LogInformation("触发 xrefs 导入: {Path} (mode={Mode})", req.JsonlPath, mode);
    // Day 9.9: 统一走 TriggerAsync (校验 + 路由 + _activeCts 已下沉到 Import*Async)
    _ = Task.Run(async () => await etl.TriggerAsync("xrefs", req.JsonlPath, mode, 0, CancellationToken.None));
    return Results.Accepted(value: etl.Progress.ToJson());
})
.WithName("EtlImportXrefs")
.WithOpenApi();

// ETL 导入 apps
app.MapPost("/api/etl/import-apps", (ImportRequest req, EtlImportService etl, ILogger<Program> logger, CancellationToken ct) =>
{
    if (!File.Exists(req.JsonlPath))
        return Results.BadRequest(new { error = "文件不存在", path = req.JsonlPath });
    if (etl.Progress.Status == "running")
        return Results.Conflict(new { error = "已有导入任务在运行,请等待完成", progress = etl.Progress.ToJson() });
    var mode = (req.Mode ?? "upsert").ToLowerInvariant();
    logger.LogInformation("触发 apps 导入: {Path} (mode={Mode})", req.JsonlPath, mode);
    // Day 9.9: 统一走 TriggerAsync (校验 + 路由 + _activeCts 已下沉到 Import*Async)
    _ = Task.Run(async () => await etl.TriggerAsync("apps", req.JsonlPath, mode, 0, CancellationToken.None));
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
    [Microsoft.AspNetCore.Mvc.FromQuery] string? cursor,
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
        var page = await svc.GetHistoryAsync(id, cap, changeType, sinceUtc, untilUtc, cursor, ct);
        return Results.Ok(page);
    }

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
        req.EntityType ?? "products", req.Mode, req.JsonlPath, req.DryRun);

    if (req.DryRun)
    {
        // Day 9.1: dry-run 校验 + 前 5 行 JSON 样本 (Day 9.4 改为 50 行), 避免大文件等待
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
        // Day 9.4: 50 行样本足够覆盖 99% 字段异构场景 (OEM/MR/D1-8/H1-4 等 17+ 字段)
        const int SampleSizeForSchema = 50;    // 前端展示 (Day 9.4: 5 → 50)
        const int SampleSizeForMissing = 1000; // 字段缺失统计抽样
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

    // Day 11 Phase 1 BUG FIX A: 之前硬编码 "products", 前端 UI entity 选择器不生效
    //   - DTO 已有 EntityType 字段 (ProductHistoryDto.cs:42), 但端点没用
    //   - 修复: 用 req.EntityType ?? "products" 路由到对应 Import*Async
    //   - 同时传 cascade 参数 (BUG FIX A: 之前 DTO 缺字段, 被静默丢弃)
    var entityType = (req.EntityType ?? "products").Trim().ToLowerInvariant();
    if (entityType != "products" && entityType != "xrefs" && entityType != "apps")
        return Results.BadRequest(new { error = "EntityType 必须是 products/xrefs/apps", value = entityType });
    var cascade = req.Cascade ?? true;  // 兼容旧调用 (不传 = true)
    var p = await etl.TriggerAsync(entityType, req.JsonlPath, req.Mode ?? "upsert", 0, ct, cascade);
    return Results.Ok(p.ToJson());
})
.WithName("AdminTriggerEtl")
.RequireRateLimiting("etl");

// Day 9.1: 后台取消 ETL 任务 (后台 ETL 页面 "取消" 按钮)
//   Day 9.4: 接受 body { reason }, 透传给 EtlImportService.CancelActiveTask(reason)
//     reason 写入 etl_progress_log.cancel_reason, 供运维审计
//     缺省时使用 "用户取消" (向后兼容)
app.MapDelete("/api/admin/etl/task", (EtlImportService etl, [Microsoft.AspNetCore.Mvc.FromBody] CancelRequest? body) =>
{
    var reason = string.IsNullOrWhiteSpace(body?.Reason) ? "用户取消" : body!.Reason!.Trim();
    // Day 9.5: reasonCode 可选 (USER_REQUEST/TIMEOUT/SYSTEM_SHUTDOWN/ADMIN_OVERRIDE/OTHER), 兜底 USER_REQUEST
    var reasonCode = string.IsNullOrWhiteSpace(body?.ReasonCode) ? "USER_REQUEST" : body!.ReasonCode!.Trim();
    var normalizedCode = EtlProgress.NormalizeReasonCode(reasonCode);
    var cancelled = etl.CancelActiveTask(reason, reasonCode);
    if (!cancelled)
        // Day 9.5: 即便没活跃任务, 也回显规范化后的 code, 便于前端 echo 用户输入
        return Results.Ok(new { cancelled = false, reason = "无活跃任务", reasonCode, normalizedCode });
    return Results.Ok(new
    {
        cancelled = true,
        reason,
        reasonCode,
        normalizedCode
    });
})
.WithName("AdminCancelEtl")
.RequireRateLimiting("etl");

// P1.1 (Task 3): 后台暂停 ETL 任务 (后台 ETL 页面 "暂停" 按钮)
//   与 Cancel 区别: Cancel 走 _activeCts.Cancel() 抛 OperationCanceledException,
//                   Pause 走 _pausedFlag=1, 当前批次跑完后优雅退出
//   checkpoint_id 写入 etl_progress_log, Resume 时按此值续读
app.MapPost("/api/admin/etl/pause", (EtlImportService etl, ILogger<Program> logger) =>
{
    var paused = etl.PauseActiveTask();
    if (!paused)
        return Results.Ok(new { paused = false, reason = "无活跃任务或任务已被取消" });
    logger.LogInformation("ETL 暂停信号已发送 (admin 手动暂停)");
    // 返回当前累计行数作为 checkpoint,前端显示
    return Results.Ok(new
    {
        paused = true,
        checkpointId = etl.Progress.Read,
        entity = etl.Progress.CurrentFile
    });
})
.WithName("AdminPauseEtl")
.RequireRateLimiting("etl");

// P1.1 (Task 3): 后台恢复 ETL 任务 (后台 ETL 页面 "恢复" 按钮)
//   找到最近一条 status='paused' 的记录, 读 checkpoint_id, 触发新 ETL 从该行续读
//   与 Cancel 区别: Cancel 终止, Resume 续跑
//   找不到 paused 记录时 404 (前端弹窗提示)
app.MapPost("/api/admin/etl/resume", async (EtlImportService etl, ILogger<Program> logger, CancellationToken ct) =>
{
    try
    {
        var (checkpointId, entity, mode, filePath) = await etl.GetLastPausedCheckpointAsync();
        if (!File.Exists(filePath))
            return Results.BadRequest(new { error = "暂停时记录的 JSONL 文件不存在, 无法 Resume", filePath });
        logger.LogInformation("ETL Resume 触发 entity={Entity} mode={Mode} checkpointId={Cp} file={File}",
            entity, mode, checkpointId, filePath);
        // 统一走 TriggerAsync, startLineNo=checkpointId 让 ETL 跳过已读行
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
.WithName("AdminResumeEtl")
.RequireRateLimiting("etl");




// Day 8.4: 后台 ETL 进度查询 (后台 ETL 页面 3s 轮询)
// Day 9.4: 后台 ETL 进度 SSE 流 (替换 3s 轮询)
//   格式: text/event-stream, 实时推送 activeTask 状态
//   客户端 EventSource 关闭时 (页面卸载), ct 触发, 自动停止推送
//   WHY 不用 SignalR: 运维监控场景用 SSE 更轻, EventSource API 是 W3C 标准
// Day 9.6: 跨实例广播 (PG NOTIFY/LISTEN)
//   - 订阅 IEtlProgressBroadcaster, 收到消息时立即推给客户端
//   - 15s 心跳注释行 (避免代理/Nginx 60s 超时断开)
//   - broadcaster 不可用 (单 PG 故障) 时降级为 1s 轮询本地 GetActiveTaskInfo
app.MapGet("/api/admin/etl/progress/stream", async (HttpContext ctx, EtlImportService etl, IEtlProgressBroadcaster broadcaster) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";  // 禁用 nginx 缓冲
    // 立即推本地一帧 (避免客户端等 broadcaster 第一帧)
    var first = etl.GetActiveTaskInfo();
    var firstJson = System.Text.Json.JsonSerializer.Serialize(first);
    await ctx.Response.WriteAsync($"data: {firstJson}\n\n", ctx.RequestAborted);
    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

    // Day 9.6: 订阅跨实例广播, 收到 NOTIFY 时立即推
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
                // 客户端断开 (ct 触发), 静默
            }
        });
    }

    try
    {
        // 心跳循环: 15s 发一次注释行, 保持连接活跃 + 顺便做 broadcaster 兜底轮询
        var lastLocalJson = firstJson;
        while (!ctx.RequestAborted.IsCancellationRequested)
        {
            await Task.Delay(15000, ctx.RequestAborted);
            // broadcaster 未连上时,15s 兜底轮询本地 (单实例降级)
            if (!broadcaster.IsListening)
            {
                var localJson = System.Text.Json.JsonSerializer.Serialize(etl.GetActiveTaskInfo());
                if (localJson != lastLocalJson)
                {
                    lastLocalJson = localJson;
                    await ctx.Response.WriteAsync($"data: {localJson}\n\n", ctx.RequestAborted);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                }
            }
            // 注释行 (SSE 注释, 浏览器忽略) — 保持连接
            await ctx.Response.WriteAsync(": keepalive\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }
    }
    catch (OperationCanceledException) { /* 客户端断开 */ }
    finally
    {
        subscription?.Dispose();
    }
    return Results.Empty;  // 占位, 不会执行到这里
});
app.MapGet("/api/admin/etl/progress", (EtlImportService etl) =>
{
    return Results.Ok(etl.GetActiveTaskInfo());
})
.WithName("AdminEtlProgress")
.RequireRateLimiting("etl");

// Day 9.8: ETL 历史日志查询 + reason_code 聚合
//   - /history: 拉最近 N 条记录, 给前端表格/列表用
//   - /history/aggregate: 按 reason_code 聚合, 给饼图用
//   - 两端都不分页 (admin 后台 + 数据量 < 1000/天), 简单 list 即可
app.MapGet("/api/admin/etl/history", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? status,
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
.WithName("AdminEtlHistory")
.RequireRateLimiting("etl");

// Day 9.8: 按 reason_code 聚合 (饼图数据源)
//   5 枚举固定: USER_REQUEST / TIMEOUT / SYSTEM_SHUTDOWN / ADMIN_OVERRIDE / OTHER
//   未分类 (NULL) 用 LEGACY 表示 (旧记录无 reason_code)
//   统计口径: status='cancelled' 的所有记录 (历史数据可能 status 是 completed/failed 但有 reason_code, 不计入)
app.MapGet("/api/admin/etl/history/aggregate", async (ProductDbContext db, CancellationToken ct) =>
{
    // Day 9.8: GROUP BY reason_code, 同时统计总数 + 各枚举占比
    //   不用 EF 的 GroupBy (翻译复杂), 改用原生 SQL 一行解决
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
.WithName("AdminEtlHistoryAggregate")
.RequireRateLimiting("etl");

// =================== Day 10: 后台字典管理端点 (P1.3 OEM 品牌字典) ===================
//   设计要点:
//   - 全部走 /api/admin/dict/* 前缀, 鉴权中间件自动保护
//   - 限流走 global 分区 (与产品管理一致, 默认 600/分钟)
//   - 错误统一转 ProblemDetails (与 admin/products 端点风格一致)
//   - list 支持 ?includeDeleted=true 看审计; 默认只看未删
//   - typeahead 是独立端点, 返回精简字段, 给表单自动补全专用

// 列出 OEM 品牌字典
app.MapGet("/api/admin/dict/oem-brands", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
    [Microsoft.AspNetCore.Mvc.FromQuery] bool? includeDeleted,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    OemBrandDictService svc, CancellationToken ct) =>
{
    var items = await svc.ListOemBrandsAsync(q, includeDeleted ?? false, limit, ct);
    return Results.Ok(new { count = items.Count, items });
})
.WithName("AdminListOemBrands");

// Typeahead (后台产品表单分区 2 自动补全)
app.MapGet("/api/admin/dict/oem-brands/typeahead", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    OemBrandDictService svc, CancellationToken ct) =>
{
    var items = await svc.TypeaheadOemBrandsAsync(q, limit, ct);
    return Results.Ok(new { count = items.Count, items });
})
.WithName("AdminTypeaheadOemBrands");

// 新增 OEM 品牌
app.MapPost("/api/admin/dict/oem-brands", async (
    OemBrandCreateRequest body,
    OemBrandDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.Brand))
        return Results.BadRequest(new { error = "brand 不能为空" });
    try
    {
        var item = await svc.CreateOemBrandAsync(body.Brand, body.SortOrder, ct);
        return Results.Created($"/api/admin/dict/oem-brands/{item.Id}", item);
    }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
})
.WithName("AdminCreateOemBrand");

// 更新 OEM 品牌
app.MapPut("/api/admin/dict/oem-brands/{id:long}", async (
    long id,
    OemBrandUpdateRequest body,
    OemBrandDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try
    {
        var item = await svc.UpdateOemBrandAsync(id, body.Brand, body.SortOrder, ct);
        return Results.Ok(item);
    }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
})
.WithName("AdminUpdateOemBrand");

// 软删除 OEM 品牌
app.MapDelete("/api/admin/dict/oem-brands/{id:long}", async (
    long id, OemBrandDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try
    {
        await svc.DeleteOemBrandAsync(id, ct);
        return Results.Ok(new { id, deleted = true });
    }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
})
.WithName("AdminDeleteOemBrand");

// 恢复已删除 OEM 品牌
app.MapPost("/api/admin/dict/oem-brands/{id:long}/restore", async (
    long id, OemBrandDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try
    {
        var item = await svc.RestoreOemBrandAsync(id, ct);
        return Results.Ok(item);
    }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
})
.WithName("AdminRestoreOemBrand");

// 批量重排序 (前端拖拽后调用)
app.MapPost("/api/admin/dict/oem-brands/reorder", async (
    OemBrandReorderRequest body,
    OemBrandDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try
    {
        await svc.ReorderOemBrandsAsync(body.Items, ct);
        return Results.Ok(new { updated = body.Items?.Count ?? 0 });
    }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
})
.WithName("AdminReorderOemBrands");

// =================== Day 10+ P2.2: 6 个新字典管理端点 ===================
//   4 个单字段字典 (Product Name 1/2, Type, OEM 3): 完全复用 Day 10 OEM Brand 模式
//   3 个多字段字典 (Media/Machine/Engine): DTO 含多个字段, 端点签名差异
//   路径命名: 复数 kebab-case, 与 Day 10 一致
//     - /api/admin/dict/product-name1s
//     - /api/admin/dict/product-name2s
//     - /api/admin/dict/types
//     - /api/admin/dict/oem-no3s
//     - /api/admin/dict/medias
//     - /api/admin/dict/machines
//     - /api/admin/dict/engines

// ---------- 字典 1: Product Name 1 ----------
app.MapGet("/api/admin/dict/product-name1s", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
    [Microsoft.AspNetCore.Mvc.FromQuery] bool? includeDeleted,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    ProductName1DictService svc, CancellationToken ct) =>
{
    var items = await svc.ListProductName1sAsync(q, includeDeleted ?? false, limit, ct);
    return Results.Ok(new { count = items.Count, items });
}).WithName("AdminListProductName1s");
app.MapGet("/api/admin/dict/product-name1s/typeahead", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    ProductName1DictService svc, CancellationToken ct) =>
{
    var items = await svc.TypeaheadProductName1sAsync(q, limit, ct);
    return Results.Ok(new { count = items.Count, items });
}).WithName("AdminTypeaheadProductName1s");
app.MapPost("/api/admin/dict/product-name1s", async (
    ProductName1CreateRequest body, ProductName1DictService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.ProductName1)) return Results.BadRequest(new { error = "productName1 不能为空" });
    try { var item = await svc.CreateProductName1Async(body.ProductName1, body.SortOrder, ct);
        return Results.Created($"/api/admin/dict/product-name1s/{item.Id}", item); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminCreateProductName1");
app.MapPut("/api/admin/dict/product-name1s/{id:long}", async (
    long id, ProductName1UpdateRequest body, ProductName1DictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { return Results.Ok(await svc.UpdateProductName1Async(id, body.ProductName1, body.SortOrder, ct)); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminUpdateProductName1");
app.MapDelete("/api/admin/dict/product-name1s/{id:long}", async (
    long id, ProductName1DictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { await svc.DeleteProductName1Async(id, ct); return Results.Ok(new { id, deleted = true }); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminDeleteProductName1");
app.MapPost("/api/admin/dict/product-name1s/{id:long}/restore", async (
    long id, ProductName1DictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { return Results.Ok(await svc.RestoreProductName1Async(id, ct)); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminRestoreProductName1");
app.MapPost("/api/admin/dict/product-name1s/reorder", async (
    ProductName1ReorderRequest body, ProductName1DictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { await svc.ReorderProductName1sAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminReorderProductName1s");

// ---------- 字典 2: Product Name 2 ----------
app.MapGet("/api/admin/dict/product-name2s", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
    [Microsoft.AspNetCore.Mvc.FromQuery] bool? includeDeleted,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    ProductName2DictService svc, CancellationToken ct) =>
{
    var items = await svc.ListProductName2sAsync(q, includeDeleted ?? false, limit, ct);
    return Results.Ok(new { count = items.Count, items });
}).WithName("AdminListProductName2s");
app.MapGet("/api/admin/dict/product-name2s/typeahead", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    ProductName2DictService svc, CancellationToken ct) =>
{
    var items = await svc.TypeaheadProductName2sAsync(q, limit, ct);
    return Results.Ok(new { count = items.Count, items });
}).WithName("AdminTypeaheadProductName2s");
app.MapPost("/api/admin/dict/product-name2s", async (
    ProductName2CreateRequest body, ProductName2DictService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.ProductName2)) return Results.BadRequest(new { error = "productName2 不能为空" });
    try { var item = await svc.CreateProductName2Async(body.ProductName2, body.SortOrder, ct);
        return Results.Created($"/api/admin/dict/product-name2s/{item.Id}", item); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminCreateProductName2");
app.MapPut("/api/admin/dict/product-name2s/{id:long}", async (
    long id, ProductName2UpdateRequest body, ProductName2DictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { return Results.Ok(await svc.UpdateProductName2Async(id, body.ProductName2, body.SortOrder, ct)); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminUpdateProductName2");
app.MapDelete("/api/admin/dict/product-name2s/{id:long}", async (
    long id, ProductName2DictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { await svc.DeleteProductName2Async(id, ct); return Results.Ok(new { id, deleted = true }); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminDeleteProductName2");
app.MapPost("/api/admin/dict/product-name2s/{id:long}/restore", async (
    long id, ProductName2DictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { return Results.Ok(await svc.RestoreProductName2Async(id, ct)); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminRestoreProductName2");
app.MapPost("/api/admin/dict/product-name2s/reorder", async (
    ProductName2ReorderRequest body, ProductName2DictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { await svc.ReorderProductName2sAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminReorderProductName2s");

// ---------- 字典 3: Type (固定 5 值) ----------
app.MapGet("/api/admin/dict/types", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
    [Microsoft.AspNetCore.Mvc.FromQuery] bool? includeDeleted,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    TypeDictService svc, CancellationToken ct) =>
{
    var items = await svc.ListTypesAsync(q, includeDeleted ?? false, limit, ct);
    return Results.Ok(new { count = items.Count, items });
}).WithName("AdminListTypes");
app.MapGet("/api/admin/dict/types/typeahead", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    TypeDictService svc, CancellationToken ct) =>
{
    var items = await svc.TypeaheadTypesAsync(q, limit, ct);
    return Results.Ok(new { count = items.Count, items });
}).WithName("AdminTypeaheadTypes");
app.MapPost("/api/admin/dict/types", async (
    TypeCreateRequest body, TypeDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.Type)) return Results.BadRequest(new { error = "type 不能为空" });
    try { var item = await svc.CreateTypeAsync(body.Type, body.SortOrder, ct);
        return Results.Created($"/api/admin/dict/types/{item.Id}", item); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminCreateType");
app.MapPut("/api/admin/dict/types/{id:long}", async (
    long id, TypeUpdateRequest body, TypeDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { return Results.Ok(await svc.UpdateTypeAsync(id, body.Type, body.SortOrder, ct)); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminUpdateType");
app.MapDelete("/api/admin/dict/types/{id:long}", async (
    long id, TypeDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { await svc.DeleteTypeAsync(id, ct); return Results.Ok(new { id, deleted = true }); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminDeleteType");
app.MapPost("/api/admin/dict/types/{id:long}/restore", async (
    long id, TypeDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { return Results.Ok(await svc.RestoreTypeAsync(id, ct)); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminRestoreType");
app.MapPost("/api/admin/dict/types/reorder", async (
    TypeReorderRequest body, TypeDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { await svc.ReorderTypesAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminReorderTypes");

// ---------- 字典 4: OEM 3 ----------
app.MapGet("/api/admin/dict/oem-no3s", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
    [Microsoft.AspNetCore.Mvc.FromQuery] bool? includeDeleted,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    OemNo3DictService svc, CancellationToken ct) =>
{
    var items = await svc.ListOemNo3sAsync(q, includeDeleted ?? false, limit, ct);
    return Results.Ok(new { count = items.Count, items });
}).WithName("AdminListOemNo3s");
app.MapGet("/api/admin/dict/oem-no3s/typeahead", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    OemNo3DictService svc, CancellationToken ct) =>
{
    var items = await svc.TypeaheadOemNo3sAsync(q, limit, ct);
    return Results.Ok(new { count = items.Count, items });
}).WithName("AdminTypeaheadOemNo3s");
app.MapPost("/api/admin/dict/oem-no3s", async (
    OemNo3CreateRequest body, OemNo3DictService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.OemNo3)) return Results.BadRequest(new { error = "oemNo3 不能为空" });
    try { var item = await svc.CreateOemNo3Async(body.OemNo3, body.SortOrder, ct);
        return Results.Created($"/api/admin/dict/oem-no3s/{item.Id}", item); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminCreateOemNo3");
app.MapPut("/api/admin/dict/oem-no3s/{id:long}", async (
    long id, OemNo3UpdateRequest body, OemNo3DictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { return Results.Ok(await svc.UpdateOemNo3Async(id, body.OemNo3, body.SortOrder, ct)); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminUpdateOemNo3");
app.MapDelete("/api/admin/dict/oem-no3s/{id:long}", async (
    long id, OemNo3DictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { await svc.DeleteOemNo3Async(id, ct); return Results.Ok(new { id, deleted = true }); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminDeleteOemNo3");
app.MapPost("/api/admin/dict/oem-no3s/{id:long}/restore", async (
    long id, OemNo3DictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { return Results.Ok(await svc.RestoreOemNo3Async(id, ct)); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminRestoreOemNo3");
app.MapPost("/api/admin/dict/oem-no3s/reorder", async (
    OemNo3ReorderRequest body, OemNo3DictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { await svc.ReorderOemNo3sAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminReorderOemNo3s");

// ---------- 字典 5: Media (2 字段) ----------
app.MapGet("/api/admin/dict/medias", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
    [Microsoft.AspNetCore.Mvc.FromQuery] bool? includeDeleted,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    MediaDictService svc, CancellationToken ct) =>
{
    var items = await svc.ListMediasAsync(q, includeDeleted ?? false, limit, ct);
    return Results.Ok(new { count = items.Count, items });
}).WithName("AdminListMedias");
app.MapGet("/api/admin/dict/medias/typeahead", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    MediaDictService svc, CancellationToken ct) =>
{
    var items = await svc.TypeaheadMediasAsync(q, limit, ct);
    return Results.Ok(new { count = items.Count, items });
}).WithName("AdminTypeaheadMedias");
app.MapPost("/api/admin/dict/medias", async (
    MediaCreateRequest body, MediaDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.MediaName)) return Results.BadRequest(new { error = "mediaName 不能为空" });
    try { var item = await svc.CreateMediaAsync(body.MediaName, body.MediaModel, body.SortOrder, ct);
        return Results.Created($"/api/admin/dict/medias/{item.Id}", item); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminCreateMedia");
app.MapPut("/api/admin/dict/medias/{id:long}", async (
    long id, MediaUpdateRequest body, MediaDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { return Results.Ok(await svc.UpdateMediaAsync(id, body.MediaName, body.MediaModel, body.SortOrder, ct)); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminUpdateMedia");
app.MapDelete("/api/admin/dict/medias/{id:long}", async (
    long id, MediaDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { await svc.DeleteMediaAsync(id, ct); return Results.Ok(new { id, deleted = true }); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminDeleteMedia");
app.MapPost("/api/admin/dict/medias/{id:long}/restore", async (
    long id, MediaDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { return Results.Ok(await svc.RestoreMediaAsync(id, ct)); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminRestoreMedia");
app.MapPost("/api/admin/dict/medias/reorder", async (
    MediaReorderRequest body, MediaDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { await svc.ReorderMediasAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminReorderMedias");

// ---------- 字典 6: Machine (3 字段) ----------
app.MapGet("/api/admin/dict/machines", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
    [Microsoft.AspNetCore.Mvc.FromQuery] bool? includeDeleted,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    MachineDictService svc, CancellationToken ct) =>
{
    var items = await svc.ListMachinesAsync(q, includeDeleted ?? false, limit, ct);
    return Results.Ok(new { count = items.Count, items });
}).WithName("AdminListMachines");
app.MapGet("/api/admin/dict/machines/typeahead", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    MachineDictService svc, CancellationToken ct) =>
{
    var items = await svc.TypeaheadMachinesAsync(q, limit, ct);
    return Results.Ok(new { count = items.Count, items });
}).WithName("AdminTypeaheadMachines");
app.MapPost("/api/admin/dict/machines", async (
    MachineCreateRequest body, MachineDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.MachineBrand)) return Results.BadRequest(new { error = "machineBrand 不能为空" });
    try { var item = await svc.CreateMachineAsync(body.MachineBrand, body.MachineModel, body.MachineName, body.SortOrder, body.MachineCategory, ct);
        return Results.Created($"/api/admin/dict/machines/{item.Id}", item); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminCreateMachine");
app.MapPut("/api/admin/dict/machines/{id:long}", async (
    long id, MachineUpdateRequest body, MachineDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { return Results.Ok(await svc.UpdateMachineAsync(id, body.MachineBrand, body.MachineModel, body.MachineName, body.SortOrder, body.MachineCategory, ct)); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminUpdateMachine");
app.MapDelete("/api/admin/dict/machines/{id:long}", async (
    long id, MachineDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { await svc.DeleteMachineAsync(id, ct); return Results.Ok(new { id, deleted = true }); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminDeleteMachine");
app.MapPost("/api/admin/dict/machines/{id:long}/restore", async (
    long id, MachineDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { return Results.Ok(await svc.RestoreMachineAsync(id, ct)); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminRestoreMachine");
app.MapPost("/api/admin/dict/machines/reorder", async (
    MachineReorderRequest body, MachineDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { await svc.ReorderMachinesAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminReorderMachines");

// ---------- 字典 7: Engine (2 字段) ----------
app.MapGet("/api/admin/dict/engines", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
    [Microsoft.AspNetCore.Mvc.FromQuery] bool? includeDeleted,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    EngineDictService svc, CancellationToken ct) =>
{
    var items = await svc.ListEnginesAsync(q, includeDeleted ?? false, limit, ct);
    return Results.Ok(new { count = items.Count, items });
}).WithName("AdminListEngines");
app.MapGet("/api/admin/dict/engines/typeahead", async (
    [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    EngineDictService svc, CancellationToken ct) =>
{
    var items = await svc.TypeaheadEnginesAsync(q, limit, ct);
    return Results.Ok(new { count = items.Count, items });
}).WithName("AdminTypeaheadEngines");
app.MapPost("/api/admin/dict/engines", async (
    EngineCreateRequest body, EngineDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.EngineBrand)) return Results.BadRequest(new { error = "engineBrand 不能为空" });
    try { var item = await svc.CreateEngineAsync(body.EngineBrand, body.EngineType, body.SortOrder, ct);
        return Results.Created($"/api/admin/dict/engines/{item.Id}", item); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminCreateEngine");
app.MapPut("/api/admin/dict/engines/{id:long}", async (
    long id, EngineUpdateRequest body, EngineDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { return Results.Ok(await svc.UpdateEngineAsync(id, body.EngineBrand, body.EngineType, body.SortOrder, ct)); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminUpdateEngine");
app.MapDelete("/api/admin/dict/engines/{id:long}", async (
    long id, EngineDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { await svc.DeleteEngineAsync(id, ct); return Results.Ok(new { id, deleted = true }); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminDeleteEngine");
app.MapPost("/api/admin/dict/engines/{id:long}/restore", async (
    long id, EngineDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { return Results.Ok(await svc.RestoreEngineAsync(id, ct)); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminRestoreEngine");
app.MapPost("/api/admin/dict/engines/reorder", async (
    EngineReorderRequest body, EngineDictService svc, HttpContext ctx, CancellationToken ct) =>
{
    try { await svc.ReorderEnginesAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
    catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
    catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
}).WithName("AdminReorderEngines");

// ===== Day 11 P4.2: 字典 schema 契约端点 =====
//   反射 8 个 Dict* entity + XrefOemBrand, 返每个 entity 的字段名 + 类型 + 是否可空
//   前端 frontend/tests/contract/dict-schema.test.ts 用 zod 校验, 改字段不改前端 → CI fail
//   WHY reflection: 不写硬编码 DTO, 自动跟随 EF Core Migration 的字段变更
app.MapGet("/api/admin/dict/_schema", () =>
{
    // 8 个字典 (P1.3 OEM 品牌 + P2.2 7 个新字典)
    //   - XrefOemBrand 是 OEM 品牌, 历史命名 (Day 10), 不重命名为 DictOemBrand 避免大改
    //   - DictProductName1/2/Type/OemNo3 是单字段字典, 与 Day 10 XrefOemBrand 一致
    //   - DictMedia/Machine/Engine 是多字段字典 (主字段 + ExtraSearchProperties)
    var dictTypes = new[]
    {
        typeof(SakuraFilter.Core.Entities.XrefOemBrand),
        typeof(SakuraFilter.Core.Entities.DictProductName1),
        typeof(SakuraFilter.Core.Entities.DictProductName2),
        typeof(SakuraFilter.Core.Entities.DictType),
        typeof(SakuraFilter.Core.Entities.DictOemNo3),
        typeof(SakuraFilter.Core.Entities.DictMedia),
        typeof(SakuraFilter.Core.Entities.DictMachine),
        typeof(SakuraFilter.Core.Entities.DictEngine)
    };

    // WHY 用 record 而非 Dictionary: 客户端反序列化时字段名固定, 改结构不改客户端代码会编译报错
    //   Fields 顺序 = C# 编译顺序 (与 EF Core Migration 一致)
    var schema = dictTypes.Select(t => new
    {
        Entity = t.Name,
        Table = GetPgTableName(t),
        Fields = t.GetProperties()
            .Select(p => new
            {
                Name = p.Name,
                CSharpType = ToCSharpTypeName(p.PropertyType),
                Nullable = IsNullable(p),
                HasColumn = p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.ColumnAttribute), false).Any()
            })
            .ToArray()
    });

    return Results.Ok(new
    {
        generatedAt = DateTime.UtcNow.ToString("O"),
        count = dictTypes.Length,
        dictionaries = schema
    });
})
.WithName("AdminDictSchema");

// 本地函数: C# Type → 契约客户端期望的类型字符串
//   - DateTime → "datetime"
//   - DateTime? → "datetime?"
//   - string → "string"
//   - long/int/bool → "long"/"int"/"bool"
//   - decimal? → "decimal?"
static string ToCSharpTypeName(Type t)
{
    var underlying = Nullable.GetUnderlyingType(t);
    var baseName = underlying?.Name ?? t.Name;
    var nullable = underlying != null ? "?" : "";
    return baseName.ToLower() switch
    {
        "int32" => "int" + nullable,
        "int64" => "long" + nullable,
        "datetime" => "datetime" + nullable,
        "boolean" => "bool" + nullable,
        "decimal" => "decimal" + nullable,
        "double" => "double" + nullable,
        _ => baseName.ToLower() + nullable
    };
}

static bool IsNullable(System.Reflection.PropertyInfo p)
{
    // Reference type 默认可空 (string)
    if (!p.PropertyType.IsValueType) return true;
    // Nullable<T> = 可空
    return Nullable.GetUnderlyingType(p.PropertyType) != null;
}

static string GetPgTableName(Type t)
{
    // 提取 EF Core 表名 (P0.2 baseline: 字典表命名规则 dict_xxx)
    // 简化映射: 客户端已知命名约定
    return t.Name switch
    {
        "XrefOemBrand" => "xref_oem_brand",
        "DictProductName1" => "dict_product_name1",
        "DictProductName2" => "dict_product_name2",
        "DictType" => "dict_type",
        "DictOemNo3" => "dict_oem_no3",
        "DictMedia" => "dict_media",
        "DictMachine" => "dict_machine",
        "DictEngine" => "dict_engine",
        _ => t.Name.ToLower()
    };
}

app.Run();

// Day 9.2: dry-run JSON Schema 校验结果
//   显式 record 而非 tuple: 跨 lambda 调用 .MissingFields/.TypeMismatches (元组字段名跨方法不可见)
public record LineSchemaReport(
    int LineNo,
    Dictionary<string, string> Fields,
    List<string> MissingFields,
    List<string> TypeMismatches,
    string? Error);

// Day 11 改进 1: 统一 ETL 端点入口, EntityType 可选参数
//   - 不传或传 products: 走 /api/etl/import (默认, 兼容旧调用)
//   - 传 xrefs/apps: 路由到对应 Import*Async
//   - 旧端点 /import-xrefs /import-apps 保留 (向后兼容)
public record ImportRequest(string JsonlPath, string? Mode, string? EntityType, bool? Cascade);

// Day 8.2: 批量对比请求体
// Day 9.4: ETL 取消请求体, 携带取消原因写到 etl_progress_log
public record CancelRequest(string? Reason, string? ReasonCode);




public record CompareRequest(List<long> Ids);

// Day 7.5: 死信查询参数 (运维可见性)
// Day 7.10: 增加 recovery_count / last_recovery_at / last_recovery_error 字段
// Day 7.10.1: 增加 status / recovered_at / recovered_to_pending_id 字段
public record DeadLetterItem(long Id, long OriginalId, string Operation, int RetryCount,
    string? LastError, DateTime CreatedAt, DateTime MovedAt, string PayloadPreview,
    int RecoveryCount, DateTime? LastRecoveryAt, string? LastRecoveryError,
    string Status, DateTime? RecoveredAt, long? RecoveredToPendingId);

// Day 10: OEM 品牌字典请求体 (P1.3)
public record OemBrandCreateRequest(string Brand, int? SortOrder);
public record OemBrandUpdateRequest(string? Brand, int? SortOrder);

// P5.5: 前端性能埋点批量上报 DTO
//   ts: 客户端时间戳 (ISO8601 string, 简化绑定, 后端不强制解析)
//   字段全部 nullable + 服务层 ?? 兜底 (与 AdminProductSearchRequest 一致)
public record FrontendPerfSample(string? Path, string? Method, int? StatusCode, double? DurationMs, string? Ts);
public record FrontendPerfBatch(List<FrontendPerfSample>? Samples);
