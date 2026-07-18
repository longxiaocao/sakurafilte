using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// V24-F61 (spec 26.13.7 建议 1): DefaultSettingsEnsurer 单元测试
///
/// 测试目标:
///   - 全新插入 (0 已存在 → N 条 INSERT)
///   - 部分已存在 (M 已存在 → N-M 条 INSERT)
///   - 全部已存在 (N 已存在 → 0 条 INSERT)
///   - 空 defaults 列表 (直接 return, 不触发 SQL)
///   - 日志输出验证 (插入时记录 LogInformation)
///
/// WHY 单元测试: V24-F60 抽取的公共 helper 被 6 个 Service 调用,
///   任何回归都会影响所有后台服务的默认配置初始化
///
/// 注: 使用 EF Core InMemory, 不依赖 PG 特性
///   V24-F52 复用: TestProductDbContext 子类 Ignore Alert* 实体 (JsonDocument InMemory 不兼容)
/// </summary>
public class DefaultSettingsEnsurerTests
{
    private sealed class TestProductDbContext : ProductDbContext
    {
        public TestProductDbContext(DbContextOptions<ProductDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
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

    private static readonly (string Key, string Value, string Description)[] SampleDefaults =
    {
        ("alert.threshold", "100", "告警阈值"),
        ("alert.interval_sec", "60", "告警间隔"),
        ("alert.suppress_window", "300", "告警抑制窗口"),
    };

    [Fact]
    public async Task EnsureAsync_AllNew_InsertsAllDefaults()
    {
        await using var db = CreateInMemoryDb();
        var defaults = SampleDefaults;

        await DefaultSettingsEnsurer.EnsureAsync(db, defaults, NullLogger.Instance, "TestService", default);

        var settings = await db.SystemSettings.ToListAsync();
        settings.Should().HaveCount(3);
        settings.Select(s => s.Key).Should().BeEquivalentTo(new[] { "alert.threshold", "alert.interval_sec", "alert.suppress_window" });
        settings.Should().AllSatisfy(s =>
        {
            s.Value.Should().NotBeNullOrEmpty();
            s.Description.Should().NotBeNullOrEmpty();
            s.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        });
    }

    [Fact]
    public async Task EnsureAsync_PartiallyExists_InsertsOnlyMissing()
    {
        await using var db = CreateInMemoryDb();
        // 预置 1 条已存在
        db.SystemSettings.Add(new SystemSetting
        {
            Key = "alert.threshold",
            Value = "200",  // 不同于默认值, EnsureAsync 不应覆盖
            Description = "旧描述",
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        await DefaultSettingsEnsurer.EnsureAsync(db, SampleDefaults, NullLogger.Instance, "TestService", default);

        var settings = await db.SystemSettings.ToDictionaryAsync(s => s.Key);
        settings.Should().HaveCount(3);

        // 已存在的记录保持原值 (不被覆盖)
        settings["alert.threshold"].Value.Should().Be("200");
        settings["alert.threshold"].Description.Should().Be("旧描述");

        // 缺失的 2 条被插入
        settings["alert.interval_sec"].Value.Should().Be("60");
        settings["alert.suppress_window"].Value.Should().Be("300");
    }

    [Fact]
    public async Task EnsureAsync_AllExist_InsertsNothing()
    {
        await using var db = CreateInMemoryDb();
        foreach (var (key, value, desc) in SampleDefaults)
        {
            db.SystemSettings.Add(new SystemSetting { Key = key, Value = value, Description = desc, UpdatedAt = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();

        await DefaultSettingsEnsurer.EnsureAsync(db, SampleDefaults, NullLogger.Instance, "TestService", default);

        var settings = await db.SystemSettings.ToListAsync();
        settings.Should().HaveCount(3);  // 数量不变
    }

    [Fact]
    public async Task EnsureAsync_EmptyDefaults_ReturnsImmediately()
    {
        await using var db = CreateInMemoryDb();
        var emptyDefaults = Array.Empty<(string, string, string)>();

        await DefaultSettingsEnsurer.EnsureAsync(db, emptyDefaults, NullLogger.Instance, "TestService", default);

        (await db.SystemSettings.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureAsync_PreservesExistingValue_DoesNotOverwrite()
    {
        // WHY: 用户可能手动修改过配置值 (如 alert.threshold 从 100 改为 500),
        //   EnsureAsync 不应覆盖用户自定义值
        await using var db = CreateInMemoryDb();
        db.SystemSettings.Add(new SystemSetting
        {
            Key = "alert.threshold",
            Value = "500",  // 用户自定义值
            Description = "用户自定义",
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        await DefaultSettingsEnsurer.EnsureAsync(db, SampleDefaults, NullLogger.Instance, "TestService", default);

        var setting = await db.SystemSettings.SingleAsync(s => s.Key == "alert.threshold");
        setting.Value.Should().Be("500");  // 保持用户自定义值
        setting.Description.Should().Be("用户自定义");
    }

    [Fact]
    public async Task EnsureAsync_DuplicateKeysInDefaults_InsertsOnce()
    {
        // WHY: 防御性编程 - defaults 数组中如果存在重复 key (调用方 bug),
        //   EnsureAsync 不应插入重复记录 (会触发 DB 唯一约束冲突)
        await using var db = CreateInMemoryDb();
        var duplicateDefaults = new[]
        {
            ("dup.key", "v1", "d1"),
            ("dup.key", "v2", "d2"),  // 重复 key
            ("other.key", "v3", "d3")
        };

        await DefaultSettingsEnsurer.EnsureAsync(db, duplicateDefaults, NullLogger.Instance, "TestService", default);

        var settings = await db.SystemSettings.ToListAsync();
        settings.Should().HaveCount(2);  // dup.key 只插入 1 次
        settings.Single(s => s.Key == "dup.key").Value.Should().Be("v1");  // 第一次的值
    }

    [Fact]
    public async Task EnsureAsync_LogsInsertedConfigs()
    {
        // WHY: 启动日志应记录插入了哪些配置, 便于排查 "为什么这个配置值是默认值" 问题
        await using var db = CreateInMemoryDb();
        var logger = new TestLogger<DefaultSettingsEnsurerTests>();

        await DefaultSettingsEnsurer.EnsureAsync(db, SampleDefaults, logger, "MyService", default);

        // 3 条插入日志
        logger.Logs.Count(l => l.logLevel == LogLevel.Information).Should().Be(3);
        logger.Logs.Should().AllSatisfy(l =>
        {
            l.message.Should().Contain("MyService");
            l.message.Should().Contain("插入");
        });
    }

    [Fact]
    public async Task EnsureAsync_DoesNotLogWhenAllExist()
    {
        // WHY: 全部已存在时不应输出日志, 避免启动日志噪音
        await using var db = CreateInMemoryDb();
        foreach (var (key, value, desc) in SampleDefaults)
        {
            db.SystemSettings.Add(new SystemSetting { Key = key, Value = value, Description = desc, UpdatedAt = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();
        var logger = new TestLogger<DefaultSettingsEnsurerTests>();

        await DefaultSettingsEnsurer.EnsureAsync(db, SampleDefaults, logger, "MyService", default);

        logger.Logs.Should().BeEmpty();
    }

    /// <summary>
    /// 简易测试 logger, 收集所有日志便于断言
    /// </summary>
    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel logLevel, string message)> Logs { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Logs.Add((logLevel, formatter(state, exception)));
        }
    }
}
