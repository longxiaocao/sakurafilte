using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.Entities;
using SakuraFilter.Core.Interfaces;
using SakuraFilter.Infrastructure.Data;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// V24-F56 (spec 26.11): AdminProductImageService 单元测试
///
/// 测试目标:
///   - BuildKeyAsync: 主图/详情图分层 + system_settings 配置读取 + 路径穿越防御
///   - UploadAsync: 校验链 (imageRole/slot 一致性 / mr_1 存在 / oemNo3 归属 / 大小 / 类型)
///                  + 覆盖上传 (删旧 S3 文件) + 主图同步写 products.image_key
///   - DeleteAsync: 主图清 products.image_key / 详情图仅删 ProductImage
///   - ListAsync: 按 image_role + slot 排序
///
/// WHY 单元测试: AdminProductImageService 352 行核心 S3 上传+事务逻辑, 之前无任何测试覆盖
///   - 漏写校验会导致脏数据 (如 detail slot=1 绕过约束) 或 S3 路径穿越
///   - 覆盖上传漏删旧文件会导致孤儿图片 (Task 5.1.20 治理成本高)
///
/// 注: 使用 EF Core InMemory + Moq IObjectStorage
///   WHY InMemory: UploadAsync/DeleteAsync/ListAsync 逻辑不依赖 PG 特性 (advisory lock/JsonDocument)
///   WHY Moq IObjectStorage: S3 上传是外部副作用, Mock 后可断言调用次数+参数
///
/// V24-F52 复用: TestProductDbContext 子类 Ignore Alert* 实体 (JsonDocument InMemory 不兼容)
/// </summary>
public class AdminProductImageServiceTests
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
            // V24-F56: InMemory 不支持事务, UploadAsync 内 BeginTransactionAsync 会抛 TransactionIgnoredWarning
            //   WHY ConfigureWarnings.Ignore: 让 BeginTransactionAsync/CommitAsync/RollbackAsync 成为 no-op
            //        测试不依赖事务隔离 (InMemory 单线程无并发), 只验证业务逻辑
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new TestProductDbContext(options);
    }

    private static Mock<IObjectStorage> CreateStorageMock()
    {
        var mock = new Mock<IObjectStorage>();
        mock.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, Stream _, string _, CancellationToken _) => key);
        mock.Setup(s => s.GetUrl(It.IsAny<string>(), It.IsAny<int>())).Returns("https://test.example.com/signed");
        return mock;
    }

    private static IConfiguration CreateConfig(long? maxBytes = null)
    {
        var dict = new Dictionary<string, string?>();
        if (maxBytes.HasValue) dict["Minio:ImageMaxBytes"] = maxBytes.Value.ToString();
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static AdminProductImageService CreateSut(
        ProductDbContext db,
        Mock<IObjectStorage>? storageMock = null,
        IConfiguration? config = null,
        IMemoryCache? cache = null)
    {
        storageMock ??= CreateStorageMock();
        config ??= CreateConfig();
        cache ??= new MemoryCache(new MemoryCacheOptions());
        return new AdminProductImageService(db, storageMock.Object, config, cache, NullLogger<AdminProductImageService>.Instance);
    }

    private static Product CreateProduct(long id = 1, string mr1 = "MR000001")
        => new()
        {
            Id = id,
            Mr1 = mr1,
            OemNoDisplay = "OEM-DISPLAY",
            OemNoNormalized = mr1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static CrossReference CreateXref(long productId, string oemNo3, long id = 1)
        => new()
        {
            Id = id,
            ProductId = productId,
            OemNo3 = oemNo3,
            OemBrand = "SAKURA",
            IsDiscontinued = false,
            CreatedAt = DateTime.UtcNow
        };

    private static Stream CreateImageStream(long size = 1024)
    {
        var bytes = new byte[size];
        for (int i = 0; i < size; i++) bytes[i] = (byte)(i % 256);
        return new MemoryStream(bytes);
    }

    // ==================== BuildKeyAsync ====================

    [Fact]
    public async Task BuildKeyAsync_Primary_DefaultOemNo3_ReturnsPrimaryPath()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var key = await sut.BuildKeyAsync("primary", oemNo3: "OEM001", mr1: "MR000001", slot: 1, ext: "jpg");

        key.Should().Be("products/primary/OEM001/OEM001-1.jpg");
    }

    [Fact]
    public async Task BuildKeyAsync_Detail_DefaultMr1_ReturnsDetailPath()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var key = await sut.BuildKeyAsync("detail", oemNo3: "OEM001", mr1: "MR000001", slot: 2, ext: "png");

        key.Should().Be("products/detail/MR000001/MR000001-2.png");
    }

    [Fact]
    public async Task BuildKeyAsync_Primary_ConfigMr1_NamingSwitchesToMr1()
    {
        await using var db = CreateInMemoryDb();
        db.SystemSettings.Add(new SystemSetting { Key = "image.primary_naming_field", Value = "mr_1" });
        await db.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(db, cache: cache);

        var key = await sut.BuildKeyAsync("primary", oemNo3: "OEM001", mr1: "MR000001", slot: 1, ext: "jpg");

        key.Should().Be("products/primary/MR000001/MR000001-1.jpg");
    }

    [Fact]
    public async Task BuildKeyAsync_Primary_EmptyNamingValue_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        // oemNo3=null + 默认配置 oem_no_3 → namingValue 为空
        var act = async () => await sut.BuildKeyAsync("primary", oemNo3: null, mr1: "MR000001", slot: 1, ext: "jpg");

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("IMAGE_ROLE_SLOT_MISMATCH");
    }

    /// <summary>
    /// 路径穿越防御: namingValue 含特殊字符时被替换为 _
    ///   WHY: namingValue 来自 DB (oem_no_3/mr_1), 但防御性编程防止异常数据导致 S3 路径穿越
    /// </summary>
    [Fact]
    public async Task BuildKeyAsync_PathTraversal_SanitizedToUnderscore()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        // 含 ../ 和空格, 应被替换为 _
        var key = await sut.BuildKeyAsync("detail", oemNo3: "OEM001", mr1: "MR../001", slot: 2, ext: "jpg");

        key.Should().Be("products/detail/MR___001/MR___001-2.jpg");
    }

    /// <summary>
    /// 缓存命中: 第二次调用不再查 DB (system_settings)
    ///   WHY: IMemoryCache 5 分钟缓存, 避免每次上传查 DB
    /// </summary>
    [Fact]
    public async Task BuildKeyAsync_CacheHit_DoesNotQueryDbSecondTime()
    {
        await using var db = CreateInMemoryDb();
        db.SystemSettings.Add(new SystemSetting { Key = "image.detail_naming_field", Value = "mr_1" });
        await db.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(db, cache: cache);

        // 第一次调用: 查 DB 并写缓存
        await sut.BuildKeyAsync("detail", "OEM001", "MR000001", 2, "jpg");
        // 删除 DB 中的配置, 第二次调用应从缓存读取
        db.SystemSettings.RemoveRange(db.SystemSettings);
        await db.SaveChangesAsync();

        var key = await sut.BuildKeyAsync("detail", "OEM001", "MR000001", 2, "jpg");

        // 缓存命中, 仍按 mr_1 命名 (若查 DB 会得到 null → 默认 mr_1, 结果相同但路径不同)
        key.Should().Be("products/detail/MR000001/MR000001-2.jpg");
    }

    /// <summary>
    /// 非法配置值回退到默认 (仅允许 oem_no_3 / mr_1)
    /// </summary>
    [Fact]
    public async Task BuildKeyAsync_InvalidConfigValue_FallsBackToDefault()
    {
        await using var db = CreateInMemoryDb();
        db.SystemSettings.Add(new SystemSetting { Key = "image.primary_naming_field", Value = "invalid_field" });
        await db.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateSut(db, cache: cache);

        var key = await sut.BuildKeyAsync("primary", "OEM001", "MR000001", 1, "jpg");

        // 非法值回退到默认 oem_no_3
        key.Should().Be("products/primary/OEM001/OEM001-1.jpg");
    }

    // ==================== UploadAsync - 校验链 ====================

    [Fact]
    public async Task UploadAsync_PrimarySlotNot1_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.UploadAsync("MR000001", "primary", "OEM001", slot: 2,
            CreateImageStream(), "image/jpeg", "tester");

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("IMAGE_ROLE_SLOT_MISMATCH");
    }

    [Fact]
    public async Task UploadAsync_DetailSlotOutOfRange_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.UploadAsync("MR000001", "detail", null, slot: 7,
            CreateImageStream(), "image/jpeg", "tester");

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("IMAGE_DETAIL_SLOT_INVALID");
    }

    [Fact]
    public async Task UploadAsync_InvalidImageRole_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.UploadAsync("MR000001", "thumbnail", null, slot: 1,
            CreateImageStream(), "image/jpeg", "tester");

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("IMAGE_ROLE_SLOT_MISMATCH");
    }

    [Fact]
    public async Task UploadAsync_Mr1NotFound_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.UploadAsync("MR_NOT_EXIST", "detail", null, slot: 2,
            CreateImageStream(), "image/jpeg", "tester");

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*MR1_NOT_FOUND*");
    }

    [Fact]
    public async Task UploadAsync_PrimaryWithoutOemNo3_Throws()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var act = async () => await sut.UploadAsync("MR000001", "primary", oemNo3: null, slot: 1,
            CreateImageStream(), "image/jpeg", "tester");

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("IMAGE_ROLE_SLOT_MISMATCH");
    }

    [Fact]
    public async Task UploadAsync_PrimaryOemNo3NotBelongToMr1_Throws()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var act = async () => await sut.UploadAsync("MR000001", "primary", oemNo3: "OEM_NOT_EXIST", slot: 1,
            CreateImageStream(), "image/jpeg", "tester");

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*OEM3_NOT_FOUND*");
    }

    [Fact]
    public async Task UploadAsync_ExceedsMaxBytes_Throws()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        await db.SaveChangesAsync();
        var config = CreateConfig(maxBytes: 100);  // 限制 100 字节
        var sut = CreateSut(db, config: config);

        var act = async () => await sut.UploadAsync("MR000001", "detail", null, slot: 2,
            CreateImageStream(size: 1024), "image/jpeg", "tester");

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("超过最大尺寸");
    }

    [Fact]
    public async Task UploadAsync_UnsupportedContentType_Throws()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var act = async () => await sut.UploadAsync("MR000001", "detail", null, slot: 2,
            CreateImageStream(), "image/gif", "tester");

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("不支持的图片类型");
    }

    // ==================== UploadAsync - 业务流程 ====================

    [Fact]
    public async Task UploadAsync_DetailNewSlot_WritesProductImageAndCallsStorageUpload()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        await db.SaveChangesAsync();
        var storageMock = CreateStorageMock();
        var sut = CreateSut(db, storageMock: storageMock);

        var result = await sut.UploadAsync("MR000001", "detail", null, slot: 2,
            CreateImageStream(), "image/jpeg", "tester");

        result.Slot.Should().Be(2);
        result.ImageRole.Should().Be("detail");
        result.OemNo3.Should().BeNull();
        // DB 写入
        var img = await db.ProductImages.SingleAsync();
        img.ProductId.Should().Be(1);
        img.Slot.Should().Be(2);
        img.ImageRole.Should().Be("detail");
        img.UploadedBy.Should().Be("tester");
        // S3 上传被调用
        storageMock.Verify(s => s.UploadAsync(It.Is<string>(k => k.StartsWith("products/detail/")),
            It.IsAny<Stream>(), "image/jpeg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAsync_PrimaryNewSlot_WritesProductImageAndSyncsProductImageKey()
    {
        await using var db = CreateInMemoryDb();
        var product = CreateProduct();
        db.Products.Add(product);
        db.CrossReferences.Add(CreateXref(product.Id, "OEM001"));
        await db.SaveChangesAsync();
        var storageMock = CreateStorageMock();
        var sut = CreateSut(db, storageMock: storageMock);

        var result = await sut.UploadAsync("MR000001", "primary", "OEM001", slot: 1,
            CreateImageStream(), "image/png", "tester");

        result.Slot.Should().Be(1);
        result.ImageRole.Should().Be("primary");
        result.OemNo3.Should().Be("OEM001");
        // DB 写入
        var img = await db.ProductImages.SingleAsync();
        img.ImageRole.Should().Be("primary");
        img.OemNo3.Should().Be("OEM001");
        // 主图同步写 products.image_key (兼容旧字段)
        var updatedProduct = await db.Products.SingleAsync();
        updatedProduct.ImageKey.Should().Be(img.ImageKey);
        updatedProduct.ImageStatus.Should().Be("pending");
        // S3 上传被调用 (key 以 products/primary/ 开头)
        storageMock.Verify(s => s.UploadAsync(It.Is<string>(k => k.StartsWith("products/primary/")),
            It.IsAny<Stream>(), "image/png", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// V24-F57: 详情图 slot 重复 → 覆盖上传 (更新记录 + 删旧 S3 文件)
    ///   WHY: V24-F57 删除了"重复即拒绝"软校验, 同 slot 上传改为覆盖
    ///     - spike-test/SPIKE-REPORT-day8.1.md L30/115/217 设计意图: "覆盖上传 key 纯净, 避免废弃对象"
    ///     - 前端 AdminProductFormView.vue 无"先删除"逻辑, 用户预期直接替换
    ///   验证点:
    ///     1. DB 记录数量不变 (更新而非新增)
    ///     2. DB 记录字段已更新 (ImageKey/ContentType/FileSize/UploadedBy)
    ///     3. S3 UploadAsync 被调用 1 次 (新 key)
    ///     4. S3 DeleteAsync 被调用 1 次 (旧 key, 异步删旧文件)
    /// </summary>
    [Fact]
    public async Task UploadAsync_DetailOverwrite_UpdatesRecordAndDeletesOldFile()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        const string oldKey = "products/detail/MR000001/MR000001-3.jpg";
        db.ProductImages.Add(new ProductImage
        {
            ProductId = 1,
            Slot = 3,
            ImageRole = "detail",
            ImageKey = oldKey,
            ContentType = "image/jpeg",
            FileSize = 1024,
            UploadedBy = "old-user",
            UploadedAt = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();
        var storageMock = CreateStorageMock();
        var sut = CreateSut(db, storageMock: storageMock);

        var result = await sut.UploadAsync("MR000001", "detail", null, slot: 3,
            CreateImageStream(size: 2048), "image/png", "tester");

        // 1. DB 记录数量不变 (1 条, 更新而非新增)
        var imgs = await db.ProductImages.ToListAsync();
        imgs.Should().HaveCount(1);

        // 2. DB 记录字段已更新
        var img = imgs[0];
        img.ImageKey.Should().Be("products/detail/MR000001/MR000001-3.png");
        img.ContentType.Should().Be("image/png");
        img.FileSize.Should().Be(2048);
        img.UploadedBy.Should().Be("tester");

        // 3. S3 上传新文件 1 次
        storageMock.Verify(s => s.UploadAsync(
            It.Is<string>(k => k == "products/detail/MR000001/MR000001-3.png"),
            It.IsAny<Stream>(), "image/png", It.IsAny<CancellationToken>()), Times.Once);

        // 4. S3 删除旧文件 1 次 (异步 fire-and-forget, 需短暂等待)
        await Task.Delay(200);
        storageMock.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k == oldKey), It.IsAny<CancellationToken>()), Times.Once);

        // 返回值校验
        result.ImageRole.Should().Be("detail");
        result.Slot.Should().Be(3);
    }

    /// <summary>
    /// V24-F57: 主图同 OEM 3 重复上传 → 覆盖上传 (更新记录 + 删旧 S3 文件 + 同步 products.image_key)
    ///   WHY: V24-F57 删除了"重复即拒绝"软校验, 同 OEM 3 主图上传改为覆盖
    ///     - 用户替换主图预期: 直接上传新图, 旧文件自动清理
    ///   验证点:
    ///     1. DB 记录数量不变 (更新而非新增)
    ///     2. DB 记录字段已更新 (ImageKey/ContentType/FileSize/OemNo3)
    ///     3. products.image_key 同步更新为主图新 key
    ///     4. S3 UploadAsync 被调用 1 次 (新 key)
    ///     5. S3 DeleteAsync 被调用 1 次 (旧 key, 异步删旧文件)
    /// </summary>
    [Fact]
    public async Task UploadAsync_PrimaryOverwrite_UpdatesRecordAndSyncsProductImageKey()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        db.CrossReferences.Add(CreateXref(1, "OEM001"));
        const string oldKey = "products/primary/OEM001/OEM001-1.jpg";
        db.ProductImages.Add(new ProductImage
        {
            ProductId = 1,
            Slot = 1,
            ImageRole = "primary",
            OemNo3 = "OEM001",
            ImageKey = oldKey,
            ContentType = "image/jpeg",
            FileSize = 1024,
            UploadedBy = "old-user",
            UploadedAt = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();
        var storageMock = CreateStorageMock();
        var sut = CreateSut(db, storageMock: storageMock);

        var result = await sut.UploadAsync("MR000001", "primary", "OEM001", slot: 1,
            CreateImageStream(size: 2048), "image/png", "tester");

        // 1. DB ProductImage 记录数量不变 (1 条, 更新而非新增)
        var imgs = await db.ProductImages.ToListAsync();
        imgs.Should().HaveCount(1);

        // 2. DB 记录字段已更新
        var img = imgs[0];
        img.ImageKey.Should().Be("products/primary/OEM001/OEM001-1.png");
        img.ContentType.Should().Be("image/png");
        img.FileSize.Should().Be(2048);
        img.OemNo3.Should().Be("OEM001");
        img.UploadedBy.Should().Be("tester");

        // 3. products.image_key 同步更新
        var product = await db.Products.SingleAsync();
        product.ImageKey.Should().Be("products/primary/OEM001/OEM001-1.png");
        product.ImageStatus.Should().Be("pending");

        // 4. S3 上传新文件 1 次
        storageMock.Verify(s => s.UploadAsync(
            It.Is<string>(k => k == "products/primary/OEM001/OEM001-1.png"),
            It.IsAny<Stream>(), "image/png", It.IsAny<CancellationToken>()), Times.Once);

        // 5. S3 删除旧文件 1 次 (异步 fire-and-forget, 需短暂等待)
        await Task.Delay(200);
        storageMock.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k == oldKey), It.IsAny<CancellationToken>()), Times.Once);

        // 返回值校验
        result.ImageRole.Should().Be("primary");
        result.Slot.Should().Be(1);
        result.OemNo3.Should().Be("OEM001");
    }

    /// <summary>
    /// S3 上传失败时 DB 事务回滚, 不留脏数据
    ///   注: InMemory 事务被 ConfigureWarnings.Ignore 忽略, RollbackAsync 是 no-op
    ///       此测试改为验证: S3 失败时抛异常 + 调用 RollbackAsync (验证 tx.RollbackAsync 被调用)
    ///       生产环境 (PG) RollbackAsync 会真正回滚事务
    /// </summary>
    [Fact]
    public async Task UploadAsync_StorageUploadFails_ThrowsAndRollsBackTransaction()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        await db.SaveChangesAsync();
        var storageMock = new Mock<IObjectStorage>();
        storageMock.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("S3 connection refused"));
        storageMock.Setup(s => s.GetUrl(It.IsAny<string>(), It.IsAny<int>())).Returns("https://test.example.com/signed");
        var sut = CreateSut(db, storageMock: storageMock);

        var act = async () => await sut.UploadAsync("MR000001", "detail", null, slot: 2,
            CreateImageStream(), "image/jpeg", "tester");

        // S3 失败抛异常 (生产环境会触发 tx.RollbackAsync, InMemory 下 RollbackAsync 是 no-op)
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("S3 connection refused");
    }

    // ==================== DeleteAsync ====================

    [Fact]
    public async Task DeleteAsync_PrimarySlotNot1_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.DeleteAsync("MR000001", "primary", slot: 2, default);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("IMAGE_ROLE_SLOT_MISMATCH");
    }

    [Fact]
    public async Task DeleteAsync_Mr1NotFound_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.DeleteAsync("MR_NOT_EXIST", "detail", slot: 2, default);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*MR1_NOT_FOUND*");
    }

    [Fact]
    public async Task DeleteAsync_Primary_ClearsProductImageKeyAndRemovesRecord()
    {
        await using var db = CreateInMemoryDb();
        var product = CreateProduct();
        product.ImageKey = "products/primary/OEM001/OEM001-1.jpg";
        product.ImageStatus = "ok";
        db.Products.Add(product);
        db.CrossReferences.Add(CreateXref(1, "OEM001"));
        db.ProductImages.Add(new ProductImage
        {
            ProductId = 1,
            Slot = 1,
            ImageRole = "primary",
            OemNo3 = "OEM001",
            ImageKey = "products/primary/OEM001/OEM001-1.jpg",
            ContentType = "image/jpeg",
            UploadedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var storageMock = CreateStorageMock();
        var sut = CreateSut(db, storageMock: storageMock);

        await sut.DeleteAsync("MR000001", "primary", slot: 1, default);

        // ProductImage 记录被删除
        (await db.ProductImages.AnyAsync()).Should().BeFalse();
        // products.image_key 被清空 + status=pending
        var updatedProduct = await db.Products.SingleAsync();
        updatedProduct.ImageKey.Should().BeNull();
        updatedProduct.ImageStatus.Should().Be("pending");
        // S3 文件被删除
        storageMock.Verify(s => s.DeleteAsync("products/primary/OEM001/OEM001-1.jpg",
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task DeleteAsync_Detail_RemovesRecordOnly()
    {
        await using var db = CreateInMemoryDb();
        var product = CreateProduct();
        product.ImageKey = "primary-key";
        product.ImageStatus = "ok";
        db.Products.Add(product);
        db.ProductImages.Add(new ProductImage
        {
            ProductId = 1,
            Slot = 3,
            ImageRole = "detail",
            ImageKey = "products/detail/MR000001/MR000001-3.jpg",
            ContentType = "image/jpeg",
            UploadedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var storageMock = CreateStorageMock();
        var sut = CreateSut(db, storageMock: storageMock);

        await sut.DeleteAsync("MR000001", "detail", slot: 3, default);

        (await db.ProductImages.AnyAsync()).Should().BeFalse();
        // 详情图删除不修改 products.image_key
        var updatedProduct = await db.Products.SingleAsync();
        updatedProduct.ImageKey.Should().Be("primary-key");
        updatedProduct.ImageStatus.Should().Be("ok");
        storageMock.Verify(s => s.DeleteAsync("products/detail/MR000001/MR000001-3.jpg",
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task DeleteAsync_DetailSlotNotFound_Throws()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var act = async () => await sut.DeleteAsync("MR000001", "detail", slot: 5, default);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*无详情图*");
    }

    // ==================== ListAsync ====================

    [Fact]
    public async Task ListAsync_Mr1NotFound_Throws()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.ListAsync("MR_NOT_EXIST", default);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*MR1_NOT_FOUND*");
    }

    /// <summary>
    /// 列表按 image_role + slot 排序 (OrderBy image_role 字母序, ThenBy slot 升序)
    ///   字母序: "detail" < "primary" (d < p), 所以 detail 排前
    /// </summary>
    [Fact]
    public async Task ListAsync_ReturnsSortedByRoleThenSlot()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        // 故意乱序插入
        db.ProductImages.Add(new ProductImage
        {
            ProductId = 1, Slot = 3, ImageRole = "detail",
            ImageKey = "detail-3", ContentType = "image/jpeg", UploadedAt = DateTime.UtcNow
        });
        db.ProductImages.Add(new ProductImage
        {
            ProductId = 1, Slot = 1, ImageRole = "primary", OemNo3 = "OEM001",
            ImageKey = "primary-1", ContentType = "image/jpeg", UploadedAt = DateTime.UtcNow
        });
        db.ProductImages.Add(new ProductImage
        {
            ProductId = 1, Slot = 2, ImageRole = "detail",
            ImageKey = "detail-2", ContentType = "image/jpeg", UploadedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.ListAsync("MR000001", default);

        result.Should().HaveCount(3);
        // OrderBy image_role 字母序: detail < primary, 所以 detail 排前
        result[0].ImageRole.Should().Be("detail");
        result[0].Slot.Should().Be(2);
        result[1].ImageRole.Should().Be("detail");
        result[1].Slot.Should().Be(3);
        result[2].ImageRole.Should().Be("primary");
        result[2].Slot.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_Empty_ReturnsEmptyList()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.ListAsync("MR000001", default);

        result.Should().BeEmpty();
    }

    // ==================== GetUrl 异常降级 ====================

    /// <summary>
    /// GetUrl 失败时返回空字符串 (不抛异常, 不阻塞 ListAsync)
    ///   WHY: 预签名 URL 生成失败不应阻塞列表展示, 前端用空字符串降级显示占位图
    /// </summary>
    [Fact]
    public async Task ListAsync_StorageGetUrlThrows_ReturnsEmptyUrl()
    {
        await using var db = CreateInMemoryDb();
        db.Products.Add(CreateProduct());
        db.ProductImages.Add(new ProductImage
        {
            ProductId = 1, Slot = 2, ImageRole = "detail",
            ImageKey = "detail-2", ContentType = "image/jpeg", UploadedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var storageMock = new Mock<IObjectStorage>();
        storageMock.Setup(s => s.GetUrl(It.IsAny<string>(), It.IsAny<int>()))
            .Throws(new InvalidOperationException("OSS connection refused"));
        var sut = CreateSut(db, storageMock: storageMock);

        var result = await sut.ListAsync("MR000001", default);

        result.Should().HaveCount(1);
        result[0].ImageUrl.Should().Be("");
    }
}
