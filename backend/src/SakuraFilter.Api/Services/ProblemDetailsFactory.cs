using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace SakuraFilter.Api.Services;

/// <summary>
/// Day 8.4: ProblemDetails 统一错误格式 (RFC 7807)
/// 用途: 所有 4xx/5xx 响应统一为 {type, title, status, detail, instance}
/// 好处:
///   - 前端 axios 拦截器只需 switch status 就能显示用户友好提示
///   - 后端业务异常 (KeyNotFoundException/InvalidOperationException) 统一映射
///   - 减少每个端点 try-catch
/// P0-2 修复: 5xx 兜底分支不再泄露 ex.Message, 改为通用提示 + 日志记录
///   WHY: ex.Message 可能含 SQL/堆栈/文件路径等敏感信息, 违反 OWASP Top 10 Security Misconfiguration
///   4xx 业务异常保留 ex.Message (业务友好提示, 无敏感信息)
/// </summary>
public static class ProblemDetailsFactory
{
    /// <summary>
    /// 把异常类型映射为 HTTP 状态码 (Day 8.4 MVP 范围)
    /// P0-2: 5xx 兜底分支需传 logger 记录原始异常, 不再把 ex.Message 写入 detail
    /// </summary>
    public static IResult FromException(HttpContext ctx, Exception ex, ILogger? logger = null)
    {
        // P0-2: 5xx 异常先记日志 (含完整 ex.Message + 堆栈), 供运维排查
        if (ex is not ArgumentException
            and not KeyNotFoundException
            and not InvalidOperationException
            and not UnauthorizedAccessException
            and not OperationCanceledException)
        {
            logger?.LogError(ex, "未处理异常 path={Path} method={Method}",
                ctx.Request.Path, ctx.Request.Method);
        }

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

            // P0-2 修复: 5xx 兜底不再泄露 ex.Message, 仅返回通用提示
            _ => Results.Problem(
                title: "Internal Server Error",
                detail: "服务内部错误,请联系管理员",
                statusCode: StatusCodes.Status500InternalServerError,
                instance: ctx.Request.Path)
        };
    }
}
