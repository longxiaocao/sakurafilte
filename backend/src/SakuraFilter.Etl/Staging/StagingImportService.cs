using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace SakuraFilter.Etl.Staging;

/// <summary>
/// 将三个 Excel 原始表导入 staging schema。
/// 正式 products/xrefs/apps 表不在本服务的写入范围内。
/// </summary>
public sealed class StagingImportService
{
    private const int BatchCommandLimit = 250;

    public async Task<StagingImportReport> ImportAsync(
        StagingImportOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        if (options.DryRun)
            return await InspectDryRunAsync(options, cancellationToken);

        await using var connection = new NpgsqlConnection(options.PgConnectionString);
        await connection.OpenAsync(cancellationToken);

        var batchId = await CreateBatchAsync(connection, options, cancellationToken);
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var sourceReports = new List<StagingSourceReport>
            {
                await ImportSourceAsync(connection, transaction, batchId, options.SpecsPath, StagingSourceKind.ProductSpecs, cancellationToken),
                await ImportSourceAsync(connection, transaction, batchId, options.OemNumbersPath, StagingSourceKind.OemNumbers, cancellationToken),
                await ImportSourceAsync(connection, transaction, batchId, options.ApplicationsPath, StagingSourceKind.Applications, cancellationToken)
            };
            var issueCount = sourceReports.Sum(x => x.IssueRows);
            await UpdateBatchAsync(connection, transaction, batchId, sourceReports, issueCount, "completed", null, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new StagingImportReport(batchId, sourceReports, issueCount, false);
        }
        catch (Exception ex)
        {
            await MarkBatchFailedAsync(connection, batchId, ex.Message, cancellationToken);
            throw;
        }
    }

