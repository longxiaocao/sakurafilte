using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Api.Extensions;
using SakuraFilter.Api.Services;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Endpoints;

// OEM 品牌 DTO（其他 7 类字典 DTO 已存在 SakuraFilter.Api.Services 命名空间下）
// 之所以单独声明：原 Program.cs 中作为顶级 record 存在，迁移到 Endpoints 命名空间下。
public record OemBrandCreateRequest(string Brand, int? SortOrder);
public record OemBrandUpdateRequest(string? Brand, int? SortOrder);

/// <summary>
/// 字典管理端点：admin 角色鉴权 + global 限流。
/// 8 类字典: OemBrand / ProductName1 / ProductName2 / Type / OemNo3 / Media / Machine / Engine + schema 契约。
/// </summary>
public static class DictionaryEndpoints
{
    public static IEndpointRouteBuilder MapDictionaryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/dict").WithTags("AdminDict")
            .RequireAuthorization("Admin")  // V24-F19: spec F11
            .RequireRateLimiting("global");

        MapOemBrandEndpoints(group);
        MapProductName1Endpoints(group);
        MapProductName2Endpoints(group);
        MapTypeEndpoints(group);
        MapOemNo3Endpoints(group);
        MapMediaEndpoints(group);
        MapMachineEndpoints(group);
        MapEngineEndpoints(group);
        MapSchemaEndpoint(group);

