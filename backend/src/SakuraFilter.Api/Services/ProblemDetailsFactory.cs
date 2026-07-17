using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    // P2-7 修复 v2: DB 异常错误码 (让前端可区分乐观锁冲突/唯一约束/外键/其他 DB 错误)
    private const string ErrDbConflict = "ERR_DB_CONFLICT";        // 唯一约束/乐观锁冲突
    private const string ErrDbConstraint = "ERR_DB_CONSTRAINT";    // 外键/非空约束
    private const string ErrDbTimeout = "ERR_DB_TIMEOUT";          // 超时/死锁

    // V2 错误码(大写下划线格式,无 ERR_ 前缀)
    private const string V2Mr1Required = "MR1_REQUIRED";
    private const string V2Mr1FormatInvalid = "MR1_FORMAT_INVALID";
    private const string V2Mr1AlreadyExists = "MR1_ALREADY_EXISTS";
    private const string V2Oem3AlreadyExists = "OEM3_ALREADY_EXISTS";
    private const string V2MachineTypeInvalid = "MACHINE_TYPE_INVALID";
    private const string V2XrefConflict = "XREF_CONFLICT";
    private const string V2SearchPageTooDeep = "SEARCH_PAGE_TOO_DEEP";
    private const string V2CursorInvalid = "CURSOR_INVALID";
    private const string V2CursorExpired = "CURSOR_EXPIRED";
    // V2 Task 3.2: 图片分层上传错误码
    private const string V2ImageRoleSlotMismatch = "IMAGE_ROLE_SLOT_MISMATCH";
    private const string V2ImageDetailSlotInvalid = "IMAGE_DETAIL_SLOT_INVALID";
    private const string V2ImagePrimaryDuplicate = "IMAGE_PRIMARY_DUPLICATE";
    private const string V2ImageDetailSlotDuplicate = "IMAGE_DETAIL_SLOT_DUPLICATE";
    private const string V2Mr1NotFound = "MR1_NOT_FOUND";
    private const string V2Oem3NotFound = "OEM3_NOT_FOUND";

    /// <summary>
    /// 把异常类型映射为 HTTP 状态码 (Day 8.4 MVP 范围)
    /// P0-2: 5xx 兜底分支需传 logger 记录原始异常, 不再把 ex.Message 写入 detail
    /// P2-7 修复 v2: 增强 EF Core 异常映射 (DbUpdateException/DbUpdateConcurrencyException)
    ///   WHY: 之前 DbUpdateException 落到 500 兜底, 用户体验差且无 errorCode 区分
    ///        增强后: 唯一约束冲突 → 409, 乐观锁冲突 → 409, 外键约束 → 400
    /// </summary>
    public static IResult FromException(HttpContext ctx, Exception ex, ILogger? logger = null)
    {
        // P0-2: 5xx 异常先记日志 (含完整 ex.Message + 堆栈), 供运维排查
        // P2-7 修复 v2: 排除 DbUpdateException/DbUpdateConcurrencyException (业务可恢复, 记 Warning 即可)
        if (ex is not ArgumentException
            and not KeyNotFoundException
            and not InvalidOperationException
            and not UnauthorizedAccessException
            and not OperationCanceledException
            and not DbUpdateConcurrencyException
            and not DbUpdateException)
        {
            logger?.LogError(ex, "未处理异常 path={Path} method={Method}",
                ctx.Request.Path, ctx.Request.Method);
        }
        // P2-7 修复 v2: DB 异常记 Warning (业务可恢复, 非 5xx 致命错误)
        else if (ex is DbUpdateConcurrencyException or DbUpdateException)
        {
            logger?.LogWarning(ex, "DB 异常 path={Path} method={Method}",
                ctx.Request.Path, ctx.Request.Method);
        }

        return ex switch
        {
            ArgumentException ae => Results.Problem(
                title: "Bad Request",
                detail: ae.Message,
                statusCode: StatusCodes.Status400BadRequest,
                instance: ctx.Request.Path,
                extensions: new Dictionary<string, object?> { ["errorCode"] = MapErrorCode(ae.Message) }),

            KeyNotFoundException ke => Results.Problem(
                title: "Not Found",
                detail: ke.Message,
                statusCode: StatusCodes.Status404NotFound,
                instance: ctx.Request.Path,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrNotFound }),

            // V2: InvalidOperationException 根据消息内容映射到 V2 错误码(向后兼容旧 ERR_CONFLICT)
            InvalidOperationException io => Results.Problem(
                title: "Conflict",
                detail: io.Message,
                statusCode: StatusCodes.Status409Conflict,
                instance: ctx.Request.Path,
                extensions: new Dictionary<string, object?> { ["errorCode"] = MapErrorCode(io.Message) }),

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

            // P2-7 修复 v2: 乐观锁冲突 (xmin 不匹配或并发更新) → 409 Conflict
            DbUpdateConcurrencyException => Results.Problem(
                title: "数据已被修改",
                detail: "数据已被其他用户修改, 请刷新后重试",
                statusCode: StatusCodes.Status409Conflict,
                instance: ctx.Request.Path,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrDbConflict }),

            // P2-7 修复 v2: DbUpdateException 按 InnerException SqlState 细分
            //   23505 unique_violation → 409 (唯一约束冲突)
            //   23503 foreign_key_violation → 400 (外键约束)
            //   23502 not_null_violation → 400 (非空约束)
            //   40P01 deadlock_detected → 408 (死锁, 建议重试)
            DbUpdateException due => Results.Problem(
                title: "数据约束冲突",
                detail: GetDbUpdateDetail(due),
                statusCode: GetDbUpdateStatusCode(due),
                instance: ctx.Request.Path,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = GetDbUpdateErrorCode(due)
                }),

            // P0-2 修复: 5xx 兜底不再泄露 ex.Message, 仅返回通用提示
            _ => Results.Problem(
                title: "Internal Server Error",
                detail: "服务内部错误,请联系管理员",
                statusCode: StatusCodes.Status500InternalServerError,
                instance: ctx.Request.Path,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrInternal })
        };
    }

    // P2-7 修复 v2: 根据 DbUpdateException 的 InnerException SqlState 提取友好提示
    private static string GetDbUpdateDetail(DbUpdateException due)
    {
        // Npgsql PostgresException 有 SqlState 属性
        var pgEx = due.InnerException as Npgsql.PostgresException;
        return pgEx?.SqlState switch
        {
            "23505" => "数据已存在 (唯一约束冲突)",
            "23503" => "关联数据不存在 (外键约束冲突)",
            "23502" => "必填字段为空 (非空约束冲突)",
            "40P01" => "数据库死锁, 请重试",
            _ => "数据保存失败, 请检查输入"
        };
    }

    // P2-7 修复 v2: 根据 SqlState 映射 HTTP 状态码
    private static int GetDbUpdateStatusCode(DbUpdateException due)
    {
        var pgEx = due.InnerException as Npgsql.PostgresException;
        return pgEx?.SqlState switch
        {
            "23505" => StatusCodes.Status409Conflict,           // unique_violation → 409
            "23503" => StatusCodes.Status400BadRequest,         // foreign_key_violation → 400
            "23502" => StatusCodes.Status400BadRequest,         // not_null_violation → 400
            "40P01" => StatusCodes.Status408RequestTimeout,    // deadlock_detected → 408
            _ => StatusCodes.Status400BadRequest
        };
    }

    // P2-7 修复 v2: 根据 SqlState 映射 errorCode
    private static string GetDbUpdateErrorCode(DbUpdateException due)
    {
        var pgEx = due.InnerException as Npgsql.PostgresException;
        return pgEx?.SqlState switch
        {
            "23505" => ErrDbConflict,
            "40P01" => ErrDbTimeout,
            _ => ErrDbConstraint
        };
    }

    // V2: 根据异常消息内容映射到 V2 错误码(无 ERR_ 前缀),未匹配时回退到旧错误码
    private static string MapErrorCode(string message)
    {
        if (message.Contains("MR1_REQUIRED")) return V2Mr1Required;
        if (message.Contains("MR1_FORMAT_INVALID")) return V2Mr1FormatInvalid;
        if (message.Contains("MR1_ALREADY_EXISTS")) return V2Mr1AlreadyExists;
        if (message.Contains("OEM3_ALREADY_EXISTS")) return V2Oem3AlreadyExists;
        if (message.Contains("MACHINE_TYPE_INVALID")) return V2MachineTypeInvalid;
        if (message.Contains("XREF_CONFLICT")) return V2XrefConflict;
        if (message.Contains("SEARCH_PAGE_TOO_DEEP")) return V2SearchPageTooDeep;
        if (message.Contains("CURSOR_INVALID")) return V2CursorInvalid;
        if (message.Contains("CURSOR_EXPIRED")) return V2CursorExpired;
        // V2 Task 3.2: 图片分层上传错误码
        if (message.Contains("IMAGE_ROLE_SLOT_MISMATCH")) return V2ImageRoleSlotMismatch;
        if (message.Contains("IMAGE_DETAIL_SLOT_INVALID")) return V2ImageDetailSlotInvalid;
        if (message.Contains("IMAGE_PRIMARY_DUPLICATE")) return V2ImagePrimaryDuplicate;
        if (message.Contains("IMAGE_DETAIL_SLOT_DUPLICATE")) return V2ImageDetailSlotDuplicate;
        if (message.Contains("MR1_NOT_FOUND")) return V2Mr1NotFound;
        if (message.Contains("OEM3_NOT_FOUND")) return V2Oem3NotFound;
        // 向后兼容: 未匹配 V2 错误码时,根据 HTTP 语义返回旧错误码
        return ErrValidationFailed;
    }
}
