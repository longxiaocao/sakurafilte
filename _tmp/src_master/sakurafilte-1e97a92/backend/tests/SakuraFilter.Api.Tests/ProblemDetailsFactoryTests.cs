using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SakuraFilter.Api.Services;
using System.Runtime.CompilerServices;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// V24-F65 (spec 26.14.9 建议 2): ProblemDetailsFactory 单元测试
///
/// 测试目标: 全局异常→ProblemDetails 映射 (RFC 7807)
///   - ArgumentException/KeyNotFoundException/InvalidOperationException/UnauthorizedAccessException
///   - OperationCanceledException (499)
///   - DbUpdateConcurrencyException (乐观锁冲突 409)
///   - DbUpdateException + Npgsql.PostgresException 23505/23503/23502/40P01
///   - 5xx 兜底分支不泄露 ex.Message (P0-2 安全要求)
///   - V2 错误码映射 (MR1_REQUIRED / OEM3_ALREADY_EXISTS 等 15 个)
///   - logger 调用: 5xx 记 LogError, DB 异常记 LogWarning, 业务异常不记
///
/// WHY ProblemDetailsFactory: 全局异常映射, 影响所有 API 错误响应格式
///   - 错误映射错位会导致前端拦截器误判状态码 (e.g. 409 误为 500 触发 Sentry)
///   - ex.Message 泄露会暴露 SQL/堆栈/文件路径 (OWASP Security Misconfiguration)
/// </summary>
public class ProblemDetailsFactoryTests
{
    private static HttpContext CreateContext(string path = "/api/test")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = "GET";
        return ctx;
    }

    /// <summary>
    /// 从 IResult 提取 ProblemHttpResult, 验证状态码 + errorCode
    ///   WHY ProblemHttpResult: Results.Problem(...) 的实际返回类型
    /// </summary>
    private static ProblemHttpResult AsProblem(IResult result)
    {
        result.Should().BeOfType<ProblemHttpResult>();
        return (ProblemHttpResult)result;
    }

    private static string GetErrorCode(ProblemHttpResult problem)
    {
        problem.ProblemDetails.Extensions.Should().ContainKey("errorCode");
        return problem.ProblemDetails.Extensions["errorCode"]!.ToString()!;
    }

    // ==================== 4xx 业务异常 ====================

    [Fact]
    public void ArgumentException_Returns400_WithErrValidationFailed()
    {
        var ctx = CreateContext();
        var ex = new ArgumentException("参数缺失");

        var result = ProblemDetailsFactory.FromException(ctx, ex);
        var problem = AsProblem(result);

        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Title.Should().Be("Bad Request");
        problem.ProblemDetails.Detail.Should().Be("参数缺失");
        GetErrorCode(problem).Should().Be("ERR_VALIDATION_FAILED");
    }

    [Fact]
    public void KeyNotFoundException_Returns404_WithErrNotFound()
    {
        var ctx = CreateContext();
        var ex = new KeyNotFoundException("产品 id=999 不存在");

        var result = ProblemDetailsFactory.FromException(ctx, ex);
        var problem = AsProblem(result);

        problem.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        GetErrorCode(problem).Should().Be("ERR_NOT_FOUND");
        problem.ProblemDetails.Detail.Should().Contain("产品 id=999");
    }

    [Fact]
    public void UnauthorizedAccessException_Returns403_WithErrForbidden()
    {
        var ctx = CreateContext();
        var ex = new UnauthorizedAccessException("无权限");

        var result = ProblemDetailsFactory.FromException(ctx, ex);
        var problem = AsProblem(result);

        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        GetErrorCode(problem).Should().Be("ERR_FORBIDDEN");
    }

    [Fact]
    public void OperationCanceledException_Returns499_WithErrCancelled()
    {
        var ctx = CreateContext();
        var ex = new OperationCanceledException("客户端断开");

        var result = ProblemDetailsFactory.FromException(ctx, ex);
        var problem = AsProblem(result);

        problem.StatusCode.Should().Be(499);
        GetErrorCode(problem).Should().Be("ERR_CANCELLED");
        problem.ProblemDetails.Detail.Should().Be("客户端断开或服务端超时");
    }

    // ==================== DbUpdateConcurrencyException ====================

    [Fact]
    public void DbUpdateConcurrencyException_Returns409_WithErrDbConflict()
    {
        // WHY: 乐观锁冲突 (xmin 不匹配) → 409, 让前端提示用户"数据已被修改, 请刷新"
        var ctx = CreateContext();
        var ex = new DbUpdateConcurrencyException("xmin 不匹配");

        var result = ProblemDetailsFactory.FromException(ctx, ex);
        var problem = AsProblem(result);

        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        GetErrorCode(problem).Should().Be("ERR_DB_CONFLICT");
        problem.ProblemDetails.Detail.Should().Contain("数据已被其他用户修改");
    }

    // ==================== DbUpdateException + PostgresException ====================

    [Fact]
    public void DbUpdateException_WithPostgres23505_Returns409_WithErrDbConflict()
    {
        // WHY: 唯一约束冲突 (23505) → 409, 让前端区分"重复提交"vs"普通校验失败"
        var ctx = CreateContext();
        var pgEx = new Npgsql.PostgresException("duplicate key", "ERROR", "ERROR", "23505");
        var ex = new DbUpdateException("db error", pgEx);

        var result = ProblemDetailsFactory.FromException(ctx, ex);
        var problem = AsProblem(result);

        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        GetErrorCode(problem).Should().Be("ERR_DB_CONFLICT");
        problem.ProblemDetails.Detail.Should().Contain("唯一约束冲突");
    }

    [Fact]
    public void DbUpdateException_WithPostgres23503_Returns400_WithErrDbConstraint()
    {
        // WHY: 外键约束 (23503) → 400, 让前端提示"关联数据不存在"
        var ctx = CreateContext();
        var pgEx = new Npgsql.PostgresException("foreign key", "ERROR", "ERROR", "23503");
        var ex = new DbUpdateException("db error", pgEx);

        var result = ProblemDetailsFactory.FromException(ctx, ex);
        var problem = AsProblem(result);

        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        GetErrorCode(problem).Should().Be("ERR_DB_CONSTRAINT");
        problem.ProblemDetails.Detail.Should().Contain("外键约束冲突");
    }

    [Fact]
    public void DbUpdateException_WithPostgres23502_Returns400_WithErrDbConstraint()
    {
        // WHY: 非空约束 (23502) → 400
        var ctx = CreateContext();
        var pgEx = new Npgsql.PostgresException("not null", "ERROR", "ERROR", "23502");
        var ex = new DbUpdateException("db error", pgEx);

        var result = ProblemDetailsFactory.FromException(ctx, ex);
        var problem = AsProblem(result);

        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        GetErrorCode(problem).Should().Be("ERR_DB_CONSTRAINT");
        problem.ProblemDetails.Detail.Should().Contain("非空约束冲突");
    }

    [Fact]
    public void DbUpdateException_WithPostgres40P01_Returns408_WithErrDbTimeout()
    {
        // WHY: 死锁 (40P01) → 408, 让前端提示"数据库死锁, 请重试"
        var ctx = CreateContext();
        var pgEx = new Npgsql.PostgresException("deadlock detected", "ERROR", "ERROR", "40P01");
        var ex = new DbUpdateException("db error", pgEx);

        var result = ProblemDetailsFactory.FromException(ctx, ex);
        var problem = AsProblem(result);

        problem.StatusCode.Should().Be(StatusCodes.Status408RequestTimeout);
        GetErrorCode(problem).Should().Be("ERR_DB_TIMEOUT");
        problem.ProblemDetails.Detail.Should().Contain("数据库死锁");
    }

    [Fact]
    public void DbUpdateException_NoInnerException_Returns400_WithErrDbConstraint()
    {
        // WHY: 无 InnerException 时走默认分支 (其他未识别的 DB 异常)
        var ctx = CreateContext();
        var ex = new DbUpdateException("db error", innerException: null);

        var result = ProblemDetailsFactory.FromException(ctx, ex);
        var problem = AsProblem(result);

        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        GetErrorCode(problem).Should().Be("ERR_DB_CONSTRAINT");
    }

    // ==================== 5xx 兜底 ====================

    [Fact]
    public void UnknownException_Returns500_WithErrInternal_DoesNotLeakMessage()
    {
        // P0-2 安全要求: 5xx 兜底分支不泄露 ex.Message, 仅返回通用提示
        //   WHY: ex.Message 可能含 SQL/堆栈/文件路径, 违反 OWASP Security Misconfiguration
        var ctx = CreateContext();
        var ex = new InvalidOperationException("DATABASE CONNECTION STRING=postgres://user:pass@host:5432/db");

        // 注: InvalidOperationException 走 409 分支, 用其他异常测 5xx
        var ex2 = new IOException("连接超时, internal stack: at Npgsql.Internal.NpgsqlConnector.ConnectAsync");

        var result = ProblemDetailsFactory.FromException(ctx, ex2);
        var problem = AsProblem(result);

        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        GetErrorCode(problem).Should().Be("ERR_INTERNAL");
        problem.ProblemDetails.Detail.Should().Be("服务内部错误,请联系管理员");
        problem.ProblemDetails.Detail.Should().NotContain("连接超时");
        problem.ProblemDetails.Detail.Should().NotContain("NpgsqlConnector");
    }

    [Fact]
    public void UnknownException_LogsError_WithRequestInfo()
    {
        // 5xx 异常必须记日志 (含完整 ex.Message + 堆栈), 供运维排查
        var ctx = CreateContext(path: "/api/admin/products/999");
        ctx.Request.Method = "DELETE";
        var ex = new IOException("连接超时");
        var loggerMock = new Mock<ILogger>();
        loggerMock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        ProblemDetailsFactory.FromException(ctx, ex, loggerMock.Object);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("未处理异常") && v.ToString()!.Contains("/api/admin/products/999") && v.ToString()!.Contains("DELETE")),
                ex,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void DbUpdateException_LogsWarning_NotError()
    {
        // P2-7 修复 v2: DB 异常记 Warning (业务可恢复, 非 5xx 致命错误)
        var ctx = CreateContext();
        var pgEx = new Npgsql.PostgresException("dup", "ERROR", "ERROR", "23505");
        var ex = new DbUpdateException("db error", pgEx);
        var loggerMock = new Mock<ILogger>();
        loggerMock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        ProblemDetailsFactory.FromException(ctx, ex, loggerMock.Object);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("DB 异常")),
                ex,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "DB 异常不应记 Error 级别");
    }

    [Fact]
    public void BusinessException_DoesNotLog()
    {
        // 业务异常 (ArgumentException/KeyNotFoundException 等) 不记日志
        //   WHY: 高频业务异常 (如 404) 记日志会污染日志流, 由端点层按需记日志
        var ctx = CreateContext();
        var ex = new KeyNotFoundException("产品不存在");
        var loggerMock = new Mock<ILogger>();

        ProblemDetailsFactory.FromException(ctx, ex, loggerMock.Object);

        loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    // ==================== InvalidOperationException + V2 错误码映射 ====================

    [Theory]
    [InlineData("MR1_REQUIRED: mr1 字段必填", "MR1_REQUIRED")]
    [InlineData("MR1_FORMAT_INVALID: mr1 格式错误", "MR1_FORMAT_INVALID")]
    [InlineData("MR1_ALREADY_EXISTS: mr1 已存在", "MR1_ALREADY_EXISTS")]
    [InlineData("OEM3_ALREADY_EXISTS: oem3 已存在", "OEM3_ALREADY_EXISTS")]
    [InlineData("MACHINE_TYPE_INVALID: 机型类型错误", "MACHINE_TYPE_INVALID")]
    [InlineData("XREF_CONFLICT: xref 冲突", "XREF_CONFLICT")]
    [InlineData("SEARCH_PAGE_TOO_DEEP: 分页过深", "SEARCH_PAGE_TOO_DEEP")]
    [InlineData("CURSOR_INVALID: cursor 无效", "CURSOR_INVALID")]
    [InlineData("CURSOR_EXPIRED: cursor 过期", "CURSOR_EXPIRED")]
    [InlineData("IMAGE_ROLE_SLOT_MISMATCH: 图片 role slot 不匹配", "IMAGE_ROLE_SLOT_MISMATCH")]
    [InlineData("IMAGE_DETAIL_SLOT_INVALID: detail slot 无效", "IMAGE_DETAIL_SLOT_INVALID")]
    [InlineData("IMAGE_PRIMARY_DUPLICATE: primary 重复", "IMAGE_PRIMARY_DUPLICATE")]
    [InlineData("IMAGE_DETAIL_SLOT_DUPLICATE: detail slot 重复", "IMAGE_DETAIL_SLOT_DUPLICATE")]
    [InlineData("MR1_NOT_FOUND: mr1 不存在", "MR1_NOT_FOUND")]
    [InlineData("OEM3_NOT_FOUND: oem3 不存在", "OEM3_NOT_FOUND")]
    public void InvalidOperationException_WithV2ErrorCode_Returns409_WithMappedCode(string message, string expectedCode)
    {
        // V2: InvalidOperationException 根据消息内容映射到 V2 错误码 (无 ERR_ 前缀)
        //   WHY: 让前端按 errorCode 精准映射 UI 文案/埋点, 比 HTTP status 更细粒度
        var ctx = CreateContext();
        var ex = new InvalidOperationException(message);

        var result = ProblemDetailsFactory.FromException(ctx, ex);
        var problem = AsProblem(result);

        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        GetErrorCode(problem).Should().Be(expectedCode);
    }

    [Fact]
    public void InvalidOperationException_NoV2Code_Returns409_WithErrValidationFailed()
    {
        // 向后兼容: 未匹配 V2 错误码时, 回退到 ERR_VALIDATION_FAILED
        var ctx = CreateContext();
        var ex = new InvalidOperationException("产品已存在 (oem_no_normalized=MR001)");

        var result = ProblemDetailsFactory.FromException(ctx, ex);
        var problem = AsProblem(result);

        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        GetErrorCode(problem).Should().Be("ERR_VALIDATION_FAILED");
    }

    // ==================== Instance 字段 ====================

    [Fact]
    public void AllExceptions_SetInstanceToRequestPath()
    {
        // instance 字段记录请求路径, 便于前端/运维定位出错端点
        var ctx = CreateContext(path: "/api/admin/products");
        var ex = new ArgumentException("test");

        var result = ProblemDetailsFactory.FromException(ctx, ex);
        var problem = AsProblem(result);

        problem.ProblemDetails.Instance.Should().Be("/api/admin/products");
    }
}
