using Ganss.Xss;

namespace SakuraFilter.Api.Services;

/// <summary>
/// 安全加固阶段4: XSS 消毒服务 (单例)
/// 职责: 封装 Ganss.Xss.HtmlSanitizer, 对用户输入的字符串进行 HTML 消毒
/// 设计:
///   - Sanitize: 允许基本 HTML 标签 (br/p/strong/em/ul/ol/li), 适用于富文本字段
///   - SanitizePlainText: 移除所有 HTML 标签, 适用于纯文本字段 (Email/FullName/Remark)
///   - 禁止 script/iframe/style/event handler (onclick/onerror 等)
/// WHY 单例: HtmlSanitizer 是线程安全的, 复用实例避免重复构造开销
/// WHY 不消毒 Username: 由 Validator 限制为 [a-zA-Z0-9_-], 无注入风险
/// WHY 不消毒 Password: 直接 BCrypt 哈希, HTML 字符不影响安全性
/// </summary>
public class XssSanitizer
{
    // 允许基本 HTML 标签的消毒器 (富文本场景, 如备注中的换行/加粗)
    private readonly HtmlSanitizer _htmlSanitizer;

    // 纯文本消毒器 (移除所有 HTML 标签, 仅保留文本内容)
    private readonly HtmlSanitizer _plainTextSanitizer;

    public XssSanitizer()
    {
        // 基本 HTML 消毒器: 仅允许安全标签, 禁止 script/iframe/style/event handler
        _htmlSanitizer = new HtmlSanitizer();
        _htmlSanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "br", "p", "strong", "em", "ul", "ol", "li" })
        {
            _htmlSanitizer.AllowedTags.Add(tag);
        }
        // 默认已禁止 script/iframe/style, 并移除所有 event handler 属性 (onclick/onerror 等)
        // 显式禁止 style 属性 (防止 CSS 注入)
        _htmlSanitizer.AllowedAttributes.Remove("style");
        _htmlSanitizer.AllowedAttributes.Remove("class");
        _htmlSanitizer.AllowedAttributes.Remove("id");

        // 纯文本消毒器: 不允许任何标签, 所有 HTML 标签被移除, 仅保留文本内容
        _plainTextSanitizer = new HtmlSanitizer();
        _plainTextSanitizer.AllowedTags.Clear();
        _plainTextSanitizer.AllowedAttributes.Clear();
    }

    /// <summary>
    /// 消毒 HTML 输入 (允许基本安全标签)
    /// 输入 null/空返回原值, 否则移除危险标签 (script/iframe/style) 和事件处理器
    /// 适用: 富文本字段 (如 Remark, 允许 br/p/strong 等)
    /// </summary>
    public string? Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return _htmlSanitizer.Sanitize(input);
    }

    /// <summary>
    /// 消毒为纯文本 (移除所有 HTML 标签)
    /// 输入 null/空返回原值, 否则剥离所有 HTML, 仅保留文本内容
    /// 适用: 纯文本字段 (Email/FullName/Remark 等, 不应包含任何 HTML)
    /// </summary>
    public string? SanitizePlainText(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return _plainTextSanitizer.Sanitize(input);
    }
}
