using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Infrastructure.Data;
using SakuraFilter.Search;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// v30-12: ResilientSearchProvider 单元测试 (方案 C: 不 mock, 用真实 provider + EF Core InMemory)
///
/// 测试目标 (覆盖: 熔断状态管理 + 健康检查 + 降级逻辑):
///   - Initialize: 启动时标记降级状态 (避免首次搜索等 1s 超时)
///   - IsCircuitBreakerOpen: 暴露熔断状态 (1=open/failing, 0=closed/healthy)
///   - IsPrimaryHealthyAsync: 单独探活 Meili (不重试不触发熔断)
///   - IsFallbackHealthyAsync: 单独探活 PG (InMemory CanConnect=false, 验证非 relational 场景兜底)
///   - HealthCheckAsync: 主备任一可用即健康
///
/// WHY 不 mock MeiliSearchProvider/PostgresSearchProvider:
///   1. 两者方法非 virtual, Moq 无法直接拦截 (callBase=true 仍调真实实现)
///   2. 改 virtual 侵入生产代码, 可能被意外 override
///   3. 用真实 provider + EF Core InMemory: 避免引入 SQLite 新依赖 (规则 6.3)
///
/// WHY 用 EF Core InMemory 而非 SQLite InMemory:
///   EF Core InMemory 是非 relational provider, Database.CanConnectAsync() 返回 false
///   SQLite InMemory 是 relational provider, CanConnectAsync() 返回 true
///   本测试刻意接受 InMemory 的 false 行为, 验证"主备都不可用"的兜底场景
///   (生产中 PG 真实可用时 CanConnect=true, 走集成测试覆盖, 不在单测范围)
///
/// 未覆盖 (走集成测试, 需真实 PG+Meili):
///   - SearchAsync 熔断分支 (需 mock 或真实 Meili 故障注入)
///   - IndexAsync/DeleteAsync 双写补偿
///   - PG 真实可用的 HealthCheckAsync=true 路径
/// </summary>
public class ResilientSearchProviderTests
{
    private static ProductDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ProductDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ProductDbContext(options);
    }

    private static MeiliSearchProvider CreateMeiliProvider(string endpoint = "http://invalid-meili-localhost:9999")
    {
        // 用无效 endpoint 创建, HealthCheckAsync catch 后返回 false (不抛异常)
        var opts = Options.Create(new MeiliSearchOptions
        {
            Endpoint = endpoint,
            ApiKey = null,
            IndexName = "products",
            WriteTargets = new List<string> { "products" },
            TimeoutMs = 100  // 短超时, 加速测试
        });
        return new MeiliSearchProvider(opts, NullLogger<MeiliSearchProvider>.Instance, CreateInMemoryDb());
    }

    private static PostgresSearchProvider CreatePgProvider()
    {
        // EF Core InMemory: Database.CanConnectAsync() 返回 false (非 relational provider)
        return new PostgresSearchProvider(CreateInMemoryDb(), NullLogger<PostgresSearchProvider>.Instance);
    }

    private static ResilientSearchProvider CreateResilient(
        MeiliSearchProvider? primary = null,
        PostgresSearchProvider? fallback = null)
    {
        return new ResilientSearchProvider(
            primary ?? CreateMeiliProvider(),
            fallback ?? CreatePgProvider(),
            NullLogger<ResilientSearchProvider>.Instance);
    }

    // ===== Initialize 测试 =====

    [Fact]
    public void Initialize_Default_Keeps_Primary_Available()
    {
        // 覆盖: Initialize(true) 不改变默认状态 (IsCircuitBreakerOpen = false)
        var provider = CreateResilient();
        provider.Initialize(primaryAvailable: true);
        provider.IsCircuitBreakerOpen.Should().BeFalse();
    }

    [Fact]
    public void Initialize_False_Marks_Primary_Unavailable()
    {
        // 覆盖: Initialize(false) 立即标记降级 (避免首次搜索等 1s 超时)
        //   场景: 启动探活发现 Meili 不可用, 立即降级到 PG 兜底
        var provider = CreateResilient();
        provider.Initialize(primaryAvailable: false);
        provider.IsCircuitBreakerOpen.Should().BeTrue();
    }

    // ===== IsCircuitBreakerOpen 测试 =====

    [Fact]
    public void IsCircuitBreakerOpen_Default_False()
    {
        // 覆盖: 默认熔断关闭 (primary 可用)
        var provider = CreateResilient();
        provider.IsCircuitBreakerOpen.Should().BeFalse();
    }

    // ===== IsPrimaryHealthyAsync 测试 =====

    [Fact]
    public async Task IsPrimaryHealthyAsync_Invalid_Endpoint_Returns_False()
    {
        // 覆盖: Meili 不可达时 IsPrimaryHealthyAsync 返回 false (不抛异常)
        //   WHY 不抛: IsPrimaryHealthyAsync 内部 try-catch, 任何异常返回 false
        //   用于 /health/ready 探活, 不应抛异常影响端点响应
        var provider = CreateResilient();
        var healthy = await provider.IsPrimaryHealthyAsync();
        healthy.Should().BeFalse();
    }

    // ===== IsFallbackHealthyAsync 测试 =====

    [Fact]
    public async Task IsFallbackHealthyAsync_InMemory_Pg_Returns_False()
    {
        // 覆盖: EF Core InMemory CanConnectAsync 返回 false (非 relational provider)
        //   生产中 PG 真实可用时 CanConnect=true, 走集成测试覆盖, 不在单测范围
        //   本测试验证 InMemory 场景下 IsFallbackHealthyAsync 的兜底返回值
        var provider = CreateResilient();
        var healthy = await provider.IsFallbackHealthyAsync();
        healthy.Should().BeFalse();
    }

    // ===== HealthCheckAsync 测试 =====

    [Fact]
    public async Task HealthCheckAsync_All_Unavailable_Returns_False()
    {
        // 覆盖: 主备都不可用时返回 false
        //   场景: Meili 挂 (无效 endpoint) + PG 挂 (InMemory CanConnect=false) → 整体 unhealthy
        //   WHY 这是 /health 端点的兜底返回值, 影响运维判断
        var provider = CreateResilient();
        var healthy = await provider.HealthCheckAsync();
        healthy.Should().BeFalse();
    }

    [Fact]
    public async Task HealthCheckAsync_Disposed_Pg_Returns_False()
    {
        // 覆盖: DbContext Dispose 后 CanConnectAsync 抛 ObjectDisposedException → catch 返回 false
        //   场景: 应用关闭时 DbContext 已释放, 健康检查应安全返回 false 而非抛异常
        var disposedDb = CreateInMemoryDb();
        var pgProvider = new PostgresSearchProvider(disposedDb, NullLogger<PostgresSearchProvider>.Instance);
        disposedDb.Dispose();
        var meiliProvider = CreateMeiliProvider();
        var provider = new ResilientSearchProvider(
            meiliProvider, pgProvider, NullLogger<ResilientSearchProvider>.Instance);

        var healthy = await provider.HealthCheckAsync();
        healthy.Should().BeFalse();
    }

    // ===== Name 属性测试 =====

    [Fact]
    public void Name_Returns_Resilient_Identifier()
    {
        // 覆盖: Name 属性标识 resilient(meili→pg) (用于日志和 metrics)
        var provider = CreateResilient();
        provider.Name.Should().Be("resilient(meili→pg)");
    }
}


