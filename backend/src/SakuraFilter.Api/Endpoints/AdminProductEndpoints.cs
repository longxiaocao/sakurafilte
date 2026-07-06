using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SakuraFilter.Api.DTOs;
using SakuraFilter.Api.Extensions;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// 后台产品管理端点 (admin 角色)。
/// </summary>
public static class AdminProductEndpoints
{
    public static IEndpointRouteBuilder MapAdminProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/products").WithTags("AdminProducts");

        // 新增产品
        group.MapPost("/", async (ProductFormDto form, AdminProductService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try
            {
                var user = ResolveUser(ctx);
                var p = await svc.CreateAsync(form, user, ct);
                return Results.Created($"/api/admin/products/{p.Id}", p);
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
            {
                return Results.Conflict(new ProblemDetails
                {
                    Title = "产品已存在",
                    Status = StatusCodes.Status409Conflict,
                    Detail = $"OEM 号已存在: {pgEx.Detail}"
                });
            }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        })
        .WithSummary("后台创建产品 (含 cross-references + machine-applications 嵌套创建)").WithName("AdminCreateProduct");

        // 列表
        group.MapGet("/", async (
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromQuery] string? type,
            [FromQuery] string? keyword,
            [FromQuery] bool? includeDiscontinued,
            AdminProductService svc, CancellationToken ct) =>
        {
            var (items, total) = await svc.ListAsync(
                page ?? 1, pageSize ?? 50, type, keyword, includeDiscontinued ?? false, ct);
            return Results.Ok(new { total, page = page ?? 1, pageSize = pageSize ?? 50, items });
        })
        .WithSummary("后台产品列表 (支持搜索/筛选/排序/分页, 含 published/discontinued 状态)").WithName("AdminListProducts");

        // 高级搜索
        group.MapGet("/search", async (
            [AsParameters] AdminProductSearchRequest req,
            AdminProductService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try
            {
                var (items, total, nextCursor, countModeUsed) = await svc.SearchAsync(req, ct);
                var page = req.Page ?? 1;
                var pageSize = req.PageSize ?? 50;
                var countMode = req.NormalizeCountMode();
                var pagingMode = req.NormalizePagingMode();
                bool hasMore = items.Count >= pageSize
                    && (countMode == "none" || total > (long)page * pageSize);
                return Results.Ok(new
                {
                    total,
                    countMode,
                    countModeUsed,
                    pagingMode,
                    hasMore,
                    nextCursor,
                    page,
                    pageSize,
                    sizeTolerance = req.SizeTolerance ?? 5m,
                    items
                });
            }
            catch (ArgumentException ex)
            {
                return ProblemDetailsFactory.FromException(ctx, ex);
            }
        })
        .WithSummary("后台产品搜索 (admin 用, 含下架, 8 字段)").WithName("AdminSearchProducts");

        // 批量对比
        group.MapPost("/compare", async (
            CompareRequest body,
            AdminProductService svc, CancellationToken ct) =>
        {
            if (body?.Ids is null || body.Ids.Count == 0)
                return Results.BadRequest(new { error = "ids 不能为空" });
            if (body.Ids.Count > 6)
                return Results.BadRequest(new { error = "对比最多 6 个产品", given = body.Ids.Count });
            var items = await svc.CompareAsync(body.Ids, null, ct);
            return Results.Ok(new { count = items.Count, items });
        })
        .WithSummary("后台产品对比 (admin 用, 不排除下架, 上限 6 个)").WithName("AdminCompareProducts");

        // 详情
        group.MapGet("/{id:long}", async (long id, AdminProductService svc, AdminProductImageService imgSvc, HttpContext ctx, CancellationToken ct) =>
        {
            try
            {
                var p = await svc.GetByIdAsync(id, ct);
                var imgs = await imgSvc.ListAsync(id, ct);
                return Results.Ok(p with { Images = imgs });
            }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        })
        .WithSummary("后台产品详情 (含全部字段, 不排除下架)").WithName("AdminGetProduct");

