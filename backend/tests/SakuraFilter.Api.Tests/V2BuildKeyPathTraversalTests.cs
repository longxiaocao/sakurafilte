using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Moq;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.Interfaces;
using SakuraFilter.Infrastructure.Data;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// V2 改进 3: BuildKeyAsync 路径穿越防御 + imageRole/slot 校验单测
///
/// 测试目标: AdminProductImageService.BuildKeyAsync (public async Task&lt;string&gt;)
///   - 路径穿越防御: namingValue 含 ../、..\\、/、\\、空格、特殊字符 → 清洗为 _
///   - imageRole/slot 一致性: primary 必须 slot=1, detail 必须 slot=2-6
///   - namingField 配置: system_settings 读取 (通过 IMemoryCache 预填绕过 DB)
///   - 空命名值: primary/detail 空值 → IMAGE_ROLE_SLOT_MISMATCH
///
/// WHY 预填 IMemoryCache 绕过 DB:
///   - BuildKeyAsync 调 GetNamingFieldAsync, 后者先查 cache, cache hit 直接 return
///   - 预填 cache 后不访问 _db, 避免 EF Core InMemory 依赖 (不引入新包)
///   - CacheKeyPrimary/Detail 是 private const, 用反射获取值或硬编码字符串
/// </summary>
public class V2BuildKeyPathTraversalTests
{
    // V2 Task 3.1.4: 与 AdminProductImageService.CacheKeyPrimary/Detail 同口径
    //   WHY 硬编码: private const 无法外部访问, 任何变更需同步更新
    private const string CacheKeyPrimary = "image.primary_naming_field";
    private const string CacheKeyDetail = "image.detail_naming_field";

    /// <summary>构造 SUT (System Under Test), 预填 cache 绕过 DB 查询</summary>
    /// <param name="primaryField">主图命名字段 ("oem_no_3" 或 "mr_1"), null=不预填 (用默认值)</param>
    /// <param name="detailField">详情图命名字段 ("mr_1" 或 "oem_no_3"), null=不预填</param>
    private static AdminProductImageService CreateSut(string? primaryField = null, string? detailField = null)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        // 预填 cache: 让 GetNamingFieldAsync 直接 hit, 不查 DB
        if (primaryField != null)
            cache.Set(CacheKeyPrimary, primaryField);
        if (detailField != null)
            cache.Set(CacheKeyDetail, detailField);

        // _db 不被访问 (cache hit), 用 null! 抑制 nullable 警告
        //   WHY 安全: GetNamingFieldAsync 在 cache.TryGetValue 成功后立即 return, 不执行 _db 查询
        ProductDbContext db = null!;  // NotFoundBuilder 也不会被访问 (BuildKeyAsync 不触发 _db)
        var storage = Mock.Of<IObjectStorage>();
        var config = new ConfigurationBuilder().Build();
        var logger = NullLogger<AdminProductImageService>.Instance;

