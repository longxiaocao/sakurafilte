using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SakuraFilter.Api.Services;

/// <summary>
/// 表单绑定缺 Content-Type 错误的「前置处置」中间件 (2026-10-03, P2 技术债修复)。
///
/// WHY 需要它: 参数含 <c>IFormFile</c> 的端点在**参数绑定阶段**由 <c>RequestDelegateFactory</c>
///   调用 <c>FormFeature.ReadForm()</c>; 请求无 Content-Type 时框架抛固定消息的
///   <see cref="InvalidOperationException"/>。若放任其冒泡, ASP.NET Core 的
///   <c>ExceptionHandlerMiddleware</c> 会**先**以 Error 级别落一条
///   "An unhandled exception has occurred while executing the request." + 完整堆栈,
///   之后才轮到 <see cref="ProblemDetailsFactory"/> 生成 400 响应。
///   于是「客户端漏传 Content-Type」这种 4xx 输入问题被记成 ERROR —— 污染日志流、可能触发
///   错误率告警, 也会淹没真正的服务端故障。
///
/// 行为: 仅拦截「消息前缀匹配表单缺 Content-Type」的 <see cref="InvalidOperationException"/>
///   (判定复用 <see cref="ProblemDetailsFactory.IsFormBindingMissingContentType"/>, 单一来源),
///   记 Warning 并按既有 ProblemDetails 契约返回 400; **其余异常一律原样抛出**, 交由外层
///   <c>UseExceptionHandler</c> / <c>UseDeveloperExceptionPage</c> 按原逻辑处理, 不影响任何既有错误路径。
///
/// 注册位置: 必须注册在异常处理中间件**之前**(更外层), 否则异常先被后者捕获并记 Error, 本中间件失效。
/// </summary>
public sealed class FormBindingErrorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<FormBindingErrorMiddleware> _logger;

    public FormBindingErrorMiddleware(RequestDelegate next, ILogger<FormBindingErrorMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        // 响应已开始写出时不能改写 (交给外层兜底), 故 HasStarted 一并作为守卫条件
        catch (InvalidOperationException ex)
            when (!ctx.Response.HasStarted && ProblemDetailsFactory.IsFormBindingMissingContentType(ex))
        {
            _logger.LogWarning(
                "表单请求缺少 Content-Type (客户端输入问题, 非服务端故障) path={Path} method={Method}",
                ctx.Request.Path, ctx.Request.Method);

            var result = ProblemDetailsFactory.FromException(ctx, ex);
            await result.ExecuteAsync(ctx);
        }
    }
}
