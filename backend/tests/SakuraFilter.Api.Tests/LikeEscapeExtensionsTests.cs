using FluentAssertions;
using SakuraFilter.Core.Extensions;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// LIKE 模式转义单测
/// 覆盖: 三个特殊字符 (%, _, \) 的转义, NULL/空串, 顺序正确性
/// </summary>
public class LikeEscapeExtensionsTests
{
    [Fact]
    public void Escape_NullOrEmpty_ReturnsAsIs()
    {
        // WHY: null/空 不抛异常, 与原 string.Replace 行为一致, 调用方不用额外判空
        string? input = null;
        input.EscapeLikePattern().Should().BeNull();
        string.Empty.EscapeLikePattern().Should().Be(string.Empty);
    }

    [Fact]
    public void Escape_PercentSign_PrependsBackslash()
    {
        // WHY: 50% 之类的输入, % 是 ILIKE 通配符, 必须转义为字面量
        "50%".EscapeLikePattern().Should().Be("50\\%");
    }

    [Fact]
    public void Escape_Underscore_PrependsBackslash()
    {
        // WHY: _ 在 ILIKE 中匹配任意单字符, 必须转义为字面量下划线
        "AC_0101".EscapeLikePattern().Should().Be("AC\\_0101");
    }

    [Fact]
    public void Escape_Backslash_DoublesIt()
    {
        // WHY: \ 是 ESCAPE 字符, 必须先于其他字符转义, 否则双重转义
        "a\\b".EscapeLikePattern().Should().Be("a\\\\b");
    }

    [Fact]
    public void Escape_AllThreeCharsTogether_AllEscaped()
    {
        // WHY: 组合场景, 顺序是 \\ → % → _, 验证实际顺序正确
        "10%_test\\".EscapeLikePattern().Should().Be("10\\%\\_test\\\\");
    }

    [Fact]
    public void Escape_PlainText_Unchanged()
    {
        "AC 0101".EscapeLikePattern().Should().Be("AC 0101");
        "E2E20260705".EscapeLikePattern().Should().Be("E2E20260705");
    }

    [Fact]
    public void Escape_BackslashBeforePercent_DoesNotDoubleEscape()
    {
        // WHY 顺序关键: 先转 \, 再转 %, 否则 \\ 会变 \\\\\, 重复转义
        //   错误顺序: "a\%" → "a\\\\%" (用 \\ 转 \, 但前面又有 \\, 多了一个)
        //   正确顺序: "a\%" → "a\\\\\\%" (\\ → \\\\, 然后 % → \\%)
        var input = @"a\%";
        var result = input.EscapeLikePattern();
        result.Should().Be(@"a\\\%");
        // 验证: 用 EF.ILike 三参重载 + ESCAPE '\\' 拼接, 应正确匹配字面量 a\%
        // 即: 实际查询模式 = "a\\\%" → 还原为字面量 "a\%" (PG ILIKE 行为)
    }

    [Fact]
    public void Escape_UnicodeInput_PassesThrough()
    {
        // WHY: 中文/Emoji 不应被转义, 只处理 SQL 通配符
        "滤清器 AC".EscapeLikePattern().Should().Be("滤清器 AC");
        "🔧".EscapeLikePattern().Should().Be("🔧");
    }
}
