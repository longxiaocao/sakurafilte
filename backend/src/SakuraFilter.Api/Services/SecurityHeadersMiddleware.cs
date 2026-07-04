namespace SakuraFilter.Api.Services;

/// <summary>
/// 安全加固阶段4: 安全响应头中间件
/// 职责: 在每个响应上添加安全相关 HTTP 头, 防御常见 Web 攻击 (XSS/点击劫持/MIME 嗅探等)
/// 设计:
///   - 通过 IHostEnvironment 判断环境, 开发环境用宽松 CSP (Swagger 需要 unsafe-inline/eval)
///   - 生产环境用严格 CSP (禁止 unsafe-inline/eval)
///   - 中间件风格与 CorrelationIdMiddleware 一致 (RequestDelegate 构造, 不用 IMiddleware)
/// WHY 中间件而非 Filter: 安全头需对静态文件 + API 响应统一生效, 中间件粒度最合适
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _env;
    private readonly ILogger<SecurityHeadersMiddleware> _logger;

    // 开发环境 CSP: 允许 'unsafe-inline' + 'unsafe-eval' (Swagger UI 依赖)
    private const string DevCsp =
        "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; " +
        "font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; " +
        "base-uri 'self'; form-action 'self'";

    // 生产环境 CSP: 严格禁止 unsafe-inline/eval
    private const string ProdCsp =
        "default-src 'self'; script-src 'self'; " +
        "style-src 'self'; img-src 'self' data: https:; " +
        "font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; " +
        "base-uri 'self'; form-action 'self'";

    public SecurityHeadersMiddleware(
        RequestDelegate next,
        IHostEnvironment env,
        ILogger<SecurityHeadersMiddleware> logger)
    {
        _next = next;
        _env = env;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // WHY 在 _next 前设置头: ASP.NET Core 响应头在 body 写入后才提交,
        //   这里设置只是登记到 Response.Headers 集合, 后续中间件/端点可覆盖 (但通常不会)
        var headers = context.Response.Headers;

        // 防止 MIME 嗅探 (IE/Chrome 旧版会按内容猜测类型, 导致 text/plain 被当 HTML 执行)
        headers["X-Content-Type-Options"] = "nosniff";

        // 禁止页面被嵌入 iframe (防点击劫持)
        headers["X-Frame-Options"] = "DENY";

        // 控制 Referrer 信息泄露 (仅同源 + 跨源时降级为 origin)
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // 旧浏览器 XSS 过滤 (现代浏览器已废弃, 但兼容老版本)
        headers["X-XSS-Protection"] = "1; mode=block";

        // 禁用浏览器敏感权限 (地理位置/麦克风/摄像头), API 服务端不需要这些
        headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

        // CSP: 按环境区分 (开发 Swagger 需宽松)
        headers["Content-Security-Policy"] = _env.IsDevelopment() ? DevCsp : ProdCsp;

        // 跨源隔离 (防 Spectre 类侧信道攻击, 配合 COEP)
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";

        await _next(context);
    }
}
