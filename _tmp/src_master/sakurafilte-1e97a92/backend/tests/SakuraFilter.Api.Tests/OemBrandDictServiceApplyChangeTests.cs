using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// V24-F52 (spec Task 5.1.21): OemBrandDictService.ApplyChangeAsync 单元测试
///
/// 测试目标:
///   - ApplyChangeAsync 为引用该 brand 的产品写入 search_index_pending 记录
///   - 无受影响产品时不写入 (避免无效记录)
///   - 1 产品多 xref 同 brand 时仅写 1 条 (distinct product_id)
///   - payload 格式含 product_id + mr1 + trigger (IndexReplayWorker 兼容)
///   - UpdateOemBrandAsync/RestoreOemBrandAsync/DeleteOemBrandAsync 触发 ApplyChangeAsync
///
/// WHY 单元测试: ApplyChangeAsync 是字典变更→索引重建的关键链路
///   - 漏写 search_index_pending 会导致搜索结果排序滞后 (brand_sort_order 不同步)
///   - 重复写入会导致 IndexReplayWorker 无效负载
///
/// 注: 使用 EF Core InMemory provider (避免依赖真实 PostgreSQL)
///   WHY InMemory: ApplyChangeAsync 逻辑不依赖 PG 特性 (ILIKE/advisory lock),
///                  InMemory 足以验证 product_id 查询 + search_index_pending 写入
///
/// V24-F52 修复: AlertRule/AlertHistory/SecurityEvent 实体含 JsonDocument 字段
///   InMemory provider 无法绑定 JsonDocument 构造函数 (内部非 public 字段)
///   用 TestProductDbContext 子类 Ignore 这些实体, 不影响 ApplyChangeAsync 测试
/// </summary>
public class OemBrandDictServiceApplyChangeTests
{
    /// <summary>
    /// 测试专用 ProductDbContext: 跳过 JsonDocument 实体的 EF Core 映射
    ///   WHY: AlertRule.Channels/Conditions/Recipients + AlertHistory.ContentJson/Recipients
    ///        + SecurityEvent.Details 均为 JsonDocument 类型, InMemory provider 在
    ///        ConstructorBindingConvention 阶段无法绑定其内部构造函数参数, 导致 DbContext 初始化失败
    ///   生产环境用 Npgsql.EntityFrameworkCore.PostgreSQL, Npgsql 能正确映射 jsonb → JsonDocument
    ///   测试只验证 ApplyChangeAsync (不涉及 Alert* 实体), Ignore 不影响覆盖率
    /// </summary>
    private sealed class TestProductDbContext : ProductDbContext
    {
        public TestProductDbContext(DbContextOptions<ProductDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            // 覆盖 base 中 ApplyConfigurationsFromAssembly 注册的 Alert* 实体
            //   WHY: base.OnModelCreating 已通过 ApplyConfigurationsFromAssembly 加载
            //        AlertRuleConfiguration/AlertHistoryConfiguration/SecurityEventConfiguration,
            //        mb.Ignore<T>() 显式移除实体映射, 跳过 JsonDocument 构造函数绑定
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
        var db = new TestProductDbContext(options);
        return db;
    }

    private static OemBrandDictService CreateSut(ProductDbContext db)
        => new(db, NullLogger<OemBrandDictService>.Instance);

    private static Product CreateProduct(long id, string mr1, string oemBrand, string oemNo3 = "OEM001")
    {
        var product = new Product
        {
            Id = id,
            Mr1 = mr1,
            ProductName1 = "Test Product",
            Type = "filter"
        };
        product.CrossReferences.Add(new CrossReference
        {
            ProductId = id,
            OemBrand = oemBrand,
            OemNo3 = oemNo3,
            Oem2 = null,
            SortOrder = 0,
            IsDiscontinued = false
        });
        return product;
    }

