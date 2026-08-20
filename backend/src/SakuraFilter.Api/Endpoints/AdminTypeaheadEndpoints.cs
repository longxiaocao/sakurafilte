using Microsoft.EntityFrameworkCore;
using Npgsql;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// typeahead 字典表重建端点 — ETL 导入后调用, 刷新全量 distinct 值快照。
/// WHY: typeahead_dict 是 typeahead 查询的快速路径 (GIN trgm, 万行级);
///      明细表 (cross_references/machine_applications) 数据变化后需重建, 否则候选值缺失。
/// 成本: 全量 distinct 重建 (万行级) 秒级完成, 幂等 (TRUNCATE + INSERT)。
/// </summary>
public static class AdminTypeaheadEndpoints
{
    private const string RebuildSql = """
        TRUNCATE typeahead_dict;
        INSERT INTO typeahead_dict (field, value)
        SELECT 'oem-brand', oem_brand FROM cross_references WHERE oem_brand IS NOT NULL AND oem_brand <> '' GROUP BY oem_brand
        UNION ALL
        SELECT 'oem-no2', oem_2 FROM products WHERE oem_2 IS NOT NULL AND oem_2 <> '' GROUP BY oem_2
        UNION ALL
        SELECT 'oem-no3', oem_no_3 FROM cross_references WHERE oem_no_3 IS NOT NULL AND oem_no_3 <> '' GROUP BY oem_no_3
        UNION ALL
        SELECT 'machine-brand', machine_brand FROM machine_applications WHERE machine_brand IS NOT NULL AND machine_brand <> '' GROUP BY machine_brand
        UNION ALL
        SELECT 'machine-model', machine_model FROM machine_applications WHERE machine_model IS NOT NULL AND machine_model <> '' GROUP BY machine_model
        UNION ALL
        SELECT 'model-name', model_name FROM machine_applications WHERE model_name IS NOT NULL AND model_name <> '' GROUP BY model_name
        UNION ALL
        SELECT 'engine-brand', engine_brand FROM machine_applications WHERE engine_brand IS NOT NULL AND engine_brand <> '' GROUP BY engine_brand
        UNION ALL
        SELECT 'engine-type', engine_type FROM machine_applications WHERE engine_type IS NOT NULL AND engine_type <> '' GROUP BY engine_type;
        """;

    public static void MapAdminTypeaheadEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin/typeahead").WithTags("AdminTypeahead")
            .RequireAuthorization("Admin");

        g.MapPost("/rebuild", async (ProductDbContext db, CancellationToken ct) =>
        {
            var conn = db.Database.GetDbConnection();
            var opened = conn.State != System.Data.ConnectionState.Open;
            if (opened) await conn.OpenAsync(ct);
            try
            {
                await using var cmd = new NpgsqlCommand(RebuildSql, (NpgsqlConnection)conn);
                cmd.CommandTimeout = 120;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            finally
            {
                if (opened && conn.State == System.Data.ConnectionState.Open) await conn.CloseAsync();
            }
            return Results.Ok(new { ok = true, message = "typeahead_dict 已重建 (全量 distinct 快照刷新)" });
        }).WithName("AdminRebuildTypeaheadDict").WithOpenApi();
    }
}
