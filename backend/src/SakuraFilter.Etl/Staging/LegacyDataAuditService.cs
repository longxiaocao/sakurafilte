using Npgsql;

namespace SakuraFilter.Etl.Staging;

/// <summary>
/// 替换旧正式数据前的只读审计，不含任何 INSERT、UPDATE、DELETE 或 TRUNCATE。
/// </summary>
public sealed class LegacyDataAuditService
{
    public async Task<LegacyDataAuditReport> AuditAsync(
        LegacyDataAuditOptions options,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(options.PgConnectionString);
        await connection.OpenAsync(cancellationToken);

        var hasProducts = await TableExistsAsync(connection, "products", cancellationToken);
        var hasCrossReferences = await TableExistsAsync(connection, "cross_references", cancellationToken);
        var hasMachineApplications = await TableExistsAsync(connection, "machine_applications", cancellationToken);
        var hasProductImages = await TableExistsAsync(connection, "product_images", cancellationToken);
        var hasEtlProgressLog = await TableExistsAsync(connection, "etl_progress_log", cancellationToken);

        var databaseName = await ScalarStringAsync(connection, "SELECT current_database();", cancellationToken);
        var databaseSize = await ScalarStringAsync(connection, "SELECT pg_size_pretty(pg_database_size(current_database()));", cancellationToken);
        var productCounts = hasProducts
            ? await ProductCountsAsync(connection, cancellationToken)
            : (Rows: (long?)null, MissingMr1: (long?)null, MissingOem: (long?)null);

        return new LegacyDataAuditReport(
            databaseName,
            databaseSize,
            hasProducts,
            hasCrossReferences,
            hasMachineApplications,
            hasProductImages,
            hasEtlProgressLog,
            productCounts.Rows,
            productCounts.MissingMr1,
            productCounts.MissingOem,
            hasCrossReferences ? await CountRowsAsync(connection, "cross_references", cancellationToken) : null,
            hasMachineApplications ? await CountRowsAsync(connection, "machine_applications", cancellationToken) : null,
            hasProductImages ? await CountRowsAsync(connection, "product_images", cancellationToken) : null,
            hasEtlProgressLog ? await CountRowsAsync(connection, "etl_progress_log", cancellationToken) : null);
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string tableName, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass(@table_name) IS NOT NULL;", connection);
        command.Parameters.AddWithValue("table_name", $"public.{tableName}");
        return (bool)(await command.ExecuteScalarAsync(ct) ?? false);
    }

    private static async Task<string> ScalarStringAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync(ct) ?? string.Empty);
    }

    private static async Task<(long? Rows, long? MissingMr1, long? MissingOem)> ProductCountsAsync(
        NpgsqlConnection connection,
        CancellationToken ct)
    {
        const string sql = """
            SELECT count(*),
                   count(*) FILTER (WHERE mr_1 IS NULL OR btrim(mr_1) = ''),
                   count(*) FILTER (WHERE oem_no_normalized IS NULL OR btrim(oem_no_normalized) = '')
            FROM public.products;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static async Task<long> CountRowsAsync(NpgsqlConnection connection, string tableName, CancellationToken ct)
    {
        var sql = tableName switch
        {
            "cross_references" => "SELECT count(*) FROM public.cross_references;",
            "machine_applications" => "SELECT count(*) FROM public.machine_applications;",
            "product_images" => "SELECT count(*) FROM public.product_images;",
            "etl_progress_log" => "SELECT count(*) FROM public.etl_progress_log;",
            _ => throw new ArgumentOutOfRangeException(nameof(tableName))
        };
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(ct) ?? 0L);
    }
}
