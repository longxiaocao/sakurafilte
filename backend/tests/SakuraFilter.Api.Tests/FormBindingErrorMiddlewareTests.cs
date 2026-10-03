using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SakuraFilter.Api.Services;
using System.Text;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// 表单绑定缺 Content-Type 前置中间件单元测试 (2026-10-03 P2 技术债修复)。
///
/// 测试目标: <see cref="FormBindingErrorMiddleware"/> 只拦截「表单缺 Content-Type」这一类框架异常,
///   其余异常必须原样抛出。
///
/// WHY 需要这层测试: 该中间件注册在 ExceptionHandlerMiddleware **之后 (更内层)**, 是
///   「防止客户端输入问题被记为 Error 级服务端故障」的唯一手段。拦截过宽会吞掉真实服务端
///   异常 (掩盖故障), 过窄则日志噪音回归 —— 两侧都是生产可观测性风险, 必须有回归保护。
///   注: 注册顺序本身由 2026-10-03 生产复测确认 (顺序写反时本中间件永不触发), 见
///   MiddlewarePipelineExtensions 第 2.5 步注释。
/// </summary>
public class FormBindingErrorMiddlewareTests
{
    /// <summary>框架在 FormFeature.ReadForm() 抛出的固定消息 (.NET 8 稳定, 见 ProblemDetailsFactory)</summary>
    private const string FormBindingMessage =
        "This request does not have a Content-Type header. Form content is only supported when the Content-Type header is set to 'application/x-www-form-urlencoded' or 'multipart/form-data'.";

    private static (FormBindingErrorMiddleware Middleware, HttpContext Context, MemoryStream Body) Create(
        RequestDelegate next)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var ctx = new DefaultHttpContext { RequestServices = services };
        ctx.Request.Path = "/api/admin/etl/upload";
        ctx.Request.Method = "POST";
        var body = new MemoryStream();
        ctx.Response.Body = body;
        var logger = services.GetRequiredService<ILogger<FormBindingErrorMiddleware>>();
        return (new FormBindingErrorMiddleware(next, logger), ctx, body);
    }

    [Fact]
    public async Task InvokeAsync_FormBindingMissingContentType_Returns400_WithFriendlyDetail()
    {
        // 覆盖: 参数绑定阶段抛出的固定英文异常 → 中间件就地响应 400, 不冒泡到 ExceptionHandlerMiddleware
        //   (后者会先以 Error 级别记 "An unhandled exception has occurred ..." + 堆栈)
        var (middleware, ctx, body) = Create(_ => throw new InvalidOperationException(FormBindingMessage));

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var json = Encoding.UTF8.GetString(body.ToArray());
        json.Should().Contain("multipart/form-data");
        // 防御: 不得把框架英文原文透传给调用方
        json.Should().NotContain("This request does not have a Content-Type header");
    }

    [Fact]
    public async Task InvokeAsync_OtherInvalidOperationException_IsRethrown()
    {
        // 回归保护: 业务 InvalidOperationException (如 MR1 冲突) 必须原样冒泡, 交由全局异常映射返回 409
        var (middleware, ctx, _) = Create(_ => throw new InvalidOperationException("MR1_ALREADY_EXISTS: mr1 已存在"));

        var act = async () => await middleware.InvokeAsync(ctx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("MR1_ALREADY_EXISTS*");
    }

    [Fact]
    public async Task InvokeAsync_NonInvalidOperationException_IsRethrown()
    {
        // 回归保护: 其他异常类型不受本中间件影响
        var (middleware, ctx, _) = Create(_ => throw new ArgumentException("bad arg"));

        var act = async () => await middleware.InvokeAsync(ctx);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task InvokeAsync_ResponseAlreadyStarted_IsRethrown()
    {
        // 边界: 响应已开始写出时不能改写 (会造成半截响应/二次写入异常), 必须原样冒泡交给外层兜底
        var (middleware, ctx, _) = Create(_ => throw new InvalidOperationException(FormBindingMessage));
        ctx.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        var act = async () => await middleware.InvokeAsync(ctx);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>测试替身: 强制 HasStarted=true, 覆盖「响应已开始」守卫分支</summary>
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state) { }

        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
