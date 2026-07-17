using FluentAssertions;
using SakuraFilter.Core.Extensions;
using Xunit;

namespace SakuraFilter.Etl.Tests;

/// <summary>
/// v24 架构清理: Core.Extensions.LikeEscapeExtensions 单元测试
///
/// 测试目标: 直接验证 Core 层实现 (不依赖 Api 层 shim 转发)
///   - 与 SakuraFilter.Api.Tests.LikeEscapeExtensionsTests 形成双重保障
///   - WHY 双重保障: Api 层 shim 调用 Core 实现, 若 Core 实现被误改, 两套测试同时失败
///   - WHY 放 Etl.Tests: 该项目已引用 SakuraFilter.Core, 且 LikeEscape 主要消费方之一
///     PostgresSearchProvider 在 Search 项目, 但 Search 不引用 Api, 测试必须在非 Api 项目
///
/// 覆盖: 三个特殊字符 (%, _, \) 的转义 + NULL/空串 + 顺序正确性 + Unicode + 长输入
/// </summary>
public class CoreLikeEscapeExtensionsTests
{
    // ===== NULL / 空串 =====

    [Fact]
    public void Escape_NullInput_ReturnsNull()
    {
        // WHY: null 不抛异常, 与原 string.Replace 行为一致, 调用方不用额外判空
        string? input = null;
        input!.EscapeLikePattern().Should().BeNull();
    }

    [Fact]
    public void Escape_EmptyString_ReturnsEmpty()
    {
        string.Empty.EscapeLikePattern().Should().Be(string.Empty);
    }

    // ===== 单字符转义 =====

    [Fact]
    public void Escape_PercentSign_PrependsBackslash()
    {
        // WHY: % 是 ILIKE 通配符 (匹配任意字符序列), 必须转义为字面量
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

    // ===== 组合场景 =====

    [Fact]
    public void Escape_AllThreeCharsTogether_AllEscaped()
    {
        // WHY: 组合场景, 顺序是 \\ → % → _, 验证实际顺序正确
        "10%_test\\".EscapeLikePattern().Should().Be("10\\%\\_test\\\\");
    }

    [Fact]
    public void Escape_BackslashBeforePercent_DoesNotDoubleEscape()
    {
        // WHY 顺序关键: 先转 \, 再转 %, 否则 \\ 会变 \\\\\, 重复转义
        //   正确顺序: "a\%" → "a\\\\\\%" (\\ → \\\\, 然后 % → \\%)
        var input = @"a\%";
        var result = input.EscapeLikePattern();
        result.Should().Be(@"a\\\%");
    }

    // ===== 纯文本 (无特殊字符) =====

    [Fact]
    public void Escape_PlainText_Unchanged()
    {
        "AC 0101".EscapeLikePattern().Should().Be("AC 0101");
        "E2E20260705".EscapeLikePattern().Should().Be("E2E20260705");
    }

    // ===== Unicode / Emoji =====

    [Fact]
    public void Escape_ChineseInput_Unchanged()
    {
        // WHY: 中文不应被转义, 只处理 SQL 通配符
        "滤清器 AC".EscapeLikePattern().Should().Be("滤清器 AC");
    }

    [Fact]
    public void Escape_EmojiInput_Unchanged()
    {
        // WHY: Emoji 多字节字符不应被转义
        "🔧".EscapeLikePattern().Should().Be("🔧");
    }

    // ===== D7/D8 螺纹规格实际用例 (v24 修复场景) =====

    [Fact]
    public void Escape_ThreadSpec_M14x1_5_Unchanged()
    {
        // WHY: D7/D8 螺纹规格如 "M14×1.5" 不含 SQL 通配符, 应原样返回
        //   × 是 Unicode 乘号 (U+00D7), 不是 SQL 通配符
        "M14×1.5".EscapeLikePattern().Should().Be("M14×1.5");
    }

    [Fact]
    public void Escape_ThreadSpec_WithUnderscore_EscapesUnderscore()
    {
        // WHY: 部分螺纹规格用 _ 分隔 (如 "M14_1.5"), _ 必须转义
        "M14_1.5".EscapeLikePattern().Should().Be("M14\\_1.5");
    }

    // ===== 边界场景 =====

    [Fact]
    public void Escape_OnlySpecialChars_AllEscaped()
    {
        // WHY: 全是特殊字符的输入, 应全部转义
        "%_\\".EscapeLikePattern().Should().Be("\\%\\_\\\\");
    }

    [Fact]
    public void Escape_LongInput_HandlesCorrectly()
    {
        // WHY: 长输入 (如 1000 字符) 不应导致性能问题或栈溢出
        var input = new string('a', 1000) + "%" + new string('b', 1000);
        var result = input.EscapeLikePattern();
        result.Should().Contain("\\%");
        result!.Length.Should().Be(input.Length + 1, "仅 % 增加一个 \\ 转义符");  // CS8602: input 非 null, result 非 null, ! 抑制
    }

    [Fact]
    public void Escape_RepeatedSpecialChars_AllEscaped()
    {
        // WHY: 重复特殊字符应全部转义, 不能只转义第一个
        "%%%".EscapeLikePattern().Should().Be("\\%\\%\\%");
        "___".EscapeLikePattern().Should().Be("\\_\\_\\_");
        "\\\\".EscapeLikePattern().Should().Be("\\\\\\\\");
    }
}
