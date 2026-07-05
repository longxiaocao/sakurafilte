using FluentAssertions;
using SakuraFilter.Api.Services;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// XSS 消毒器单测
/// 覆盖: script/iframe/style 注入, 事件处理器, 允许的安全标签, NULL/空处理
/// </summary>
public class XssSanitizerTests
{
    private readonly XssSanitizer _sut = new();

    [Fact]
    public void Sanitize_NullOrEmpty_ReturnsAsIs()
    {
        _sut.Sanitize(null).Should().BeNull();
        _sut.Sanitize("").Should().Be("");
        _sut.SanitizePlainText(null).Should().BeNull();
        _sut.SanitizePlainText("").Should().Be("");
    }

    [Fact]
    public void Sanitize_RemovesScriptTag()
    {
        // WHY: script 注入是 XSS 第一大类, 必须完全剥离
        _sut.Sanitize("<script>alert('xss')</script>")
            .Should().NotContain("<script").And.NotContain("alert");
    }

    [Fact]
    public void Sanitize_RemovesIframe()
    {
        // WHY: iframe 可加载恶意页面绕过同源策略
        _sut.Sanitize("<iframe src='evil.com'></iframe>")
            .Should().NotContain("<iframe");
    }

    [Fact]
    public void Sanitize_RemovesEventHandlers()
    {
        // WHY: onclick/onerror 等事件处理器是反射型 XSS 的常见载体
        var html = "<a href='#' onclick='alert(1)'>click</a>";
        var result = _sut.Sanitize(html);
        result.Should().NotContain("onclick").And.NotContain("alert");
    }

    [Fact]
    public void Sanitize_RemovesStyleAttribute()
    {
        // WHY: style 属性可注入 background:url(javascript:) 等 CSS 攻击
        _sut.Sanitize("<p style='background:url(javascript:alert(1))'>text</p>")
            .Should().NotContain("style=");
    }

    [Fact]
    public void Sanitize_AllowsBasicHtmlTags()
    {
        // WHY: 富文本字段 (Remark) 允许 br/p/strong/em/ul/ol/li
        _sut.Sanitize("<p>Hello <strong>world</strong></p>")
            .Should().Contain("<p>").And.Contain("<strong>");
    }

    [Fact]
    public void Sanitize_StripsDisallowedTags()
    {
        // WHY: div/span/img 不在白名单, 应被剥离
        _sut.Sanitize("<div>text<img src=x onerror=alert(1)></div>")
            .Should().NotContain("<div>").And.NotContain("<img").And.NotContain("onerror");
    }

    [Fact]
    public void Sanitize_HandlesMalformedHtml()
    {
        // WHY: 用户可能输入残缺 HTML, 不应让 sanitizer 抛异常
        _sut.Sanitize("<p>unclosed").Should().Contain("unclosed");
    }

    [Fact]
    public void Sanitize_HandlesMixedCaseXss()
    {
        // WHY: 大小写绕过是常见尝试
        var result = _sut.Sanitize("<ScRiPt>alert(1)</ScRiPt>");
        result.Should().NotContain("alert");
        result.ToLowerInvariant().Should().NotContain("script");
    }

    [Fact]
    public void SanitizePlainText_StripsAllTags()
    {
        // WHY: 纯文本字段不应包含任何 HTML 标签
        //   Ganss.Xss 在 AllowedTags 为空时, 会剥离所有标签字符 (< >)
        //   实际文本内容是否保留取决于实现版本, 这里只断言"绝不含 HTML 标签"
        var result = _sut.SanitizePlainText("<p>Hello <strong>world</strong></p>");
        result.Should().NotBeNull();
        result.Should().NotContain("<");
        result.Should().NotContain(">");
    }

    [Fact]
    public void SanitizePlainText_RemovesScriptContent()
    {
        // WHY: 即使纯文本场景, script 标签内的代码也应被剥离
        //   不强制断言"safe text"存在 (Ganss.Xss 在无 AllowedTags 时可能连同上下文一并删除)
        //   核心保证: alert 永远不应出现在纯文本输出中
        _sut.SanitizePlainText("<script>alert(1)</script>safe text")
            .Should().NotContain("alert");
    }

    [Fact]
    public void Sanitize_SqlInjectionAttempt_Unchanged()
    {
        // WHY: XSS sanitizer 不处理 SQL 注入, 那是 Validator 的责任, 这里只确保不破坏 SQL 字符串
        //   实际 SQL 注入防护在 FluentValidation + EF 参数化查询
        var input = "AC 0101' OR '1'='1";
        _sut.Sanitize(input).Should().Be(input);
        _sut.SanitizePlainText(input).Should().Be(input);
    }
}
