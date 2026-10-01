using Npgsql;

namespace SakuraFilter.Etl.Staging;

public sealed class StagingCleanService
{
    public async Task<StagingCleanReport> RefreshAsync(
        StagingCleanOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options.BatchId <= 0) throw new ArgumentOutOfRangeException(nameof(options.BatchId));
        if (string.IsNullOrWhiteSpace(options.PgConnectionString))
            throw new ArgumentException("PostgreSQL 连接串不能为空", nameof(options.PgConnectionString));

        await using var connection = new NpgsqlConnection(options.PgConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var refresh = new NpgsqlCommand("SELECT staging.refresh_oem_clean(@batch_id);", connection))
        {
            refresh.Parameters.AddWithValue("batch_id", options.BatchId);
            await refresh.ExecuteNonQueryAsync(cancellationToken);
        }

        const string reportSql = """
            SELECT batch_id, status, product_specs_rows, product_spec_conflict_rows,
                   oem_numbers_rows, oem_numbers_merged_rows, applications_rows
            FROM staging.clean_runs
            WHERE batch_id = @batch_id;
            """;

        await using var reportCommand = new NpgsqlCommand(reportSql, connection);
        reportCommand.Parameters.AddWithValue("batch_id", options.BatchId);
        await using var reader = await reportCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException($"批次 {options.BatchId} 未生成 clean 运行记录");

        return new StagingCleanReport(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6));
    }
}
