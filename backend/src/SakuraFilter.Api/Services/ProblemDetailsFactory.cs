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
/// IMPROVE-3: 在 extensions 中追加 errorCode 字段 (机器可读错误码)
///   WHY: HTTP status 粒度太粗 (400 可能是参数缺失/格式错误/业务校验失败), errorCode 让前端可精准映射 UI 文案/埋点
///   前端使用方式: axios 拦截器读取 err.response.data.errorCode, switch (errorCode) 显示针对性提示
///   错误码命名规范: ERR_XXX_YYY (全大写下划线), 如 ERR_VALIDATION_FAILED / ERR_NOT_FOUND
/// </summary>
public static class ProblemDetailsFactory
{
    // IMPROVE-3: 错误码常量 (ERR_XXX_YYY 格式, 与 HTTP status 一一对应)
    // 前端约定: 读取 extensions.errorCode 字段做 switch 映射, 比 status 更细粒度
    private const string ErrValidationFailed = "ERR_VALIDATION_FAILED";
    private const string ErrNotFound = "ERR_NOT_FOUND";
    private const string ErrConflict = "ERR_CONFLICT";
    private const string ErrForbidden = "ERR_FORBIDDEN";
    private const string ErrCancelled = "ERR_CANCELLED";
    private const string ErrInternal = "ERR_INTERNAL";

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
                instance: ctx.Request.Path,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrValidationFailed }),

            KeyNotFoundException ke => Results.Problem(
                title: "Not Found",
                detail: ke.Message,
                statusCode: StatusCodes.Status404NotFound,
                instance: ctx.Request.Path,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrNotFound }),

            InvalidOperationException io => Results.Problem(
                title: "Conflict",
                detail: io.Message,
                statusCode: StatusCodes.Status409Conflict,
                instance: ctx.Request.Path,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrConflict }),

            UnauthorizedAccessException ua => Results.Problem(
                title: "Forbidden",
                detail: ua.Message,
                statusCode: StatusCodes.Status403Forbidden,
                instance: ctx.Request.Path,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrForbidden }),

            OperationCanceledException => Results.Problem(
                title: "Request Cancelled",
                detail: "客户端断开或服务端超时",
                statusCode: 499,
                instance: ctx.Request.Path,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrCancelled }),

            // P0-2 修复: 5xx 兜底不再泄露 ex.Message, 仅返回通用提示
            _ => Results.Problem(
                title: "Internal Server Error",
                detail: "服务内部错误,请联系管理员",
                statusCode: StatusCodes.Status500InternalServerError,
                instance: ctx.Request.Path,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrInternal })
        };
    }
}