    private static void ValidateOptions(StagingImportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PgConnectionString))
            throw new ArgumentException("必须提供 PostgreSQL 连接串", nameof(options));
        foreach (var path in new[] { options.SpecsPath, options.OemNumbersPath, options.ApplicationsPath })
        {
            if (!File.Exists(path)) throw new FileNotFoundException("找不到待导入 Excel", path);
        }
    }

    private static async Task<long> CreateBatchAsync(NpgsqlConnection connection, StagingImportOptions options, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(@"
            INSERT INTO staging.import_batches
                (specs_file_name, oem_numbers_file_name, applications_file_name)
            VALUES ($1, $2, $3)
            RETURNING id", connection);
        command.Parameters.AddWithValue(Path.GetFileName(options.SpecsPath));
        command.Parameters.AddWithValue(Path.GetFileName(options.OemNumbersPath));
        command.Parameters.AddWithValue(Path.GetFileName(options.ApplicationsPath));
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task<StagingImportReport> InspectDryRunAsync(StagingImportOptions options, CancellationToken ct)
    {
        var reports = new List<StagingSourceReport>
        {
            await InspectSourceAsync(options.SpecsPath, StagingSourceKind.ProductSpecs, ct),
            await InspectSourceAsync(options.OemNumbersPath, StagingSourceKind.OemNumbers, ct),
            await InspectSourceAsync(options.ApplicationsPath, StagingSourceKind.Applications, ct)
        };
        return new StagingImportReport(0, reports, reports.Sum(x => x.IssueRows), true);
    }

    private static async Task<StagingSourceReport> InspectSourceAsync(string path, StagingSourceKind kind, CancellationToken ct)
    {
        var sourceRows = 0L;
        var blankOemRows = 0L;
        var issueRows = 0L;
        var deduplicator = kind == StagingSourceKind.Applications ? new StagingApplicationDeduplicator() : null;
        await foreach (var row in StagingWorkbookReader.ReadAsync(path, kind, ct))
        {
            sourceRows++;
            if (row.OemNo1Normalized is null) { blankOemRows++; issueRows++; }
            if (deduplicator is null || deduplicator.TryAccept(row.RowHash)) { }
        }
        return new StagingSourceReport(SourceName(kind), sourceRows, deduplicator?.AcceptedCount ?? sourceRows, deduplicator?.DuplicateCount ?? 0, blankOemRows, issueRows);
    }

    private static async Task<StagingSourceReport> ImportSourceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long batchId,
        string path,
        StagingSourceKind sourceKind,
        CancellationToken ct)
    {
        var sourceRows = 0L;
        var stagedRows = 0L;
        var blankOemRows = 0L;
        var issueRows = 0L;
        var commands = new List<NpgsqlBatchCommand>(BatchCommandLimit);
        var anchors = new Dictionary<string, AnchorAccumulator>(StringComparer.Ordinal);
        var deduplicator = sourceKind == StagingSourceKind.Applications
            ? new StagingApplicationDeduplicator()
            : null;

        await foreach (var row in StagingWorkbookReader.ReadAsync(path, sourceKind, ct))
        {
            sourceRows++;
            if (row.OemNo1Normalized is null)
            {
                blankOemRows++;
                issueRows++;
                commands.Add(BuildIssueCommand(batchId, sourceKind, row));
            }

            if (deduplicator is not null && !deduplicator.TryAccept(row.RowHash))
                continue;

            commands.Add(BuildRawCommand(batchId, sourceKind, row));
            stagedRows++;
            if (row.OemNo1Normalized is not null)
            {
                if (!anchors.TryGetValue(row.OemNo1Normalized, out var accumulator))
                {
                    accumulator = new AnchorAccumulator(row.OemNo1Raw ?? row.OemNo1Normalized);
                    anchors.Add(row.OemNo1Normalized, accumulator);
                }
                accumulator.Increment(sourceKind);
            }

            if (commands.Count >= BatchCommandLimit)
                await FlushCommandsAsync(connection, transaction, commands, ct);
        }

        await FlushCommandsAsync(connection, transaction, commands, ct);
        await UpsertAnchorsAsync(connection, transaction, anchors, ct);
        var duplicateRows = deduplicator?.DuplicateCount ?? 0;
        return new StagingSourceReport(SourceName(sourceKind), sourceRows, stagedRows, duplicateRows, blankOemRows, issueRows);
    }

    private static NpgsqlBatchCommand BuildRawCommand(long batchId, StagingSourceKind sourceKind, StagingRow row)
    {
        var (sql, values) = sourceKind switch
        {
            StagingSourceKind.ProductSpecs => (
                "INSERT INTO staging.product_specs_raw (batch_id, source_row_no, oem_no_1_raw, oem_no_1_normalized, product_name_1, remark, row_hash, raw_payload) VALUES ($1,$2,$3,$4,$5,$6,$7,$8)",
                new object?[] { batchId, row.SourceRowNo, row.OemNo1Raw, row.OemNo1Normalized, Field(row, "product_name_1"), Field(row, "remark"), row.RowHash, JsonSerializer.Serialize(row.Fields) }),
            StagingSourceKind.OemNumbers => (
                "INSERT INTO staging.oem_numbers_raw (batch_id, source_row_no, oem_no_1_raw, oem_no_1_normalized, product_name_1, oem_brand, oem_no_3, row_hash, raw_payload) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9)",
                new object?[] { batchId, row.SourceRowNo, row.OemNo1Raw, row.OemNo1Normalized, Field(row, "product_name_1"), Field(row, "oem_brand"), Field(row, "oem_no_3"), row.RowHash, JsonSerializer.Serialize(row.Fields) }),
            _ => (
                "INSERT INTO staging.applications_raw (batch_id, source_row_no, oem_no_1_raw, oem_no_1_normalized, machine_brand, machine_model, product_name_2, product_name_1, model_name, engine_brand, engine_type, engine_energy, production_date, power, engine_model, row_hash, raw_payload) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17)",
                new object?[] { batchId, row.SourceRowNo, row.OemNo1Raw, row.OemNo1Normalized, Field(row, "name"), Field(row, "machine_model"), Field(row, "product_name_2"), Field(row, "product_name_1"), Field(row, "model_name"), Field(row, "engine_brand"), Field(row, "engine_type"), Field(row, "engine_energy"), Field(row, "production_date"), Field(row, "power"), Field(row, "engine_model"), row.RowHash, JsonSerializer.Serialize(row.Fields) })
        };
        var command = new NpgsqlBatchCommand(sql);
        for (var i = 0; i < values.Length; i++)
        {
            var parameter = new NpgsqlParameter { Value = values[i] ?? DBNull.Value };
            if (i == values.Length - 1)
                parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    private static NpgsqlBatchCommand BuildIssueCommand(long batchId, StagingSourceKind sourceKind, StagingRow row)
    {
        var command = new NpgsqlBatchCommand(@"
            INSERT INTO staging.validation_issues
                (batch_id, source_kind, source_row_no, issue_code, issue_message, raw_payload)
            VALUES ($1, $2, $3, $4, $5, $6)");
        command.Parameters.AddWithValue(batchId);
        command.Parameters.AddWithValue(SourceName(sourceKind));
        command.Parameters.AddWithValue(row.SourceRowNo);
        command.Parameters.AddWithValue("OEM_NO_1_EMPTY");
        command.Parameters.AddWithValue("OEM NO 1 为空，保留原始行等待人工处理");
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = JsonSerializer.Serialize(row.Fields) });
        return command;
    }

    private static async Task FlushCommandsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, List<NpgsqlBatchCommand> commands, CancellationToken ct)
    {
        if (commands.Count == 0) return;
        await using var batch = new NpgsqlBatch(connection, transaction);
        foreach (var command in commands) batch.BatchCommands.Add(command);
        await batch.ExecuteNonQueryAsync(ct);
        commands.Clear();
    }

    private static async Task UpsertAnchorsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Dictionary<string, AnchorAccumulator> anchors, CancellationToken ct)
    {
        var commands = new List<NpgsqlBatchCommand>(BatchCommandLimit);
        foreach (var (normalized, anchor) in anchors)
        {
            var command = new NpgsqlBatchCommand(@"
                INSERT INTO staging.oem_anchors
                    (oem_no_1_normalized, oem_no_1_display, specs_row_count, oem_numbers_row_count, applications_row_count)
                VALUES ($1,$2,$3,$4,$5)
                ON CONFLICT (oem_no_1_normalized) DO UPDATE SET
                    oem_no_1_display = EXCLUDED.oem_no_1_display,
                    specs_row_count = staging.oem_anchors.specs_row_count + EXCLUDED.specs_row_count,
                    oem_numbers_row_count = staging.oem_anchors.oem_numbers_row_count + EXCLUDED.oem_numbers_row_count,
                    applications_row_count = staging.oem_anchors.applications_row_count + EXCLUDED.applications_row_count,
                    last_seen_at = now()");
            command.Parameters.AddWithValue(normalized);
            command.Parameters.AddWithValue(anchor.Display);
            command.Parameters.AddWithValue(anchor.SpecsRows);
            command.Parameters.AddWithValue(anchor.OemNumbersRows);
            command.Parameters.AddWithValue(anchor.ApplicationRows);
            commands.Add(command);
            if (commands.Count >= BatchCommandLimit)
                await FlushCommandsAsync(connection, transaction, commands, ct);
        }
        await FlushCommandsAsync(connection, transaction, commands, ct);
    }

    private static async Task UpdateBatchAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long batchId, IReadOnlyList<StagingSourceReport> reports, long issueCount, string status, string? error, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(@"
            UPDATE staging.import_batches SET
                status = $2,
                specs_source_rows = $3,
                oem_numbers_source_rows = $4,
                applications_source_rows = $5,
                specs_staged_rows = $6,
                oem_numbers_staged_rows = $7,
                applications_staged_rows = $8,
                duplicate_rows = $9,
                blank_oem_rows = $10,
                issue_count = $11,
                error_message = $12,
                finished_at = now()
            WHERE id = $1", connection, transaction);
        var specs = reports.FirstOrDefault(x => x.SourceKind == "specs");
        var oems = reports.FirstOrDefault(x => x.SourceKind == "oem-numbers");
        var apps = reports.FirstOrDefault(x => x.SourceKind == "applications");
        object?[] values = { batchId, status, specs?.SourceRows ?? 0, oems?.SourceRows ?? 0, apps?.SourceRows ?? 0, specs?.StagedRows ?? 0, oems?.StagedRows ?? 0, apps?.StagedRows ?? 0, reports.Sum(x => x.DuplicateRows), reports.Sum(x => x.BlankOemRows), issueCount, error };
        foreach (var value in values) command.Parameters.AddWithValue(value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task MarkBatchFailedAsync(NpgsqlConnection connection, long batchId, string error, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("UPDATE staging.import_batches SET status = 'failed', error_message = $2, finished_at = now() WHERE id = $1", connection);
        command.Parameters.AddWithValue(batchId);
        command.Parameters.AddWithValue(error.Length > 4000 ? error[..4000] : error);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string? Field(StagingRow row, string key) => row.Fields.TryGetValue(key, out var value) ? value : null;
    private static string SourceName(StagingSourceKind kind) => kind switch { StagingSourceKind.ProductSpecs => "specs", StagingSourceKind.OemNumbers => "oem-numbers", _ => "applications" };

    private sealed class AnchorAccumulator
    {
        public AnchorAccumulator(string display) => Display = display;
        public string Display { get; }
        public long SpecsRows { get; private set; }
        public long OemNumbersRows { get; private set; }
        public long ApplicationRows { get; private set; }
        public void Increment(StagingSourceKind kind)
        {
            if (kind == StagingSourceKind.ProductSpecs) SpecsRows++;
            else if (kind == StagingSourceKind.OemNumbers) OemNumbersRows++;
            else ApplicationRows++;
        }
    }
}
