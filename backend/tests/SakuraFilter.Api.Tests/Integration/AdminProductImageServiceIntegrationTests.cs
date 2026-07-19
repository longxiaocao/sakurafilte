using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Core.Entities;
using SakuraFilter.Core.Interfaces;
using SakuraFilter.Infrastructure.Data;
using SakuraFilter.Api.Services;
using Xunit;
using Xunit.Abstractions;

namespace SakuraFilter.Api.Tests.Integration;

/// <summary>
/// V24-F83 (spec 26.17.2 P1-7): AdminProductImageService PG 集成测试
///
/// 覆盖单元测试 (AdminProductImageServiceTests.cs) 无法验证的 PG 特性:
///   - V24-F57 覆盖上传 UPDATE 路径 (vs INSERT 路径, 主图/详情图)
///   - DB 唯一约束 uq_product_images_primary (oem_no_3) / uq_product_images_detail_slot (product_id, slot)
///   - 并发竞态: 两个请求同时上传同 slot, 第二个撞 23505 → DbUpdateException (PostgresException)
///   - 23505 → ProblemDetailsFactory 映射为 409 ERR_DB_CONFLICT (端点层职责, 此处仅验证 SqlState)
///   - 主图同步 products.image_key 字段 (兼容旧字段)
///
/// 启用条件: 环境变量 PG_TEST_CONNECTION_STRING 已配置
/// 跳过条件: 未配置时测试方法直接 return (通过 IsEnabled 守卫)
/// 关联 spec: 26.17.2 P1-7, AdminProductImageService.cs L126-311 (UploadAsync + DeleteAsync)
/// </summary>
[Trait("Category", "Integration")]
[Collection("PgSequential")]
public class AdminProductImageServiceIntegrationTests : PgIntegrationTestBase
{
    private readonly ITestOutputHelper _output;

    public AdminProductImageServiceIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ========== 辅助构造 ==========

    private static ProductFormDto CreateForm(string mr1, string oem2 = "OEM001", List<XrefInput>? xrefs = null)
    {
        return new ProductFormDto
        {
            Oem2 = oem2,
            ProductName1 = "测试产品",
            Type = "oil",
            Mr1 = mr1,
            IsPublished = true,
            D1Mm = 100m, H1Mm = 200m,
            CrossReferences = xrefs ?? new()
        };
    }

    private static XrefInput CreateXref(string brand, string oem3, int sortOrder = 0, string? oem2 = null)
        => new("测试PN1", brand, oem3, oem2 ?? "OEM001", sortOrder, "commercial", true);

