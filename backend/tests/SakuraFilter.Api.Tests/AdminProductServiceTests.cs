using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Core.Entities;
using SakuraFilter.Core.Interfaces;
using SakuraFilter.Infrastructure.Data;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// V24-F64 (spec 26.13.7 建议 3): AdminProductService 单元测试
///
/// 测试目标: 覆盖不依赖 PG advisory lock 的核心方法
///   - DeleteAsync: 软删 (IsDiscontinued=true) + 历史记录 + 不可重复删除
///   - RestoreAsync: 恢复 (IsDiscontinued=false) + 历史记录 + 不可恢复未删
///   - GetByIdAsync: 产品详情 + xref + 机型 + 图片 (含 IObjectStorage URL)
///   - GetHistoryAsync: 变更历史 + 分页 cursor + 筛选
///   - EncodeCursor/DecodeCursor: HMAC 签名 + 防篡改
///
/// WHY 不测 CreateAsync/UpdateAsync:
///   - 它们调用 TryAcquireAdvisoryLockAsync (pg_try_advisory_xact_lock raw SQL)
///   - InMemory 不支持 raw SQL, 需 PG 集成测试 (后续 v26+ 补)
///   - 单元测试覆盖其他 5 个公开方法已显著降低回归风险
///
/// 注: 使用 EF Core InMemory + 真实 CursorHmac + Mock IObjectStorage
///   V24-F52 复用: TestProductDbContext 子类 Ignore Alert* 实体
/// </summary>
public class AdminProductServiceTests
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

    private static CursorHmac CreateCursorHmac()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Search:CursorHmacKey"] = "test-cursor-hmac-key-with-32-chars-min-Z9Y8"
            })
            .Build();
        return new CursorHmac(config, NullLogger<CursorHmac>.Instance);
    }

    private static AdminProductService CreateSut(ProductDbContext db, CursorHmac? cursor = null, IObjectStorage? storage = null)
        => new(db, NullLogger<AdminProductService>.Instance, cursor ?? CreateCursorHmac(), storage);

    private static Product CreateProduct(long id = 1, string oem = "OEM001", bool isDiscontinued = false) => new()
    {
        Id = id,
        OemNoDisplay = oem,
        OemNoNormalized = "MR001",
        Mr1 = "MR001",
        Oem2 = "OEM001",
        Type = "oil",
        IsPublished = true,
        IsDiscontinued = isDiscontinued,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static CrossReference CreateXref(long productId, string oemNo3 = "OEM3", string brand = "SAKURA", long id = 1) => new()
    {
        Id = id,
        ProductId = productId,
        OemNo3 = oemNo3,
        OemBrand = brand,
        IsDiscontinued = false,
        CreatedAt = DateTime.UtcNow
    };

    // ==================== DeleteAsync ====================

    [Fact]
    public async Task DeleteAsync_SetsIsDiscontinued_AndWritesHistory()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        await sut.DeleteAsync(1, "admin", default);

        var p = await db.Products.SingleAsync();
        p.IsDiscontinued.Should().BeTrue();
        p.DiscontinuedAt.Should().NotBeNull();
        var history = await db.ProductHistory.SingleAsync();
        history.ChangeType.Should().Be("discontinue");
        history.ChangedBy.Should().Be("admin");
        history.ProductId.Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.DeleteAsync(999, "admin", default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_AlreadyDiscontinued_Throws()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct(isDiscontinued: true));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var act = async () => await sut.DeleteAsync(1, "admin", default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*已下架*");
    }

    // ==================== RestoreAsync ====================

    [Fact]
    public async Task RestoreAsync_ClearsIsDiscontinued_AndWritesHistory()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct(isDiscontinued: true));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        await sut.RestoreAsync(1, "admin", default);

        var p = await db.Products.SingleAsync();
        p.IsDiscontinued.Should().BeFalse();
        p.DiscontinuedAt.Should().BeNull();
        var history = await db.ProductHistory.SingleAsync();
        history.ChangeType.Should().Be("restore");
        history.ChangedBy.Should().Be("admin");
    }

    [Fact]
    public async Task RestoreAsync_NotFound_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.RestoreAsync(999, "admin", default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task RestoreAsync_NotDiscontinued_Throws()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct(isDiscontinued: false));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var act = async () => await sut.RestoreAsync(1, "admin", default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*未下架*");
    }

    // ==================== GetByIdAsync ====================

    [Fact]
    public async Task GetByIdAsync_NotFound_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.GetByIdAsync(999, default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsProductDetail_WithXrefsAndApps()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        db.CrossReferences.Add(CreateXref(1, "OEM3A", "SAKURA"));
        db.CrossReferences.Add(CreateXref(1, "OEM3B", "HONDA", id: 2));
        db.MachineApplications.Add(new MachineApplication
        {
            Id = 1, ProductId = 1, MachineBrand = "TOYOTA", MachineModel = "Corolla", CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var storageMock = new Mock<IObjectStorage>();
        storageMock.Setup(s => s.GetPresignedUrlAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://test.example.com/signed");
        var sut = CreateSut(db, storage: storageMock.Object);

        var result = await sut.GetByIdAsync(1, default);

        result.Id.Should().Be(1);
        result.OemNoDisplay.Should().Be("OEM001");
        result.CrossReferences.Should().HaveCount(2);
        result.MachineApplications.Should().HaveCount(1);
        result.MachineApplications[0].MachineBrand.Should().Be("TOYOTA");
    }

    [Fact]
    public async Task GetByIdAsync_WithImages_ReturnsSignedUrls()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        db.ProductImages.Add(new ProductImage
        {
            Id = 1, ProductId = 1, Slot = 1, ImageKey = "products/primary/OEM001/OEM001-1.jpg",
            ImageRole = "primary", ContentType = "image/jpeg", UploadedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var storageMock = new Mock<IObjectStorage>();
        storageMock.Setup(s => s.GetPresignedUrlAsync("products/primary/OEM001/OEM001-1.jpg", 3600, It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://signed.example.com/primary.jpg");
        var sut = CreateSut(db, storage: storageMock.Object);

        var result = await sut.GetByIdAsync(1, default);

        result.Images.Should().HaveCount(1);
        result.Images[0].ImageUrl.Should().Be("https://signed.example.com/primary.jpg");
    }

    [Fact]
    public async Task GetByIdAsync_StorageFailure_ReturnsEmptyUrl_DoesNotThrow()
    {
        // WHY: per-image try-catch 保留, 单张 OSS 失败不影响其他图, 与原 foreach 语义一致
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        db.ProductImages.Add(new ProductImage
        {
            Id = 1, ProductId = 1, Slot = 1, ImageKey = "k1", ImageRole = "primary", UploadedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var storageMock = new Mock<IObjectStorage>();
        storageMock.Setup(s => s.GetPresignedUrlAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("OSS down"));
        var sut = CreateSut(db, storage: storageMock.Object);

        var result = await sut.GetByIdAsync(1, default);

        result.Images[0].ImageUrl.Should().Be("");  // 失败兜底为空字符串
    }

    [Fact]
    public async Task GetByIdAsync_NoStorage_ReturnsEmptyUrl()
    {
        // WHY: _storage == null 时返回空字符串 (无需 IObjectStorage 依赖的场景, 如测试)
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        db.ProductImages.Add(new ProductImage
        {
            Id = 1, ProductId = 1, Slot = 1, ImageKey = "k1", ImageRole = "primary", UploadedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, storage: null);

        var result = await sut.GetByIdAsync(1, default);

        result.Images[0].ImageUrl.Should().Be("");
    }

    // ==================== EncodeCursor / DecodeCursor ====================

    [Fact]
    public void EncodeCursor_DecodeCursor_RoundTrip_PreservesValues()
    {
        var sut = CreateSut(CreateInMemoryDb());
        var changedAt = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        const long id = 42;

        var encoded = sut.EncodeCursor(changedAt, id);
        var decoded = sut.DecodeCursor(encoded);

        decoded.Should().NotBeNull();
        decoded!.Id.Should().Be(id);
        decoded.ChangedAt.Ticks.Should().Be(changedAt.Ticks);
    }

    [Fact]
    public void DecodeCursor_NullOrEmpty_ReturnsNull()
    {
        var sut = CreateSut(CreateInMemoryDb());

        sut.DecodeCursor(null).Should().BeNull();
        sut.DecodeCursor("").Should().BeNull();
    }

    [Fact]
    public void DecodeCursor_TamperedSignature_ReturnsNull()
    {
        // WHY: 防止客户端篡改 (changedAt, id) 越权访问其他产品历史
        var sut = CreateSut(CreateInMemoryDb());
        var changedAt = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        const long id = 42;
        var encoded = sut.EncodeCursor(changedAt, id);

        // 篡改: 翻转 base64url 中间字符
        var tampered = encoded[..10] + (encoded[10] == 'A' ? 'B' : 'A') + encoded[11..];
        var decoded = sut.DecodeCursor(tampered);

        decoded.Should().BeNull();
    }

    [Fact]
    public void DecodeCursor_InvalidFormat_ReturnsNull()
    {
        var sut = CreateSut(CreateInMemoryDb());

        // 不是合法 base64
        sut.DecodeCursor("!!!not-base64!!!").Should().BeNull();

        // 合法 base64 但格式不对 (缺 sig 段)
        var s = "12345|42";  // 只有 2 段, 缺 sig
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));
        sut.DecodeCursor(b64).Should().BeNull();
    }

    [Fact]
    public void DecodeCursor_DifferentHmacKey_ReturnsNull()
    {
        // WHY: 不同 key 签名不匹配, 验证 CursorHmac 防篡改
        var config1 = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Search:CursorHmacKey"] = "key1-with-32-chars-min-Z9Y8W7V6U5T4S3R2" })
            .Build();
        var config2 = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Search:CursorHmacKey"] = "key2-with-32-chars-min-Z9Y8W7V6U5T4S3R2" })
            .Build();
        var encoder = new AdminProductService(CreateInMemoryDb(), NullLogger<AdminProductService>.Instance, new CursorHmac(config1, NullLogger<CursorHmac>.Instance));
        var decoder = new AdminProductService(CreateInMemoryDb(), NullLogger<AdminProductService>.Instance, new CursorHmac(config2, NullLogger<CursorHmac>.Instance));

        var encoded = encoder.EncodeCursor(DateTime.UtcNow, 1);
        var decoded = decoder.DecodeCursor(encoded);

        decoded.Should().BeNull();
    }

    // ==================== GetHistoryAsync ====================

    [Fact]
    public async Task GetHistoryAsync_NotFound_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.GetHistoryAsync(999, default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsHistory_OrderedDescending()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        var baseTime = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        db.ProductHistory.Add(new ProductHistory { Id = 1, ProductId = 1, ChangeType = "create", ChangedAt = baseTime.AddHours(-2), ChangedBy = "admin" });
        db.ProductHistory.Add(new ProductHistory { Id = 2, ProductId = 1, ChangeType = "update", ChangedAt = baseTime.AddHours(-1), ChangedBy = "operator" });
        db.ProductHistory.Add(new ProductHistory { Id = 3, ProductId = 1, ChangeType = "discontinue", ChangedAt = baseTime, ChangedBy = "admin" });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetHistoryAsync(1, limit: 50, default);

        result.Total.Should().Be(3);
        result.Items.Should().HaveCount(3);
        // 倒序 (最新在前)
        result.Items[0].Id.Should().Be(3);  // discontinue
        result.Items[1].Id.Should().Be(2);  // update
        result.Items[2].Id.Should().Be(1);  // create
    }

    [Fact]
    public async Task GetHistoryAsync_FilterByChangeType_ReturnsMatchingOnly()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        db.ProductHistory.Add(new ProductHistory { Id = 1, ProductId = 1, ChangeType = "create", ChangedAt = DateTime.UtcNow });
        db.ProductHistory.Add(new ProductHistory { Id = 2, ProductId = 1, ChangeType = "update", ChangedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetHistoryAsync(1, limit: 50, changeType: "update", default);

        result.Total.Should().Be(1);
        result.Items.Should().HaveCount(1);
        result.Items[0].ChangeType.Should().Be("update");
    }

    [Fact]
    public async Task GetHistoryAsync_Pagination_ReturnsNextCursor()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        var baseTime = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 5; i++)
        {
            db.ProductHistory.Add(new ProductHistory
            {
                Id = i, ProductId = 1, ChangeType = "update",
                ChangedAt = baseTime.AddMinutes(i), ChangedBy = "admin"
            });
        }
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetHistoryAsync(1, limit: 2, default);

        result.Total.Should().Be(5);
        result.Items.Should().HaveCount(2);  // limit=2
        result.NextCursor.Should().NotBeNull();  // 还有更多
        result.Items[0].Id.Should().Be(5);  // 最新
        result.Items[1].Id.Should().Be(4);
    }

    [Fact]
    public async Task GetHistoryAsync_LastPage_NoNextCursor()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        db.ProductHistory.Add(new ProductHistory { Id = 1, ProductId = 1, ChangeType = "create", ChangedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetHistoryAsync(1, limit: 50, default);

        result.Total.Should().Be(1);
        result.Items.Should().HaveCount(1);
        result.NextCursor.Should().BeNull();  // 末尾无下一页
    }

    [Fact]
    public async Task GetHistoryAsync_WithCursor_ReturnsOlderItems()
    {
        // WHY: keyset pagination, cursor 之后的 (changedAt, id) 严格小于 cursor
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        var baseTime = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 5; i++)
        {
            db.ProductHistory.Add(new ProductHistory
            {
                Id = i, ProductId = 1, ChangeType = "update",
                ChangedAt = baseTime.AddMinutes(i), ChangedBy = "admin"
            });
        }
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        // 第一页 (取 2 条)
        var page1 = await sut.GetHistoryAsync(1, limit: 2, default);
        page1.Items.Should().HaveCount(2);
        page1.Items[0].Id.Should().Be(5);
        page1.Items[1].Id.Should().Be(4);
        page1.NextCursor.Should().NotBeNull();

        // 第二页 (用第一页的 nextCursor)
        var page2 = await sut.GetHistoryAsync(1, 2, null, null, null, page1.NextCursor, default);
        page2.Items.Should().HaveCount(2);
        page2.Items[0].Id.Should().Be(3);
        page2.Items[1].Id.Should().Be(2);
    }

    [Fact]
    public async Task GetHistoryAsync_FilterByDateRange()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        var baseTime = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        db.ProductHistory.Add(new ProductHistory { Id = 1, ProductId = 1, ChangeType = "create", ChangedAt = baseTime.AddHours(-3) });
        db.ProductHistory.Add(new ProductHistory { Id = 2, ProductId = 1, ChangeType = "update", ChangedAt = baseTime.AddHours(-1) });
        db.ProductHistory.Add(new ProductHistory { Id = 3, ProductId = 1, ChangeType = "discontinue", ChangedAt = baseTime });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetHistoryAsync(1, 50, null, baseTime.AddHours(-2), baseTime, null, default);

        result.Total.Should().Be(2);  // -1h 和 0h, 排除 -3h
        result.Items.Should().HaveCount(2);
    }
}
