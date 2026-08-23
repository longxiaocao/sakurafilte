using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using SakuraFilter.Etl;
using Xunit;

namespace SakuraFilter.Etl.Tests;

/// <summary>
/// V24-F23: ETL GetStringOrNull 控制字符过滤测试 (spec Task 5.1.17, 修复 D3-27)
///
/// 测试目标: EtlImportService.GetStringOrNull (private static)
///   - 含 C0/C1/BMP 私用区/非字符的字符串应被清理 (长度变短)
///   - 允许 \t \n \r (Excel 多行文本兼容)
///   - null / 非字符串类型返回 null
///
/// WHY 反射: GetStringOrNull 是 private static, 不暴露公共入口
///   - 用反射调用 + 构造 JsonElement 输入, 与 ETL 实际调用路径一致
/// </summary>
public class EtlGetStringOrNullTests
{
    /// <summary>反射缓存: GetStringOrNull 方法 (private static)</summary>
    private static readonly MethodInfo GetStringOrNullMethod =
        typeof(EtlImportService).GetMethod("GetStringOrNull",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("EtlImportService.GetStringOrNull 未找到 (反射失败)");

    /// <summary>调用 private static GetStringOrNull</summary>
    private static string? InvokeGetStringOrNull(JsonElement element, string prop)
    {
        return (string?)GetStringOrNullMethod.Invoke(null, new object[] { element, prop });
    }

    /// <summary>从任意对象序列化为 JsonElement (自动转义控制字符)</summary>
    /// <remarks>WHY JsonSerializer: 手工拼接 JSON 字符串遇到 \t \n \r 等控制字符会违反 JSON 规范 (RFC 8259 要求转义),
    /// JsonDocument.Parse 会抛 JsonReaderException; 用 JsonSerializer 序列化可自动转义为 \t \n \r 字面序列</remarks>
    private static JsonElement ParseRoot<T>(T obj)
    {
        var json = JsonSerializer.Serialize(obj);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Theory]
    [InlineData("\u0000", "U+0000 NULL")]
    [InlineData("\u001F", "U+001F US")]
    [InlineData("\u007F", "U+007F DEL")]
    [InlineData("\u009F", "U+009F APC")]
    [InlineData("\uE000", "U+E000 BMP 私用区 (MarkOpen)")]
    [InlineData("\uE001", "U+E001 BMP 私用区 (MarkClose)")]
    [InlineData("\uFDD0", "U+FDD0 非字符 (StashOpen)")]
    [InlineData("\uFDD1", "U+FDD1 非字符 (StashClose)")]
    [InlineData("\uFFFE", "U+FFFE 非字符")]
    public void Etl_GetStringOrNull_ControlChar_Stripped(string badChar, string desc)
    {
        // WHY: spec D3-27 要求 GetStringOrNull 过滤控制字符, 防 ETL 数据污染 Meilisearch 高亮占位符
        //   输入 "Bad" + badChar + "Name" → 清理后应为 "BadName" (移除 1 字符)
        var elem = ParseRoot(new { name = "Bad" + badChar + "Name" });
        var result = InvokeGetStringOrNull(elem, "name");
        result.Should().NotBeNull();
        result.Should().Be("BadName", $"含 {desc} 的字符串应被清理");
    }

    [Theory]
    [InlineData("\t", "TAB")]
    [InlineData("\n", "LF")]
    [InlineData("\r", "CR")]
    [InlineData("\t\n\r", "TAB+LF+CR")]
    public void Etl_GetStringOrNull_AllowsTabNewlineCr(string allowedChar, string desc)
    {
        // WHY: Excel 多行文本含 \t \n \r, 不应被清理
        var elem = ParseRoot(new { name = "Multi" + allowedChar + "Line" });
        var result = InvokeGetStringOrNull(elem, "name");
        result.Should().NotBeNull();
        result.Should().Be($"Multi{allowedChar}Line", $"{desc} 应允许保留");
    }

    [Fact]
    public void Etl_GetStringOrNull_Null_WhenPropMissing()
    {
        // WHY: 属性不存在时返回 null (与原实现一致)
        var elem = ParseRoot(new { other = "value" });
        var result = InvokeGetStringOrNull(elem, "name");
        result.Should().BeNull();
    }

    [Fact]
    public void Etl_GetStringOrNull_Null_WhenNotStringKind()
    {
        // WHY: 非 String ValueKind (Number/Bool/Object/Array) 返回 null
        var elem = ParseRoot(new { name = 123 });
        var result = InvokeGetStringOrNull(elem, "name");
        result.Should().BeNull();
    }

    [Fact]
    public void Etl_GetStringOrNull_PassesCleanString()
    {
        // WHY: 合法字符串应原样返回 (无清理)
        var elem = ParseRoot(new { name = "Air Filter BOSCH F0001" });
        var result = InvokeGetStringOrNull(elem, "name");
        result.Should().Be("Air Filter BOSCH F0001");
    }
}