    /// <summary>创建 Mock IObjectStorage (上传/删除/URL 全部 Mock, 不真实调用 S3/MinIO)</summary>
    private static Mock<IObjectStorage> CreateStorageMock()
    {
        var mock = new Mock<IObjectStorage>();
        mock.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, Stream _, string _, CancellationToken _) => key);
        mock.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.GetUrl(It.IsAny<string>(), It.IsAny<int>()))
            .Returns("https://test.example.com/signed");
        mock.Setup(s => s.GetPresignedUrlAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://test.example.com/presigned");
        return mock;
    }

    private static IConfiguration CreateConfig(long? maxBytes = null)
    {
        var dict = new Dictionary<string, string?>();
        if (maxBytes.HasValue) dict["Minio:ImageMaxBytes"] = maxBytes.Value.ToString();
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    /// <summary>创建 AdminProductImageService SUT (连接真实 PG + Mock S3)</summary>
    private AdminProductImageService CreateSut(ProductDbContext? db = null, Mock<IObjectStorage>? storageMock = null)
    {
        db ??= CreateDbContext();
        storageMock ??= CreateStorageMock();
        // WHY 用真实 MemoryCache: GetNamingFieldAsync 调 _cache.Set 必须指定 Size (V24-F75)
        //   若用 NullCache, _cache.Set 会抛 InvalidOperationException, 导致 BuildKeyAsync 失败
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new AdminProductImageService(
            db,
            storageMock.Object,
            CreateConfig(),
            cache,
            NullLogger<AdminProductImageService>.Instance);
    }

    /// <summary>创建一个 1KB 的图片流 (模拟上传内容)</summary>
    private static Stream CreateImageStream(int bytes = 1024)
    {
        var buf = new byte[bytes];
        for (var i = 0; i < bytes; i++) buf[i] = (byte)(i % 256);
        return new MemoryStream(buf);
    }

    /// <summary>通过 AdminProductService.CreateAsync 预插产品 + xref (满足外键 + OEM 3 校验)</summary>
    private async Task<(long productId, string mr1, string oem3)> SetupProductAsync(string mr1, string oem3)
    {
        await using var db = CreateDbContext();
        var adminSvc = new AdminProductService(
            db,
            NullLogger<AdminProductService>.Instance,
            CreateCursorHmac(),
            CreateStorageMock().Object);
        var form = CreateForm(mr1, "OEM001", xrefs: new() { CreateXref("Bosch", oem3, 0) });
        var result = await adminSvc.CreateAsync(form, "test-user", default);
        return (result.Id, mr1, oem3);
    }

    /// <summary>预插一条 ProductImage (用于覆盖上传/删除测试的初始状态)</summary>
    private async Task<ProductImage> InsertImageAsync(long productId, string imageRole, string? oemNo3, short slot, string key)
    {
        await using var db = CreateDbContext();
        var img = new ProductImage
        {
            ProductId = productId,
            Slot = slot,
            ImageKey = key,
            FileSize = 1024,
            ContentType = "image/jpeg",
            IsPrimary = imageRole == "primary",
            DisplayOrder = slot,
            UploadedAt = DateTime.UtcNow,
            UploadedBy = "setup",
            OemNo3 = imageRole == "primary" ? oemNo3 : null,
            ImageRole = imageRole
        };
        db.ProductImages.Add(img);
        // 同步 product.image_key (兼容旧字段)
        if (imageRole == "primary")
        {
            var product = await db.Products.FirstAsync(p => p.Id == productId);
            product.ImageKey = key;
            product.ImageStatus = "ok";
        }
        await db.SaveChangesAsync();
        return img;
    }

    // ========== 测试用例 ==========

    /// <summary>
    /// 场景: 首次上传主图, 验证 INSERT 路径 + products.image_key 同步
    /// 覆盖: spec V2 Task 3.2 - UploadAsync 新增路径
    /// </summary>
    [Fact]
    public async Task UploadAsync_NewPrimary_InsertsNewRecordAndSyncsProductImageKey_Integration()
    {
        if (!IsEnabled) { _output.WriteLine("Skip: PG_TEST_CONNECTION_STRING 未配置"); return; }

        // Arrange: 预插 product + xref (OEM3=OEMNEW001)
        var (productId, mr1, oem3) = await SetupProductAsync("MRIMG0001", "OEMNEW001");
        var storageMock = CreateStorageMock();
        var sut = CreateSut(storageMock: storageMock);

        // Act
        var result = await sut.UploadAsync(mr1, "primary", oem3, slot: 1,
            CreateImageStream(), "image/jpeg", "tester", default);

        // Assert: SUT 返回
        result.Slot.Should().Be(1);
        result.ImageRole.Should().Be("primary");
        result.OemNo3.Should().Be(oem3);
        result.ImageKey.Should().StartWith("products/primary/");

        // Assert: DB 状态 (独立 DbContext 验证)
        await using var verifyDb = CreateDbContext();
        var img = await verifyDb.ProductImages.SingleAsync(i => i.ProductId == productId);
        img.ImageRole.Should().Be("primary");
        img.Slot.Should().Be(1);
        img.OemNo3.Should().Be(oem3);
        img.UploadedBy.Should().Be("tester");

        var product = await verifyDb.Products.FirstAsync(p => p.Id == productId);
        product.ImageKey.Should().Be(img.ImageKey, "主图上传应同步 products.image_key");

        // S3 Upload 被调用一次
        storageMock.Verify(s => s.UploadAsync(
            It.Is<string>(k => k.StartsWith("products/primary/")),
            It.IsAny<Stream>(), "image/jpeg", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 场景: 同一 OEM 3 再次上传主图 (覆盖上传), 验证 UPDATE 路径 (非 INSERT)
    /// 覆盖: spec V24-F57 - 覆盖上传死代码修复
    ///   - 旧记录 ImageKey/FileSize/UploadedAt 被更新
    ///   - DB 中仅 1 条 primary 记录 (而非新增第二条)
    ///   - products.image_key 同步为新 key
    /// </summary>
    [Fact]
    public async Task UploadAsync_OverwritePrimary_ReplacesOldImage_Integration()
    {
        if (!IsEnabled) { _output.WriteLine("Skip: PG_TEST_CONNECTION_STRING 未配置"); return; }

        // Arrange: 预插 product + xref + 已有一条 primary 记录
        var (productId, mr1, oem3) = await SetupProductAsync("MRIMG0002", "OEMOVER001");
        const string oldKey = "products/primary/OEMOVER001/OEMOVER001-1.jpg";
        await InsertImageAsync(productId, "primary", oem3, slot: 1, oldKey);

        var storageMock = CreateStorageMock();
        var sut = CreateSut(storageMock: storageMock);

        // Act: 覆盖上传 (使用 png, 新 key 不同)
        var result = await sut.UploadAsync(mr1, "primary", oem3, slot: 1,
            CreateImageStream(2048), "image/png", "overwriter", default);

        // Assert: SUT 返回的 ImageKey 应不同于 oldKey
        result.ImageKey.Should().NotBe(oldKey, "覆盖上传应生成新 key (按 ext 分层: png vs jpg)");
        result.ImageKey.Should().EndWith(".png");

        // Assert: DB 中仍只有 1 条 primary 记录, ImageKey 被更新
        await using var verifyDb = CreateDbContext();
        var imgs = await verifyDb.ProductImages
            .Where(i => i.ProductId == productId && i.ImageRole == "primary")
            .ToListAsync();
        imgs.Should().HaveCount(1, "覆盖上传应 UPDATE 旧记录, 而非 INSERT 新记录");
        imgs[0].ImageKey.Should().Be(result.ImageKey, "ImageKey 应被更新为新 key");
        imgs[0].FileSize.Should().Be(2048, "FileSize 应被更新");
        imgs[0].ContentType.Should().Be("image/png");
        imgs[0].UploadedBy.Should().Be("overwriter");

        var product = await verifyDb.Products.FirstAsync(p => p.Id == productId);
        product.ImageKey.Should().Be(result.ImageKey, "products.image_key 应同步为新 key");

        // S3 Upload 被调用一次 (新 key)
        storageMock.Verify(s => s.UploadAsync(
            It.Is<string>(k => k != oldKey && k.EndsWith(".png")),
            It.IsAny<Stream>(), "image/png", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 场景: 同一 slot 详情图再次上传 (覆盖上传), 验证 UPDATE 路径
    /// 覆盖: spec V24-F57 - 详情图 slot 覆盖上传
    /// </summary>
    [Fact]
    public async Task UploadAsync_OverwriteDetailSlot_ReplacesOldImage_Integration()
    {
        if (!IsEnabled) { _output.WriteLine("Skip: PG_TEST_CONNECTION_STRING 未配置"); return; }

        // Arrange: 预插 product + xref + 已有一条 detail slot=2 记录
        var (productId, mr1, _) = await SetupProductAsync("MRIMG0003", "OEMDET001");
        const string oldKey = "products/detail/MRIMG0003/MRIMG0003-2.jpg";
        await InsertImageAsync(productId, "detail", null, slot: 2, oldKey);

        var storageMock = CreateStorageMock();
        var sut = CreateSut(storageMock: storageMock);

        // Act: 覆盖上传 slot=2 (用 webp)
        var result = await sut.UploadAsync(mr1, "detail", null, slot: 2,
            CreateImageStream(4096), "image/webp", "overwriter", default);

        // Assert
        result.ImageKey.Should().NotBe(oldKey);
        result.ImageKey.Should().EndWith(".webp");

        await using var verifyDb = CreateDbContext();
        var imgs = await verifyDb.ProductImages
            .Where(i => i.ProductId == productId && i.ImageRole == "detail")
            .ToListAsync();
        imgs.Should().HaveCount(1, "覆盖上传应 UPDATE 旧记录");
        imgs[0].Slot.Should().Be(2);
        imgs[0].ImageKey.Should().Be(result.ImageKey);
        imgs[0].FileSize.Should().Be(4096);
        imgs[0].ContentType.Should().Be("image/webp");
    }

    /// <summary>
    /// 场景: 并发竞态 - 两个 PG 事务同时 INSERT 同一 (product_id, slot) 的详情图
    /// 覆盖: spec 26.17.2 P1-7 - 23505 唯一约束兜底
    ///
    /// 设计说明 (WHY 用 raw SQL 而非 EF Core 并发):
    ///   - AdminProductImageService.UploadAsync 内部用 EF Core + BeginTransactionAsync
    ///   - 两个并行 DbContext 调用 UploadAsync 时, EF Core 内部时序难以稳定复现 23505:
    ///     * task1 可能先 commit, task2 的 FirstOrDefaultAsync 读到 task1 写入的记录 → 走 UPDATE 路径 (不撞 23505)
    ///     * 即使两个 Task 都查到 old=null, task2 的 INSERT 在 task1 commit 后才被阻塞, 但 EF Core 可能直接抛 ObjectDisposedException
    ///   - 用 raw SQL 两个并行 NpgsqlTransaction 可稳定复现:
    ///     * tx1: BEGIN → INSERT (product_id=1, slot=2) → 不 commit, 持有行锁
    ///     * tx2: BEGIN → INSERT (product_id=1, slot=2) → 阻塞等待 tx1 释放
    ///     * tx1: COMMIT → tx2 立即抛 23505 unique_violation
    ///
    /// 端到端 23505 → 409 ERR_DB_CONFLICT 映射由 ProblemDetailsFactoryTests.cs L134 单元测试覆盖
    ///   (传 PostgresException SqlState=23505 → 验证返回 409 + errorCode=ERR_DB_CONFLICT)
    ///
    /// 此测试验证前置条件: uq_product_images_detail_slot 唯一约束存在 + 23505 触发
    /// </summary>
    [Fact]
    public async Task ConcurrentInsertSameDetailSlot_SecondThrows23505_Integration()
    {
        if (!IsEnabled) { _output.WriteLine("Skip: PG_TEST_CONNECTION_STRING 未配置"); return; }

        // Arrange: 预插 product + xref (用于满足 product_images.product_id 外键)
        var (productId, _, _) = await SetupProductAsync("MRIMG0004", "OEMCON001");

        // 两个独立 NpgsqlConnection (PG 事务并发必须独立连接)
        await using var conn1 = new NpgsqlConnection(ConnectionString);
        await using var conn2 = new NpgsqlConnection(ConnectionString);
        await conn1.OpenAsync();
        await conn2.OpenAsync();

        var tx1 = await conn1.BeginTransactionAsync();
        // tx1 先 INSERT (product_id, slot=2), 不 commit (持有行锁)
        await using (var cmd1 = conn1.CreateCommand())
        {
            cmd1.Transaction = tx1;
            cmd1.CommandText = @"
                INSERT INTO product_images (product_id, slot, image_key, file_size, content_type,
                    is_primary, display_order, uploaded_at, uploaded_by, oem_no_3, image_role)
                VALUES (@pid, 2, 'products/detail/test/test-2.jpg', 1024, 'image/jpeg',
                    false, 2, now(), 'tester', NULL, 'detail')";
            cmd1.Parameters.AddWithValue("pid", productId);
            await cmd1.ExecuteNonQueryAsync();
        }

        // tx2 尝试 INSERT 同一 (product_id, slot=2), 应阻塞并最终撞 23505
        //   WHY 用 Task.Run + 短延迟: 让 tx2 在 tx1 commit 之前开始等待行锁, 确保时序
        var tx2Task = Task.Run(async () =>
        {
            await using var tx2 = await conn2.BeginTransactionAsync();
            try
            {
                await using var cmd2 = conn2.CreateCommand();
                cmd2.Transaction = tx2;
                cmd2.CommandText = @"
                    INSERT INTO product_images (product_id, slot, image_key, file_size, content_type,
                        is_primary, display_order, uploaded_at, uploaded_by, oem_no_3, image_role)
                    VALUES (@pid, 2, 'products/detail/test2/test2-2.jpg', 2048, 'image/png',
                        false, 2, now(), 'tester2', NULL, 'detail')";
                cmd2.Parameters.AddWithValue("pid", productId);
                await cmd2.ExecuteNonQueryAsync();
                await tx2.CommitAsync();
                return (success: true, ex: (Exception?)null);
            }
            catch (Exception ex)
            {
                await tx2.RollbackAsync();
                return (success: false, ex: ex);
            }
        });

        // 给 tx2 一点时间进入等待状态 (Npgsql 命令超时默认 30s, 测试只需 100ms)
        await Task.Delay(150);
        // 释放 tx1, 让 tx2 撞 23505
        await tx1.CommitAsync();

        var (success, ex) = await tx2Task;

        // Assert: tx2 应失败 (23505)
        success.Should().BeFalse("并发 INSERT 同 (product_id, slot) 应触发唯一约束");
        ex.Should().BeOfType<PostgresException>(
            "Npgsql 应直接抛 PostgresException (未经 EF Core 包装)");
        var pgEx = (PostgresException)ex!;
        pgEx.SqlState.Should().Be("23505",
            "uq_product_images_detail_slot 唯一约束应触发 unique_violation (23505)");

        _output.WriteLine($"并发竞态验证通过: tx2 撞 23505 SqlState={pgEx.SqlState}, Message={pgEx.MessageText}");
    }

    /// <summary>
    /// 场景: 删除主图, 验证 DB 记录移除 + products.image_key 清空
    /// 覆盖: spec V2 - DeleteAsync 主图路径
    /// </summary>
    [Fact]
    public async Task DeleteAsync_RemovesPrimaryImageAndClearsProductImageKey_Integration()
    {
        if (!IsEnabled) { _output.WriteLine("Skip: PG_TEST_CONNECTION_STRING 未配置"); return; }

        // Arrange: 预插 product + xref + primary image
        var (productId, mr1, oem3) = await SetupProductAsync("MRIMG0005", "OEMDEL001");
        const string key = "products/primary/OEMDEL001/OEMDEL001-1.jpg";
        await InsertImageAsync(productId, "primary", oem3, slot: 1, key);

        var storageMock = CreateStorageMock();
        var sut = CreateSut(storageMock: storageMock);

        // Act
        await sut.DeleteAsync(mr1, "primary", slot: 1, default);

        // Assert: DB 中该图片记录已删除
        await using var verifyDb = CreateDbContext();
        var imgs = await verifyDb.ProductImages
            .Where(i => i.ProductId == productId && i.ImageRole == "primary")
            .ToListAsync();
        imgs.Should().BeEmpty("DeleteAsync 应移除 DB 记录");

        // products.image_key 应被清空 (兼容旧字段)
        var product = await verifyDb.Products.FirstAsync(p => p.Id == productId);
        product.ImageKey.Should().BeNull("主图删除应同步清空 products.image_key");

        // S3 Delete 被调用 (异步, 可能稍后才执行, 但 Moq.Verify 同步检查调用记录)
        storageMock.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k == key), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 场景: 删除详情图 slot, 验证 DB 记录移除 (不影响 products.image_key)
    /// 覆盖: spec V2 - DeleteAsync 详情图路径
    /// </summary>
    [Fact]
    public async Task DeleteAsync_RemovesDetailImage_Integration()
    {
        if (!IsEnabled) { _output.WriteLine("Skip: PG_TEST_CONNECTION_STRING 未配置"); return; }

        // Arrange: 预插 product + xref + detail image slot=3
        var (productId, mr1, _) = await SetupProductAsync("MRIMG0006", "OEMDEL002");
        const string key = "products/detail/MRIMG0006/MRIMG0006-3.jpg";
        await InsertImageAsync(productId, "detail", null, slot: 3, key);

        var storageMock = CreateStorageMock();
        var sut = CreateSut(storageMock: storageMock);

        // Act
        await sut.DeleteAsync(mr1, "detail", slot: 3, default);

        // Assert: DB 中该 detail 记录已删除
        await using var verifyDb = CreateDbContext();
        var imgs = await verifyDb.ProductImages
            .Where(i => i.ProductId == productId && i.Slot == 3)
            .ToListAsync();
        imgs.Should().BeEmpty("DeleteAsync 应移除 detail slot 记录");

        // products.image_key 应不受影响 (null, 因为没上传过主图)
        var product = await verifyDb.Products.FirstAsync(p => p.Id == productId);
        product.ImageKey.Should().BeNull("删除详情图不应影响 products.image_key");

        storageMock.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k == key), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 场景: 跨产品上传主图 - 不同 OEM 3 应都能上传成功 (验证 uq_product_images_primary 按 oem_no_3 唯一, 不跨产品冲突)
    /// 覆盖: spec V2 Task 3.2 - 主图唯一约束作用域
    /// </summary>
    [Fact]
    public async Task UploadAsync_DifferentOem3_BothPrimaryUploadsSucceed_Integration()
    {
        if (!IsEnabled) { _output.WriteLine("Skip: PG_TEST_CONNECTION_STRING 未配置"); return; }

        // Arrange: 两个产品, 各自的 OEM 3
        var (p1, mr1_1, oem3_1) = await SetupProductAsync("MRIMG0007", "OEMDIF001");
        var (p2, mr1_2, oem3_2) = await SetupProductAsync("MRIMG0008", "OEMDIF002");
        var sut = CreateSut();

        // Act: 两个产品各自上传主图
        var r1 = await sut.UploadAsync(mr1_1, "primary", oem3_1, slot: 1,
            CreateImageStream(), "image/jpeg", "tester1", default);

        // WHY 重新创建 SUT: DbContext 已 Dispose (上面 sut 共享一个 db, 但 UploadAsync 内部 begin tx 已 commit, db 仍可用)
        //   为避免 EF Core ChangeTracker 跟踪 r1 的 product 影响第二次调用, 用新 DbContext
        var sut2 = CreateSut();
        var r2 = await sut2.UploadAsync(mr1_2, "primary", oem3_2, slot: 1,
            CreateImageStream(), "image/jpeg", "tester2", default);

        // Assert: 两条独立的 primary 记录
        await using var verifyDb = CreateDbContext();
        var primaryImgs = await verifyDb.ProductImages
            .Where(i => i.ImageRole == "primary")
            .ToListAsync();
        primaryImgs.Should().HaveCount(2);
        primaryImgs.Select(i => i.OemNo3).Should().BeEquivalentTo(new[] { oem3_1, oem3_2 });
        primaryImgs.Select(i => i.ProductId).Should().BeEquivalentTo(new[] { p1, p2 });
    }

    /// <summary>
    /// 边界场景: 主图 oemNo3 不属于该 MR.1 (或已下架), 应抛 OEM3_NOT_FOUND
    /// 覆盖: spec V2 Task 3.2.4 - 主图 OEM 3 归属校验
    /// </summary>
    [Fact]
    public async Task UploadAsync_PrimaryOem3NotBelongToMr1_ThrowsOem3NotFound_Integration()
    {
        if (!IsEnabled) { _output.WriteLine("Skip: PG_TEST_CONNECTION_STRING 未配置"); return; }

        // Arrange: 预插 product + xref (OEM3=OEMOWN001)
        var (_, mr1, _) = await SetupProductAsync("MRIMG0009", "OEMOWN001");
        var sut = CreateSut();

        // Act: 上传主图但 oemNo3 用不属于该 mr1 的值
        Func<Task> act = () => sut.UploadAsync(mr1, "primary", "OEM_OTHER_999", slot: 1,
            CreateImageStream(), "image/jpeg", "tester", default);

        // Assert
        var ex = await act.Should().ThrowAsync<KeyNotFoundException>();
        ex.Which.Message.Should().Contain("OEM3_NOT_FOUND");
    }
}
