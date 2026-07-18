using Microsoft.EntityFrameworkCore;
using SakuraFilter.Api.Services;
using SakuraFilter.Etl;
using SakuraFilter.Infrastructure.Data;
using SakuraFilter.Search;

namespace SakuraFilter.Api.Extensions;

/// <summary>
/// 应用启动初始化扩展：
/// 1) 数据库迁移（按 Db:AutoMigrateOnStartup 开关）
/// 2) 默认用户 seed
/// 3) ETL 跨实例广播器初始化
/// 4) AuthTokenStore 初始化
/// 5) 搜索服务（Meili 探活 + 降级初始化）
/// </summary>
public static class StartupExtensions
{
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        var autoMigrate = app.Configuration.GetValue<bool>("Db:AutoMigrateOnStartup");
        if (!autoMigrate)
        {
            app.Logger.LogWarning("Db:AutoMigrateOnStartup=false,跳过自动迁移 (生产配置,需手动执行 dotnet ef database update)");
            return;
        }

        using var migrateScope = app.Services.CreateScope();
        var db = migrateScope.ServiceProvider.GetRequiredService<ProductDbContext>();
        db.Database.SetCommandTimeout(60);
        var migrateLogger = migrateScope.ServiceProvider.GetRequiredService<ILogger<StartupMarker>>();

        await EnsureEfmigrationsHistorySeededAsync(db, migrateLogger);

        var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
        if (pendingMigrations.Any())
        {
            migrateLogger.LogInformation("检测到 {Count} 个 pending migrations: {Migrations}",
                pendingMigrations.Count(), string.Join(", ", pendingMigrations));
            await db.Database.MigrateAsync();
            migrateLogger.LogInformation("迁移完成");
        }
        else
        {
            migrateLogger.LogInformation("无 pending migrations,跳过迁移");
        }
    }

    public static async Task SeedDefaultUsersAsync(this WebApplication app)
    {
        using var seedScope = app.Services.CreateScope();
        var userService = seedScope.ServiceProvider.GetRequiredService<UserService>();
        var seedLogger = seedScope.ServiceProvider.GetRequiredService<ILogger<StartupMarker>>();
        try
        {
            await userService.SeedDefaultUsersAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            seedLogger.LogError(ex, "默认用户 seed 失败, 不阻塞启动 (后续可手动创建)");
        }
    }

    public static void InitEtlBroadcaster(this WebApplication app)
    {
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
    }

    public static void InitAuthTokenStore(this WebApplication app)
    {
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
    }

    public static async Task InitializeSearchAsync(this WebApplication app)
    {
        // P3-6.2: 启动后探活 Meili, 不可用则立即降级 (避免首次搜索等 1s 超时)
        using var initScope = app.Services.CreateScope();
        var search = initScope.ServiceProvider.GetRequiredService<ISearchProvider>();
        if (search is ResilientSearchProvider rsp)
        {
            var meiliOk = await rsp.IsPrimaryHealthyAsync();
            rsp.Initialize(meiliOk);

            // V2 Task V17-2.3: Meili 可用时,后台异步执行 InitializeAsync 配置 schema
            //   WHY 后台执行: schema 配置含 3 次 WaitForTaskAsync (每次最多 30s),同步执行会阻塞启动
            //   独立 scope: 后台任务不持有 initScope (已 Dispose),需自建 scope 取 MeiliSearchProvider
            //   CancellationToken.None: 后台任务不随启动取消 (启动完成后仍可继续配置)
            //   try-catch: 失败时 LogWarning,不抛异常 (Meili schema 未配置仅影响 filter,搜索仍可工作)
            if (meiliOk)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var bgScope = app.Services.CreateScope();
                        var meili = bgScope.ServiceProvider.GetRequiredService<MeiliSearchProvider>();
                        await meili.InitializeAsync(CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        var bgLogger = app.Services.GetService<ILogger<StartupMarker>>();
                        bgLogger?.LogWarning(ex, "Meili InitializeAsync 后台执行失败 (搜索 filter 可能降级)");
                    }
                });
            }
        }
    }

    private static async Task EnsureEfmigrationsHistorySeededAsync(ProductDbContext db, ILogger logger)
    {
        try
        {
            // V24-F14: 三个 bug 一起修复
            //   bug 1 (exists 判断): ExecuteSqlRawAsync 对 SELECT 语句返回 -1 (非行数),
            //     导致 exists = -1 > 0 = false, 误判"表不存在", 每次启动都触发 CREATE+INSERT
            //     修复: 改用 SqlQueryRaw<int> 执行 COUNT 查询 (需 AS "Value" 列别名匹配 EF 约定)
            //   bug 2 (列名): CREATE TABLE + INSERT 用 PascalCase ("MigrationId"/"ProductVersion"),
            //     但 ProductDbContext 启用 UseSnakeCaseNamingConvention, EF 期望 snake_case 列名,
            //     INSERT 时报 42703 (字段不存在)
            //     修复: 列名改为 snake_case (migration_id/product_version)
            //   bug 3 (SqlQueryRaw 列名约定): SqlQueryRaw<int> 默认期望列名 "Value",
            //     不加 AS "Value" 会报 42703 (字段 t.Value 不存在)
            //     修复: SQL 加 AS "Value" 列别名
            var count = await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory'"
            ).FirstOrDefaultAsync();
            if (count == 0)
            {
                logger.LogInformation("__EFMigrationsHistory 表不存在,创建并标记 InitialCreate 为已应用 (老环境兼容)");
                await db.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
                        ""migration_id"" character varying(150) NOT NULL,
                        ""product_version"" character varying(32) NOT NULL,
                        CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY (""migration_id"")
                    );");
                await db.Database.ExecuteSqlRawAsync(@"
                    INSERT INTO ""__EFMigrationsHistory"" (""migration_id"", ""product_version"")
                    VALUES ('InitialCreate', '8.0.0')
                    ON CONFLICT DO NOTHING;");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "EnsureEfmigrationsHistorySeededAsync 失败,继续执行迁移 (EF 会自行处理)");
        }
    }
}

/// <summary>
/// 类型 marker: 用于 ILogger&lt;T&gt; 泛型实参（避免与同名静态类冲突）。
/// </summary>
internal sealed class StartupMarker
{
}
