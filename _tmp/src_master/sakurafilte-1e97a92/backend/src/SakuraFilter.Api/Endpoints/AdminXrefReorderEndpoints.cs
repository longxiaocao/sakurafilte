using System.Security.Claims;
using System.Text.Json;
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
                return Results.Ok(new { brands = cached });

            // 取 XrefOemBrand 字典 (仅未软删除),LEFT JOIN cross_references 统计白名单内 OEM 3 数量
            // WHY LEFT JOIN: 即使 brand 下白名单为空, 字典仍展示 (count=0),便于管理员清理
            // WHY 字典始终展示: 品牌字典独立于白名单数据, 不应因白名单为空而隐藏品牌
            //   上一版 where x != null && x.SortOrder > 0 导致白名单清空后品牌列表为空,
            //   用户无法选择品牌新增白名单 (用户反馈: "品牌列表为空, 没办法直接维护")
            // 🔧 P0 fix: 之前 where x == null || (!x.IsDiscontinued && x.SortOrder > 0) 仍有 bug:
            //   当品牌有关联产品但 sort_order=0 (白名单清空) 时, x != null 且不满足 sort_order>0,
            //   品牌被错误过滤掉, 只剩 1 条记录; 正确做法是 WHERE 不过滤关联产品, 改在 COUNT 用条件聚合
            // oem3Count 用条件聚合: 仅统计白名单内 (x != null && !x.IsDiscontinued && x.SortOrder > 0)
            var brands = await (
                from b in db.XrefOemBrands.AsNoTracking()
                where b.DeletedAt == null
                join x in db.CrossReferences.AsNoTracking()
                    on b.Brand equals x.OemBrand into bx
                from x in bx.DefaultIfEmpty()
                group x by new { b.Brand, b.SortOrder } into g
                orderby g.Key.SortOrder, g.Key.Brand
                select new
                {
                    brand = g.Key.Brand,
                    sortOrder = g.Key.SortOrder,
                    oem3Count = g.Count(x => x != null && !x.IsDiscontinued && x.SortOrder > 0)
                }).ToListAsync(ct);

            var result = brands.Cast<object>().ToList();
            // V24-F85: 用 SetWithSize 替代手写 MemoryCacheEntryOptions (避免再次遗漏 Size 声明)
            cache.SetWithSize(cacheKey, result, TimeSpan.FromMinutes(5));
            return Results.Ok(new { brands = result });
        })
        .WithSummary("获取 OEM 品牌列表 (含 sortOrder + oem3Count, 按 sortOrder 排序)")
        .WithName("AdminXrefReorder_ListBrands");

        // ===== 白名单改造: POST /brands — 新增品牌到 xref_oem_brand 字典 =====
        //   用户需求: 品牌应可独立新增, 新增后即可在该品牌下添加白名单
        //   幂等性: 若品牌已存在 (未软删) 返 409; 若曾软删则恢复 (DeletedAt = null)
        //   sort_order: 新增时 = max(sort_order) + 1, 排到字典末尾 (便于管理员后续拖拽调整)
        //   安全: trim + 非空校验, 防止空白品牌名入库
        group.MapPost("/brands", async (
            XrefBrandCreatePayload req,
            ProductDbContext db,
            IMemoryCache cache,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("AdminXrefReorder");
            if (req == null || string.IsNullOrWhiteSpace(req.Brand))
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "缺少参数", Status = StatusCodes.Status400BadRequest, Detail = "brand 必填"
                });

            var brand = req.Brand.Trim();
            if (brand.Length > 100)
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "参数过长", Status = StatusCodes.Status400BadRequest,
                    Detail = "brand 长度不能超过 100 字符"
                });

            // 检查是否已存在 (含软删除记录, 用于判断是新增还是恢复)
            var existing = await db.XrefOemBrands
                .Where(b => b.Brand == brand)
                .FirstOrDefaultAsync(ct);

            if (existing != null && existing.DeletedAt == null)
                return Results.Conflict(new ProblemDetails
                {
                    Type = "https://sakurafilter.com/errors/brand-exists",
                    Title = "品牌已存在",
                    Status = StatusCodes.Status409Conflict,
                    Detail = $"品牌 '{brand}' 已存在于字典中, 无法重复新增",
                    Extensions = { ["errorCode"] = "BRAND_EXISTS" }
                });

            if (existing != null)
            {
                // 恢复软删除记录 (保留原 sort_order, 避免重新计算末尾位置)
                existing.DeletedAt = null;
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                cache.Remove("xref.brands.list");
                logger.LogInformation("品牌字典恢复 (软删恢复): brand={Brand} sortOrder={SortOrder}", brand, existing.SortOrder);
                return Results.Created($"/api/admin/xrefs/reorder/brands/{Uri.EscapeDataString(brand)}", new
                {
                    brand = existing.Brand,
                    sortOrder = existing.SortOrder,
                    oem3Count = 0,
                    restored = true
                });
            }

            // 新增: sort_order = max(sort_order) + 1, 排到字典末尾
            //   边界: 字典为空时 max 为 null → sort_order = 1
            var maxSortOrder = await db.XrefOemBrands
                .Where(b => b.DeletedAt == null)
                .Select(b => (int?)b.SortOrder)
                .MaxAsync(ct) ?? 0;

            var entity = new XrefOemBrand
            {
                Brand = brand,
                SortOrder = maxSortOrder + 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.XrefOemBrands.Add(entity);
            await db.SaveChangesAsync(ct);
            cache.Remove("xref.brands.list");

            logger.LogInformation("品牌字典新增: brand={Brand} sortOrder={SortOrder}", brand, entity.SortOrder);
            return Results.Created($"/api/admin/xrefs/reorder/brands/{Uri.EscapeDataString(brand)}", new
            {
                brand = entity.Brand,
                sortOrder = entity.SortOrder,
                oem3Count = 0,
                restored = false
            });
        })
        .WithSummary("新增品牌到 xref_oem_brand 字典 (sort_order=max+1, 软删可恢复)")
        .WithName("AdminXrefReorder_CreateBrand");

        // ===== Task 2.1.3: GET /?oemBrand=BOSCH — 返回某 Brand 下白名单内 OEM 3 列表 =====
        //   V24-F86: 加分页 (page/pageSize) + 搜索 (q, oemNo3 模糊匹配), 解决全量加载卡顿
        //   WHY 分页: 单 Brand 下 OEM 3 可达数千条, 全量加载导致前端渲染卡顿
        //   边界: pageSize 上限 200, 防止恶意大页请求; 拖拽排序仅在当前页内生效
        //   白名单改造: WHERE 加 x.SortOrder > 0, 仅返回白名单内产品 (sort_order = 0 视为未维护排末尾, 不在此列表)
        //     白名单通常不超过几十条, 分页主要用于边界情况
        group.MapGet("/", async (
            [FromQuery(Name = "oemBrand")] string oemBrand,
            [FromQuery(Name = "page")] int? page,
            [FromQuery(Name = "pageSize")] int? pageSize,
            [FromQuery(Name = "q")] string? q,
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

            // 分页参数归一化 (默认 page=1, pageSize=50; 上限 200 防止大页攻击)
            var pageNum = page ?? 1;
            var pageSizeNum = pageSize ?? 50;
            if (pageNum < 1) pageNum = 1;
            if (pageSizeNum < 1) pageSizeNum = 50;
            if (pageSizeNum > 200) pageSizeNum = 200;

            // P1-5: 查询 xref_oem_brand 表获取 brandSortOrder (spec L729-770 要求顶层返回)
            //   WHY 顶层暴露: 前端拖拽排序时需显示该 brand 在字典中的排序位次
            var brandSortOrder = await db.XrefOemBrands
                .AsNoTracking()
                .Where(b => b.Brand == oemBrand && b.DeletedAt == null)
                .Select(b => (int?)b.SortOrder)
                .FirstOrDefaultAsync(ct);

            // 基础查询: join products 取 mr1, 过滤未软删 + 仅白名单内 (sort_order > 0)
            //   q 模糊匹配 oemNo3 (PostgreSQL ILike 不区分大小写)
            var query = from x in db.CrossReferences.AsNoTracking()
                        where x.OemBrand == oemBrand && !x.IsDiscontinued && x.SortOrder > 0
                        join p in db.Products.AsNoTracking() on x.ProductId equals p.Id
                        where string.IsNullOrWhiteSpace(q)
                              || (x.OemNo3 != null && EF.Functions.ILike(x.OemNo3, "%" + q + "%"))
                        orderby x.SortOrder, x.OemNo3
                        select new
                        {
                            id = x.Id,  // 🔧 fix: 联调发现 oemNo3 不唯一, 必须用 Id 主键定位
                            oemNo3 = x.OemNo3,
                            sortOrder = x.SortOrder,
                            mr1 = p.Mr1,
                            isPublished = x.IsPublished,
                            rowVersion = x.RowVersion  // xmin 乐观锁令牌, 透传给前端
                        };

            var total = await query.CountAsync(ct);
            var items = await query
                .Skip((pageNum - 1) * pageSizeNum)
                .Take(pageSizeNum)
                .ToListAsync(ct);

            var totalPages = pageSizeNum == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSizeNum);

            return Results.Ok(new
            {
                oemBrand,
                brandSortOrder,
                items,
                total,
                page = pageNum,
                pageSize = pageSizeNum,
                totalPages
            });
        })
        .WithSummary("获取指定 Brand 下白名单内 OEM 3 列表 (sort_order > 0, 分页 + oemNo3 搜索, 含 rowVersion 乐观锁令牌)")
        .WithName("AdminXrefReorder_ListByBrand");

        // ===== V24-F86: GET /items/{id} — 取单条 cross_reference 详情 (编辑回填用) =====
        group.MapGet("/items/{id:long}", async (
            long id,
            ProductDbContext db,
            CancellationToken ct) =>
        {
            var item = await (
                from x in db.CrossReferences.AsNoTracking()
                where x.Id == id
                join p in db.Products.AsNoTracking() on x.ProductId equals p.Id
                select new
                {
                    id = x.Id,
                    productId = x.ProductId,
                    productName1 = x.ProductName1,
                    oemBrand = x.OemBrand,
                    oemNo3 = x.OemNo3,
                    oem2 = x.Oem2,
                    sortOrder = x.SortOrder,
                    machineType = x.MachineType,
                    isPublished = x.IsPublished,
                    isDiscontinued = x.IsDiscontinued,
                    mr1 = p.Mr1,
                    rowVersion = x.RowVersion
                }).FirstOrDefaultAsync(ct);

            if (item == null)
                return Results.NotFound(new ProblemDetails
                {
                    Title = "OEM 3 不存在",
                    Status = StatusCodes.Status404NotFound,
                    Detail = $"id={id} 未找到或已删除"
                });

            return Results.Ok(item);
        })
        .WithSummary("获取单条 cross_reference 详情 (编辑回填用, 含 rowVersion)")
        .WithName("AdminXrefReorder_GetItem");

        // ===== V24-F86: POST /items — 新增单条 cross_reference (白名单改造: 新增即入白名单) =====
        //   校验: productId 必须存在于 products 表
        //   触发: search_index_pending 重建该产品 Meili 文档 (OEM 3 列表变化)
        //   白名单改造: SortOrder = max(sort_order) + 1, 新增即视为白名单内 (sort_order > 0)
        group.MapPost("/items", async (
            XrefItemCreatePayload req,
            ProductDbContext db,
            IMemoryCache cache,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("AdminXrefReorder");
            // 参数校验
            if (req.ProductId <= 0)
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "缺少参数", Status = StatusCodes.Status400BadRequest, Detail = "productId 必填"
                });
            if (string.IsNullOrWhiteSpace(req.OemBrand))
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "缺少参数", Status = StatusCodes.Status400BadRequest, Detail = "oemBrand 必填"
                });
            if (string.IsNullOrWhiteSpace(req.OemNo3))
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "缺少参数", Status = StatusCodes.Status400BadRequest, Detail = "oemNo3 必填"
                });

            // 校验 product_id 存在 (同时取 ProductName1 回填, 保持数据一致)
            var product = await db.Products.AsNoTracking()
                .Where(p => p.Id == req.ProductId)
                .Select(p => new { p.Id, p.ProductName1 })
                .FirstOrDefaultAsync(ct);
            if (product == null)
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "产品不存在",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = $"productId={req.ProductId} 在 products 表中未找到"
                });

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // 白名单改造: 新增时自动设置 SortOrder = 当前最大值 + 1, 排到白名单末尾
                //   WHY max+1: 新增的产品默认应在白名单末尾, 管理员后续拖拽调整
                //   边界: 该 brand 下尚无白名单产品 (max 为 null) → SortOrder = 1 (白名单首条)
                //   并发: 两个管理员同时新增可能拿到相同 max → sort_order 重复, 后端拖拽排序时
                //         通过 orderby sort_order, oem_no_3 兜底, 不影响功能正确性
                var maxSortOrder = await db.CrossReferences
                    .Where(x => x.OemBrand == req.OemBrand && x.SortOrder > 0)
                    .Select(x => (int?)x.SortOrder)
                    .MaxAsync(ct) ?? 0;
                var newSortOrder = maxSortOrder + 1;

                // 新增 cross_reference 行
                //   WHY 从 product 表回填 ProductName1: 表单不收集此字段, 但实体需保持一致便于审计
                var entity = new CrossReference
                {
                    ProductId = req.ProductId,
                    ProductName1 = product.ProductName1,
                    OemBrand = req.OemBrand,
                    OemNo3 = req.OemNo3,
                    Oem2 = req.Oem2,
                    SortOrder = newSortOrder,  // 白名单改造: max+1, 新增即入白名单
                    MachineType = string.IsNullOrWhiteSpace(req.MachineType) ? "others" : req.MachineType,
                    IsPublished = req.IsPublished,
                    IsDiscontinued = false,
                    CreatedAt = DateTime.UtcNow
                };
                db.CrossReferences.Add(entity);
                await db.SaveChangesAsync(ct);

                // WHY 重新查询 rowVersion: EF Core SaveChanges 后 xmin 自动刷新, 但 uint 字段需显式读取
                var newId = entity.Id;
                var newRowVersion = await db.CrossReferences.AsNoTracking()
                    .Where(x => x.Id == newId)
                    .Select(x => x.RowVersion)
                    .FirstAsync(ct);

                // V24-F51: 触发 search_index_pending 重建该产品 Meili 文档
                await EnqueueIndexRebuildAsync(db, req.ProductId, logger, ct);

                await tx.CommitAsync(ct);
                // 清 brand 列表缓存 (oem3Count 变化)
                cache.Remove("xref.brands.list");

                logger.LogInformation("OEM 3 新增到白名单成功: id={Id} brand={Brand} oemNo3={OemNo3} productId={ProductId} sortOrder={SortOrder}",
                    newId, req.OemBrand, req.OemNo3, req.ProductId, newSortOrder);

                return Results.Created($"/api/admin/xrefs/reorder/items/{newId}", new
                {
                    id = newId,
                    productId = req.ProductId,
                    productName1 = product.ProductName1,
                    oemBrand = req.OemBrand,
                    oemNo3 = req.OemNo3,
                    oem2 = req.Oem2,
                    sortOrder = newSortOrder,
                    machineType = entity.MachineType,
                    isPublished = req.IsPublished,
                    isDiscontinued = false,
                    rowVersion = newRowVersion
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                logger.LogError(ex, "OEM 3 新增失败: brand={Brand} oemNo3={OemNo3}", req.OemBrand, req.OemNo3);
                throw;
            }
        })
        .WithSummary("新增单条 cross_reference (校验 productId 存在, sort_order=max+1 入白名单, 触发索引重建)")
        .WithName("AdminXrefReorder_CreateItem");

        // ===== V24-F86: PUT /items/{id} — 编辑单条 (oemNo3 / isPublished / machineType), xmin 乐观锁 =====
        group.MapPut("/items/{id:long}", async (
            long id,
            XrefItemUpdatePayload req,
            ProductDbContext db,
            IMemoryCache cache,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("AdminXrefReorder");
            if (string.IsNullOrWhiteSpace(req.OemNo3))
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "缺少参数", Status = StatusCodes.Status400BadRequest, Detail = "oemNo3 必填"
                });

            // 先取 productId (用于触发索引重建, 不受乐观锁影响)
            var productId = await db.CrossReferences.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => (long?)x.ProductId)
                .FirstOrDefaultAsync(ct);
            if (productId == null)
                return Results.NotFound(new ProblemDetails
                {
                    Title = "OEM 3 不存在", Status = StatusCodes.Status404NotFound, Detail = $"id={id} 未找到"
                });

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // xmin 乐观锁 (与 POST / 批量排序同模式: text→xid 中转, 修复 42883/42846)
                //   0 行受影响 → xmin 不匹配 (他人改过) 或记录不存在 → 409 XREF_CONFLICT
                var rowsAffected = await db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE cross_references
                    SET oem_no_3 = {req.OemNo3},
                        is_published = {req.IsPublished},
                        machine_type = {req.MachineType}
                    WHERE id = {id}
                      AND xmin = CAST(CAST({req.RowVersion} AS text) AS xid)", ct);

                if (rowsAffected == 0)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Conflict(new ProblemDetails
                    {
                        Type = "https://sakurafilter.com/errors/xref-conflict",
                        Title = "OEM 3 编辑冲突",
                        Status = StatusCodes.Status409Conflict,
                        Detail = $"id={id} 已被其他用户修改, 请刷新重试",
                        Extensions = { ["errorCode"] = "XREF_CONFLICT" }
                    });
                }

                // V24-F51: 触发索引重建 (oemNo3 / isPublished / machineType 变化影响搜索文档)
                await EnqueueIndexRebuildAsync(db, productId.Value, logger, ct);

                await tx.CommitAsync(ct);
                cache.Remove("xref.brands.list");

                // 查询最新 rowVersion 返回给前端 (xmin 已变)
                var newRowVersion = await db.CrossReferences.AsNoTracking()
                    .Where(x => x.Id == id)
                    .Select(x => x.RowVersion)
                    .FirstAsync(ct);

                logger.LogInformation("OEM 3 编辑成功: id={Id} oemNo3={OemNo3}", id, req.OemNo3);
                return Results.Ok(new { id, rowVersion = newRowVersion });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                logger.LogError(ex, "OEM 3 编辑失败: id={Id}", id);
                throw;
            }
        })
        .WithSummary("编辑单条 cross_reference (oemNo3/isPublished/machineType, 含 xmin 乐观锁)")
        .WithName("AdminXrefReorder_UpdateItem");

        // ===== V24-F86: DELETE /items/{id} — 从白名单移除 (置 sort_order=0, 不删产品本身) =====
        //   白名单改造: 原"软删 is_discontinued=true" 改为"从白名单移除 sort_order=0"
        //   WHY 不软删: 产品本身仍可被搜索 (PostgresSearchProvider 兜底), 仅不再优先展示
        //   WHY 触发索引重建: sort_order 变化影响 Meili 文档的排序权重, 需重建
        //   rowVersion 通过 query 传递 (DELETE 通常无 body)
        group.MapDelete("/items/{id:long}", async (
            long id,
            [FromQuery(Name = "rowVersion")] uint? rowVersion,
            ProductDbContext db,
            IMemoryCache cache,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("AdminXrefReorder");

            // 先取 productId + 当前 sortOrder (用于触发索引重建 + 校验是否已在白名单外)
            var info = await db.CrossReferences.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new { x.ProductId, x.SortOrder, x.IsDiscontinued })
                .FirstOrDefaultAsync(ct);
            if (info == null)
                return Results.NotFound(new ProblemDetails
                {
                    Title = "OEM 3 不存在", Status = StatusCodes.Status404NotFound, Detail = $"id={id} 未找到"
                });
            if (info.IsDiscontinued)
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "已软删", Status = StatusCodes.Status400BadRequest,
                    Detail = $"id={id} 已是软删状态, 无法操作白名单"
                });
            if (info.SortOrder == 0)
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "不在白名单内", Status = StatusCodes.Status400BadRequest,
                    Detail = $"id={id} 当前 sort_order=0, 已不在白名单内, 无需移除"
                });

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // 从白名单移除: SET sort_order = 0 (保留 is_discontinued 不变, 产品本身不删)
                //   WHY 可选乐观锁: DELETE 场景前端可能仅持 id, rowVersion 缺省时不阻断
                int rowsAffected;
                if (rowVersion.HasValue)
                {
                    var rv = rowVersion.Value;
                    rowsAffected = await db.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE cross_references
                        SET sort_order = 0
                        WHERE id = {id}
                          AND xmin = CAST(CAST({rv} AS text) AS xid)", ct);
                }
                else
                {
                    rowsAffected = await db.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE cross_references
                        SET sort_order = 0
                        WHERE id = {id}", ct);
                }

                if (rowsAffected == 0)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Conflict(new ProblemDetails
                    {
                        Type = "https://sakurafilter.com/errors/xref-conflict",
                        Title = "OEM 3 移除冲突",
                        Status = StatusCodes.Status409Conflict,
                        Detail = $"id={id} 已被其他用户修改, 请刷新重试",
                        Extensions = { ["errorCode"] = "XREF_CONFLICT" }
                    });
                }

                // V24-F51: 触发索引重建 (sort_order 变化影响 Meili 排序权重)
                await EnqueueIndexRebuildAsync(db, info.ProductId, logger, ct);

                await tx.CommitAsync(ct);
                cache.Remove("xref.brands.list");

                logger.LogInformation("OEM 3 从白名单移除成功: id={Id} productId={ProductId} (sort_order 置 0, 产品未删除)",
                    id, info.ProductId);
                return Results.Ok(new { id, sortOrder = 0, removedFromWhitelist = true });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                logger.LogError(ex, "OEM 3 从白名单移除失败: id={Id}", id);
                throw;
            }
        })
        .WithSummary("从白名单移除单条 cross_reference (置 sort_order=0, 不删产品本身, 含可选 xmin 乐观锁)")
        .WithName("AdminXrefReorder_DeleteItem");

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
                // 收集受影响的 productId, 用于事务提交前批量入队索引重建
                // WHY: XrefReorderItem 仅含 Id/OemNo3/SortOrder/RowVersion, 需查 DB 获取 productId
                var affectedIds = req.Items.Select(x => x.Id).ToList();
                var productIds = await db.CrossReferences.AsNoTracking()
                    .Where(x => affectedIds.Contains(x.Id))
                    .Select(x => x.ProductId)
                    .Distinct()
                    .ToListAsync(ct);

                foreach (var item in req.Items)
                {
                    // 单条更新走 xmin 乐观锁 (修复漏洞 13)
                    //   SQL: UPDATE cross_references SET sort_order = @p
                    //        WHERE id = @id AND xmin = @rv
                    //   xmin 不匹配 → 0 行受影响 → 抛 XREF_CONFLICT
                    //   🔧 fix 42883/42846: PostgreSQL 无 xid = bigint 操作符, 且不允许 CAST(bigint AS xid) / CAST(integer AS xid)
                    //     原因: C# uint (4 字节) 经 Npgsql 推断为 bigint (8 字节), 与 xid 列比较报错 42883
                    //     错误尝试 1: CAST(bigint AS xid) 报 42846 (PG 不允许直接 bigint→xid)
                    //     错误尝试 2: CAST(integer AS xid) 报 42846 (PG 不允许 int4→xid 显式 cast)
                    //     正确修复: 走 text 中转 — CAST(CAST(uint AS text) AS xid)
                    //       PG 允许 text→xid 隐式转换 (CREATE CAST 定义了 text 入口)
                    //     边界: text 路径仅接受数字字符串, 非数字会报错 (本场景 rowVersion 来自 GET 接口, 类型安全)
                    //   🔧 fix 23505 (联调发现真实业务 bug): 原 SQL 用 WHERE oem_brand + oem_no_3 定位,
                    //     但 oemNo3 在 cross_references 表不唯一 (同 Brand 下 DON-00000 可对应多条不同 mr1 记录),
                    //     导致第一次 UPDATE 改了所有同 oemNo3 行, 第二次 POST 用旧 rowVersion 比对 xmin 不匹配 → 409
                    //     修复: WHERE 改用 Id 主键定位单行 (Id 唯一, 不存在误更新)
                    //     发现场景: 联调 E2E 测试 POST /api/admin/xrefs/reorder 时触发 (单测/集成测试未覆盖此 raw SQL 路径)
                    var rowsAffected = await db.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE cross_references
                        SET sort_order = {item.SortOrder}
                        WHERE id = {item.Id}
                          AND xmin = CAST(CAST({item.RowVersion} AS text) AS xid)  -- V2: xmin 乐观锁, 类型 xid; 修复 42883/42846 经 text 中转; 修复 23505 用 Id 主键定位", ct);

                    if (rowsAffected == 0)
                    {
                        // 0 行受影响: xmin 不匹配 (其他人改过) 或 OEM 3 不存在
                        // 抛异常回滚事务, ProblemDetailsFactory 映射为 409 XREF_CONFLICT
                        throw new InvalidOperationException(
                            $"XREF_CONFLICT: OEM 3 '{item.OemNo3}' 排序更新冲突 (已被其他用户修改或已删除), 请刷新重试");
                    }
                }

                // WHY 触发索引重建: sort_order 变化影响 Meili 文档的 oem_list_sort_order_min 排序权重
                //   修复遗漏: 单条 CRUD (新增/编辑/移除) 均已调用 EnqueueIndexRebuildAsync, 批量排序遗漏
                //   场景: 管理员调整白名单顺序后, 前台搜索结果未反映新顺序 (用户反馈 2026-07-31)
                foreach (var pid in productIds)
                {
                    await EnqueueIndexRebuildAsync(db, pid, logger, ct);
                }

                await tx.CommitAsync(ct);
                // 改进 2.1: 排序更新成功后清 brand 列表缓存 (oem3Count 可能变化)
                cache.Remove("xref.brands.list");
                logger.LogInformation("OEM 3 批量排序更新成功: brand={Brand} count={Count} 索引重建入队 {IndexCount} 条",
                    req.OemBrand, req.Items.Count, productIds.Count);
                return Results.Ok(new { updated = req.Items.Count, oemBrand = req.OemBrand, indexRebuildEnqueued = productIds.Count });
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

    // ===== V24-F86: 单条 CRUD 触发 search_index_pending 重建 (复用 OemBrandDictService.ApplyChangeAsync 模式) =====
    //
    /// <summary>
    /// 单条 cross_reference 变更后, 为对应产品写入 search_index_pending 重建信号
    ///   - IndexReplayWorker 后台消费, 调 BuildMr1DocumentAsync 重建 Meili 文档
    ///   - oemNo3 / isPublished / machineType / isDiscontinued 变更都会影响搜索文档, 需触发重建
    ///   - 与 OemBrandDictService.ApplyChangeAsync 区别: 本方法针对单产品, 不按 brand 聚合
    /// </summary>
    /// <param name="db">ProductDbContext (调用方负责事务)</param>
    /// <param name="productId">受影响的产品 ID</param>
    /// <param name="logger">日志器</param>
    /// <param name="ct">取消令牌</param>
    private static async Task EnqueueIndexRebuildAsync(
        ProductDbContext db, long productId, ILogger logger, CancellationToken ct)
    {
        // 查询产品 Mr1 (用于 payload, IndexReplayWorker 重建时用)
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Id, p.Mr1 })
            .FirstOrDefaultAsync(ct);
        if (product == null)
        {
            logger.LogWarning("xref 变更触发索引重建: productId={ProductId} 未找到, 跳过", productId);
            return;
        }

        // 写入 search_index_pending (Operation="index", 由 IndexReplayWorker 消费重建)
        //   payload 格式与 OemBrandDictService.ApplyChangeAsync 保持一致, trigger 区分来源
        var now = DateTime.UtcNow;
        db.SearchIndexPending.Add(new SearchIndexPending
        {
            Operation = "index",
            Payload = JsonSerializer.Serialize(new { product_id = product.Id, mr1 = product.Mr1, trigger = "xref_item_change" }),
            CreatedAt = now,
            NextRetryAt = now,
            RetryCount = 0
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("xref 变更触发产品 {ProductId} 索引重建", productId);
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
/// <param name="Id">cross_references 表主键 (联调发现 oemNo3 不唯一, 必须用 Id 主键定位)</param>
/// <param name="OemNo3">OEM 3 号 (仅前端展示用, 不参与 UPDATE WHERE)</param>
/// <param name="SortOrder">新排序值 (类竞价排名, 数值越小越靠前)</param>
/// <param name="RowVersion">xmin 乐观锁令牌 (GET 接口返回的 rowVersion, 透传回来比对)</param>
public record XrefReorderItem(
    long Id,
    string OemNo3,
    int SortOrder,
    uint RowVersion
);

/// <summary>
/// V24-F86: OEM 3 新增请求体 (POST /items)
/// </summary>
/// <param name="ProductId">关联产品 ID (必须存在于 products 表)</param>
/// <param name="OemBrand">OEM 品牌名 (与 XrefOemBrand.Brand 一致)</param>
/// <param name="OemNo3">OEM 3 号 (对外展示主键)</param>
/// <param name="Oem2">OEM 2 号 (可空)</param>
/// <param name="MachineType">机型类型 (缺省 "others")</param>
/// <param name="IsPublished">是否发布 (默认 true)</param>
public record XrefItemCreatePayload(
    long ProductId,
    string OemBrand,
    string OemNo3,
    string? Oem2,
    string? MachineType,
    bool IsPublished
);

/// <summary>
/// V24-F86: OEM 3 编辑请求体 (PUT /items/{id}), 含 xmin 乐观锁令牌
/// </summary>
/// <param name="OemNo3">OEM 3 号</param>
/// <param name="MachineType">机型类型</param>
/// <param name="IsPublished">是否发布</param>
/// <param name="RowVersion">xmin 乐观锁令牌 (GET 接口返回, 透传回来比对)</param>
public record XrefItemUpdatePayload(
    string OemNo3,
    string? MachineType,
    bool IsPublished,
    uint RowVersion
);

/// <summary>
/// 白名单改造: 新增品牌请求体 (POST /brands)
/// </summary>
/// <param name="Brand">品牌名 (与 XrefOemBrand.Brand 一致, 会 trim)</param>
public record XrefBrandCreatePayload(string Brand);
