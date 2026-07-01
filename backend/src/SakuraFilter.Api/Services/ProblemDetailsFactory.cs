using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SakuraFilter.Api.Services;

/// <summary>
/// Day 8.4: ProblemDetails 统一错误格式 (RFC 7807)
/// 用途: 所有 4xx/5xx 响应统一为 {type, title, status, detail, instance}
/// 好处:
///   - 前端 axios 拦截器只需 switch status 就能显示用户友好提示
///   - 后端业务异常 (KeyNotFoundException/InvalidOperationException) 统一映射
///   - 减少每个端点 try-catch
/// </summary>
public static class ProblemDetailsFactory
{
    /// <summary>
    /// 把异常类型映射为 HTTP 状态码 (Day 8.4 MVP 范围)
    /// </summary>
    public static IResult FromException(HttpContext ctx, Exception ex)
    {
        return ex switch
        {
            ArgumentException ae => Results.Problem(
                title: "Bad Request",
                detail: ae.Message,
                statusCode: StatusCodes.Status400BadRequest,
                instance: ctx.Request.Path),

            KeyNotFoundException ke => Results.Problem(
                title: "Not Found",
                detail: ke.Message,
                statusCode: StatusCodes.Status404NotFound,
                instance: ctx.Request.Path),

            InvalidOperationException io => Results.Problem(
                title: "Conflict",
                detail: io.Message,
                statusCode: StatusCodes.Status409Conflict,
                instance: ctx.Request.Path),

            UnauthorizedAccessException ua => Results.Problem(
                title: "Forbidden",
                detail: ua.Message,
                statusCode: StatusCodes.Status403Forbidden,
                instance: ctx.Request.Path),

            OperationCanceledException => Results.Problem(
                title: "Request Cancelled",
                detail: "客户端断开或服务端超时",
                statusCode: 499,
                instance: ctx.Request.Path),

            _ => Results.Problem(
                title: "Internal Server Error",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                instance: ctx.Request.Path)
        };
    }
}
