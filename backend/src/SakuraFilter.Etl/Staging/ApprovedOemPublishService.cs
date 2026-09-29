using Npgsql;

namespace SakuraFilter.Etl.Staging;

/// <summary>
/// 已审核映射发布预检。此阶段刻意不提供正式表写入实现。
/// </summary>
public sealed class ApprovedOemPublishService
{
    public async Task<ApprovedOemPublishPreflightReport> PreflightAsync(
        ApprovedOemPublishOptions options,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            WITH approved AS (
                SELECT oem_no_1_normalized, target_mr1
                FROM staging.oem_mr1_mapping_reviews
                WHERE batch_id = @batch_id
                  AND review_status = 'approved'
            ),
            resolved AS (
                SELECT a.oem_no_1_normalized,
                       CASE
                           WHEN p.match_count = 0 THEN 'target_mr1_not_found'
                           WHEN p.match_count <> 1 THEN 'target_mr1_not_unique'
                           WHEN p.has_discontinued THEN 'target_product_discontinued'
                           ELSE NULL
                       END AS blocked_reason
                FROM approved a
                LEFT JOIN LATERAL (
                    SELECT COUNT(*)::INTEGER AS match_count,
                           COALESCE(BOOL_OR(is_discontinued), false) AS has_discontinued
                    FROM public.products
                    WHERE mr_1 = a.target_mr1
                ) p ON TRUE
            ),
            publishable AS (
                SELECT oem_no_1_normalized
                FROM resolved
                WHERE blocked_reason IS NULL
            )
            SELECT
                (SELECT COUNT(*) FROM approved),
                (SELECT COUNT(*) FROM publishable),
                (SELECT COUNT(*) FROM resolved WHERE blocked_reason IS NOT NULL),
                (SELECT COUNT(*) FROM staging.product_specs_clean s
                 JOIN publishable p USING (oem_no_1_normalized)
                 WHERE s.batch_id = @batch_id),
                (SELECT COUNT(*) FROM staging.oem_numbers_clean x
                 JOIN publishable p USING (oem_no_1_normalized)
                 WHERE x.batch_id = @batch_id),
                (SELECT COUNT(*) FROM staging.applications_clean a
                 JOIN publishable p USING (oem_no_1_normalized)
                 WHERE a.batch_id = @batch_id),
                (SELECT COUNT(*) FROM staging.product_spec_field_conflicts c
                 JOIN publishable p USING (oem_no_1_normalized)
                 WHERE c.batch_id = @batch_id);
            """;

        await using var connection = new NpgsqlConnection(options.PgConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("batch_id", options.BatchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException($"批次 {options.BatchId} 无法生成发布预检报告");

        return new ApprovedOemPublishPreflightReport(
            options.BatchId,
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6));
    }
}
