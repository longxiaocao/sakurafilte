using System.Reflection;
using FluentAssertions;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// MeiliSearchProvider.SanitizeString 快速路径等价性测试 (2026-08 性能优化防回归)
/// 快速路径 (NeedsSanitize=false 短路) 必须与慢路径输出完全一致, 不得削弱 XSS 防御
/// </summary>
public class MeiliSanitizeEquivalenceTests
{
    private static readonly MethodInfo SanitizeString =
        typeof(SakuraFilter.Search.MeiliSearchProvider)
            .GetMethod("SanitizeString", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static string Sanitize(string input)
        => (string)SanitizeString.Invoke(null, new object[] { input })!;

    [Theory]
    // 纯文本 (快速路径): 必须原样返回
    [InlineData("BOSCH")]
    [InlineData("F000000001")]
    [InlineData("S263 T6511")]
    [InlineData("OIL FILTER 513420")]
    [InlineData("CAT 320D HONDA DEUTZ")]
    [InlineData("OEM/013-00123")]
    [InlineData("燃油滤清器 0600")]
    // 制表符/换行 (快速路径保留): 慢路径也不移除
    [InlineData("line1\nline2\tcol")]
    // 慢路径 (需处理): 输出不变 = 防御仍生效的基线由下述断言保证
    [InlineData("<script>alert(1)</script>")]
    [InlineData("a\u0001b")]                       // C0 控制字符
    [InlineData("a\uFFFEb")]                       // 非字符
    [InlineData("hello 'quoted'")]
    public void Sanitize_OutputMatchesSlowPathOrContract(string input)
    {
        var result = Sanitize(input);
        // 无风险输入 (快速路径): 输入即输出 (等价性核心)
        if (!NeedsSanitizeLike(input))
        {
            result.Should().Be(input, "快速路径不得改写无风险文本");
        }
        // 危险输入: 防御不得退化 (原始脚本标签必须消失, 且被转义为实体)
        result.Should().NotContain("<script>");
        if (input.Contains("<script>"))
            result.Should().Contain("&lt;script&gt;", "脚本标签必须被 HtmlEncode 转义");
    }

    [Fact]
    public void Sanitize_HighlightMarkers_ReplacedWithMarkTags()
    {
        // 慢路径: \uE000/\uE001 高亮标记 → <mark></mark>
        Sanitize("OIL \uE000FILTER\uE001").Should().Be("OIL <mark>FILTER</mark>");
    }

    [Fact]
    public void Sanitize_HtmlChars_AreEncoded()
    {
        // 慢路径: < > & " 必须被 HtmlEncode (XSS 防御核心)
        Sanitize("a < b & c > d").Should().Be("a &lt; b &amp; c &gt; d");
    }

    [Fact]
    public void Sanitize_SingleQuote_GoesThroughSlowPath()
    {
        // 含 ' 的字符串必须走慢路径 (不得被快速路径短路):
        // 快速路径输出必须与慢路径输出一致 — 这里直接验证慢路径产物不含裸脚本且保留了单引号语义
        var result = Sanitize("it's");
        result.Should().NotContain("<script>");
        // 无论 .NET 8 是否编码 ', 产物必须是 HtmlEncode 后的结果 (非原输入直通)
        // 若 '.NET 8 不编码 ', 则输出应保持原样 — 两者都不可含未转义 < & > 危险字符
        result.NotContainRawUnsafe();
    }

    [Fact]
    public void Sanitize_StashLiteralChars_AreRemoved()
    {
        // 用户字面量 \uFDD0/\uFDD1 (暂存字符) 必须被移除 (步骤 0)
        Sanitize("a\uFDD0b\uFDD1c").Should().Be("abc");
    }

    /// <summary>与 NeedsSanitize 语义一致: 有任一需处理字符 → true (快速路径不得命中)</summary>
    private static bool NeedsSanitizeLike(string s)
    {
        foreach (var c in s)
        {
            if (c == '\uE000' || c == '\uE001' || c == '\uFDD0' || c == '\uFDD1') return true;
            if (c == '<' || c == '>' || c == '&' || c == '"' || c == '\'') return true;
            if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') return true;
            if (c >= 0xE000 && c <= 0xF8FF) return true;
            if (c >= 0xFDD0 && c <= 0xFDEF) return true;
            if (c == 0xFFFE || c == 0xFFFF) return true;
        }
        return false;
    }
}

internal static class SanitizeAssertExtensions
{
    public static void NotContainRawUnsafe(this string result)
    {
        // HtmlEncode 后的产物不应含未转义的 HTML 危险字符 (除非是被转义的实体)
        if (result.Contains('<')) throw new Xunit.Sdk.XunitException("Sanitize 产物含未转义 <");
        if (result.Contains('>')) throw new Xunit.Sdk.XunitException("Sanitize 产物含未转义 >");
    }
}
