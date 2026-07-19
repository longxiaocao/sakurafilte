using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SakuraFilter.Api.Extensions;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// V2 Task 2.1: OEM 3 排序管理端点 (修复漏洞 13)
/// 用途: 后台管理"OEM 3 优先展示"(类竞价排名),拖拽排序后批量保存
/// 设计:
///   - 路由组 /api/admin/xrefs/reorder (admin 角色要求, 由 Program.cs 全局 AddPolicy 兜底)
///   - 单条更新走 xmin 乐观锁 (修复漏洞 13: 防止两个管理员同时改同一 OEM 3 互相覆盖)
///   - 批量更新用事务 (全成功或全回滚,避免部分写入导致排序错乱)
///   - 冲突返回 409 XREF_CONFLICT, 前端提示"刷新重试"
/// </summary>
public static class AdminXrefReorderEndpoints
{
    public static IEndpointRouteBuilder MapAdminXrefReorderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/xrefs/reorder").WithTags("AdminXrefReorder")
            .RequireAuthorization("Admin");  // V24-F19: spec F11

        // ===== Task 2.1.2: GET /brands — 返回 Brand 列表 (brand / sortOrder / oem3Count) =====
        //   改进 2.1: IMemoryCache 5 分钟缓存 (brand 字典变更频率低, 避免每次聚合查询)
        //   失效时机: POST / 排序更新后自动清缓存 (见下方 POST 端点)
        group.MapGet("/brands", async (
            ProductDbContext db,
            IMemoryCache cache,
            CancellationToken ct) =>
        {
            const string cacheKey = "xref.brands.list";
            if (cache.TryGetValue(cacheKey, out List<object>? cached) && cached != null)
                return Results.Ok(new { total = cached.Count, items = cached });

            // 取 XrefOemBrand 字典 (仅未软删除),LEFT JOIN cross_references 统计 oem3 数量
            // WHY LEFT JOIN: 即使 brand 下 OEM 3 全部下架, 字典仍展示 (count=0),便于管理员清理
            var brands = await (
                from b in db.XrefOemBrands.AsNoTracking()
                where b.DeletedAt == null
                join x in db.CrossReferences.AsNoTracking()
                    on b.Brand equals x.OemBrand into bx
                from x in bx.DefaultIfEmpty()
                where x != null && !x.IsDiscontinued
                group x by new { b.Brand, b.SortOrder } into g
                orderby g.Key.SortOrder, g.Key.Brand
                select new
                {
                    brand = g.Key.Brand,
                    sortOrder = g.Key.SortOrder,
                    oem3Count = g.Count()
                }).ToListAsync(ct);

            var result = brands.Cast<object>().ToList();
            // V24-F85: 用 SetWithSize 替代手写 MemoryCacheEntryOptions (避免再次遗漏 Size 声明)
            cache.SetWithSize(cacheKey, result, TimeSpan.FromMinutes(5));
            return Results.Ok(new { total = result.Count, items = result });
        })
        .WithSummary("获取 OEM 品牌列表 (含 sortOrder + oem3Count, 按 sortOrder 排序)")
        .WithName("AdminXrefReorder_ListBrands");

