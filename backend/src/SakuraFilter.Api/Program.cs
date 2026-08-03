using SakuraFilter.Api.Extensions;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Infrastructure.Data;

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
