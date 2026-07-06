using SakuraFilter.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 服务注册（按职责拆分到 ServiceCollectionExtensions）
builder.Services.AddSakuraFilterServices(builder.Configuration, builder.Environment);

var app = builder.Build();

// 启动初始化：数据库迁移 / 默认用户 seed / 跨实例广播器 / 搜索探活
await app.InitializeDatabaseAsync();
await app.SeedDefaultUsersAsync();
app.InitEtlBroadcaster();
app.InitAuthTokenStore();

// 中间件管道（按顺序拆分到 MiddlewarePipelineExtensions）
app.UseSakuraFilterMiddleware(builder.Configuration, app.Environment);

// 路由端点（按功能模块拆分到 Endpoints/ 目录）
app.MapSakuraFilterEndpoints();

// 启动后探活 Meili（按需降级）
await app.InitializeSearchAsync();

app.Run();
