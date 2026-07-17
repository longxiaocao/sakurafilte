using SakuraFilter.Api.Extensions;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// 服务注册（按职责拆分到 ServiceCollectionExtensions）
builder.Services.AddSakuraFilterServices(builder.Configuration, builder.Environment);

// Task 0.7.1: 注册 Razor Pages 服务（与 AddControllers 协同, 支持 P3.2 等 MVC/Razor 页面路由）
builder.Services.AddRazorPages();

// Task 0.7.3: 限流 "public" 策略 - 公开接口 120/min, 基于 RemoteIpAddress 分区
// 说明: 与 AddSakuraFilterServices 中已注册的 global/search/etl/auth policy 共存
//       (AddRateLimiter 多次调用安全追加 policy, OnRejected 处理器沿用首次注册的全局行为)
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("public", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1)
            }));
});

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
