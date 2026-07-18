using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// V24-F62 (spec 26.13.7 建议 3): BaseDictService 单元测试
///
/// 测试目标: 验证 BaseDictService 抽象基类的 7 个核心方法行为
///   - ListAsync: 过滤已软删 / keyword 搜索 / limit 默认 200
///   - TypeaheadAsync: limit 1-50 clamp / 排除已软删
///   - CreateAsync: UNIQUE 校验 / 自动分配 sortOrder (max+10)
///   - UpdateAsync: 排除自己的 UNIQUE 校验 / 仅变更非空字段
///   - DeleteAsync: 软删 / 不可重复删除
///   - RestoreAsync: 不可恢复未删除 / value 冲突检测
///   - ReorderAsync: 空列表抛错 / id 不存在抛错 / 批量更新 sortOrder
///
/// WHY 用 OemBrandDictService 作为测试代理:
///   - BaseDictService 是抽象类, 无法直接测试
///   - OemBrandDictService 是最简单的子类 (单字段, 仅 override GetXrefCountAsync)
///   - 测试通过 OemBrandDictService 验证基类的 7 方法, 同时覆盖子类的 xrefCount 聚合
///
/// 注: 使用 EF Core InMemory
///   - InMemory 不支持 ILike, BuildSearchPredicate 在 InMemory 下会抛异常
///   - 因此 keyword 搜索场景用 ToListAsync 后内存过滤验证 (或直接不传 keyword)
///   - V24-F52 复用: TestProductDbContext 子类 Ignore Alert* 实体
/// </summary>
public class BaseDictServiceTests
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

    private static OemBrandDictService CreateSut(ProductDbContext db)
        => new(db, NullLogger<OemBrandDictService>.Instance);

    private static XrefOemBrand Brand(long id, string brand, int sortOrder, DateTime? deletedAt = null)
        => new()
        {
            Id = id,
            Brand = brand,
            SortOrder = sortOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            DeletedAt = deletedAt
        };

    // ==================== ListAsync ====================

    [Fact]
    public async Task ListAsync_ExcludesSoftDeleted()
    {
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10));
        db.XrefOemBrands.Add(Brand(2, "HONDA", 20, deletedAt: DateTime.UtcNow));  // 已软删
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.ListAsync(null, includeDeleted: false, limit: null, default);

        result.Should().HaveCount(1);
        result[0].Brand.Should().Be("TOYOTA");
    }

    [Fact]
    public async Task ListAsync_IncludeDeleted_ReturnsAllWithDeletedLast()
    {
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10, deletedAt: DateTime.UtcNow));  // 已软删
        db.XrefOemBrands.Add(Brand(2, "HONDA", 20));  // 未删
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.ListAsync(null, includeDeleted: true, limit: null, default);

        result.Should().HaveCount(2);
        // 已删的排末尾 (DeletedAt != null → 1, 排在 DeletedAt == null → 0 之后)
        result[0].Brand.Should().Be("HONDA");  // 未删
        result[1].Brand.Should().Be("TOYOTA");  // 已删
    }

    [Fact]
    public async Task ListAsync_DefaultLimit_200_WhenNull()
    {
        // WHY: P0-1 修复, dict_oem_no3 527万行全表加载 OOM, 默认 limit=200 兜底
        await using var db = CreateInMemoryDb();
        for (var i = 0; i < 250; i++)
        {
            db.XrefOemBrands.Add(Brand(i + 1, $"BRAND{i}", i * 10));
        }
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.ListAsync(null, includeDeleted: false, limit: null, default);

        result.Should().HaveCount(200);  // 默认 200
    }

    [Fact]
    public async Task ListAsync_RespectsCallerLimit()
    {
        await using var db = CreateInMemoryDb();
        for (var i = 0; i < 10; i++)
        {
            db.XrefOemBrands.Add(Brand(i + 1, $"BRAND{i}", i * 10));
        }
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.ListAsync(null, includeDeleted: false, limit: 5, default);

        result.Should().HaveCount(5);
    }

    // ==================== TypeaheadAsync ====================

    [Fact]
    public async Task TypeaheadAsync_ClampsLimitTo50()
    {
        await using var db = CreateInMemoryDb();
        for (var i = 0; i < 100; i++)
        {
            db.XrefOemBrands.Add(Brand(i + 1, $"BRAND{i}", i * 10));
        }
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.TypeaheadAsync(null, limit: 1000, default);

        result.Should().HaveCount(50);  // 上限 50
    }

    [Fact]
    public async Task TypeaheadAsync_ClampsLimitToMin1()
    {
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.TypeaheadAsync(null, limit: 0, default);

        result.Should().HaveCount(1);  // 下限 1
    }

    [Fact]
    public async Task TypeaheadAsync_ExcludesSoftDeleted()
    {
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10));
        db.XrefOemBrands.Add(Brand(2, "HONDA", 20, deletedAt: DateTime.UtcNow));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.TypeaheadAsync(null, 50, default);

        result.Should().HaveCount(1);
        result[0].Brand.Should().Be("TOYOTA");
    }

    // ==================== CreateAsync ====================

    [Fact]
    public async Task CreateAsync_AutoAssignsSortOrder_MaxPlus10()
    {
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10));
        db.XrefOemBrands.Add(Brand(2, "HONDA", 30));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.CreateAsync("NISSAN", sortOrder: null, default);

        result.SortOrder.Should().Be(40);  // max(30) + 10
        result.Brand.Should().Be("NISSAN");
    }

    [Fact]
    public async Task CreateAsync_DuplicateValue_Throws()
    {
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var act = async () => await sut.CreateAsync("TOYOTA", sortOrder: null, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*已存在*");
    }

    [Fact]
    public async Task CreateAsync_DuplicateValueInSoftDeleted_Throws()
    {
        // WHY: 软删的同名占用也阻止新建, 避免恢复时冲突
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10, deletedAt: DateTime.UtcNow));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var act = async () => await sut.CreateAsync("TOYOTA", sortOrder: null, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*已存在*");
    }

    [Fact]
    public async Task CreateAsync_EmptyValue_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.CreateAsync("   ", sortOrder: null, default);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*不能为空*");
    }

    [Fact]
    public async Task CreateAsync_ValueExceedsMaxLength_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);
        var longValue = new string('A', 101);  // OemBrandDictService maxLength=100

        var act = async () => await sut.CreateAsync(longValue, sortOrder: null, default);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*长度*");
    }

    [Fact]
    public async Task CreateAsync_TrimsValue()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var result = await sut.CreateAsync("  TOYOTA  ", sortOrder: null, default);

        result.Brand.Should().Be("TOYOTA");  // trim 后
    }

    // ==================== UpdateAsync ====================

    [Fact]
    public async Task UpdateAsync_NotFound_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.UpdateAsync(999, "X", null, default);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*不存在*");
    }

    [Fact]
    public async Task UpdateAsync_DuplicateValueInOtherRow_Throws()
    {
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10));
        db.XrefOemBrands.Add(Brand(2, "HONDA", 20));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var act = async () => await sut.UpdateAsync(2, "TOYOTA", null, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*已存在*");
    }

    [Fact]
    public async Task UpdateAsync_SameValueSameRow_DoesNotThrow()
    {
        // WHY: UpdateAsync 内部检查 normalized != GetValue(entity), 同值不触发 conflict 检查
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.UpdateAsync(1, "TOYOTA", null, default);

        result.Brand.Should().Be("TOYOTA");
    }

    [Fact]
    public async Task UpdateAsync_UpdatesSortOrderOnly_WhenValueNull()
    {
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.UpdateAsync(1, null, 50, default);

        result.Brand.Should().Be("TOYOTA");  // 未变
        result.SortOrder.Should().Be(50);  // 已变
    }

    // ==================== DeleteAsync ====================

    [Fact]
    public async Task DeleteAsync_SoftDeletes_SetsDeletedAt()
    {
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        await sut.DeleteAsync(1, default);

        var entity = await db.XrefOemBrands.SingleAsync();
        entity.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_AlreadyDeleted_Throws()
    {
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10, deletedAt: DateTime.UtcNow));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var act = async () => await sut.DeleteAsync(1, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*已删除*");
    }

    [Fact]
    public async Task DeleteAsync_NotFound_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.DeleteAsync(999, default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ==================== RestoreAsync ====================

    [Fact]
    public async Task RestoreAsync_ClearsDeletedAt()
    {
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10, deletedAt: DateTime.UtcNow));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.RestoreAsync(1, default);

        result.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task RestoreAsync_NotDeleted_Throws()
    {
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var act = async () => await sut.RestoreAsync(1, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*未删除*");
    }

    [Fact]
    public async Task RestoreAsync_ValueConflict_Throws()
    {
        // WHY: 软删后, 同 value 被新条目占用, 恢复会触发 UNIQUE 冲突
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10, deletedAt: DateTime.UtcNow));
        db.XrefOemBrands.Add(Brand(2, "TOYOTA", 20));  // 新条目占用同名
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var act = async () => await sut.RestoreAsync(1, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*已被新条目占用*");
    }

    // ==================== ReorderAsync ====================

    [Fact]
    public async Task ReorderAsync_EmptyList_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.ReorderAsync(new List<(long, int)>(), default);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*不能为空*");
    }

    [Fact]
    public async Task ReorderAsync_NullList_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.ReorderAsync(null!, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReorderAsync_IdNotFound_Throws()
    {
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var act = async () => await sut.ReorderAsync(new List<(long, int)> { (1, 100), (999, 200) }, default);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*999*");
    }

    [Fact]
    public async Task ReorderAsync_UpdatesSortOrders()
    {
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(Brand(1, "TOYOTA", 10));
        db.XrefOemBrands.Add(Brand(2, "HONDA", 20));
        db.XrefOemBrands.Add(Brand(3, "NISSAN", 30));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        await sut.ReorderAsync(new List<(long, int)> { (3, 100), (1, 200), (2, 300) }, default);

        var brands = await db.XrefOemBrands.ToDictionaryAsync(b => b.Id);
        brands[1].SortOrder.Should().Be(200);
        brands[2].SortOrder.Should().Be(300);
        brands[3].SortOrder.Should().Be(100);
    }

    // ==================== GetXrefCountAsync ====================

    [Fact]
    public async Task GetXrefCountAsync_CountsMatchingXrefs()
    {
        await using var db = CreateInMemoryDb();
        db.CrossReferences.Add(new CrossReference { ProductId = 1, OemBrand = "TOYOTA", OemNo3 = "OEM001" });
        db.CrossReferences.Add(new CrossReference { ProductId = 2, OemBrand = "TOYOTA", OemNo3 = "OEM002" });
        db.CrossReferences.Add(new CrossReference { ProductId = 3, OemBrand = "HONDA", OemNo3 = "OEM003" });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var count = await sut.GetXrefCountAsync("TOYOTA", default);

        count.Should().Be(2);
    }

    [Fact]
    public async Task GetXrefCountAsync_NoMatch_Returns0()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var count = await sut.GetXrefCountAsync("NOT_EXIST", default);

        count.Should().Be(0);
    }

    // ==================== NormalizeValue ====================

    [Fact]
    public async Task CreateAsync_WithWhitespace_PreservesInternalSpaces()
    {
        // WHY: trim 仅去首尾, 内部空格保留 (如 "Foo Bar" 是合法值)
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var result = await sut.CreateAsync("  Foo Bar  ", sortOrder: null, default);

        result.Brand.Should().Be("Foo Bar");
    }
}
