using System.Net;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SakuraFilter.Api.Endpoints;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;
using Xunit;

namespace SakuraFilter.Api.Tests.Endpoints;

/// <summary>
/// Task 1: 机型三级树查询端点测试
///
/// 测试策略 (参考 BaseDictServiceTests 模式):
///   - 用例 1/2: 直接 Service 测试 (EF Core InMemory), 验证 GetTreeAsync 三级树聚合逻辑 + 空数据
///   - 用例 3: TestHost HTTP 级别测试, 验证未授权请求返回 401 (RequireAuthorization("Admin") 生效)
///
/// WHY 不用 WebApplicationFactory&lt;Program&gt;:
///   - Program.cs 含 DB 迁移/ETL 广播/搜索探活等启动逻辑, 需真实 PG + Meili, 测试环境不满足
///   - TestHost 构建最小管道 (auth + endpoint), 足够验证 401 鉴权行为
///   - 401 由 ASP.NET Core 授权中间件在 handler 之前短路, 无需真实 MachineDictService
/// </summary>
public class AdminMachineTreeEndpointsTests
{
    // ========== InMemory 测试基础设施 (复用 BaseDictServiceTests 模式) ==========

    private sealed class TestProductDbContext : ProductDbContext
    {
        public TestProductDbContext(DbContextOptions<ProductDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            // WHY 忽略 Alert* 实体: InMemory 不支持其复杂配置, 与 BaseDictServiceTests 一致
            mb.Ignore<AlertRule>();
            mb.Ignore<AlertHistory>();
            mb.Ignore<SecurityEvent>();
        }
    }

