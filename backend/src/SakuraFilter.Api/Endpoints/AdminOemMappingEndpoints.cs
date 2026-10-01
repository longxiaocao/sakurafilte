using Npgsql;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// OEM NO 1 到 MR.1 审核队列查询。只读 staging 数据，正式发布仍由独立流程负责。
/// </summary>
public static class AdminOemMappingEndpoints
{
    public static IEndpointRouteBuilder MapAdminOemMappingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/oem-mapping").WithTags("AdminOemMapping")
            .RequireAuthorization("Admin");

        group.MapGet("/batches/{batchId:long}/summary", async (
            long batchId,
            NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            const string sql = """
                SELECT r.status, r.total_oem_count, r.candidate_count, r.pending_count, r.ambiguous_count,
                       COUNT(v.*) FILTER (WHERE v.review_status = 'approved') AS approved_count,
                       COUNT(v.*) FILTER (WHERE v.review_status = 'rejected') AS rejected_count
                FROM staging.oem_mapping_candidate_runs r
                LEFT JOIN staging.oem_mr1_mapping_reviews v ON v.batch_id = r.batch_id
                WHERE r.batch_id = @batch_id
                GROUP BY r.status, r.total_oem_count, r.candidate_count, r.pending_count, r.ambiguous_count;
                """;
            await using var command = dataSource.CreateCommand(sql);
            command.Parameters.AddWithValue("batch_id", batchId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return Results.NotFound(new { code = "OEM_MAPPING_BATCH_NOT_FOUND", detail = "未找到 OEM 映射候选批次" });

            return Results.Ok(new
            {
                batchId,
                status = reader.GetString(0),
                totalOemCount = reader.GetInt64(1),
                candidateCount = reader.GetInt64(2),
                pendingCount = reader.GetInt64(3),
                ambiguousCount = reader.GetInt64(4),
                approvedCount = reader.GetInt64(5),
                rejectedCount = reader.GetInt64(6)
            });
        }).WithName("AdminOemMappingSummary").WithOpenApi();

        group.MapGet("/batches/{batchId:long}/candidates", async (
            long batchId,
            string? status,
            int page,
            int pageSize,
            NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            if (status is not null && status is not ("candidate" or "pending" or "ambiguous"))
                return Results.BadRequest(new { code = "OEM_MAPPING_STATUS_INVALID", detail = "status 只能是 candidate、pending 或 ambiguous" });

            var normalizedPage = Math.Max(1, page);
            var normalizedPageSize = Math.Clamp(pageSize == 0 ? 50 : pageSize, 1, 100);
            var offset = (normalizedPage - 1) * normalizedPageSize;
            const string sql = """
                SELECT c.oem_no_1_normalized, c.oem_no_1_display, c.candidate_status,
                       c.candidate_product_id, c.candidate_mr1, c.exact_product_match_count,
                       c.match_reason, v.review_status, v.target_mr1, v.reviewed_by, v.reviewed_at,
                       COUNT(*) OVER() AS total_count
                FROM staging.oem_mapping_candidates c
                LEFT JOIN staging.oem_mr1_mapping_reviews v
                  ON v.batch_id = c.batch_id AND v.oem_no_1_normalized = c.oem_no_1_normalized
                WHERE c.batch_id = @batch_id
                  AND (@status IS NULL OR c.candidate_status = @status)
                ORDER BY c.oem_no_1_normalized
                LIMIT @limit OFFSET @offset;
                """;
            await using var command = dataSource.CreateCommand(sql);
            command.Parameters.AddWithValue("batch_id", batchId);
            command.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);
            command.Parameters.AddWithValue("limit", normalizedPageSize);
            command.Parameters.AddWithValue("offset", offset);
            await using var reader = await command.ExecuteReaderAsync(ct);

            var items = new List<object>();
            long total = 0;
            while (await reader.ReadAsync(ct))
            {
                total = reader.GetInt64(11);
                items.Add(new
                {
                    oemNo1 = reader.GetString(0),
                    oemNo1Display = reader.GetString(1),
                    candidateStatus = reader.GetString(2),
                    candidateProductId = reader.IsDBNull(3) ? (long?)null : reader.GetInt64(3),
                    candidateMr1 = reader.IsDBNull(4) ? null : reader.GetString(4),
                    exactProductMatchCount = reader.GetInt32(5),
                    matchReason = reader.GetString(6),
                    reviewStatus = reader.IsDBNull(7) ? null : reader.GetString(7),
                    targetMr1 = reader.IsDBNull(8) ? null : reader.GetString(8),
                    reviewedBy = reader.IsDBNull(9) ? null : reader.GetString(9),
                    reviewedAt = reader.IsDBNull(10) ? (DateTime?)null : reader.GetDateTime(10)
                });
            }

            return Results.Ok(new { batchId, page = normalizedPage, pageSize = normalizedPageSize, total, items });
        }).WithName("AdminOemMappingCandidates").WithOpenApi();

        return app;
    }
}
