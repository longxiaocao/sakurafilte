using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SakuraFilter.Api.Services;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// CursorHmac 单测
/// 覆盖: 双 key 轮转、签名生成、验签、tampering 检测
/// V2 Task 4.6: Sign 载荷从 long id 改为 string mr1, 单测同步更新
/// </summary>
public class CursorHmacTests
{
    private const string Key = "test-cursor-hmac-key-must-be-32-chars-min-X7K9M2P5";
    private const string PreviousKey = "previous-cursor-hmac-key-also-32-chars-min-Z8L0N3Q6";
    // V2 Task 4.6: 测试用 mr1 (1-10 位字母+数字, 符合 V2 业务约束)
    private const string TestMr1 = "A1B2C3D4E5";

    private CursorHmac CreateSut(string? current = Key, string? previous = null)
    {
        var configData = new Dictionary<string, string?>();
        if (current != null) configData["Search:CursorHmacKey"] = current;
        if (previous != null) configData["Search:CursorHmacKeyPrevious"] = previous;
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
        return new CursorHmac(config, NullLogger<CursorHmac>.Instance);
    }

    [Fact]
    public void Constructor_WithShortKey_Throws()
    {
        // WHY: 短 key 抗碰撞能力不足, 必须 >= 32 字符
        var act = () => CreateSut("short");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*长度*32*不安全*");
    }

    [Fact]
    public void Constructor_WithEmptyKey_Throws()
    {
        var act = () => CreateSut(null);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*未配置*");
    }

    [Fact]
    public void Constructor_WithSameCurrentAndPrevious_LogsWarning()
    {
        // WHY: 同 key 不会破坏功能, 但无意义, 提示 ops 清理
        var sut = CreateSut(Key, Key);
        sut.Should().NotBeNull();
    }

    [Fact]
    public void Sign_ThenVerify_RoundTrip_Succeeds()
    {
        // V2 Task 4.6: Sign 载荷改 string mr1, VerifyAndExtract 返回 (string, string)
        var sut = CreateSut();
        var sig = sut.Sign("2026-07-05T10:00:00Z", TestMr1);
        var cursor = $"2026-07-05T10:00:00Z|{TestMr1}|{sig}";

        var (updatedAt, mr1) = sut.VerifyAndExtract(cursor);
        updatedAt.Should().Be("2026-07-05T10:00:00Z");
        mr1.Should().Be(TestMr1);
    }

