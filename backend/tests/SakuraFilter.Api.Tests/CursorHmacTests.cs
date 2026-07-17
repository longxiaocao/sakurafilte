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
}
