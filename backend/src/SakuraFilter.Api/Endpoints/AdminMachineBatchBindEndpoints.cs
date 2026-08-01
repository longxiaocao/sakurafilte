using System.Security.Claims;
using SakuraFilter.Api.Services;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// Task 2: 批量绑定 MR.1 到机型端点 (admin 角色)
/// 用途: 后台一次性将多个 MR.1 绑定到指定 dict_machine 机型, 支持追加/替换模式
/// 设计:
///   - 路由组 /api/admin/machine-apps (admin 角色要求, V24-F19 spec F11)
///   - 200 全成功 (not_found 为空) / 207 部分成功 (not_found 非空)
///   - 400 超限 (BATCH_BIND_LIMIT_EXCEEDED) 或格式错误 (MR1_FORMAT_INVALID)
///   - 404 机型不存在 (MACHINE_NOT_FOUND)
///   - 错误码无 ERR_ 前缀 (与 ProblemDetailsFactory V2 错误码风格一致)
/// </summary>
public static class AdminMachineBatchBindEndpoints
{
    public static IEndpointRouteBuilder MapAdminMachineBatchBindEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/machine-apps").WithTags("AdminMachineApps")
            .RequireAuthorization("Admin");  // V24-F19: spec F11 要求所有 /api/admin/* 端点必须 RequireAuthorization

        // POST /api/admin/machine-apps/batch-bind
        group.MapPost("/batch-bind", async (
            BatchBindRequest req,
            MachineDictService svc,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            try
            {
                var @operator = ResolveUser(ctx);
                var result = await svc.BatchBindAsync(req, @operator, ct);

                // 200 全成功 (not_found 为空) / 207 部分成功 (not_found 非空, 部分 MR.1 在 products 表不存在)
                return result.NotFound.Count == 0
                    ? Results.Ok(result)
                    : Results.Json(result, statusCode: StatusCodes.Status207MultiStatus);
            }
            catch (KeyNotFoundException ex) when (ex.Message.Contains("MACHINE_NOT_FOUND"))
            {
                return Results.Problem(
                    title: "Not Found",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status404NotFound,
                    instance: ctx.Request.Path,
                    extensions: new Dictionary<string, object?> { ["errorCode"] = "MACHINE_NOT_FOUND" });
            }
            catch (ArgumentException ex) when (ex.Message.Contains("BATCH_BIND_LIMIT_EXCEEDED"))
            {
                return Results.Problem(
                    title: "Bad Request",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    instance: ctx.Request.Path,
                    extensions: new Dictionary<string, object?> { ["errorCode"] = "BATCH_BIND_LIMIT_EXCEEDED" });
            }
            catch (ArgumentException ex) when (ex.Message.Contains("MR1_FORMAT_INVALID"))
            {
                return Results.Problem(
                    title: "Bad Request",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    instance: ctx.Request.Path,
                    extensions: new Dictionary<string, object?> { ["errorCode"] = "MR1_FORMAT_INVALID" });
            }
        })
        .WithSummary("批量绑定 MR.1 到机型 (支持追加/替换模式, 幂等, 单次上限 200 个)")
        .WithName("AdminBatchBindMr1ToMachine");

        return app;
    }

    // 从 token 提取 operator (参考 AdminProductEndpoints.ResolveUser)
    private static string ResolveUser(HttpContext ctx) =>
        ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? ctx.User.FindFirst("sub")?.Value
        ?? ctx.Request.Headers["X-User"].FirstOrDefault()
        ?? "system";
}