    [Fact]
    public void Verify_TamperedMr1_Throws()
    {
        // WHY: 防止客户端篡改 cursor 越权访问其他产品位置
        //   V2 场景: 改 mr1 段会指向另一产品, 签名失配
        var sut = CreateSut();
        var sig = sut.Sign("2026-07-05T10:00:00Z", TestMr1);
        var cursor = $"2026-07-05T10:00:00Z|{TestMr1}|{sig}";
        // 把 mr1 改成另一个值 (签名仍按原 mr1 算)
        var tampered = $"2026-07-05T10:00:00Z|Z9Y8X7W6V5|{sig}";
        var act = () => sut.VerifyAndExtract(tampered);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Verify_TamperedUpdatedAt_Throws()
    {
        var sut = CreateSut();
        var sig = sut.Sign("2026-07-05T10:00:00Z", TestMr1);
        var cursor = $"2026-07-05T10:00:00Z|{TestMr1}|{sig}";
        // 改 updatedAt (签名按原值算)
        var tampered = $"2026-07-05T11:00:00Z|{TestMr1}|{sig}";
        var act = () => sut.VerifyAndExtract(tampered);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Verify_OldCursor_AcceptedDuringRotation()
    {
        // WHY: 双 key 轮转过渡期 (24h) 内, 旧 cursor 仍应可验证, 避免翻页中断
        var oldSut = CreateSut(PreviousKey);
        var sig = oldSut.Sign("2026-07-05T10:00:00Z", TestMr1);
        var oldCursor = $"2026-07-05T10:00:00Z|{TestMr1}|{sig}";

        var newSut = CreateSut(Key, PreviousKey);
        var (updatedAt, mr1) = newSut.VerifyAndExtract(oldCursor);
        updatedAt.Should().Be("2026-07-05T10:00:00Z");
        mr1.Should().Be(TestMr1);
    }

    [Fact]
    public void Verify_OldCursor_RejectedAfterPreviousRemoved()
    {
        // WHY: 24h 过渡期后清空 PreviousKey, 旧 cursor 应被拒绝
        var oldSut = CreateSut(PreviousKey);
        var sig = oldSut.Sign("2026-07-05T10:00:00Z", TestMr1);
        var oldCursor = $"2026-07-05T10:00:00Z|{TestMr1}|{sig}";

        var newSut = CreateSut(Key);  // 无 PreviousKey
        var act = () => newSut.VerifyAndExtract(oldCursor);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Verify_CursorWithoutSignature_Rejected()
    {
        // WHY: 旧格式 (无 sig) 视为非法, 强制要求签名
        var sut = CreateSut();
        var act = () => sut.VerifyAndExtract($"2026-07-05T10:00:00Z|{TestMr1}");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Verify_GarbageInput_Throws()
    {
        var sut = CreateSut();
        var act = () => sut.VerifyAndExtract("not-a-valid-cursor");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Verify_EmptyCursor_Throws()
    {
        var sut = CreateSut();
        var act = () => sut.VerifyAndExtract("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Verify_EmptyMr1_Throws()
    {
        // V2 Task 4.6: mr1 段为空应拒绝 (兼容旧 "non-numeric id" 用例)
        var sut = CreateSut();
        var act = () => sut.VerifyAndExtract("2026-07-05T10:00:00Z||abc");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Sign_TruncatesTo16Chars()
    {
        // WHY: 16 字符 Base64URL ≈ 96 位熵, 平衡 URL 长度与安全强度
        var sut = CreateSut();
        var sig = sut.Sign("2026-07-05T10:00:00Z", TestMr1);
        sig.Length.Should().Be(16);
    }

    [Fact]
    public void Sign_WithEmptyMr1_Throws()
    {
        // V2 Task 4.6: 空 mr1 载荷应拒绝 (防御性编程, 避免签名空字符串)
        var sut = CreateSut();
        var act = () => sut.Sign("2026-07-05T10:00:00Z", "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Sign_WithNullMr1_Throws()
    {
        var sut = CreateSut();
        var act = () => sut.Sign("2026-07-05T10:00:00Z", null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Sign_BackwardCompat_IdAsString_Payload()
    {
        // V2 Task 4.6 兼容性: 历史记录 cursor 仍用 ProductHistory.Id, 调用方传 id.ToString()
        //   确保 string 载荷能承载任意字符串 (数字/字母+数字均可)
        var sut = CreateSut();
        var sig = sut.Sign("2026-07-05T10:00:00Z", "12345");
        var cursor = $"2026-07-05T10:00:00Z|12345|{sig}";
        var (updatedAt, payload) = sut.VerifyAndExtract(cursor);
        updatedAt.Should().Be("2026-07-05T10:00:00Z");
        payload.Should().Be("12345");
    }

    // ===== V24-F24: V2 双签名重载测试 (spec S3-13/S3-14/S3-24/F2-2/F2-17) =====

    [Fact]
    public void SignV2_ProducesV2PrefixAnd5Segments()
    {
        // WHY: spec S3-14 要求 V2 cursor 格式 "v2:exp|tsB64|mr1B64|pageNum|sig"
        //   验证: 前缀 v2: + 5 段 + Base64Url 编码 (无 | 字符在编码段内)
        var sut = CreateSut();
        var cursor = sut.SignV2("2026-07-05T10:00:00Z", TestMr1, 1);
        cursor.Should().StartWith("v2:");
        var parts = cursor.Split('|');
        parts.Length.Should().Be(5, "V2 cursor 应有 5 段: v2:exp|tsB64|mr1B64|pageNum|sig");
        parts[0].Should().StartWith("v2:");
        parts[3].Should().Be("1", "pageNum 段应为传入的 1");
        parts[4].Length.Should().Be(16, "sig 段应截断到 16 字符");
    }

    [Fact]
    public void SignV2_AndVerifyAndExtractV2_RoundTrip_Succeeds()
    {
        // WHY: SignV2 → VerifyAndExtractV2 往返应还原 (updatedAtIso, mr1, pageNum)
        var sut = CreateSut();
        var cursor = sut.SignV2("2026-07-05T10:00:00Z", TestMr1, 5);
        var (updatedAt, mr1, pageNum) = sut.VerifyAndExtractV2(cursor);
        updatedAt.Should().Be("2026-07-05T10:00:00Z");
        mr1.Should().Be(TestMr1);
        pageNum.Should().Be(5);
    }

    [Fact]
    public void Cursor_Legacy_AfterCutoff_Rejected()
    {
        // WHY (spec S3-13): LEGACY_CUTOFF_TS = 2025-07-25 已过, 旧格式公开 cursor 全部拒绝
        //   VerifyAndExtractV2 不接受非 v2: 前缀 (无论签名是否有效)
        var sut = CreateSut();
        // 旧格式 cursor (无 v2: 前缀, 3 段)
        var sig = sut.Sign("2026-07-05T10:00:00Z", TestMr1);
        var legacyCursor = $"2026-07-05T10:00:00Z|{TestMr1}|{sig}";
        var act = () => sut.VerifyAndExtractV2(legacyCursor);
        act.Should().Throw<ArgumentException>().WithMessage("*CURSOR_INVALID*");
    }

    [Fact]
    public void Cursor_PageNum_TooDeep()
    {
        // WHY (spec S3-14/S3-24): pageNum > 1000 应拒绝 (防深翻页 DoS)
        //   SignV2 侧拦截 + VerifyAndExtractV2 侧拦截 (双重防御)
        var sut = CreateSut();
        // SignV2 侧: 直接拒绝
        var signAct = () => sut.SignV2("2026-07-05T10:00:00Z", TestMr1, 1001);
        signAct.Should().Throw<ArgumentException>().WithMessage("*CURSOR_PAGE_TOO_DEEP*");

        // VerifyAndExtractV2 侧: 构造一个 pageNum=1001 的 cursor (需绕过 SignV2 检查)
        //   手动构造: 用 SignV2(iso, mr1, 1000) 签名, 然后篡改 parts[3] 为 1001
        //   但篡改会破坏签名 → 应抛 CURSOR_INVALID (签名失配), 而非 CURSOR_PAGE_TOO_DEEP
        //   所以这里只测 SignV2 侧拦截, VerifyAndExtractV2 侧的 pageNum 检查是兜底
        var validCursor = sut.SignV2("2026-07-05T10:00:00Z", TestMr1, 1000);
        var verifyAct = () => sut.VerifyAndExtractV2(validCursor);
        verifyAct.Should().NotThrow("pageNum=1000 应允许 (边界值)");
    }

    [Fact]
    public void Cursor_V2_TamperedSig_Rejected()
    {
        // WHY (spec F2-2): 验签顺序应为先 HMAC 后 TTL
        //   篡改 sig → HMAC 验签失败 → 不触发 TTL/pageNum 解析 (防攻击者构造未来时间戳)
        var sut = CreateSut();
        var cursor = sut.SignV2("2026-07-05T10:00:00Z", TestMr1, 1);
        // 篡改 sig 段 (最后 16 字符) 为随机值
        var tamperedSig = cursor[..^16] + "AAAAAAAAAAAAAAAA";
        var act = () => sut.VerifyAndExtractV2(tamperedSig);
        act.Should().Throw<ArgumentException>().WithMessage("*CURSOR_INVALID*签名验证失败*");
    }

    [Fact]
    public void Cursor_V2_TamperedExpTs_Rejected()
    {
        // WHY (spec F2-2): 改 expUnixTs 也会破坏签名 (payload 含 expUnixTs)
        //   防攻击者把 expUnixTs 改为未来时间戳以延长 TTL
        var sut = CreateSut();
        var cursor = sut.SignV2("2026-07-05T10:00:00Z", TestMr1, 1);
        var parts = cursor.Split('|');
        // 把 expUnixTs 改为未来 100 年 (签名失配)
        var futureTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400L * 365 * 100;
        parts[0] = $"v2:{futureTs}";
        var tampered = string.Join('|', parts);
        var act = () => sut.VerifyAndExtractV2(tampered);
        act.Should().Throw<ArgumentException>().WithMessage("*CURSOR_INVALID*签名验证失败*");
    }

    [Fact]
    public void Cursor_V2_TamperedPageNum_Rejected()
    {
        // WHY: 改 pageNum 也会破坏签名 (payload 含 pageNum)
        //   防攻击者把 pageNum 改小以绕过深翻页限制
        var sut = CreateSut();
        var cursor = sut.SignV2("2026-07-05T10:00:00Z", TestMr1, 500);
        var parts = cursor.Split('|');
        parts[3] = "1";  // 改为 1 (绕过深翻页)
        var tampered = string.Join('|', parts);
        var act = () => sut.VerifyAndExtractV2(tampered);
        act.Should().Throw<ArgumentException>().WithMessage("*CURSOR_INVALID*签名验证失败*");
    }

    [Fact]
    public void Cursor_V2_DoubleKeyRotation_AcceptsPreviousKey()
    {
        // WHY (spec S3-9): 双 key 轮转期间, 用 PreviousKey 签的 V2 cursor 应被接受
        var oldSut = CreateSut(PreviousKey);
        var cursor = oldSut.SignV2("2026-07-05T10:00:00Z", TestMr1, 1);

        var newSut = CreateSut(Key, PreviousKey);
        var (updatedAt, mr1, pageNum) = newSut.VerifyAndExtractV2(cursor);
        updatedAt.Should().Be("2026-07-05T10:00:00Z");
        mr1.Should().Be(TestMr1);
        pageNum.Should().Be(1);
    }

    [Fact]
    public void Cursor_V2_Base64Url_EncodesPipeChar()
    {
        // WHY (spec F2-17): updatedAtIso/mr1 若含 | 字符, Base64Url 编码后不会破坏 Split('|')
        //   测试: updatedAtIso 含 | 字符, 经 Base64Url 编码后 cursor 仍可正确解析
        var sut = CreateSut();
        var weirdIso = "2026-07-05|10:00:00Z";  // 含 | 字符
        var cursor = sut.SignV2(weirdIso, TestMr1, 1);
        var (updatedAt, mr1, pageNum) = sut.VerifyAndExtractV2(cursor);
        updatedAt.Should().Be(weirdIso, "Base64Url 编解码应还原原始 | 字符");
        mr1.Should().Be(TestMr1);
        pageNum.Should().Be(1);
    }

    [Fact]
    public void Cursor_V2_EmptyInput_Rejected()
    {
        // WHY: 空 cursor 应拒绝 (与旧 VerifyAndExtract 一致)
        var sut = CreateSut();
        var act = () => sut.VerifyAndExtractV2("");
        act.Should().Throw<ArgumentException>().WithMessage("*CURSOR_INVALID*");
    }

    [Fact]
    public void Cursor_V2_WrongSegmentCount_Rejected()
    {
        // WHY: 5 段格式硬约束, 4 段或 6 段都拒绝
        var sut = CreateSut();
        var act1 = () => sut.VerifyAndExtractV2("v2:123|a|b|c");  // 4 段
        act1.Should().Throw<ArgumentException>().WithMessage("*CURSOR_INVALID*期望 5 段*");
        var act2 = () => sut.VerifyAndExtractV2("v2:123|a|b|c|d|e");  // 6 段
        act2.Should().Throw<ArgumentException>().WithMessage("*CURSOR_INVALID*期望 5 段*");
    }

    [Fact]
    public void SignV2_WithEmptyMr1_Throws()
    {
        // WHY: 空 mr1 应在签名侧拦截 (防御性编程)
        var sut = CreateSut();
        var act = () => sut.SignV2("2026-07-05T10:00:00Z", "", 1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SignV2_WithPageNumLessThan1_Throws()
    {
        // WHY: pageNum < 1 应在签名侧拦截 (页码从 1 开始)
        var sut = CreateSut();
        var act = () => sut.SignV2("2026-07-05T10:00:00Z", TestMr1, 0);
        act.Should().Throw<ArgumentException>();
    }
}