    [Fact]
    public async Task ApplyChangeAsync_NoAffectedProducts_DoesNotWritePending()
    {
        // 准备: 无任何 xref 引用该 brand
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        // 执行
        await sut.ApplyChangeAsync("NonExistentBrand", isDeleted: false);

        // 断言: search_index_pending 应为空
        db.SearchIndexPending.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyChangeAsync_OneAffectedProduct_WritesOnePendingRecord()
    {
        // 准备: 1 个产品引用 BOSCH
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct(1, "MR001", "BOSCH"));
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // 执行
        await sut.ApplyChangeAsync("BOSCH", isDeleted: false);

        // 断言: 写入 1 条 search_index_pending
        var pending = db.SearchIndexPending.ToList();
        pending.Should().HaveCount(1);
        pending[0].Operation.Should().Be("index");

        // payload 含 product_id + mr1 + trigger
        var payload = JsonDocument.Parse(pending[0].Payload);
        payload.RootElement.GetProperty("product_id").GetInt64().Should().Be(1);
        payload.RootElement.GetProperty("mr1").GetString().Should().Be("MR001");
        payload.RootElement.GetProperty("trigger").GetString().Should().Be("oem_brand_dict_change");
    }

    [Fact]
    public async Task ApplyChangeAsync_MultipleProductsSameBrand_WritesAllPendingRecords()
    {
        // 准备: 3 个产品都引用 BOSCH
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct(1, "MR001", "BOSCH", "OEM001"));
        db.Products.Add(CreateProduct(2, "MR002", "BOSCH", "OEM002"));
        db.Products.Add(CreateProduct(3, "MR003", "BOSCH", "OEM003"));
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // 执行
        await sut.ApplyChangeAsync("BOSCH", isDeleted: false);

        // 断言: 写入 3 条 search_index_pending
        var pending = db.SearchIndexPending.ToList();
        pending.Should().HaveCount(3);
        pending.Select(p => JsonDocument.Parse(p.Payload).RootElement.GetProperty("product_id").GetInt64())
            .Should().BeEquivalentTo(new[] { 1L, 2L, 3L });
    }

    [Fact]
    public async Task ApplyChangeAsync_OneProductMultipleXrefSameBrand_WritesOnlyOnePendingRecord()
    {
        // 准备: 1 个产品有 3 个 xref 都引用 BOSCH (不同 oem_no_3)
        //   WHY distinct: 避免 1 产品多 xref 重复写入 search_index_pending
        await using var db = CreateInMemoryDb();
        var product = new Product
        {
            Id = 1,
            Mr1 = "MR001",
            ProductName1 = "Test",
            Type = "filter"
        };
        product.CrossReferences.Add(new CrossReference { ProductId = 1, OemBrand = "BOSCH", OemNo3 = "OEM001" });
        product.CrossReferences.Add(new CrossReference { ProductId = 1, OemBrand = "BOSCH", OemNo3 = "OEM002" });
        product.CrossReferences.Add(new CrossReference { ProductId = 1, OemBrand = "BOSCH", OemNo3 = "OEM003" });
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // 执行
        await sut.ApplyChangeAsync("BOSCH", isDeleted: false);

        // 断言: 仅写入 1 条 (distinct product_id)
        var pending = db.SearchIndexPending.ToList();
        pending.Should().HaveCount(1);
        var payload = JsonDocument.Parse(pending[0].Payload);
        payload.RootElement.GetProperty("product_id").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task ApplyChangeAsync_MixedBrands_OnlyWritesAffectedBrandProducts()
    {
        // 准备: 3 个产品引用不同 brand
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct(1, "MR001", "BOSCH"));
        db.Products.Add(CreateProduct(2, "MR002", "MANN"));
        db.Products.Add(CreateProduct(3, "MR003", "WIX"));
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // 执行: 仅对 MANN 触发
        await sut.ApplyChangeAsync("MANN", isDeleted: false);

        // 断言: 仅写入 1 条 (product_id=2)
        var pending = db.SearchIndexPending.ToList();
        pending.Should().HaveCount(1);
        var payload = JsonDocument.Parse(pending[0].Payload);
        payload.RootElement.GetProperty("product_id").GetInt64().Should().Be(2);
        payload.RootElement.GetProperty("mr1").GetString().Should().Be("MR002");
    }

    [Fact]
    public async Task ApplyChangeAsync_PayloadContainsTriggerField_ForIndexReplayWorkerCompat()
    {
        // 准备
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct(1, "MR001", "BOSCH"));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        // 执行
        await sut.ApplyChangeAsync("BOSCH", isDeleted: true);

