using SakuraFilter.Api.Extensions;
using SakuraFilter.Core.DTOs;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Infrastructure.Data;
using Npgsql;
using System.Text.Json;

// 🔧 fix(审查): 兼容历史/新库列类型差异 (timestamp vs timestamptz) 与实体 DateTime Kind
//   WHY: Npgsql 8 严格 Kind 检查 — timestamptz 拒绝 Kind=Unspecified, timestamp 拒绝 Kind=UTC;
//        项目列类型各地不一致 (CI 新库 EF 迁移 timestamptz, 本地/生产旧库 SQL 迁移 timestamp,
//        实体统一 DateTime.UtcNow) → Day 10 create 500 "Cannot write DateTime with Kind" (CI 实测)
//   Fix: 启用 Npgsql legacy 行为 (关闭严格 Kind 校验, 统一按本地时间语义转换), 必须在使用 Npgsql 前设置
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// 生产部署一次性迁移模式: dotnet SakuraFilter.Api.dll --migrate-db
//   WHY: prod 配置 Db:AutoMigrateOnStartup=false (避免多实例并发迁移),
//        部署编排 (docker compose db-init 服务) 显式执行 EF 迁移后再启动 API
//        与 CI 模式 (EnsureCreated + SQL 脚本) 等价, 更规范 (含迁移历史表)
if (args.Contains("--migrate-db"))
{
    var migrateConn = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings__Postgres 未配置 (--migrate-db 模式)");
    var migrateOpts = new DbContextOptionsBuilder<ProductDbContext>()
        .UseNpgsql(migrateConn)
        .Options;
    using var migrateDb = new ProductDbContext(migrateOpts);
    await migrateDb.Database.MigrateAsync();
    Console.WriteLine("EF 迁移完成 (--migrate-db)");
    return;
}

var builder = WebApplication.CreateBuilder(args);

// 🔧 fix(审查): 存储配置 DB 覆盖 (运维中心"存储配置"页保存后重启生效)
//   读 system_settings.storage_config → 追加 in-memory 配置 (优先级高于环境变量)
await ApplyStorageConfigOverridesAsync(builder);

// 服务注册（按职责拆分到 ServiceCollectionExtensions）
//   RateLimit 策略 (global/search/etl/auth/public/sitemap) 已在 AddSakuraFilterServices 内统一注册
builder.Services.AddSakuraFilterServices(builder.Configuration, builder.Environment);

// Task 0.7.1: 注册 Razor Pages 服务（与 AddControllers 协同, 支持 P3.2 等 MVC/Razor 页面路由）
builder.Services.AddRazorPages();

var app = builder.Build();

// 启动初始化：数据库迁移 / 默认用户 seed / 跨实例广播器 / 搜索探活
await app.InitializeDatabaseAsync();
await app.SeedDefaultUsersAsync();
app.InitEtlBroadcaster();
app.InitAuthTokenStore();

// 中间件管道（按顺序拆分到 MiddlewarePipelineExtensions）
// 注意: UseRateLimiter 已在 UseSakuraFilterMiddleware 内部第 6 步条件性调用 (基于 RateLimit:Enabled 配置),
//       此处无需重复添加 app.UseRateLimiter(), 避免破坏开发环境的限流开关
app.UseSakuraFilterMiddleware(builder.Configuration, app.Environment);

// 路由端点（按功能模块拆分到 Endpoints/ 目录）
app.MapSakuraFilterEndpoints();

// Task 0.7.2: Razor Pages 端点映射 (通常在 MapControllers 之后)
app.MapRazorPages();

// 启动后探活 Meili（按需降级）
await app.InitializeSearchAsync();

app.Run();

// 🔧 fix(审查): 存储配置 DB 覆盖 (运维中心"存储配置"页保存后重启生效)
//   读 system_settings.storage_config → 追加 in-memory 配置 (优先级高于环境变量)
//   WHY: 存储客户端是单例, 无法热切换; 保存后重启容器即按新配置启动
static async Task ApplyStorageConfigOverridesAsync(WebApplicationBuilder builder)
{
    var connStr = builder.Configuration.GetConnectionString("Postgres")
        ?? builder.Configuration["ConnectionStrings__Postgres"];
    if (string.IsNullOrEmpty(connStr)) return;
    try
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT value FROM system_settings WHERE key = 'storage_config' LIMIT 1", conn);
        var raw = await cmd.ExecuteScalarAsync() as string;
        if (string.IsNullOrEmpty(raw)) return;
        var cfg = JsonSerializer.Deserialize<StorageConfigDto>(raw);
        if (cfg == null || string.IsNullOrEmpty(cfg.Provider)) return;
        var kv = new Dictionary<string, string?>
        {
            ["Storage:Provider"] = cfg.Provider,
            ["Minio:Endpoint"] = cfg.Minio?.Endpoint,
            ["Minio:AccessKey"] = cfg.Minio?.AccessKey,
            ["Minio:SecretKey"] = cfg.Minio?.SecretKey,
            ["Minio:BucketName"] = cfg.Minio?.BucketName,
            ["Minio:PublicEndpoint"] = cfg.Minio?.PublicEndpoint,
            ["R2:Endpoint"] = cfg.R2?.Endpoint,
            ["R2:AccessKeyId"] = cfg.R2?.AccessKeyId,
            ["R2:AccessKeySecret"] = cfg.R2?.AccessKeySecret,
            ["R2:BucketName"] = cfg.R2?.BucketName,
            ["R2:PublicEndpoint"] = cfg.R2?.PublicEndpoint,
            ["Aliyun:Endpoint"] = cfg.Aliyun?.Endpoint,
            ["Aliyun:AccessKeyId"] = cfg.Aliyun?.AccessKeyId,
            ["Aliyun:AccessKeySecret"] = cfg.Aliyun?.AccessKeySecret,
            ["Aliyun:BucketName"] = cfg.Aliyun?.BucketName,
            ["Aliyun:PublicEndpoint"] = cfg.Aliyun?.PublicEndpoint,
            ["Aliyun:CdnEndpoint"] = cfg.Aliyun?.CdnEndpoint,
        };
        builder.Configuration.AddInMemoryCollection(kv.Where(p => p.Value != null)!);
        Console.WriteLine($"[Storage] 已应用 system_settings 存储配置覆盖: Provider={cfg.Provider}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Storage] storage_config 覆盖失败(忽略, 用环境变量): {ex.Message}");
    }
}