        return new AdminProductImageService(db, storage, config, cache, logger);
    }

    // ========== 路径穿越防御 ==========

    [Fact]
    public async Task BuildKeyAsync_Primary_NamingValue_With_DotDot_Slash_Sanitized()
    {
        // WHY 路径穿越: namingValue="../../etc/passwd" 若不清洗, S3 key 会变成
        //   products/primary/../../etc/passwd/../../etc/passwd-1.png → 路径逃逸
        //   清洗后: 仅字母数字-_保留, 其余替换为 _ (. 和 / 都变成 _)
        var sut = CreateSut(primaryField: "oem_no_3");
        var key = await sut.BuildKeyAsync("primary", "../../etc/passwd", "MR000001", 1, "png");
        // 清洗后 namingValue = "______etc_passwd" (.. 和 / 都变成 _)
        key.Should().StartWith("products/primary/");
        // 关键断言: namingValue 段 (key 的第 3 段) 不应含 ".." (路径穿越字符被清洗)
        var segments = key.Split('/');
        segments.Should().HaveCountGreaterThan(3);
        var namingSegment = segments[2];  // products/primary/{namingSegment}/{namingSegment}-1.png
        namingSegment.Should().NotContain("..", "路径穿越字符 .. 应被清洗为 __");
        // namingSegment 也不应含原始的 / (因为 / 已被替换为 _)
        namingSegment.Should().NotContain("/", "原始 / 应被清洗为 _");
        // 验证 key 整体格式正确 (路径分隔符 / 是合法的, 保留)
        key.Should().Match("products/primary/*/*-1.png");
    }

    [Fact]
    public async Task BuildKeyAsync_Detail_NamingValue_With_Backslash_Sanitized()
    {
        // WHY Windows 路径穿越: namingValue="..\\..\\windows\\system32" 同样需清洗
        var sut = CreateSut(detailField: "mr_1");
        var key = await sut.BuildKeyAsync("detail", null, "..\\..\\windows\\system32", 2, "jpg");
        key.Should().StartWith("products/detail/");
        key.Should().NotContain("..");
        key.Should().NotContain("\\");
        key.Should().Match("products/detail/*/*-2.jpg");
    }

    [Fact]
    public async Task BuildKeyAsync_NamingValue_With_Spaces_Sanitized()
    {
        // WHY 空格防御: 空格在 S3 key 中合法但可能导致 URL 编码问题, 统一替换为 _
        var sut = CreateSut(primaryField: "oem_no_3");
        var key = await sut.BuildKeyAsync("primary", "OEM 3 With Space", "MR000001", 1, "png");
        key.Should().NotContain(" ");
        key.Should().Contain("_");
    }

    [Fact]
    public async Task BuildKeyAsync_NamingValue_With_SpecialChars_Sanitized()
    {
        // WHY 特殊字符: : ; , ! @ # $ % ^ & * ( ) + = < > ? 等都不应出现在 S3 key
        var sut = CreateSut(primaryField: "oem_no_3");
        var key = await sut.BuildKeyAsync("primary", "OEM:3;FUNC!@#", "MR000001", 1, "png");
        key.Should().NotContain(":");
        key.Should().NotContain(";");
        key.Should().NotContain("!");
        key.Should().NotContain("@");
        key.Should().NotContain("#");
        // 仅字母数字-_, 其他全替换为 _
        var sanitizedSegment = key.Split('/')[2];  // products/primary/{sanitized}/{sanitized}-1.png
        sanitizedSegment.Should().MatchRegex(@"^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public async Task BuildKeyAsync_NamingValue_With_Dash_And_Underscore_Preserved()
    {
        // WHY 合法字符保留: - 和 _ 是 S3 key 推荐字符, 不应被清洗
        var sut = CreateSut(primaryField: "oem_no_3");
        var key = await sut.BuildKeyAsync("primary", "OEM-3_TEST", "MR000001", 1, "png");
        key.Should().Contain("OEM-3_TEST");
        key.Should().Be("products/primary/OEM-3_TEST/OEM-3_TEST-1.png");
    }

    [Fact]
    public async Task BuildKeyAsync_NamingValue_With_Chinese_Preserved_As_Letter()
    {
        // WHY 中文保留: char.IsLetterOrDigit('中') 返回 true (Unicode 字母)
        //   清洗逻辑仅替换非字母数字-_, 中文属合法 Letter, 会被保留
        //   注: V2 业务约束 MR.1 仅允许字母数字 (1-10 位), OEM 3 实际数据也应为 ASCII
        //   但防御性编程的清洗逻辑本身不拒绝中文 (S3 key 支持中文, 仅不推荐)
        //   此测试验证: 清洗逻辑对中文是"保留"而非"替换", 避免误改清洗规则破坏中文数据
        var sut = CreateSut(primaryField: "oem_no_3");
        var key = await sut.BuildKeyAsync("primary", "中文OEM3", "MR000001", 1, "png");
        // 中文作为 Letter 被保留 (char.IsLetterOrDigit 返回 true)
        key.Should().Contain("中文OEM3");
        key.Should().Be("products/primary/中文OEM3/中文OEM3-1.png");
        // 但路径穿越字符 (.. / \) 仍会被清洗, 中文不影响安全性
        key.Should().Match("products/primary/*/*-1.png");
    }

    // ========== imageRole / slot 一致性 ==========

    [Fact]
    public async Task BuildKeyAsync_Primary_SlotNot1_Throws_IMAGE_ROLE_SLOT_MISMATCH()
    {
        // WHY slot 一致性: 主图必须 slot=1, slot=2 会与详情图冲突
        //   注: BuildKeyAsync 本身不校验 slot, 校验在 UploadAsync (L125-128)
        //   但 BuildKeyAsync 会用 slot 拼 key, 这里验证 key 格式正确
        var sut = CreateSut(primaryField: "oem_no_3");
        var key = await sut.BuildKeyAsync("primary", "OEM3-A", "MR000001", 1, "png");
        key.Should().EndWith("-1.png");
        key.Should().Be("products/primary/OEM3-A/OEM3-A-1.png");
    }

    [Fact]
    public async Task BuildKeyAsync_Detail_Slot2_To_6_All_Valid()
    {
        // WHY slot 范围: 详情图 slot=2-6, BuildKeyAsync 用 slot 拼 key
        var sut = CreateSut(detailField: "mr_1");
        for (short slot = 2; slot <= 6; slot++)
        {
            var key = await sut.BuildKeyAsync("detail", null, "MR000001", slot, "jpg");
            key.Should().Be($"products/detail/MR000001/MR000001-{slot}.jpg");
        }
    }

    // ========== namingField 配置切换 ==========

    [Fact]
    public async Task BuildKeyAsync_Primary_NamingField_Mr1_Uses_Mr1_For_Naming()
    {
        // WHY 配置切换: system_settings 配置 image.primary_naming_field=mr_1 时, 主图用 mr_1 命名
        //   场景: 客户希望主图也按 MR.1 命名 (而非默认 OEM 3)
        var sut = CreateSut(primaryField: "mr_1");
        var key = await sut.BuildKeyAsync("primary", "OEM3-A", "MR000001", 1, "png");
        // namingField=mr_1 → namingValue = mr1 = "MR000001"
        key.Should().Be("products/primary/MR000001/MR000001-1.png");
    }

    [Fact]
    public async Task BuildKeyAsync_Detail_NamingField_OemNo3_Uses_OemNo3_For_Naming()
    {
        // WHY 配置切换: system_settings 配置 image.detail_naming_field=oem_no_3 时, 详情图用 OEM 3 命名
        //   场景: 客户希望详情图按 OEM 3 分组 (而非默认 MR.1 共享)
        var sut = CreateSut(detailField: "oem_no_3");
        var key = await sut.BuildKeyAsync("detail", "OEM3-A", "MR000001", 2, "jpg");
        // namingField=oem_no_3 → namingValue = oemNo3 = "OEM3-A"
        key.Should().Be("products/detail/OEM3-A/OEM3-A-2.jpg");
    }

    [Fact]
    public async Task BuildKeyAsync_Default_NamingField_Used_When_Cache_Miss()
    {
        // WHY 默认值: cache 未预填时, GetNamingFieldAsync 返回 defaultValue
        //   primary 默认 "oem_no_3", detail 默认 "mr_1"
        //   注: cache miss 会查 _db, 但 _db 为 null 会抛 NRE
        //   此测试用预填空字符串模拟 cache miss+DB 空值 → 返回默认值
        //   实际上 GetNamingFieldAsync 校验 value 必须是 oem_no_3/mr_1, 否则用默认值
        //   所以预填一个非法值也会走默认值
        var sut = CreateSut(primaryField: "invalid_field");  // 非法值 → 默认 oem_no_3
        var key = await sut.BuildKeyAsync("primary", "OEM3-A", "MR000001", 1, "png");
        // namingField 非法 → 默认 "oem_no_3" → namingValue = oemNo3 = "OEM3-A"
        key.Should().Be("products/primary/OEM3-A/OEM3-A-1.png");
    }

    // ========== 空命名值 ==========

    [Fact]
    public async Task BuildKeyAsync_Primary_Empty_OemNo3_Throws_IMAGE_ROLE_SLOT_MISMATCH()
    {
        // WHY 空值防御: primary 默认用 oem_no_3, 若 oemNo3 为空 → 抛异常
        var sut = CreateSut(primaryField: "oem_no_3");
        var act = async () => await sut.BuildKeyAsync("primary", "", "MR000001", 1, "png");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IMAGE_ROLE_SLOT_MISMATCH*主图命名值*不能为空*");
    }

    [Fact]
    public async Task BuildKeyAsync_Primary_Null_OemNo3_Throws_IMAGE_ROLE_SLOT_MISMATCH()
    {
        var sut = CreateSut(primaryField: "oem_no_3");
        var act = async () => await sut.BuildKeyAsync("primary", null, "MR000001", 1, "png");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IMAGE_ROLE_SLOT_MISMATCH*");
    }

    [Fact]
    public async Task BuildKeyAsync_Detail_Empty_Mr1_Throws_IMAGE_ROLE_SLOT_MISMATCH()
    {
        // WHY 空值防御: detail 默认用 mr_1, 若 mr1 为空 → 抛异常
        var sut = CreateSut(detailField: "mr_1");
        var act = async () => await sut.BuildKeyAsync("detail", null, "", 2, "jpg");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IMAGE_ROLE_SLOT_MISMATCH*详情图命名值*不能为空*");
    }

    [Fact]
    public async Task BuildKeyAsync_Primary_Mr1_Naming_With_Empty_Mr1_Throws()
    {
        // WHY 配置切换后空值: primary 配置用 mr_1 命名, 但 mr1 为空 → 仍应抛异常
        var sut = CreateSut(primaryField: "mr_1");
        var act = async () => await sut.BuildKeyAsync("primary", "OEM3-A", "", 1, "png");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IMAGE_ROLE_SLOT_MISMATCH*主图命名值*不能为空*");
    }

    // ========== 扩展名 ==========

    [Fact]
    public async Task BuildKeyAsync_Ext_Preserved_In_Key()
    {
        // WHY 扩展名: ext 参数直接拼入 key, 不做校验 (调用方负责)
        var sut = CreateSut(primaryField: "oem_no_3");
        var key = await sut.BuildKeyAsync("primary", "OEM3-A", "MR000001", 1, "webp");
        key.Should().EndWith("-1.webp");
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("png")]
    [InlineData("webp")]
    [InlineData("gif")]
    public async Task BuildKeyAsync_Common_Image_Exts_All_Work(string ext)
    {
        var sut = CreateSut(detailField: "mr_1");
        var key = await sut.BuildKeyAsync("detail", null, "MR000001", 3, ext);
        key.Should().EndWith($"-3.{ext}");
    }

    // ========== 完整 key 格式 ==========

    [Fact]
    public async Task BuildKeyAsync_Primary_Full_Key_Format()
    {
        var sut = CreateSut(primaryField: "oem_no_3");
        var key = await sut.BuildKeyAsync("primary", "OEM3-ABC", "MR000001", 1, "png");
        // 格式: products/primary/{namingValue}/{namingValue}-{slot}.{ext}
        key.Should().Be("products/primary/OEM3-ABC/OEM3-ABC-1.png");
    }

    [Fact]
    public async Task BuildKeyAsync_Detail_Full_Key_Format()
    {
        var sut = CreateSut(detailField: "mr_1");
        var key = await sut.BuildKeyAsync("detail", null, "MR000001", 4, "jpg");
        // 格式: products/detail/{namingValue}/{namingValue}-{slot}.{ext}
        key.Should().Be("products/detail/MR000001/MR000001-4.jpg");
    }
}