        // 断言: payload 含 trigger 字段 (IndexReplayWorker 用此字段区分重建来源)
        var pending = db.SearchIndexPending.Single();
        var payload = JsonDocument.Parse(pending.Payload);
        payload.RootElement.GetProperty("trigger").GetString().Should().Be("oem_brand_dict_change");
        payload.RootElement.GetProperty("product_id").GetInt64().Should().Be(1);
        payload.RootElement.GetProperty("mr1").GetString().Should().Be("MR001");
    }

    [Fact]
    public async Task ApplyChangeAsync_OperationIsIndex_NotDelete()
    {
        // 准备
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct(1, "MR001", "BOSCH"));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        // 执行
        await sut.ApplyChangeAsync("BOSCH", isDeleted: true);

        // 断言: Operation = "index" (字典变更不删除产品, 仅重建文档)
        //   WHY index 而非 delete: 字典软删后, 产品仍存在, 仅 BrandSortOrder 变化
        var pending = db.SearchIndexPending.Single();
        pending.Operation.Should().Be("index");
    }

    [Fact]
    public async Task ApplyChangeAsync_RetryCountAndNextRetryAtInitialized()
    {
        // 准备
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct(1, "MR001", "BOSCH"));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        // 执行
        var beforeTs = DateTime.UtcNow;
        await sut.ApplyChangeAsync("BOSCH", isDeleted: false);

        // 断言: RetryCount=0, NextRetryAt ≈ now (立即可被 IndexReplayWorker 消费)
        var pending = db.SearchIndexPending.Single();
        pending.RetryCount.Should().Be(0);
        pending.NextRetryAt.Should().BeCloseTo(beforeTs, TimeSpan.FromSeconds(5));
        pending.CreatedAt.Should().BeCloseTo(beforeTs, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UpdateOemBrandAsync_TriggersApplyChangeAsync()
    {
        // 准备: 1 个字典 + 1 个引用产品
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(new XrefOemBrand { Id = 1, Brand = "BOSCH", SortOrder = 10 });
        db.Products.Add(CreateProduct(100, "MR100", "BOSCH"));
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // 执行: 更新 sort_order
        await sut.UpdateOemBrandAsync(1, brand: null, sortOrder: 20);

        // 断言: search_index_pending 写入 1 条 (brand_sort_order 变更触发重建)
        var pending = db.SearchIndexPending.ToList();
        pending.Should().HaveCount(1);
        var payload = JsonDocument.Parse(pending[0].Payload);
        payload.RootElement.GetProperty("product_id").GetInt64().Should().Be(100);
    }

    [Fact]
    public async Task DeleteOemBrandAsync_TriggersApplyChangeAsync()
    {
        // 准备: 1 个字典 + 1 个引用产品
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(new XrefOemBrand { Id = 1, Brand = "BOSCH", SortOrder = 10 });
        db.Products.Add(CreateProduct(100, "MR100", "BOSCH"));
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // 执行: 软删除
        await sut.DeleteOemBrandAsync(1);

        // 断言: search_index_pending 写入 1 条 (BrandSortOrder 变 null 触发重建)
        var pending = db.SearchIndexPending.ToList();
        pending.Should().HaveCount(1);
    }

    [Fact]
    public async Task RestoreOemBrandAsync_TriggersApplyChangeAsync()
    {
        // 准备: 1 个已软删字典 + 1 个引用产品
        await using var db = CreateInMemoryDb();
        db.XrefOemBrands.Add(new XrefOemBrand
        {
            Id = 1,
            Brand = "BOSCH",
            SortOrder = 10,
            DeletedAt = DateTime.UtcNow
        });
        db.Products.Add(CreateProduct(100, "MR100", "BOSCH"));
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // 执行: 恢复
        await sut.RestoreOemBrandAsync(1);

        // 断言: search_index_pending 写入 1 条 (BrandSortOrder 从 null 恢复触发重建)
        var pending = db.SearchIndexPending.ToList();
        pending.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateOemBrandAsync_DoesNotTriggerApplyChangeAsync()
    {
        // 准备: 无任何产品引用新 brand
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        // 执行: 新增 brand
        await sut.CreateOemBrandAsync("NEW_BRAND", sortOrder: 10);

        // 断言: 不触发索引重建 (新增 brand 无 xref 引用, 不影响现有产品)
        db.SearchIndexPending.Should().BeEmpty();
    }
}
