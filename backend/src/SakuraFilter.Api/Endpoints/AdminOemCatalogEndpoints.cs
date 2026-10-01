using System.Text.Json;
using System.Security.Claims;
using Npgsql;
using NpgsqlTypes;
using SakuraFilter.Etl.Staging;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// OEM NO 1 锚点目录的后台核验接口。只读 catalog schema，不依赖尚未提供的 MR.1。
/// </summary>
public static class AdminOemCatalogEndpoints
{
    public static IEndpointRouteBuilder MapAdminOemCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/oem-catalog").WithTags("AdminOemCatalog")
            .RequireAuthorization("Admin");

        group.MapGet("/summary", async (NpgsqlDataSource dataSource, CancellationToken ct) =>
        {
            const string sql = """
                SELECT
                    (SELECT batch_id FROM catalog.oem_import_runs
                     WHERE status = 'completed' ORDER BY finished_at DESC NULLS LAST, batch_id DESC LIMIT 1),
                    (SELECT COUNT(*) FROM catalog.oem_products),
                    (SELECT COUNT(*) FROM catalog.oem_products WHERE spec_payload <> '{}'::jsonb),
                    (SELECT COUNT(*) FROM catalog.oem_cross_references),
                    (SELECT COUNT(*) FROM catalog.oem_machine_applications),
                    (SELECT COUNT(*) FROM catalog.oem_mr1_mappings WHERE ended_at IS NULL);
                """;
            await using var command = dataSource.CreateCommand(sql);
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);

            return Results.Ok(new
            {
                lastPublishedBatchId = reader.IsDBNull(0) ? (long?)null : reader.GetInt64(0),
                oemProductCount = reader.GetInt64(1),
                productWithSpecCount = reader.GetInt64(2),
                crossReferenceCount = reader.GetInt64(3),
                machineApplicationCount = reader.GetInt64(4),
                activeMr1MappingCount = reader.GetInt64(5)
            });
        }).WithName("AdminOemCatalogSummary").WithOpenApi();

        group.MapGet("/products", async (
            string? q,
            int page,
            int pageSize,
            NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            var normalizedPage = Math.Clamp(page, 1, 100_000);
            var normalizedPageSize = Math.Clamp(pageSize == 0 ? 50 : pageSize, 1, 100);
            var offset = (normalizedPage - 1) * normalizedPageSize;
            var query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
            const string countSql = """
                SELECT COUNT(*)
                FROM catalog.oem_products p
                WHERE @query IS NULL
                   OR p.oem_no_1_normalized ILIKE '%' || @query || '%'
                   OR p.oem_no_1_display ILIKE '%' || @query || '%'
                   OR p.product_name_candidates::text ILIKE '%' || @query || '%';
                """;
            await using var countCommand = dataSource.CreateCommand(countSql);
            AddSearchParameter(countCommand, query);
            var total = (long)(await countCommand.ExecuteScalarAsync(ct) ?? 0L);

            const string itemsSql = """
                WITH filtered AS (
                    SELECT p.id, p.oem_no_1_normalized, p.oem_no_1_display, p.product_name_candidates,
                           p.spec_conflict_field_count, p.source_batch_id, p.updated_at
                    FROM catalog.oem_products p
                    WHERE @query IS NULL
                       OR p.oem_no_1_normalized ILIKE '%' || @query || '%'
                       OR p.oem_no_1_display ILIKE '%' || @query || '%'
                       OR p.product_name_candidates::text ILIKE '%' || @query || '%'
                    ORDER BY p.oem_no_1_normalized
                    LIMIT @limit OFFSET @offset
                )
                SELECT p.oem_no_1_normalized, p.oem_no_1_display, p.product_name_candidates,
                       p.spec_conflict_field_count, p.source_batch_id, p.updated_at,
                       (SELECT COUNT(*) FROM catalog.oem_cross_references x WHERE x.oem_product_id = p.id),
                       (SELECT COUNT(*) FROM catalog.oem_machine_applications a WHERE a.oem_product_id = p.id),
                       (SELECT m.mr1 FROM catalog.oem_mr1_mappings m
                        WHERE m.oem_product_id = p.id AND m.ended_at IS NULL LIMIT 1)
                FROM filtered p;
                """;
            await using var command = dataSource.CreateCommand(itemsSql);
            AddSearchParameter(command, query);
            command.Parameters.AddWithValue("limit", normalizedPageSize);
            command.Parameters.AddWithValue("offset", offset);
            await using var reader = await command.ExecuteReaderAsync(ct);

            var items = new List<object>();
            while (await reader.ReadAsync(ct))
            {
                items.Add(new
                {
                    oemNo1 = reader.GetString(0),
                    oemNo1Display = reader.GetString(1),
                    productNameCandidates = ReadJson(reader, 2),
                    specConflictFieldCount = reader.GetInt32(3),
                    sourceBatchId = reader.GetInt64(4),
                    updatedAt = reader.GetDateTime(5),
                    crossReferenceCount = reader.GetInt64(6),
                    machineApplicationCount = reader.GetInt64(7),
                    activeMr1 = reader.IsDBNull(8) ? null : reader.GetString(8)
                });
            }

            return Results.Ok(new { page = normalizedPage, pageSize = normalizedPageSize, total, items });
        }).WithName("AdminOemCatalogProducts").WithOpenApi();

        group.MapGet("/products/{**oemNo1}", async (
            string oemNo1,
            NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            // catch-all 路由可能保留 %2F；先还原一次再沿用导入阶段的 OEM 规范化规则。
            var normalizedOem = StagingKeyNormalizer.NormalizeOemNo1(Uri.UnescapeDataString(oemNo1));
            if (normalizedOem is null)
                return Results.BadRequest(new { code = "OEM_NO_1_REQUIRED", detail = "OEM NO 1 不能为空" });

            const string sql = """
                SELECT p.oem_no_1_normalized, p.oem_no_1_display, p.product_name_candidates, p.spec_payload,
                       p.spec_conflict_field_count, p.source_batch_id, p.created_at, p.updated_at,
                       (SELECT COUNT(*) FROM catalog.oem_cross_references x WHERE x.oem_product_id = p.id),
                       (SELECT COUNT(*) FROM catalog.oem_machine_applications a WHERE a.oem_product_id = p.id),
                       (SELECT m.mr1 FROM catalog.oem_mr1_mappings m
                        WHERE m.oem_product_id = p.id AND m.ended_at IS NULL LIMIT 1)
                FROM catalog.oem_products p
                WHERE p.oem_no_1_normalized = @oem_no_1;
                """;
            await using var command = dataSource.CreateCommand(sql);
            command.Parameters.AddWithValue("oem_no_1", normalizedOem);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return Results.NotFound(new { code = "OEM_CATALOG_PRODUCT_NOT_FOUND", detail = "未找到该 OEM NO 1 目录记录" });

            return Results.Ok(new
            {
                oemNo1 = reader.GetString(0),
                oemNo1Display = reader.GetString(1),
                productNameCandidates = ReadJson(reader, 2),
                specPayload = ReadJson(reader, 3),
                specConflictFieldCount = reader.GetInt32(4),
                sourceBatchId = reader.GetInt64(5),
                createdAt = reader.GetDateTime(6),
                updatedAt = reader.GetDateTime(7),
                crossReferenceCount = reader.GetInt64(8),
                machineApplicationCount = reader.GetInt64(9),
                activeMr1 = reader.IsDBNull(10) ? null : reader.GetString(10)
            });
        }).WithName("AdminOemCatalogProductDetail").WithOpenApi();

        group.MapGet("/products/xrefs/{**oemNo1}", async (
            string oemNo1, int page, int pageSize, NpgsqlDataSource dataSource, CancellationToken ct) =>
        {
            var normalizedOem = StagingKeyNormalizer.NormalizeOemNo1(Uri.UnescapeDataString(oemNo1));
            if (normalizedOem is null) return Results.BadRequest(new { code = "OEM_NO_1_REQUIRED", detail = "OEM NO 1 不能为空" });
            var (normalizedPage, normalizedPageSize, offset) = NormalizePage(page, pageSize);
            const string sql = """
                WITH target AS (SELECT id FROM catalog.oem_products WHERE oem_no_1_normalized = @oem_no_1),
                rows AS (SELECT x.product_name_1, x.oem_brand, x.oem_no_3, x.source_row_nos, x.merged_source_row_count,
                                 COUNT(*) OVER() total FROM catalog.oem_cross_references x JOIN target t ON t.id=x.oem_product_id ORDER BY x.oem_brand_key, x.oem_no_3_key LIMIT @limit OFFSET @offset)
                SELECT product_name_1, oem_brand, oem_no_3, source_row_nos, merged_source_row_count, total FROM rows;
                """;
            await using var command = dataSource.CreateCommand(sql);
            command.Parameters.AddWithValue("oem_no_1", normalizedOem); command.Parameters.AddWithValue("limit", normalizedPageSize); command.Parameters.AddWithValue("offset", offset);
            await using var reader = await command.ExecuteReaderAsync(ct); var items = new List<object>(); long total = 0;
            while (await reader.ReadAsync(ct)) { total = reader.GetInt64(5); items.Add(new { productName1 = reader.IsDBNull(0) ? null : reader.GetString(0), oemBrand = reader.IsDBNull(1) ? null : reader.GetString(1), oemNo3 = reader.IsDBNull(2) ? null : reader.GetString(2), sourceRowNos = reader.GetFieldValue<int[]>(3), mergedSourceRowCount = reader.GetInt32(4) }); }
            return Results.Ok(new { page = normalizedPage, pageSize = normalizedPageSize, total, items });
        }).WithName("AdminOemCatalogCrossReferences").WithOpenApi();

        group.MapGet("/products/applications/{**oemNo1}", async (
            string oemNo1, int page, int pageSize, NpgsqlDataSource dataSource, CancellationToken ct) =>
        {
            var normalizedOem = StagingKeyNormalizer.NormalizeOemNo1(Uri.UnescapeDataString(oemNo1));
            if (normalizedOem is null) return Results.BadRequest(new { code = "OEM_NO_1_REQUIRED", detail = "OEM NO 1 不能为空" });
            var (normalizedPage, normalizedPageSize, offset) = NormalizePage(page, pageSize);
            const string sql = """
                WITH target AS (SELECT id FROM catalog.oem_products WHERE oem_no_1_normalized = @oem_no_1),
                rows AS (SELECT a.machine_brand, a.machine_model, a.model_name, a.engine_brand, a.engine_model, a.production_date, a.power, a.source_row_no, COUNT(*) OVER() total FROM catalog.oem_machine_applications a JOIN target t ON t.id=a.oem_product_id ORDER BY a.source_row_no LIMIT @limit OFFSET @offset)
                SELECT machine_brand, machine_model, model_name, engine_brand, engine_model, production_date, power, source_row_no, total FROM rows;
                """;
            await using var command = dataSource.CreateCommand(sql);
            command.Parameters.AddWithValue("oem_no_1", normalizedOem); command.Parameters.AddWithValue("limit", normalizedPageSize); command.Parameters.AddWithValue("offset", offset);
            await using var reader = await command.ExecuteReaderAsync(ct); var items = new List<object>(); long total = 0;
            while (await reader.ReadAsync(ct)) { total = reader.GetInt64(8); items.Add(new { machineBrand = reader.IsDBNull(0) ? null : reader.GetString(0), machineModel = reader.IsDBNull(1) ? null : reader.GetString(1), modelName = reader.IsDBNull(2) ? null : reader.GetString(2), engineBrand = reader.IsDBNull(3) ? null : reader.GetString(3), engineModel = reader.IsDBNull(4) ? null : reader.GetString(4), productionDate = reader.IsDBNull(5) ? null : reader.GetString(5), power = reader.IsDBNull(6) ? null : reader.GetString(6), sourceRowNo = reader.GetInt32(7) }); }
            return Results.Ok(new { page = normalizedPage, pageSize = normalizedPageSize, total, items });
        }).WithName("AdminOemCatalogApplications").WithOpenApi();

        group.MapPut("/products/mr1/{**oemNo1}", async (
            string oemNo1,
            OemMr1MappingRequest request,
            HttpContext context,
            NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            var normalizedOem = StagingKeyNormalizer.NormalizeOemNo1(Uri.UnescapeDataString(oemNo1));
            if (normalizedOem is null)
                return Results.BadRequest(new { code = "OEM_NO_1_REQUIRED", detail = "OEM NO 1 不能为空" });

            var normalizedMr1 = string.IsNullOrWhiteSpace(request.Mr1) ? null : request.Mr1.Trim().ToUpperInvariant();
            var assignedBy = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.User.FindFirst("sub")?.Value
                ?? "admin";
            const string sql = "SELECT catalog.set_oem_mr1_mapping(@oem_no_1, @mr1, @assigned_by, @reason);";
            try
            {
                await using var command = dataSource.CreateCommand(sql);
                command.Parameters.AddWithValue("oem_no_1", normalizedOem);
                command.Parameters.AddWithValue("mr1", NpgsqlDbType.Text, (object?)normalizedMr1 ?? DBNull.Value);
                command.Parameters.AddWithValue("assigned_by", assignedBy);
                command.Parameters.AddWithValue("reason", NpgsqlDbType.Text, (object?)request.ChangeReason?.Trim() ?? DBNull.Value);
                await command.ExecuteNonQueryAsync(ct);
                return Results.NoContent();
            }
            catch (PostgresException ex) when (ex.SqlState == "P0002")
            {
                return Results.NotFound(new { code = "OEM_CATALOG_PRODUCT_NOT_FOUND", detail = ex.MessageText });
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict(new { code = "MR1_ALREADY_MAPPED", detail = ex.MessageText });
            }
        }).WithName("AdminSetOemCatalogMr1").WithOpenApi();

        return app;
    }

    private static JsonElement ReadJson(NpgsqlDataReader reader, int ordinal) =>
        JsonSerializer.Deserialize<JsonElement>(reader.GetString(ordinal));

    private static void AddSearchParameter(NpgsqlCommand command, string? query) =>
        command.Parameters.AddWithValue("query", NpgsqlDbType.Text, (object?)query ?? DBNull.Value);

    private static (int Page, int PageSize, int Offset) NormalizePage(int page, int pageSize)
    {
        var normalizedPage = Math.Clamp(page, 1, 100_000);
        var normalizedPageSize = Math.Clamp(pageSize == 0 ? 50 : pageSize, 1, 100);
        return (normalizedPage, normalizedPageSize, (normalizedPage - 1) * normalizedPageSize);
    }

    private sealed record OemMr1MappingRequest(string? Mr1, string? ChangeReason);
}
