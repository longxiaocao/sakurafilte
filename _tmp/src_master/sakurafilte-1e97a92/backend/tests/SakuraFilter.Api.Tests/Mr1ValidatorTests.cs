using FluentAssertions;
using SakuraFilter.Core.Validation;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// V2 Task V17-1.3: Mr1Validator 静态工具单测
///   覆盖 Normalize (抛出式) + TryNormalize (非抛出式) 所有分支
///
/// 测试目标:
///   - MR1_REQUIRED: null/空/空白 → 抛 ArgumentException
///   - MR1_FORMAT_INVALID: 含非法字符或长度超限 → 抛 ArgumentException
///   - Trim 处理: 前后空格被 Trim
///   - 长度边界: 1 字符通过, 10 字符通过, 11 字符拒绝
///   - 字符集: 字母/数字通过, 特殊字符/中文/空格拒绝
///   - TryNormalize: 返回 (true, normalized, null) 或 (false, null, errorReason)
/// </summary>
public class Mr1ValidatorTests
{
    // ===== Normalize: 正常用例 =====

    [Theory]
    [InlineData("MR000001")]           // 8 位字母数字
    [InlineData("ABC1234567")]         // 10 位 (边界)
    [InlineData("A")]                  // 1 位 (边界)
    [InlineData("0")]                  // 1 位数字
    [InlineData("MR-001")]             // 含连字符 (应拒绝)
    public void Normalize_ValidMr1_ReturnsTrimmed(string input)
    {
        // 注意: "MR-001" 含连字符不在合法字符集,应抛异常,拆到下面拒绝用例
        if (input.Contains('-'))
        {
            var act = () => Mr1Validator.Normalize(input);
            act.Should().Throw<ArgumentException>();
            return;
        }
        var result = Mr1Validator.Normalize(input);
        result.Should().Be(input);
    }

    [Fact]
    public void Normalize_WithLeadingTrailingSpaces_ReturnsTrimmed()
    {
        var result = Mr1Validator.Normalize("  MR000001  ");
        result.Should().Be("MR000001");
    }

    // ===== Normalize: MR1_REQUIRED 用例 =====

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void Normalize_NullOrEmpty_ThrowsArgumentException(string? input)
    {
        var act = () => Mr1Validator.Normalize(input);
        var ex = act.Should().Throw<ArgumentException>().Which;
        ex.Message.Should().Contain("MR1_REQUIRED");
    }

    // ===== Normalize: MR1_FORMAT_INVALID 用例 =====

    [Theory]
    [InlineData("MR-001")]             // 含连字符
    [InlineData("MR_001")]             // 含下划线
    [InlineData("MR 001")]             // 含空格
    [InlineData("MR.001")]             // 含点
    [InlineData("MR/001")]             // 含斜杠
    [InlineData("中文MR001")]           // 含中文
    [InlineData("MR@001")]             // 含特殊字符
    [InlineData("MR001!")]             // 含感叹号
    public void Normalize_InvalidCharacters_ThrowsFormatException(string input)
    {
        var act = () => Mr1Validator.Normalize(input);
        var ex = act.Should().Throw<ArgumentException>().Which;
        ex.Message.Should().Contain("MR1_FORMAT_INVALID");
    }

    [Fact]
    public void Normalize_Length11_ThrowsArgumentException()
    {
        // 11 位字母数字 (超过 10 位上限)
        var input = "ABCDEFGHIJK";  // 11 位
        var act = () => Mr1Validator.Normalize(input);
        var ex = act.Should().Throw<ArgumentException>().Which;
        ex.Message.Should().Contain("MR1_FORMAT_INVALID");
    }

    [Fact]
    public void Normalize_Length10_ReturnsValue()
    {
        // 10 位字母数字 (边界,应通过)
        var input = "ABCDEFGHIJ";  // 10 位
        var result = Mr1Validator.Normalize(input);
        result.Should().Be(input);
    }

    // ===== TryNormalize: 非抛出式 =====

    [Fact]
    public void TryNormalize_ValidMr1_ReturnsTrueAndNormalized()
    {
        var ok = Mr1Validator.TryNormalize("MR000001", out var normalized, out var error);
        ok.Should().BeTrue();
        normalized.Should().Be("MR000001");
        error.Should().BeNull();
    }

    [Fact]
    public void TryNormalize_WithSpaces_ReturnsTrimmed()
    {
        var ok = Mr1Validator.TryNormalize("  MR000001  ", out var normalized, out var error);
        ok.Should().BeTrue();
        normalized.Should().Be("MR000001");
        error.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalize_NullOrEmpty_ReturnsFalseWithReason(string? input)
    {
        var ok = Mr1Validator.TryNormalize(input, out var normalized, out var error);
        ok.Should().BeFalse();
        normalized.Should().BeNull();
        error.Should().Be("MR1_REQUIRED");
    }

    [Theory]
    [InlineData("MR-001")]
    [InlineData("MR_001")]
    [InlineData("中文MR001")]
    [InlineData("ABCDEFGHIJK")]  // 11 位
    public void TryNormalize_InvalidFormat_ReturnsFalseWithReason(string input)
    {
        var ok = Mr1Validator.TryNormalize(input, out var normalized, out var error);
        ok.Should().BeFalse();
        normalized.Should().BeNull();
        error.Should().Be("MR1_FORMAT_INVALID");
    }

    // ===== Mr1MaxLength 常量 =====

    [Fact]
    public void Mr1MaxLength_ShouldBe10()
    {
        Mr1Validator.Mr1MaxLength.Should().Be(10);
    }
}
