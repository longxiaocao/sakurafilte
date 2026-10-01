using System.Runtime.CompilerServices;
using ClosedXML.Excel;

namespace SakuraFilter.Etl.Staging;

/// <summary>
/// 将 Excel 原始表逐行转换为 staging 行。
/// WHY: staging 阶段只负责解析和记录，不按 OEM NO 1 合并，避免丢失车型应用明细。
/// </summary>
public static class StagingWorkbookReader
{
    private static readonly IReadOnlyDictionary<string, string> KnownHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["oemno1"] = "oem_no_1",
            ["oemno01"] = "oem_no_1",
            ["name"] = "name",
            ["machinemodel"] = "machine_model",
            ["productname1"] = "product_name_1",
            ["productname2"] = "product_name_2",
            ["modelname"] = "model_name",
            ["enginebrand"] = "engine_brand",
            ["enginetype"] = "engine_type",
            ["engineenergy"] = "engine_energy",
            ["productiondate"] = "production_date",
            ["power"] = "power",
            ["enginemodel"] = "engine_model",
            ["oembrand"] = "oem_brand",
            ["oemno3"] = "oem_no_3",
            ["remark"] = "remark"
        };

    public static async IAsyncEnumerable<StagingRow> ReadAsync(
        string sourcePath,
        StagingSourceKind sourceKind,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("找不到 Excel 文件", sourcePath);

        await Task.CompletedTask;

        using var workbook = new XLWorkbook(sourcePath);
        var sheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidDataException("XLSX 缺少工作表");
        var headerRow = sheet.FirstRowUsed()
            ?? throw new InvalidDataException("XLSX 缺少表头行");

        var headers = headerRow.CellsUsed()
            .ToDictionary(cell => cell.Address.ColumnNumber, cell => NormalizeHeader(cell.GetString()));
        if (headers.Count == 0 || !headers.Values.Contains("oem_no_1", StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("XLSX 缺少 OEM NO 1 列");

        var oemColumn = headers.First(pair => pair.Value.Equals("oem_no_1", StringComparison.OrdinalIgnoreCase)).Key;
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = headers
                .OrderBy(pair => pair.Key)
                .Select(pair => NullIfBlank(row.Cell(pair.Key).GetFormattedString()))
                .ToArray();
            if (values.All(value => value is null)) continue;

            var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var orderedKeys = headers.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
            for (var i = 0; i < orderedKeys.Length; i++)
                fields[orderedKeys[i]] = values[i];

            var oemRaw = NullIfBlank(row.Cell(oemColumn).GetFormattedString());
            yield return new StagingRow(
                sourceKind,
                row.RowNumber(),
                oemRaw,
                StagingKeyNormalizer.NormalizeOemNo1(oemRaw),
                fields,
                StagingKeyNormalizer.ComputeRowHash(values));

        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeHeader(string value)
    {
        var compact = new string(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (KnownHeaders.TryGetValue(compact, out var mapped)) return mapped;

        var result = new System.Text.StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) result.Append(ch);
            else if (result.Length > 0 && result[^1] != '_') result.Append('_');
        }
        return result.ToString().Trim('_');
    }
}
