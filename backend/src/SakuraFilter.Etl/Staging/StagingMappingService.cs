using Npgsql;

namespace SakuraFilter.Etl.Staging;

public sealed class StagingMappingService
{
    public async Task<StagingMappingRefreshReport> RefreshCandidatesAsync(
        StagingMappingRefreshOptions options,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(options.PgConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var refreshCommand = new NpgsqlCommand(
            "SELECT staging.refresh_oem_mapping_candidates(@batch_id);", connection))
        {
            refreshCommand.Parameters.AddWithValue("batch_id", options.BatchId);
            await refreshCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string reportSql = """
            SELECT batch_id, status, total_oem_count, candidate_count, pending_count, ambiguous_count
            FROM staging.oem_mapping_candidate_runs
            WHERE batch_id = @batch_id;
            """;
        await using var reportCommand = new NpgsqlCommand(reportSql, connection);
        reportCommand.Parameters.AddWithValue("batch_id", options.BatchId);
        await using var reader = await reportCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException($"批次 {options.BatchId} 未生成 OEM 映射候选运行记录");

        return new StagingMappingRefreshReport(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5));
    }

    public async Task ReviewAsync(
        StagingMappingReviewOptions options,
        CancellationToken cancellationToken = default)
    {
        var normalizedOem = StagingKeyNormalizer.NormalizeOemNo1(options.OemNo1);
        if (normalizedOem is null) throw new ArgumentException("OEM NO 1 不能为空", nameof(options.OemNo1));

        const string existsSql = """
            SELECT EXISTS (
                SELECT 1 FROM staging.oem_mapping_candidates
                WHERE batch_id = @batch_id AND oem_no_1_normalized = @oem_no_1_normalized
            );
            """;
        const string upsertSql = """
            INSERT INTO staging.oem_mr1_mapping_reviews (
                batch_id, oem_no_1_normalized, review_status, target_product_id,
                target_mr1, review_reason, reviewed_by, reviewed_at, updated_at)
            VALUES (
                @batch_id, @oem_no_1_normalized, @review_status, @target_product_id,
                @target_mr1, @review_reason, @reviewed_by, now(), now())
            ON CONFLICT (batch_id, oem_no_1_normalized) DO UPDATE
            SET review_status = EXCLUDED.review_status,
                target_product_id = EXCLUDED.target_product_id,
                target_mr1 = EXCLUDED.target_mr1,
                review_reason = EXCLUDED.review_reason,
                reviewed_by = EXCLUDED.reviewed_by,
                reviewed_at = now(),
                updated_at = now();
            """;

        await using var connection = new NpgsqlConnection(options.PgConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var existsCommand = new NpgsqlCommand(existsSql, connection, transaction))
        {
            existsCommand.Parameters.AddWithValue("batch_id", options.BatchId);
            existsCommand.Parameters.AddWithValue("oem_no_1_normalized", normalizedOem);
            if (!(bool)(await existsCommand.ExecuteScalarAsync(cancellationToken) ?? false))
                throw new InvalidOperationException($"批次 {options.BatchId} 中不存在 OEM NO 1 '{normalizedOem}' 的映射候选");
        }

        await using (var upsertCommand = new NpgsqlCommand(upsertSql, connection, transaction))
        {
            upsertCommand.Parameters.AddWithValue("batch_id", options.BatchId);
            upsertCommand.Parameters.AddWithValue("oem_no_1_normalized", normalizedOem);
            upsertCommand.Parameters.AddWithValue("review_status", options.ReviewStatus);
            upsertCommand.Parameters.AddWithValue("target_product_id", (object?)options.TargetProductId ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("target_mr1", (object?)options.TargetMr1?.Trim() ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("review_reason", (object?)options.ReviewReason?.Trim() ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("reviewed_by", options.ReviewedBy.Trim());
            await upsertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