        // 更新
        group.MapPut("/{id:long}", async (long id, ProductFormDto form, AdminProductService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try
            {
                var user = ResolveUser(ctx);
                var p = await svc.UpdateAsync(id, form, user, ct);
                return Results.Ok(p);
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
            {
                return Results.Conflict(new ProblemDetails
                {
                    Title = "产品已存在",
                    Status = StatusCodes.Status409Conflict,
                    Detail = $"OEM 号已存在: {pgEx.Detail}"
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("已被其他用户修改") || ex.Message.Contains("lost update"))
            {
                return Results.Conflict(new ProblemDetails
                {
                    Title = "数据已被修改",
                    Status = StatusCodes.Status409Conflict,
                    Detail = ex.Message
                });
            }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        })
        .WithSummary("后台更新产品 (xmin 乐观锁, 409 冲突)").WithName("AdminUpdateProduct");

        // 软删除
        group.MapDelete("/{id:long}", async (long id, AdminProductService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try
            {
                var user = ResolveUser(ctx);
                await svc.DeleteAsync(id, user, ct);
                return Results.Ok(new { id, discontinued = true });
            }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        })
        .WithSummary("后台软删除产品 (is_discontinued=true, 保留历史)").WithName("AdminDeleteProduct");

        // 恢复
        group.MapPost("/{id:long}/restore", async (long id, AdminProductService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try
            {
                var user = ResolveUser(ctx);
                await svc.RestoreAsync(id, user, ct);
                return Results.Ok(new { id, restored = true });
            }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        })
        .WithSummary("后台恢复已下架产品 (is_discontinued=false)").WithName("AdminRestoreProduct");

        // 上传产品图
        group.MapPost("/{id:long}/images/{slot:int}", async (
            long id, int slot, HttpRequest req, AdminProductImageService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (slot < 1 || slot > 6) return Results.BadRequest(new { error = "slot 必须在 1-6 之间" });
            if (!req.HasFormContentType) return Results.BadRequest(new { error = "需 multipart/form-data" });
            var form = await req.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file == null) return Results.BadRequest(new { error = "缺 file 字段" });
            var user = ResolveUser(req, ctx);
            try
            {
                using var stream = file.OpenReadStream();
                var img = await svc.UploadAsync(id, (short)slot, stream, file.ContentType ?? "image/jpeg", user, ct);
                return Results.Ok(img);
            }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("图片超过最大尺寸") || ex.Message.Contains("超过最大"))
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status413RequestEntityTooLarge,
                    title: "Payload Too Large");
            }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        })
        .WithName("AdminUploadProductImage")
        .DisableAntiforgery();

        // 删除产品图
        group.MapDelete("/{id:long}/images/{slot:int}", async (long id, int slot, AdminProductImageService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (slot < 1 || slot > 6) return Results.BadRequest(new { error = "slot 必须在 1-6 之间" });
            try
            {
                await svc.DeleteAsync(id, (short)slot, ct);
                return Results.Ok(new { productId = id, slot, deleted = true });
            }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        })
        .WithName("AdminDeleteProductImage");

        // 列出产品图
        group.MapGet("/{id:long}/images", async (long id, AdminProductImageService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(id, ct)))
        .WithName("AdminListProductImages");

        // 变更历史
        group.MapGet("/{id:long}/history", async (
            long id,
            [FromQuery] string? changeType,
            [FromQuery] string? since,
            [FromQuery] string? until,
            [FromQuery] int? limit,
            [FromQuery] string? cursor,
            AdminProductService svc,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            try
            {
                DateTime? sinceUtc = null, untilUtc = null;
                if (!string.IsNullOrEmpty(since))
                {
                    if (!DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var ps))
                        return Results.BadRequest(new { error = "since 必须是 ISO8601", since });
                    sinceUtc = ps;
                }
                if (!string.IsNullOrEmpty(until))
                {
                    if (!DateTime.TryParse(until, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var pu))
                        return Results.BadRequest(new { error = "until 必须是 ISO8601", until });
                    untilUtc = pu;
                }
                var cap = Math.Clamp(limit ?? 50, 1, 200);
                var page = await svc.GetHistoryAsync(id, cap, changeType, sinceUtc, untilUtc, cursor, ct);
                return Results.Ok(page);
            }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        })
        .WithName("AdminGetProductHistory")
        .RequireRateLimiting("global");

        return app;
    }

    private static string ResolveUser(HttpContext ctx) =>
        ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? ctx.User.FindFirst("sub")?.Value
        ?? ctx.Request.Headers["X-User"].FirstOrDefault()
        ?? "system";

    private static string ResolveUser(HttpRequest req, HttpContext ctx) =>
        ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? ctx.User.FindFirst("sub")?.Value
        ?? req.Headers["X-User"].FirstOrDefault()
        ?? "system";
}
