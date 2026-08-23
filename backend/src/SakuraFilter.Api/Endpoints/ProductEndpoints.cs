using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Infrastructure.Data;
using SakuraFilter.Search;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// 公开产品端点：搜索、健康检查、产品详情（无鉴权）。
/// </summary>
public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        // 搜索接口
        app.MapPost("/api/search", async (SearchRequest req, ISearchProvider search, CancellationToken ct) =>
        {
            var result = await search.SearchAsync(req, ct);
            return Results.Ok(new { provider = search.Name, result });
        })
        .WithSummary("产品搜索 (走 ISearchProvider 抽象, Resilient 主备自动切换)").WithName("SearchProducts")
        .WithOpenApi()
        .RequireRateLimiting("search");

        // 搜索健康检查
        app.MapGet("/api/search/health", async (ISearchProvider search, CancellationToken ct) =>
        {
            var healthy = await search.HealthCheckAsync(ct);
            return Results.Ok(new { provider = search.Name, healthy });
        })
        .WithSummary("搜索健康检查 (主备状态)").WithName("SearchHealth")
        .WithOpenApi();

        // 产品详情
        app.MapGet("/api/products/{oem}", async (string oem, ProductDbContext db, CancellationToken ct) =>
        {
            var p = await db.Products.AsNoTracking()
                .FirstOrDefaultAsync(x => x.OemNoNormalized == oem || x.OemNoDisplay == oem, ct);
            if (p is null) return Results.NotFound();

            var xrefs = await db.CrossReferences.AsNoTracking()
                .Where(x => x.ProductId == p.Id)
                .Select(x => new CrossReferenceDto(x.OemBrand, x.OemNo3, x.ProductName1))
                .ToListAsync(ct);

            var apps = await db.MachineApplications.AsNoTracking()
                .Where(m => m.ProductId == p.Id)
                .Select(m => new MachineApplicationDto(
                    m.MachineBrand, m.MachineModel, m.ModelName,
                    m.EngineBrand, m.EngineType, m.EngineEnergy))
                .ToListAsync(ct);

            return Results.Ok(new ProductDetail(
                p.Id, p.OemNoDisplay, p.OemNoNormalized, p.Remark, p.Type,
                p.D1Mm, p.D2Mm, p.D3Mm, p.H1Mm, p.H2Mm, p.H3Mm,
                p.D7Thread, p.D8Thread,
                p.Media, p.SealingMaterial, p.Efficiency1, p.CollapsePressureBar, p.TempRange,
                p.QtyPerCarton, p.WeightKgs, p.CartonLengthMm, p.CartonWidthMm, p.CartonHeightMm,
                p.ImageKey, xrefs, apps
            ));
        })
        .WithSummary("产品详情 (按 OEM 精确/规范化查询, 含 cross-references + machine-applications)").WithName("GetProductByOem")
        .WithOpenApi();

        return app;
    }
}
