using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SakuraFilter.Core.Interfaces;
using SakuraFilter.Infrastructure.Data;
using SakuraFilter.Api.Services;
using Xunit;

namespace SakuraFilter.Api.Tests.Integration;

/// <summary>
/// V24-F81 (spec 26.17.2 P1-3): PG 集成测试基类
///
/// 设计目标:
///   提供真实的 PostgreSQL 连接 (非 EF Core InMemory), 用于验证依赖 PG 特性的代码路径:
///     - advisory lock (pg_try_advisory_xact_lock)
///     - FOR UPDATE SKIP LOCKED
///     - 23505 唯一约束冲突
///     - xmin 乐观锁令牌
///     - raw SQL / CTE / LATERAL JOIN
///
/// 为什么不用 Testcontainers:
///   - Testcontainers 需要本地 Docker 守护进程, 团队成员环境不一
///   - 项目已有本地 PG 实例 (spike_test_v3), 复用更轻量
///   - 通过环境变量 PG_TEST_CONNECTION_STRING 注入连接串, CI 中可用 service container 启动 PG
///
/// 测试隔离策略:
///   - 每个测试前调用 ResetDatabaseAsync, 用 TRUNCATE ... RESTART IDENTITY CASCADE 清空所有业务表
///   - 标记 [Collection("PgSequential")] 串行执行, 避免并发污染
///   - 标记 [Trait("Category", "Integration")], 单元测试基线 dotnet test --filter Category!=Integration 排除
///
/// 启用方式:
///   - 设置环境变量 PG_TEST_CONNECTION_STRING (默认读 .env 中的 ConnectionStrings__Postgres)
///   - 未设置且 .env 不存在时, 所有继承此基类的测试 Skip
///
/// 关联 spec: 26.17.2 P1-3, 26.17.2 P1-4
/// </summary>
public abstract class PgIntegrationTestBase : IAsyncLifetime
{
    private readonly string? _connectionString;
    private readonly bool _isEnabled;

    protected PgIntegrationTestBase()
    {
        // 优先级: 环境变量 PG_TEST_CONNECTION_STRING > .env 中的 ConnectionStrings__Postgres
        _connectionString = Environment.GetEnvironmentVariable("PG_TEST_CONNECTION_STRING");
        if (string.IsNullOrEmpty(_connectionString))
        {
            _connectionString = LoadConnectionStringFromEnvFile();
        }
        _isEnabled = !string.IsNullOrEmpty(_connectionString);
    }

    /// <summary>测试是否启用 (连接串已配置). false 时测试应 Skip</summary>
    protected bool IsEnabled => _isEnabled;

    /// <summary>当前测试使用的 PG 连接串</summary>
    protected string ConnectionString => _connectionString ?? throw new InvalidOperationException("PG_TEST_CONNECTION_STRING 未配置");

    /// <summary>从 .env 文件加载连接串 (本地开发兜底)</summary>
    private static string? LoadConnectionStringFromEnvFile()
    {
        // WHY 简易解析: 避免引入 DotNetEnv 包, 仅读取 .env 中的 PG_TEST_CONNECTION_STRING
        //   查找路径: 当前工作目录向上查找 .env, 最多 8 层
        //   优先级: PG_TEST_CONNECTION_STRING (测试专用) > ConnectionStrings__Postgres (后端服务用)
        //   建议: 本地开发在 .env 中添加 PG_TEST_CONNECTION_STRING 指向 sakurafilter_int_tests 库
        //         避免污染 spike_test_v3 开发库
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var envPath = Path.Combine(dir, ".env");
            if (File.Exists(envPath))
            {
                var lines = File.ReadAllLines(envPath);
                string? testConn = null, backendConn = null;
                foreach (var line in lines)
                {
                    if (line.StartsWith("PG_TEST_CONNECTION_STRING=", StringComparison.OrdinalIgnoreCase))
                        testConn = line.Substring("PG_TEST_CONNECTION_STRING=".Length).Trim();
                    else if (line.StartsWith("ConnectionStrings__Postgres=", StringComparison.OrdinalIgnoreCase))
                        backendConn = line.Substring("ConnectionStrings__Postgres=".Length).Trim();
                }
                // 测试专用连接串优先; 没有则 fallback 到后端连接串 (但会污染开发库, 仅本地调试用)
                return testConn ?? backendConn;
            }
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return null;
    }

    /// <summary>创建连接到测试库的 ProductDbContext</summary>
    protected ProductDbContext CreateDbContext()
    {
        if (!_isEnabled) throw new InvalidOperationException("PG 集成测试未启用 (PG_TEST_CONNECTION_STRING 未配置)");
        // WHY 不显式调用 UseSnakeCaseNamingConvention: ProductDbContext.OnConfiguring 已自动调用
        var options = new DbContextOptionsBuilder<ProductDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new ProductDbContext(options);
    }

    /// <summary>创建真实的 CursorHmac (V2 双签名 + Base64Url)</summary>
    protected static CursorHmac CreateCursorHmac()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Search:CursorHmacKey"] = "pg-integration-test-cursor-hmac-key-32+"
            })
            .Build();
        return new CursorHmac(config, NullLogger<CursorHmac>.Instance);
    }

    /// <summary>创建 AdminProductService SUT (连接真实 PG)</summary>
    protected AdminProductService CreateAdminProductService(ProductDbContext? db = null, IObjectStorage? storage = null)
    {
        db ??= CreateDbContext();
        return new AdminProductService(
            db,
            NullLogger<AdminProductService>.Instance,
            CreateCursorHmac(),
            storage);
    }

    /// <summary>
    /// 测试前重置数据库: TRUNCATE 所有业务表 RESTART IDENTITY CASCADE
    /// WHY: CASCADE 自动级联清外键依赖, RESTART IDENTITY 重置自增序列
    /// </summary>
    protected async Task ResetDatabaseAsync()
    {
        if (!_isEnabled) return;
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // V24-F81: 列出所有业务表 (不含 __EFMigrationsHistory, 不含 partition6_placeholder 等占位空表)
        //   WHY 显式列举: 避免动态查询 pg_tables 时漏表或包含系统表
        //   顺序: 先清子表 (有外键依赖), 再清主表; CASCADE 已能处理顺序, 显式列举便于审计
        cmd.CommandText = @"
TRUNCATE TABLE
    product_images,
    product_history,
    cross_references,
    machine_applications,
    search_index_dead_letter,
    search_index_pending,
    etl_progress_log,
    refresh_tokens,
    login_audit_logs,
    alert_history,
    security_events,
    products,
    users,
    alert_rules,
    xref_oem_brand,
    dict_product_name1,
    dict_product_name2,
    dict_type,
    dict_oem_no3,
    dict_media,
    dict_machine,
    dict_engine,
    system_settings
RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task InitializeAsync()
    {
        if (!_isEnabled) return;
        await ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        // WHY 不在 Dispose 中清表: 同一 Collection 内下一个测试的 InitializeAsync 会清
        //   避免最后一次清表浪费 IO (CI 节省几秒)
        return Task.CompletedTask;
    }
}

/// <summary>
/// xUnit Collection 定义: PG 集成测试串行执行
/// WHY: 多个集成测试并发会共享同一 PG 实例, advisory lock / 23505 / TRUNCATE 会互相干扰
/// </summary>
[CollectionDefinition("PgSequential")]
public class PgSequentialCollection { }