    private static ProductDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ProductDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new TestProductDbContext(options);
    }

    private static MachineDictService CreateSut(ProductDbContext db)
    {
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        return new MachineDictService(db, NullLogger<MachineDictService>.Instance, cache);
    }

    private static DictMachine Machine(long id, string brand, string? model, string? name,
        string category = "others", DateTime? deletedAt = null)
        => new()
        {
            Id = id,
            MachineBrand = brand,
            MachineModel = model,
            MachineName = name,
            MachineCategory = category,
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            DeletedAt = deletedAt
        };

    // ==================== 用例 1: 查询成功 — 有数据时返回三级树 ====================

    // 覆盖: 三级树查询成功场景 (category → brand → model 分组聚合 + 字母序排序)
    [Fact]
    public async Task GetTreeAsync_WithData_ReturnsThreeLevelTree()
    {
        await using var db = CreateInMemoryDb();
        // 准备: 2 个 category, 每个 category 下 2 个 brand, brand 下 1-2 个 model
        //   WHY 打乱插入顺序: 验证 OrderBy 排序生效 (非依赖插入顺序)
        db.DictMachines.Add(Machine(1, "Caterpillar", "320D", "Excavator", "Construction"));
        db.DictMachines.Add(Machine(2, "BOBCAT", "S130", "Skid Steer", "Construction"));
        db.DictMachines.Add(Machine(3, "John Deere", "5050E", "Tractor", "Agriculture"));
        // WHY model=null: 验证 fallback 到 machine_name "Excavator" 的逻辑 (GetTreeAsync: m.MachineModel ?? m.MachineName ?? "")
        db.DictMachines.Add(Machine(4, "Caterpillar", null, "Excavator", "Construction"));
        db.DictMachines.Add(Machine(5, "John Deere", "5075E", "Tractor", "Agriculture"));
        db.DictMachines.Add(Machine(6, "Komatsu", "PC200", "Excavator", "Construction", deletedAt: DateTime.UtcNow));  // 已软删, 不应出现
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetTreeAsync();

        // 验证一级 (category): Agriculture < Construction (字母序)
        result.Should().HaveCount(2);
        result[0].Category.Should().Be("Agriculture");
        result[1].Category.Should().Be("Construction");

        // 验证 Agriculture 下 1 个 brand (John Deere), 2 个 model (5050E < 5075E)
        var agri = result[0];
        agri.Brands.Should().HaveCount(1);
        agri.Brands[0].Brand.Should().Be("John Deere");
        agri.Brands[0].Models.Should().HaveCount(2);
        agri.Brands[0].Models[0].ModelName.Should().Be("5050E");
        agri.Brands[0].Models[1].ModelName.Should().Be("5075E");

        // 验证 Construction 下 2 个 brand (BOBCAT < Caterpillar, 字母序), 软删行已排除
        var constr = result[1];
        constr.Brands.Should().HaveCount(2);
        constr.Brands[0].Brand.Should().Be("BOBCAT");
        constr.Brands[1].Brand.Should().Be("Caterpillar");

        // 验证 Caterpillar 下 2 个 model (model=null fallback 到 machine_name "Excavator")
        var cat = constr.Brands[1];
        cat.Models.Should().HaveCount(2);
        cat.Models.Select(m => m.ModelName).Should().BeInAscendingOrder();
        cat.Models.Should().Contain(m => m.ModelName == "320D" && m.MachineId == 1);
        // model=null 的行 fallback 到 machine_name
        cat.Models.Should().Contain(m => m.ModelName == "Excavator" && m.MachineId == 4);

        // 验证软删行 (id=6 Komatsu) 不在结果中
        result.Should().NotContain(n => n.Category == "Construction"
            && n.Brands.Any(b => b.Brand == "Komatsu"));
    }

    // ==================== 用例 2: 空数据返回空数组 ====================

    // 覆盖: 空数据返回空数组 (无数据时返回 200 + [])
    [Fact]
    public async Task GetTreeAsync_NoData_ReturnsEmptyList()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var result = await sut.GetTreeAsync();

        // 验证: 返回空 List, 不返回 null (前端可直接 v-for 渲染)
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    // ==================== 用例 3: 未授权 401 ====================

    // 覆盖: 未授权 401 — 不带 token 返回 401 (RequireAuthorization("Admin") 生效)
    [Fact]
    public async Task MachineTreeEndpoint_WithoutAuth_Returns401()
    {
        // 构建 TestHost 最小管道: auth + authz + 真实端点映射
        //   WHY 不走 Program.cs: 启动逻辑需真实 PG/Meili, TestHost 最小管道足够验证 401
        //   401 由授权中间件在 endpoint handler 之前短路, MachineDictService 不会被解析
        // 注: IHost 不实现 IAsyncDisposable (仅 IDisposable), 用同步 using
        //   WHY 不用 await using: .NET 8 IHost 未实现 IAsyncDisposable, 编译错误 CS8417
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    // WHY 必须加 AddRouting: UseRouting 要求该服务, 否则启动抛 InvalidOperationException
                    services.AddRouting();
                    // 注册 auth: NoopAuthHandler 永不认证 → 请求始终匿名
                    services.AddAuthentication("Noop")
                        .AddScheme<AuthenticationSchemeOptions, NoopAuthHandler>("Noop", _ => { });
                    // 注册 authz: Admin 策略要求已认证用户 (匿名请求 → 401)
                    services.AddAuthorization(o => o.AddPolicy("Admin", p => p.RequireAuthenticatedUser()));
                    // WHY 注册 MachineDictService 依赖: 路由元数据推断阶段要求端点参数类型已注册为服务,
                    //   否则尝试从 body 推断 (GET 请求禁止 body → 启动抛 InvalidOperationException).
                    //   401 短路后 handler 不会被调用, 依赖仅为元数据推断占位, 无需真实可用
                    services.AddDbContext<ProductDbContext>(o => o.UseInMemoryDatabase("machine_tree_401_test"));
                    services.AddMemoryCache();
                    services.AddLogging();
                    services.AddScoped<MachineDictService>();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    // 映射真实端点 (handler 不会被调用, 因 auth 短路返回 401)
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapAdminMachineTreeEndpoints();
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // 不带 Authorization header 发起请求
        var response = await client.GetAsync("/api/admin/machine-tree");

        // 验证: 匿名请求被授权中间件拦截, 返回 401 Unauthorized
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// 测试用 Noop 认证 Handler: 永不认证任何请求 (返回 NoResult = 匿名)
    /// WHY: 不引入 JwtBearer 配置复杂性, 仅需触发 401 鉴权路径
    /// </summary>
    private sealed class NoopAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public NoopAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }
}
