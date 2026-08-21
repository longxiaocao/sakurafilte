using SakuraFilter.Etl;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// typeahead 字典表重建端点 — ETL 导入后调用, 刷新全量 distinct 值快照。
/// WHY: typeahead_dict 是 typeahead 查询的快速路径 (GIN trgm, 万行级);
///      明细表 (cross_references/machine_applications) 数据变化后需重建, 否则候选值缺失。
/// 成本: 全量 distinct 重建 (万行级) 秒级完成, 幂等 (TRUNCATE + INSERT)。
///
/// 2026-08-21 P1 修复: SQL 与重建执行抽到 TypeaheadDictRebuildService (单一来源),
///   ETL 导入成功后自动调用同一逻辑 (见 EtlImportService), 本端点保留手动兜底入口。
/// </summary>
public static class AdminTypeaheadEndpoints
{
    public static void MapAdminTypeaheadEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin/typeahead").WithTags("AdminTypeahead")
            .RequireAuthorization("Admin");

        g.MapPost("/rebuild", async (
            TypeaheadDictRebuildService rebuild,
            CancellationToken ct) =>
        {
            await rebuild.RebuildAsync(ct);
            return Results.Ok(new { ok = true, message = "typeahead_dict 已重建 (全量 distinct 快照刷新)" });
        }).WithName("AdminRebuildTypeaheadDict").WithOpenApi();
    }
}