        return app;
    }

    // -------------------- OEM 品牌 --------------------

    private static void MapOemBrandEndpoints(IEndpointRouteBuilder group)
    {
        var g = group.MapGroup("/oem-brands");

        g.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] bool? includeDeleted, [FromQuery] int? limit,
            OemBrandDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListOemBrandsAsync(q, includeDeleted ?? false, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminListOemBrands");

        g.MapGet("/typeahead", async (
            [FromQuery] string? q, [FromQuery] int? limit,
            OemBrandDictService svc, CancellationToken ct) =>
        {
            var items = await svc.TypeaheadOemBrandsAsync(q, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminTypeaheadOemBrands");

        g.MapPost("/", async (
            OemBrandCreateRequest body, OemBrandDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Brand))
                return Results.BadRequest(new { error = "brand 不能为空" });
            try
            {
                var item = await svc.CreateOemBrandAsync(body.Brand, body.SortOrder, ct);
                return Results.Created($"/api/admin/dict/oem-brands/{item.Id}", item);
            }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminCreateOemBrand");

        g.MapPut("/{id:long}", async (
            long id, OemBrandUpdateRequest body, OemBrandDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.UpdateOemBrandAsync(id, body.Brand, body.SortOrder, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminUpdateOemBrand");

        g.MapDelete("/{id:long}", async (long id, OemBrandDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.DeleteOemBrandAsync(id, ct); return Results.Ok(new { id, deleted = true }); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminDeleteOemBrand");

        g.MapPost("/{id:long}/restore", async (long id, OemBrandDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.RestoreOemBrandAsync(id, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminRestoreOemBrand");

        g.MapPost("/reorder", async (
            OemBrandReorderRequest body, OemBrandDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.ReorderOemBrandsAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminReorderOemBrands");
    }

    // -------------------- Product Name 1 --------------------

    private static void MapProductName1Endpoints(IEndpointRouteBuilder group)
    {
        var g = group.MapGroup("/product-name1s");
        g.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] bool? includeDeleted, [FromQuery] int? limit,
            ProductName1DictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListProductName1sAsync(q, includeDeleted ?? false, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminListProductName1s");

        g.MapGet("/typeahead", async (
            [FromQuery] string? q, [FromQuery] int? limit,
            ProductName1DictService svc, CancellationToken ct) =>
        {
            var items = await svc.TypeaheadProductName1sAsync(q, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminTypeaheadProductName1s");

        g.MapPost("/", async (
            ProductName1CreateRequest body, ProductName1DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProductName1)) return Results.BadRequest(new { error = "productName1 不能为空" });
            try { var item = await svc.CreateProductName1Async(body.ProductName1, body.SortOrder, ct);
                return Results.Created($"/api/admin/dict/product-name1s/{item.Id}", item); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminCreateProductName1");

        g.MapPut("/{id:long}", async (
            long id, ProductName1UpdateRequest body, ProductName1DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.UpdateProductName1Async(id, body.ProductName1, body.SortOrder, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminUpdateProductName1");

        g.MapDelete("/{id:long}", async (long id, ProductName1DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.DeleteProductName1Async(id, ct); return Results.Ok(new { id, deleted = true }); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminDeleteProductName1");

        g.MapPost("/{id:long}/restore", async (long id, ProductName1DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.RestoreProductName1Async(id, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminRestoreProductName1");

        g.MapPost("/reorder", async (
            ProductName1ReorderRequest body, ProductName1DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.ReorderProductName1sAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminReorderProductName1s");
    }

    // -------------------- Product Name 2 --------------------

    private static void MapProductName2Endpoints(IEndpointRouteBuilder group)
    {
        var g = group.MapGroup("/product-name2s");
        g.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] bool? includeDeleted, [FromQuery] int? limit,
            ProductName2DictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListProductName2sAsync(q, includeDeleted ?? false, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminListProductName2s");

        g.MapGet("/typeahead", async (
            [FromQuery] string? q, [FromQuery] int? limit,
            ProductName2DictService svc, CancellationToken ct) =>
        {
            var items = await svc.TypeaheadProductName2sAsync(q, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminTypeaheadProductName2s");

        g.MapPost("/", async (
            ProductName2CreateRequest body, ProductName2DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProductName2)) return Results.BadRequest(new { error = "productName2 不能为空" });
            try { var item = await svc.CreateProductName2Async(body.ProductName2, body.SortOrder, ct);
                return Results.Created($"/api/admin/dict/product-name2s/{item.Id}", item); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminCreateProductName2");

        g.MapPut("/{id:long}", async (
            long id, ProductName2UpdateRequest body, ProductName2DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.UpdateProductName2Async(id, body.ProductName2, body.SortOrder, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminUpdateProductName2");

        g.MapDelete("/{id:long}", async (long id, ProductName2DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.DeleteProductName2Async(id, ct); return Results.Ok(new { id, deleted = true }); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminDeleteProductName2");

        g.MapPost("/{id:long}/restore", async (long id, ProductName2DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.RestoreProductName2Async(id, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminRestoreProductName2");

        g.MapPost("/reorder", async (
            ProductName2ReorderRequest body, ProductName2DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.ReorderProductName2sAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminReorderProductName2s");
    }

    // -------------------- Type --------------------

    private static void MapTypeEndpoints(IEndpointRouteBuilder group)
    {
        var g = group.MapGroup("/types");
        g.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] bool? includeDeleted, [FromQuery] int? limit,
            TypeDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListTypesAsync(q, includeDeleted ?? false, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminListTypes");

        g.MapGet("/typeahead", async (
            [FromQuery] string? q, [FromQuery] int? limit,
            TypeDictService svc, CancellationToken ct) =>
        {
            var items = await svc.TypeaheadTypesAsync(q, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminTypeaheadTypes");

        g.MapPost("/", async (
            TypeCreateRequest body, TypeDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Type)) return Results.BadRequest(new { error = "type 不能为空" });
            try { var item = await svc.CreateTypeAsync(body.Type, body.SortOrder, ct);
                return Results.Created($"/api/admin/dict/types/{item.Id}", item); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminCreateType");

        g.MapPut("/{id:long}", async (
            long id, TypeUpdateRequest body, TypeDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.UpdateTypeAsync(id, body.Type, body.SortOrder, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminUpdateType");

        g.MapDelete("/{id:long}", async (long id, TypeDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.DeleteTypeAsync(id, ct); return Results.Ok(new { id, deleted = true }); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminDeleteType");

        g.MapPost("/{id:long}/restore", async (long id, TypeDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.RestoreTypeAsync(id, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminRestoreType");

        g.MapPost("/reorder", async (
            TypeReorderRequest body, TypeDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.ReorderTypesAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminReorderTypes");
    }

    // -------------------- OEM No3 --------------------

    private static void MapOemNo3Endpoints(IEndpointRouteBuilder group)
    {
        var g = group.MapGroup("/oem-no3s");
        g.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] bool? includeDeleted, [FromQuery] int? limit,
            OemNo3DictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListOemNo3sAsync(q, includeDeleted ?? false, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminListOemNo3s");

        g.MapGet("/typeahead", async (
            [FromQuery] string? q, [FromQuery] int? limit,
            OemNo3DictService svc, CancellationToken ct) =>
        {
            var items = await svc.TypeaheadOemNo3sAsync(q, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminTypeaheadOemNo3s");

        g.MapPost("/", async (
            OemNo3CreateRequest body, OemNo3DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.OemNo3)) return Results.BadRequest(new { error = "oemNo3 不能为空" });
            try { var item = await svc.CreateOemNo3Async(body.OemNo3, body.SortOrder, ct);
                return Results.Created($"/api/admin/dict/oem-no3s/{item.Id}", item); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminCreateOemNo3");

        g.MapPut("/{id:long}", async (
            long id, OemNo3UpdateRequest body, OemNo3DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.UpdateOemNo3Async(id, body.OemNo3, body.SortOrder, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminUpdateOemNo3");

        g.MapDelete("/{id:long}", async (long id, OemNo3DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.DeleteOemNo3Async(id, ct); return Results.Ok(new { id, deleted = true }); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminDeleteOemNo3");

        g.MapPost("/{id:long}/restore", async (long id, OemNo3DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.RestoreOemNo3Async(id, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminRestoreOemNo3");

        g.MapPost("/reorder", async (
            OemNo3ReorderRequest body, OemNo3DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.ReorderOemNo3sAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminReorderOemNo3s");
    }

    // -------------------- Media (2 字段) --------------------

    private static void MapMediaEndpoints(IEndpointRouteBuilder group)
    {
        var g = group.MapGroup("/medias");
        g.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] bool? includeDeleted, [FromQuery] int? limit,
            MediaDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListMediasAsync(q, includeDeleted ?? false, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminListMedias");
        g.MapGet("/typeahead", async (
            [FromQuery] string? q, [FromQuery] int? limit,
            MediaDictService svc, CancellationToken ct) =>
        {
            var items = await svc.TypeaheadMediasAsync(q, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminTypeaheadMedias");
        g.MapPost("/", async (
            MediaCreateRequest body, MediaDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.MediaName)) return Results.BadRequest(new { error = "mediaName 不能为空" });
            try { var item = await svc.CreateMediaAsync(body.MediaName, body.MediaModel, body.SortOrder, ct);
                return Results.Created($"/api/admin/dict/medias/{item.Id}", item); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminCreateMedia");
        g.MapPut("/{id:long}", async (
            long id, MediaUpdateRequest body, MediaDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.UpdateMediaAsync(id, body.MediaName, body.MediaModel, body.SortOrder, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminUpdateMedia");
        g.MapDelete("/{id:long}", async (long id, MediaDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.DeleteMediaAsync(id, ct); return Results.Ok(new { id, deleted = true }); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminDeleteMedia");
        g.MapPost("/{id:long}/restore", async (long id, MediaDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.RestoreMediaAsync(id, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminRestoreMedia");
        g.MapPost("/reorder", async (
            MediaReorderRequest body, MediaDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.ReorderMediasAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminReorderMedias");
    }

    // -------------------- Machine (3 字段) --------------------

    private static void MapMachineEndpoints(IEndpointRouteBuilder group)
    {
        var g = group.MapGroup("/machines");
        g.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] bool? includeDeleted, [FromQuery] int? limit,
            MachineDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListMachinesAsync(q, includeDeleted ?? false, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminListMachines");
        g.MapGet("/typeahead", async (
            [FromQuery] string? q, [FromQuery] int? limit,
            MachineDictService svc, CancellationToken ct) =>
        {
            var items = await svc.TypeaheadMachinesAsync(q, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminTypeaheadMachines");
        g.MapPost("/", async (
            MachineCreateRequest body, MachineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.MachineBrand)) return Results.BadRequest(new { error = "machineBrand 不能为空" });
            try { var item = await svc.CreateMachineAsync(body.MachineBrand, body.MachineModel, body.MachineName, body.SortOrder, body.MachineCategory, ct);
                return Results.Created($"/api/admin/dict/machines/{item.Id}", item); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminCreateMachine");
        g.MapPut("/{id:long}", async (
            long id, MachineUpdateRequest body, MachineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.UpdateMachineAsync(id, body.MachineBrand, body.MachineModel, body.MachineName, body.SortOrder, body.MachineCategory, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminUpdateMachine");
        g.MapDelete("/{id:long}", async (long id, MachineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.DeleteMachineAsync(id, ct); return Results.Ok(new { id, deleted = true }); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminDeleteMachine");
        g.MapPost("/{id:long}/restore", async (long id, MachineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.RestoreMachineAsync(id, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminRestoreMachine");
        g.MapPost("/reorder", async (
            MachineReorderRequest body, MachineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.ReorderMachinesAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminReorderMachines");
    }

    // -------------------- Engine (2 字段) --------------------

    private static void MapEngineEndpoints(IEndpointRouteBuilder group)
    {
        var g = group.MapGroup("/engines");
        g.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] bool? includeDeleted, [FromQuery] int? limit,
            EngineDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListEnginesAsync(q, includeDeleted ?? false, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminListEngines");
        g.MapGet("/typeahead", async (
            [FromQuery] string? q, [FromQuery] int? limit,
            EngineDictService svc, CancellationToken ct) =>
        {
            var items = await svc.TypeaheadEnginesAsync(q, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminTypeaheadEngines");
        g.MapPost("/", async (
            EngineCreateRequest body, EngineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.EngineBrand)) return Results.BadRequest(new { error = "engineBrand 不能为空" });
            try { var item = await svc.CreateEngineAsync(body.EngineBrand, body.EngineType, body.SortOrder, ct);
                return Results.Created($"/api/admin/dict/engines/{item.Id}", item); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminCreateEngine");
        g.MapPut("/{id:long}", async (
            long id, EngineUpdateRequest body, EngineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.UpdateEngineAsync(id, body.EngineBrand, body.EngineType, body.SortOrder, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminUpdateEngine");
        g.MapDelete("/{id:long}", async (long id, EngineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.DeleteEngineAsync(id, ct); return Results.Ok(new { id, deleted = true }); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminDeleteEngine");
        g.MapPost("/{id:long}/restore", async (long id, EngineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.RestoreEngineAsync(id, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminRestoreEngine");
        g.MapPost("/reorder", async (
            EngineReorderRequest body, EngineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.ReorderEnginesAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminReorderEngines");
    }

    // -------------------- schema 契约端点 --------------------

    private static void MapSchemaEndpoint(IEndpointRouteBuilder group)
    {
        // V24-F13: nullable 字段改用 EF Core metadata 判断 (而非纯反射)
        //   WHY: ReflectionExtensions.IsNullable() 对所有引用类型一律返回 true,
        //        无法识别 DictMachine.MachineCategory 配置了 .IsRequired() (DB 列 NOT NULL)
        //   方案: 从 ProductDbContext.Model 取 IEntityType.FindProperty().IsNullable,
        //        它综合 CLR 类型 + Fluent API 配置, 与 DB 列实际 NOT NULL 一致
        //   兜底: 若属性未在 EF metadata 中注册 (如导航属性), 回退到反射判断
        group.MapGet("/_schema", (ProductDbContext db) =>
        {
            var dictTypes = new[]
            {
                typeof(SakuraFilter.Core.Entities.XrefOemBrand),
                typeof(SakuraFilter.Core.Entities.DictProductName1),
                typeof(SakuraFilter.Core.Entities.DictProductName2),
                typeof(SakuraFilter.Core.Entities.DictType),
                typeof(SakuraFilter.Core.Entities.DictOemNo3),
                typeof(SakuraFilter.Core.Entities.DictMedia),
                typeof(SakuraFilter.Core.Entities.DictMachine),
                typeof(SakuraFilter.Core.Entities.DictEngine)
            };
            var schema = dictTypes.Select(t =>
            {
                var et = db.Model.FindEntityType(t);
                return new
                {
                    Entity = t.Name,
                    Table = SakuraFilter.Api.Extensions.TableNameMapper.GetPgTableName(t),
                    Fields = t.GetProperties()
                        .Select(p =>
                        {
                            var efProp = et?.FindProperty(p.Name);
                            return new
                            {
                                Name = p.Name,
                                CSharpType = p.PropertyType.ToCSharpTypeName(),
                                Nullable = efProp?.IsNullable ?? p.IsNullable(),
                                HasColumn = p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.ColumnAttribute), false).Any()
                            };
                        })
                        .ToArray()
                };
            });
            return Results.Ok(new
            {
                generatedAt = DateTime.UtcNow.ToString("O"),
                count = dictTypes.Length,
                dictionaries = schema
            });
        })
        .WithName("AdminDictSchema");
    }
}