        // ===== Task 2.1.3: GET /?oemBrand=BOSCH — 返回某 Brand 下 OEM 3 列表 =====
        group.MapGet("/", async (
            [FromQuery(Name = "oemBrand")] string oemBrand,
            ProductDbContext db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(oemBrand))
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "缺少参数",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "oemBrand 参数必填"
                });

            var items = await (
                from x in db.CrossReferences.AsNoTracking()
                where x.OemBrand == oemBrand && !x.IsDiscontinued
                join p in db.Products.AsNoTracking() on x.ProductId equals p.Id
                orderby x.SortOrder, x.OemNo3
                select new
                {
                    oemNo3 = x.OemNo3,
                    sortOrder = x.SortOrder,
                    mr1 = p.Mr1,
                    isPublished = x.IsPublished,
                    rowVersion = x.RowVersion  // xmin 乐观锁令牌, 透传给前端
                }).ToListAsync(ct);

            return Results.Ok(new { total = items.Count, items });
        })
        .WithSummary("获取指定 Brand 下 OEM 3 列表 (按 sortOrder 排序, 含 rowVersion 乐观锁令牌)")
        .WithName("AdminXrefReorder_ListByBrand");

        // ===== Task 2.1.4/2.1.5/2.1.6: POST / — 批量更新 sort_order (含乐观锁 + 事务) =====
        group.MapPost("/", async (
            XrefReorderRequest req,
            ProductDbContext db,
            IMemoryCache cache,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("AdminXrefReorder");
            if (string.IsNullOrWhiteSpace(req.OemBrand))
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "缺少参数",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "oemBrand 必填"
                });
            if (req.Items == null || req.Items.Count == 0)
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "缺少参数",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "items 不能为空"
                });

            // 事务: 全成功或全回滚 (避免部分写入导致排序错乱)
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                foreach (var item in req.Items)
                {
                    // 单条更新走 xmin 乐观锁 (修复漏洞 13)
                    //   SQL: UPDATE cross_references SET sort_order = @p
                    //        WHERE oem_brand = @b AND oem_no_3 = @o AND xmin = @rv
                    //   xmin 不匹配 → 0 行受影响 → 抛 XREF_CONFLICT
                    var rowsAffected = await db.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE cross_references
                        SET sort_order = {item.SortOrder}
                        WHERE oem_brand = {req.OemBrand}
                          AND oem_no_3 = {item.OemNo3}
                          AND xmin = {item.RowVersion}  -- V2: xmin 乐观锁, 类型 xid (uint)", ct);

                    if (rowsAffected == 0)
                    {
                        // 0 行受影响: xmin 不匹配 (其他人改过) 或 OEM 3 不存在
                        // 抛异常回滚事务, ProblemDetailsFactory 映射为 409 XREF_CONFLICT
                        throw new InvalidOperationException(
                            $"XREF_CONFLICT: OEM 3 '{item.OemNo3}' 排序更新冲突 (已被其他用户修改或已删除), 请刷新重试");
                    }
                }

                await tx.CommitAsync(ct);
                // 改进 2.1: 排序更新成功后清 brand 列表缓存 (oem3Count 可能变化)
                cache.Remove("xref.brands.list");
                logger.LogInformation("OEM 3 批量排序更新成功: brand={Brand} count={Count}", req.OemBrand, req.Items.Count);
                return Results.Ok(new { updated = req.Items.Count });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("XREF_CONFLICT"))
            {
                await tx.RollbackAsync(ct);
                return Results.Conflict(new ProblemDetails
                {
                    Type = "https://sakurafilter.com/errors/xref-conflict",
                    Title = "OEM 3 排序冲突",
                    Status = StatusCodes.Status409Conflict,
                    Detail = ex.Message,
                    Extensions = { ["errorCode"] = "XREF_CONFLICT" }
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                logger.LogError(ex, "OEM 3 批量排序更新失败: brand={Brand}", req.OemBrand);
                throw;
            }
        })
        .WithSummary("批量更新某 Brand 下 OEM 3 的 sort_order (含 xmin 乐观锁, 单事务全成功或全回滚)")
        .WithName("AdminXrefReorder_Update");

        return app;
    }
}

/// <summary>
/// V2 Task 2.1.4: OEM 3 批量排序请求体
/// </summary>
/// <param name="OemBrand">品牌名 (与 XrefOemBrand.Brand 一致)</param>
/// <param name="Items">OEM 3 列表 (含 sortOrder + rowVersion 乐观锁令牌)</param>
public record XrefReorderRequest(
    string OemBrand,
    List<XrefReorderItem> Items
);

/// <summary>
/// V2 Task 2.1.4: OEM 3 排序单项
/// </summary>
/// <param name="OemNo3">OEM 3 号</param>
/// <param name="SortOrder">新排序值 (类竞价排名, 数值越小越靠前)</param>
/// <param name="RowVersion">xmin 乐观锁令牌 (GET 接口返回的 rowVersion, 透传回来比对)</param>
public record XrefReorderItem(
    string OemNo3,
    int SortOrder,
    uint RowVersion
);
