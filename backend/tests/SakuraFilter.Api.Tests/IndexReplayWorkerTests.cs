using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.Entities;
using SakuraFilter.Etl;
using SakuraFilter.Infrastructure.Data;
using System.Reflection;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// V24-F66 (spec 26.14.9 建议 1): IndexReplayWorker 单元测试
///
/// 测试目标:
///   - UpdateRetryAsync (private static): 重试计数递增 + last_error 截断 + 指数退避
///   - ProcessDeadLetterAsync (private): retry_count >= 5 转移到死信队列 + 复用 recovered 死信
///
/// WHY 不测 ProcessPendingAsync: 它依赖 pg_try_advisory_xact_lock raw SQL (InMemory 不支持)
///   需 PG 集成测试 (Testcontainers, 后续 v26+ 补)
///
/// 测试方式: 反射调用 private/private static 方法
///   WHY UpdateRetryAsync/ProcessDeadLetterAsync 是私有方法, 改 internal 会扩大 API 表面
///        反射测试虽稍丑, 但保持生产代码封装性, 测试仍能覆盖核心逻辑
///
/// 注: 使用 EF Core InMemory + TestProductDbContext (V24-F52 复用模式)
/// </summary>
public class IndexReplayWorkerTests
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
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new TestProductDbContext(options);
    }

    private static IndexReplayWorker CreateWorker(IServiceProvider sp, int batchSize = 500)
    {
        var options = new EtlOptions { IndexReplayBatchSize = batchSize, IndexReplayPollSeconds = 10 };
        var wrapped = Options.Create(options);
        var hostedStatusMock = new Mock<IHostedServiceStatus>();
        return new IndexReplayWorker(sp, NullLogger<IndexReplayWorker>.Instance, wrapped, hostedStatusMock.Object);
    }

    /// <summary>
    /// 构造一个 IServiceProvider, GetRequiredService&lt;ProductDbContext&gt;() 返回指定 db
    ///   WHY IndexReplayWorker.ProcessDeadLetterAsync 通过 _sp.CreateScope().ServiceProvider.GetRequiredService<ProductDbContext>()
    ///        获取 db, 需 mock 此调用链
    ///   注: 用 AddSingleton 而非 AddScoped, 因为 ProcessDeadLetterAsync 内部 using scope 会在方法结束时
    ///       dispose scope, AddScoped 注册的 db 会被 dispose, 导致测试无法继续查询 db 验证结果
    ///       AddSingleton 的 db 不会被 scope dispose, 测试可在 worker 调用后继续查询
    /// </summary>
    private static IServiceProvider CreateServiceProviderWithDb(ProductDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_ => db);
        return services.BuildServiceProvider();
    }

    // 反射获取 private static 方法
    private static MethodInfo GetUpdateRetryMethod()
        => typeof(IndexReplayWorker).GetMethod("UpdateRetryAsync", BindingFlags.NonPublic | BindingFlags.Static)
           ?? throw new InvalidOperationException("UpdateRetryAsync 方法未找到");

    // 反射获取 private 实例方法
    private static MethodInfo GetProcessDeadLetterMethod()
        => typeof(IndexReplayWorker).GetMethod("ProcessDeadLetterAsync", BindingFlags.NonPublic | BindingFlags.Instance)
           ?? throw new InvalidOperationException("ProcessDeadLetterAsync 方法未找到");

    // ==================== UpdateRetryAsync ====================

    [Fact]
    public async Task UpdateRetry_IncrementsRetryCount()
    {
        await using var db = CreateInMemoryDb();
        var items = new List<SearchIndexPending>
        {
            new() { Id = 1, Operation = "index", Payload = "{}", RetryCount = 0 }
        };

        await (Task)GetUpdateRetryMethod().Invoke(null, new object[] { db, items, "error", CancellationToken.None })!;

        items[0].RetryCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdateRetry_SetsLastError()
    {
        await using var db = CreateInMemoryDb();
        var items = new List<SearchIndexPending>
        {
            new() { Id = 1, Operation = "index", Payload = "{}", RetryCount = 0 }
        };

        await (Task)GetUpdateRetryMethod().Invoke(null, new object[] { db, items, "Meili timeout", CancellationToken.None })!;

        items[0].LastError.Should().Be("Meili timeout");
    }

    [Fact]
    public async Task UpdateRetry_TruncatesLastErrorTo500Chars()
    {
        // WHY: last_error 列长度限制 (varchar(500)), 避免超长错误堆栈导致 DB 写入失败
        await using var db = CreateInMemoryDb();
        var longError = new string('x', 1000);
        var items = new List<SearchIndexPending>
        {
            new() { Id = 1, Operation = "index", Payload = "{}", RetryCount = 0 }
        };

        await (Task)GetUpdateRetryMethod().Invoke(null, new object[] { db, items, longError, CancellationToken.None })!;

        items[0].LastError.Should().HaveLength(500);
        items[0].LastError.Should().Be(new string('x', 500));
    }

    [Fact]
    public async Task UpdateRetry_FirstRetry_Uses60sBackoff()
    {
        // BackoffSeconds = { 60, 120, 300, 600, 1800 }
        //   retry_count 0→1 用 BackoffSeconds[0] = 60s
        await using var db = CreateInMemoryDb();
        var before = DateTime.UtcNow;
        var items = new List<SearchIndexPending>
        {
            new() { Id = 1, Operation = "index", Payload = "{}", RetryCount = 0 }
        };

        await (Task)GetUpdateRetryMethod().Invoke(null, new object[] { db, items, "err", CancellationToken.None })!;

        var after = DateTime.UtcNow;
        items[0].NextRetryAt.Should().BeAfter(before.AddSeconds(59));  // 容忍 1s 抖动
        items[0].NextRetryAt.Should().BeBefore(after.AddSeconds(61));
    }

    [Fact]
    public async Task UpdateRetry_SecondRetry_Uses120sBackoff()
    {
        await using var db = CreateInMemoryDb();
        var before = DateTime.UtcNow;
        var items = new List<SearchIndexPending>
        {
            new() { Id = 1, Operation = "index", Payload = "{}", RetryCount = 1 }
        };

        await (Task)GetUpdateRetryMethod().Invoke(null, new object[] { db, items, "err", CancellationToken.None })!;

        items[0].RetryCount.Should().Be(2);
        items[0].NextRetryAt.Should().BeAfter(before.AddSeconds(119));
        items[0].NextRetryAt.Should().BeBefore(DateTime.UtcNow.AddSeconds(121));
    }

    [Fact]
    public async Task UpdateRetry_FifthRetry_Uses1800sBackoff()
    {
        // retry_count 4→5 用 BackoffSeconds[4] = 1800s
        await using var db = CreateInMemoryDb();
        var before = DateTime.UtcNow;
        var items = new List<SearchIndexPending>
        {
            new() { Id = 1, Operation = "index", Payload = "{}", RetryCount = 4 }
        };

        await (Task)GetUpdateRetryMethod().Invoke(null, new object[] { db, items, "err", CancellationToken.None })!;

        items[0].RetryCount.Should().Be(5);
        items[0].NextRetryAt.Should().BeAfter(before.AddSeconds(1799));
    }

    [Fact]
    public async Task UpdateRetry_RetryCountExceedsBackoffArray_UsesLastValue()
    {
        // WHY: retry_count > BackoffSeconds.Length 时用 BackoffSeconds[^1] = 1800s
        //   防止数组越界 + 保证最大退避不超过 30 分钟
        await using var db = CreateInMemoryDb();
        var before = DateTime.UtcNow;
        var items = new List<SearchIndexPending>
        {
            new() { Id = 1, Operation = "index", Payload = "{}", RetryCount = 10 }
        };

        await (Task)GetUpdateRetryMethod().Invoke(null, new object[] { db, items, "err", CancellationToken.None })!;

        items[0].RetryCount.Should().Be(11);
        items[0].NextRetryAt.Should().BeAfter(before.AddSeconds(1799));
        items[0].NextRetryAt.Should().BeBefore(DateTime.UtcNow.AddSeconds(1801));
    }

    [Fact]
    public async Task UpdateRetry_PersistsChangesToDb()
    {
        // WHY: UpdateRetryAsync 末尾调 SaveChangesAsync, 验证 DB 实际持久化
        await using var db = CreateInMemoryDb();
        db.SearchIndexPending.Add(new SearchIndexPending
        {
            Id = 1, Operation = "index", Payload = "{}", RetryCount = 0
        });
        await db.SaveChangesAsync();

        var items = await db.SearchIndexPending.ToListAsync();
        await (Task)GetUpdateRetryMethod().Invoke(null, new object[] { db, items, "err", CancellationToken.None })!;

        // 重新查 DB 验证持久化
        var dbItem = await db.SearchIndexPending.AsNoTracking().SingleAsync();
        dbItem.RetryCount.Should().Be(1);
        dbItem.LastError.Should().Be("err");
    }

    [Fact]
    public async Task UpdateRetry_HandlesMultipleItems()
    {
        await using var db = CreateInMemoryDb();
        var items = new List<SearchIndexPending>
        {
            new() { Id = 1, Operation = "index", Payload = "{}", RetryCount = 0 },
            new() { Id = 2, Operation = "delete", Payload = "[\"mr1\"]", RetryCount = 2 },
            new() { Id = 3, Operation = "index", Payload = "{}", RetryCount = 4 }
        };

        await (Task)GetUpdateRetryMethod().Invoke(null, new object[] { db, items, "err", CancellationToken.None })!;

        items.Should().HaveCount(3);
        items[0].RetryCount.Should().Be(1);
        items[1].RetryCount.Should().Be(3);
        items[2].RetryCount.Should().Be(5);
    }

    // ==================== ProcessDeadLetterAsync ====================

    [Fact]
    public async Task ProcessDeadLetter_NoExhaustedItems_DoesNothing()
    {
        // WHY: 无 retry_count >= 5 条目时, 不操作死信表
        await using var db = CreateInMemoryDb();
        db.SearchIndexPending.Add(new SearchIndexPending
        {
            Id = 1, Operation = "index", Payload = "{}", RetryCount = 3  // < 5, 不转死信
        });
        await db.SaveChangesAsync();
        var sp = CreateServiceProviderWithDb(db);
        var worker = CreateWorker(sp);

        var task = (Task)GetProcessDeadLetterMethod().Invoke(worker, new object[] { CancellationToken.None })!;
        await task;

        var deadLetters = await db.SearchIndexDeadLetters.ToListAsync();
        deadLetters.Should().BeEmpty();
        var pending = await db.SearchIndexPending.ToListAsync();
        pending.Should().HaveCount(1);  // 原条目保留
    }

    [Fact]
    public async Task ProcessDeadLetter_ExhaustedItem_CreatesNewDeadLetter()
    {
        // retry_count >= 5 时, pending 条目转移到死信表, pending 表清空
        await using var db = CreateInMemoryDb();
        db.SearchIndexPending.Add(new SearchIndexPending
        {
            Id = 1, Operation = "index", Payload = "{\"mr1\":\"MR001\"}", RetryCount = 5,
            LastError = "Meili timeout", CreatedAt = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();
        var sp = CreateServiceProviderWithDb(db);
        var worker = CreateWorker(sp);

        await (Task)GetProcessDeadLetterMethod().Invoke(worker, new object[] { CancellationToken.None })!;

        var deadLetters = await db.SearchIndexDeadLetters.ToListAsync();
        deadLetters.Should().HaveCount(1);
        var dl = deadLetters[0];
        dl.OriginalId.Should().Be(1);
        dl.Operation.Should().Be("index");
        dl.Payload.Should().Be("{\"mr1\":\"MR001\"}");
        dl.RetryCount.Should().Be(5);
        dl.LastError.Should().Be("Meili timeout");
        dl.Status.Should().Be("active");
        dl.RecoveryCount.Should().Be(0);
        dl.RecoveredAt.Should().BeNull();

        // pending 表应清空
        var pending = await db.SearchIndexPending.ToListAsync();
        pending.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessDeadLetter_MultipleExhaustedItems_AllTransferred()
    {
        await using var db = CreateInMemoryDb();
        db.SearchIndexPending.AddRange(
            new SearchIndexPending { Id = 1, Operation = "index", Payload = "{}", RetryCount = 5, LastError = "e1" },
            new SearchIndexPending { Id = 2, Operation = "delete", Payload = "[]", RetryCount = 7, LastError = "e2" },
            new SearchIndexPending { Id = 3, Operation = "index", Payload = "{}", RetryCount = 6, LastError = "e3" }
        );
        await db.SaveChangesAsync();
        var sp = CreateServiceProviderWithDb(db);
        var worker = CreateWorker(sp);

        await (Task)GetProcessDeadLetterMethod().Invoke(worker, new object[] { CancellationToken.None })!;

        var deadLetters = await db.SearchIndexDeadLetters.ToListAsync();
        deadLetters.Should().HaveCount(3);
        deadLetters.Select(d => d.OriginalId).Should().BeEquivalentTo(new[] { 1L, 2L, 3L });

        var pending = await db.SearchIndexPending.ToListAsync();
        pending.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessDeadLetter_ReusesRecoveredDeadLetter_WhenSamePayload()
    {
        // Day 7.10.1 BUG FIX: 同一 payload 已 recovered 的死信, 复用其 recovery_count
        //   WHY: 之前方案删除死信行后, 新入队的死信 recovery_count=0, max 限位失效
        //   现在: status='recovered' 保留, 新失败时检查同 payload 最近 dead_letter, 找到则复用
        await using var db = CreateInMemoryDb();
        // 已有 recovered 死信 (recovery_count=1)
        db.SearchIndexDeadLetters.Add(new SearchIndexDeadLetter
        {
            Id = 100, OriginalId = 99, Operation = "index", Payload = "{\"mr1\":\"MR001\"}",
            RetryCount = 5, LastError = "old error", CreatedAt = DateTime.UtcNow.AddDays(-2),
            MovedAt = DateTime.UtcNow.AddDays(-1), Status = "recovered",
            RecoveryCount = 1, RecoveredAt = DateTime.UtcNow.AddDays(-1)
        });
        // 新 pending 条目, 同 operation + payload, retry_count=5
        db.SearchIndexPending.Add(new SearchIndexPending
        {
            Id = 1, Operation = "index", Payload = "{\"mr1\":\"MR001\"}", RetryCount = 5, LastError = "new error"
        });
        await db.SaveChangesAsync();
        var sp = CreateServiceProviderWithDb(db);
        var worker = CreateWorker(sp);

        await (Task)GetProcessDeadLetterMethod().Invoke(worker, new object[] { CancellationToken.None })!;

        // 不应新增死信 (复用 existing)
        var deadLetters = await db.SearchIndexDeadLetters.ToListAsync();
        deadLetters.Should().HaveCount(1);
        var dl = deadLetters[0];
        dl.Id.Should().Be(100);
        dl.Status.Should().Be("active");  // 重置为 active
        dl.RecoveryCount.Should().Be(1);  // 保持不变 (入死信不递增, 恢复时才 +1)
        dl.RetryCount.Should().Be(5);  // 更新为新的 retry_count
        dl.LastError.Should().Be("new error");  // 更新为新的 last_error
        dl.RecoveredAt.Should().BeNull();  // 清除旧 recovered 标记

        var pending = await db.SearchIndexPending.ToListAsync();
        pending.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessDeadLetter_DoesNotReuse_ActiveDeadLetter()
    {
        // status='active' 的死信不复用 (仍在等待处理), 应新建死信
        await using var db = CreateInMemoryDb();
        db.SearchIndexDeadLetters.Add(new SearchIndexDeadLetter
        {
            Id = 100, OriginalId = 99, Operation = "index", Payload = "{\"mr1\":\"MR001\"}",
            RetryCount = 5, LastError = "old", Status = "active", RecoveryCount = 0
        });
        db.SearchIndexPending.Add(new SearchIndexPending
        {
            Id = 1, Operation = "index", Payload = "{\"mr1\":\"MR001\"}", RetryCount = 5, LastError = "new"
        });
        await db.SaveChangesAsync();
        var sp = CreateServiceProviderWithDb(db);
        var worker = CreateWorker(sp);

        await (Task)GetProcessDeadLetterMethod().Invoke(worker, new object[] { CancellationToken.None })!;

        var deadLetters = await db.SearchIndexDeadLetters.ToListAsync();
        deadLetters.Should().HaveCount(2);  // 原 active + 新建
        deadLetters.Count(d => d.Status == "active").Should().Be(2);
    }

    [Fact]
    public async Task ProcessDeadLetter_RespectsBatchSize()
    {
        // BatchSize 限制单批处理数量, 避免长事务
        await using var db = CreateInMemoryDb();
        for (var i = 1; i <= 10; i++)
        {
            db.SearchIndexPending.Add(new SearchIndexPending
            {
                Id = i, Operation = "index", Payload = $"{{\"id\":{i}}}", RetryCount = 5, LastError = "e"
            });
        }
        await db.SaveChangesAsync();
        var sp = CreateServiceProviderWithDb(db);
        var worker = CreateWorker(sp, batchSize: 3);  // 仅处理 3 条

        await (Task)GetProcessDeadLetterMethod().Invoke(worker, new object[] { CancellationToken.None })!;

        var deadLetters = await db.SearchIndexDeadLetters.ToListAsync();
        deadLetters.Should().HaveCount(3);
        var pending = await db.SearchIndexPending.ToListAsync();
        pending.Should().HaveCount(7);  // 剩 7 条未处理
    }

    [Fact]
    public async Task ProcessDeadLetter_NewDeadLetter_PreservesCreatedAt()
    {
        // CreatedAt 来自 pending 条目 (原入队时间), 不是当前时间
        //   WHY: 保留入队历史, 便于排查"何时首次失败"
        await using var db = CreateInMemoryDb();
        var originalCreatedAt = DateTime.UtcNow.AddDays(-3);
        db.SearchIndexPending.Add(new SearchIndexPending
        {
            Id = 1, Operation = "index", Payload = "{}", RetryCount = 5,
            LastError = "err", CreatedAt = originalCreatedAt
        });
        await db.SaveChangesAsync();
        var sp = CreateServiceProviderWithDb(db);
        var worker = CreateWorker(sp);

        await (Task)GetProcessDeadLetterMethod().Invoke(worker, new object[] { CancellationToken.None })!;

        var dl = await db.SearchIndexDeadLetters.SingleAsync();
        dl.CreatedAt.Should().Be(originalCreatedAt);
        dl.MovedAt.Should().BeAfter(originalCreatedAt);  // MovedAt 是转入死信时间 (当前)
    }
}
