using Npgsql;

namespace SakuraFilter.Etl.Staging;

/// <summary>
/// 将完成 clean 的 OEM 数据发布到独立 catalog schema，不写旧 public 产品表。
/// </summary>
public sealed class OemCatalogPublishService
{
    // 批次 1 需要集合写入 70 万级机型记录，不能使用 Npgsql 默认 30 秒命令超时。
    public const int PublishCommandTimeoutSeconds = 0;

    public async Task<OemCatalogPublishReport> PublishAsync(
        OemCatalogPublishOptions options,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(options.PgConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var command = new NpgsqlCommand(
            "SELECT catalog.publish_oem_clean_batch(@batch_id);", connection))
        {
            command.CommandTimeout = PublishCommandTimeoutSeconds;
            command.Parameters.AddWithValue("batch_id", options.BatchId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string reportSql = """
            SELECT batch_id, status, oem_product_rows, cross_reference_rows, machine_application_rows
            FROM catalog.oem_import_runs
            WHERE batch_id = @batch_id;
            """;
        await using var reportCommand = new NpgsqlCommand(reportSql, connection);
        reportCommand.Parameters.AddWithValue("batch_id", options.BatchId);
        await using var reader = await reportCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException($"批次 {options.BatchId} 未生成 OEM 目录发布记录");

        return new OemCatalogPublishReport(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4));
    }
}
