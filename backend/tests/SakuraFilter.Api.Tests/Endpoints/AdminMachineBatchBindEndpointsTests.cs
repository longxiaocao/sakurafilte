using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;
using Xunit;

namespace SakuraFilter.Api.Tests.Endpoints;

/// <summary>
/// Task 2: 批量绑定 MR.1 到机型 — service 层单元测试
///
/// 测试策略: 直接调用 MachineDictService.BatchBindAsync (service 层),
///   验证业务逻辑 + 异常类型 (KeyNotFoundException → 404, ArgumentException → 400)
///   WHY service 层而非 HTTP 端点层: 现有测试均为 service 层 (参考 AdminProductServiceTests),
///     HTTP 状态码映射在端点层 by ProblemDetailsFactory, 此处通过异常类型间接验证
///
/// 测试用例覆盖:
///   1. 批量追加成功 (replace=false)
///   2. 批量替换成功 (replace=true, 含 removed)
///   3. 部分 MR.1 不存在 (not_found 非空)
///   4. 超限 400 (201 个 MR.1)
///   5. 机型不存在 404
///   6. 幂等性 (重复提交, 第二次 bound=0 skipped=全部)
///
/// 注: 使用 EF Core InMemory + 忽略事务警告 (InMemory 不支持真实事务, 但代码逻辑等价)
///   V24-F52 复用: TestProductDbContext 子类 Ignore Alert* 实体
/// </summary>
public class AdminMachineBatchBindEndpointsTests
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

    private static MachineDictService CreateSut(ProductDbContext db)
    {
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        return new MachineDictService(db, NullLogger<MachineDictService>.Instance, cache);
    }

    /// <summary>创建测试用 dict_machine 字典条目</summary>
    private static DictMachine CreateMachine(long id = 1, string brand = "TOYOTA") => new()
    {
        Id = id,
        MachineBrand = brand,
        MachineCategory = "others",
        SortOrder = 0,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    /// <summary>创建测试用 product (含 mr_1)</summary>
    private static Product CreateProduct(long id, string mr1) => new()
    {
        Id = id,
        Mr1 = mr1,
        OemNoDisplay = mr1,
        OemNoNormalized = mr1,
        Type = "oil",
        IsPublished = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    // ==================== 用例 1: 批量追加成功 ====================

    /// <summary>
    /// 覆盖: Task 2 SubTask 2.1 — replace=false 增量追加, bound 统计实际插入数
    /// </summary>
    [Fact]
    public async Task BatchBindAsync_Append_Success_ReturnsBoundCount()
    {
        await using var db = CreateInMemoryDb();
        db.DictMachines.Add(CreateMachine(1, "TOYOTA"));
        db.Products.Add(CreateProduct(10, "MR001"));
        db.Products.Add(CreateProduct(11, "MR002"));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var req = new BatchBindRequest(1, new List<string> { "MR001", "MR002" }, Replace: false);
        var result = await sut.BatchBindAsync(req, "admin", default);

        result.Bound.Should().Be(2);
        result.Skipped.Should().Be(0);
        result.Removed.Should().Be(0);
        result.NotFound.Should().BeEmpty();
        // 验证绑定已写入 DB
        var bindings = await db.MachineMr1Bindings.ToListAsync();
        bindings.Should().HaveCount(2);
    }

    // ==================== 用例 2: 批量替换成功 ====================

    /// <summary>
    /// 覆盖: Task 2 SubTask 2.1 — replace=true 先删后插, removed 记录已删除数
    /// </summary>
    [Fact]
    public async Task BatchBindAsync_Replace_Success_ReturnsRemovedCount()
    {
        await using var db = CreateInMemoryDb();
        db.DictMachines.Add(CreateMachine(1, "TOYOTA"));
        db.Products.Add(CreateProduct(10, "MR001"));
        db.Products.Add(CreateProduct(11, "MR002"));
        // 预置 3 条旧绑定 (replace 时应被删除)
        db.MachineMr1Bindings.Add(new MachineMr1Binding { MachineId = 1, Mr1 = "OLD001", CreatedAt = DateTime.UtcNow });
        db.MachineMr1Bindings.Add(new MachineMr1Binding { MachineId = 1, Mr1 = "OLD002", CreatedAt = DateTime.UtcNow });
        db.MachineMr1Bindings.Add(new MachineMr1Binding { MachineId = 1, Mr1 = "OLD003", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var req = new BatchBindRequest(1, new List<string> { "MR001", "MR002" }, Replace: true);
        var result = await sut.BatchBindAsync(req, "admin", default);

        result.Bound.Should().Be(2);
        result.Removed.Should().Be(3);  // 3 条旧绑定被删除
        result.Skipped.Should().Be(0);  // replace 模式已清空, 无需跳过
        result.NotFound.Should().BeEmpty();
        // 验证 DB 最终状态: 仅 2 条新绑定 (旧绑定已删除)
        var bindings = await db.MachineMr1Bindings.Where(b => b.MachineId == 1).ToListAsync();
        bindings.Should().HaveCount(2);
        bindings.Select(b => b.Mr1).Should().BeEquivalentTo(new[] { "MR001", "MR002" });
    }

    // ==================== 用例 3: 部分 MR.1 不存在 ====================

    /// <summary>
    /// 覆盖: Task 2 SubTask 2.1 — mr1List 中部分 mr_1 在 products 表不存在, not_found 非空
    ///   端点层映射为 207 Multi-Status (此处 service 层验证返回值)
    /// </summary>
    [Fact]
    public async Task BatchBindAsync_PartialNotFound_ReturnsNotFoundList()
    {
        await using var db = CreateInMemoryDb();
        db.DictMachines.Add(CreateMachine(1, "TOYOTA"));
        db.Products.Add(CreateProduct(10, "MR001"));
        // MR002 不在 products 表
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var req = new BatchBindRequest(1, new List<string> { "MR001", "MR002" }, Replace: false);
        var result = await sut.BatchBindAsync(req, "admin", default);

        result.Bound.Should().Be(1);  // 仅 MR001 绑定成功
        result.NotFound.Should().ContainSingle().Which.Should().Be("MR002");
    }

    // ==================== 用例 4: 超限 400 ====================

    /// <summary>
    /// 覆盖: Task 2 SubTask 2.1 — mr1List 数量 > 200 抛 ArgumentException (BATCH_BIND_LIMIT_EXCEEDED)
    ///   端点层映射为 400 Bad Request
    /// </summary>
    [Fact]
    public async Task BatchBindAsync_ExceedLimit_ThrowsArgumentException()
    {
        await using var db = CreateInMemoryDb();
        db.DictMachines.Add(CreateMachine(1, "TOYOTA"));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        // 201 个 MR.1 (超 200 上限)
        var mr1List = Enumerable.Range(1, 201).Select(i => $"MR{i:000}").ToList();
        var req = new BatchBindRequest(1, mr1List, Replace: false);

        var act = async () => await sut.BatchBindAsync(req, "admin", default);

        (await act.Should().ThrowAsync<ArgumentException>())
            .WithMessage("*BATCH_BIND_LIMIT_EXCEEDED*");
    }

    // ==================== 用例 5: 机型不存在 404 ====================

    /// <summary>
    /// 覆盖: Task 2 SubTask 2.1 — machineId 在 dict_machines 不存在抛 KeyNotFoundException (MACHINE_NOT_FOUND)
    ///   端点层映射为 404 Not Found
    /// </summary>
    [Fact]
    public async Task BatchBindAsync_MachineNotFound_ThrowsKeyNotFoundException()
    {
        await using var db = CreateInMemoryDb();
        // 不创建 dict_machine id=999999
        db.Products.Add(CreateProduct(10, "MR001"));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var req = new BatchBindRequest(999999, new List<string> { "MR001" }, Replace: false);

        var act = async () => await sut.BatchBindAsync(req, "admin", default);

        (await act.Should().ThrowAsync<KeyNotFoundException>())
            .WithMessage("*MACHINE_NOT_FOUND*");
    }

    // ==================== 用例 6: 幂等性 ====================

    /// <summary>
    /// 覆盖: Task 2 SubTask 2.1 — 同一请求重复提交 (replace=false), 第二次 bound=0 skipped=全部
    ///   验证幂等: 已存在的 (machine_id, mr_1) 绑定跳过, 不重复插入
    /// </summary>
    [Fact]
    public async Task BatchBindAsync_DuplicateSubmit_SecondTimeAllSkipped()
    {
        await using var db = CreateInMemoryDb();
        db.DictMachines.Add(CreateMachine(1, "TOYOTA"));
        db.Products.Add(CreateProduct(10, "MR001"));
        db.Products.Add(CreateProduct(11, "MR002"));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var req = new BatchBindRequest(1, new List<string> { "MR001", "MR002" }, Replace: false);

        // 第一次提交: 全部新增
        var result1 = await sut.BatchBindAsync(req, "admin", default);
        result1.Bound.Should().Be(2);
        result1.Skipped.Should().Be(0);

        // 第二次提交 (相同请求): 全部跳过 (幂等)
        var result2 = await sut.BatchBindAsync(req, "admin", default);
        result2.Bound.Should().Be(0);
        result2.Skipped.Should().Be(2);  // 2 个已存在, 全部跳过
        result2.NotFound.Should().BeEmpty();

        // DB 中仍只有 2 条绑定 (未重复插入)
        var bindings = await db.MachineMr1Bindings.Where(b => b.MachineId == 1).ToListAsync();
        bindings.Should().HaveCount(2);
    }

    // ==================== 补充: MR.1 格式错误 ====================

    /// <summary>
    /// 覆盖: Task 2 SubTask 2.1 — MR.1 格式不符 ^[A-Za-z0-9]{1,10}$ 抛 ArgumentException (MR1_FORMAT_INVALID)
    ///   端点层映射为 400 Bad Request
    /// </summary>
    [Theory]
    [InlineData("MR-001")]    // 含连字符
    [InlineData("MR_001")]    // 含下划线
    [InlineData("")]          // 空字符串
    [InlineData("MRRRRRRRRRRR")]  // 超长 (>10 字符)
    public async Task BatchBindAsync_InvalidMr1Format_ThrowsArgumentException(string invalidMr1)
    {
        await using var db = CreateInMemoryDb();
        db.DictMachines.Add(CreateMachine(1, "TOYOTA"));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var req = new BatchBindRequest(1, new List<string> { invalidMr1 }, Replace: false);

        var act = async () => await sut.BatchBindAsync(req, "admin", default);

        (await act.Should().ThrowAsync<ArgumentException>())
            .WithMessage("*MR1_FORMAT_INVALID*");
    }
}
