using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

namespace SakuraFilter.Etl;

/// <summary>
/// 将项目规划V2约定的 XLSX 工作表转换为既有 JSONL ETL 契约。
/// WHY: 复用已验证的分批写入、幂等、进度和 Meili 索引逻辑，避免维护第二条导入管线。
/// </summary>
public static class EtlSpreadsheetAdapter
{
    private static readonly IReadOnlyDictionary<string, string[]> SheetNames =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["products"] = new[] { "products", "产品区", "Sheet1" },
            ["xrefs"] = new[] { "xrefs", "oem区", "OEM区", "Sheet1" },
            ["apps"] = new[] { "apps", "机型区", "Sheet1" }
        };

    private static readonly IReadOnlyDictionary<string, string> HeaderMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mr1"] = "mr_1", ["mr.1"] = "mr_1", ["mr_1"] = "mr_1",
            // OEM NO.1 = oem_2 (客户 Excel 实际命名，非系统设计)
            ["oemno1"] = "oem_2", ["oem1"] = "oem_2", ["oem_no_1"] = "oem_2",
            ["oemno2"] = "oem_2", ["oem2"] = "oem_2", ["oem_no_2"] = "oem_2",
            ["oemno3"] = "oem_no_3", ["oem3"] = "oem_no_3", ["oem_no_3"] = "oem_no_3",
            ["oembrand"] = "oem_brand", ["productname1"] = "product_name_1",
            ["productname2"] = "product_name_2", ["productname3"] = "product_name_3",
            ["machinebrand"] = "machine_brand", ["machinemodel"] = "machine_model",
            ["modelname"] = "model_name", ["enginebrand"] = "engine_brand",
            ["enginetype"] = "engine_type", ["engineenergy"] = "engine_energy",
            ["machinetype"] = "machine_type", ["machinecategory"] = "machine_category",
            ["dimension1d1"] = "d1_mm", ["dimension2d2"] = "d2_mm", ["dimension3d3"] = "d3_mm", ["dimension4d4"] = "d4_mm",
            ["height1h1"] = "h1_mm", ["height2h2"] = "h2_mm", ["height3h3"] = "h3_mm", ["height4h4"] = "h4_mm",
            ["thread1d7"] = "d7_thread", ["thread2d8"] = "d8_thread"
            , ["nocheckvalves"] = "no_check_valves", ["nobypassvalves"] = "no_bypass_valves"
            , ["bypassvalvesettinglr"] = "bypass_valve_lr", ["bypassvalvesettinghr"] = "bypass_valve_hr"
            , ["bypasspressure"] = "bypass_pressure", ["collapsepressure"] = "collapse_pressure_bar"
            // 🔧 fix(数据导入漏项): 客户原始文件 data0827.xlsx 的参数列名 (D1/D2/D3/H1/D7/H2/H3/...),
            //   此前不在 HeaderMap → 转成未映射的 json key "d1/h1/..." → EtlImportService 读 d1_mm 得 NULL,
            //   导致尺寸/参数列整体静默丢失。这里把这类列名归一化后 (MapHeader 会去非字母数字) 显式映射到规范字段。
            , ["d1"] = "d1_mm", ["d2"] = "d2_mm", ["d3"] = "d3_mm", ["d4"] = "d4_mm"
            , ["h1"] = "h1_mm", ["h2"] = "h2_mm", ["h3"] = "h3_mm", ["h4"] = "h4_mm"
            , ["d7"] = "d7_thread", ["d8"] = "d8_thread"
            , ["type"] = "type", ["media"] = "media"
            , ["efficiency1"] = "efficiency_1", ["efficiency2"] = "efficiency_2"
            , ["sealmaterial"] = "sealing_material", ["temperaturerange"] = "temp_range"
            , ["remark"] = "remark"
            , ["outerdiameter"] = "d1_mm", ["innerdiameter"] = "d2_mm"
        };

    public static async Task<string> ConvertAsync(string sourcePath, string entityType, CancellationToken ct)
    {
        if (!Path.GetExtension(sourcePath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return sourcePath;

        var normalizedEntity = entityType.Trim().ToLowerInvariant() switch
        {
            "product" => "products", "xref" or "cross_references" => "xrefs",
            "machine_applications" => "apps", var value => value
        };
        if (!SheetNames.ContainsKey(normalizedEntity))
            throw new ArgumentException("EntityType 必须是 products/xrefs/apps");

        var outputDir = Path.Combine(Path.GetTempPath(), "sakurafilter-etl");
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, $"{Path.GetFileNameWithoutExtension(sourcePath)}-{normalizedEntity}-{Guid.NewGuid():N}.jsonl");

        using var workbook = new XLWorkbook(sourcePath);
        var sheet = SheetNames[normalizedEntity]
            .Select(name => workbook.Worksheets.FirstOrDefault(ws => string.Equals(ws.Name, name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(ws => ws != null)
            ?? throw new ArgumentException($"未找到 {normalizedEntity} 工作表，可用名称: {string.Join("/", SheetNames[normalizedEntity])}");
        var headerRow = sheet.FirstRowUsed() ?? throw new ArgumentException("XLSX 缺少表头行");
        var headers = headerRow.CellsUsed().ToDictionary(
            cell => cell.Address.ColumnNumber,
            cell => MapHeader(cell.GetString(), normalizedEntity),
            EqualityComparer<int>.Default);

        await using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            ct.ThrowIfCancellationRequested();
            var record = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                if (header.Value == null) continue;
                var value = row.Cell(header.Key).GetFormattedString().Trim();
                if (value.Length > 0) record[header.Value] = value;
            }
            if (record.Count == 0) continue;
            ApplyCompatibilityFields(record, normalizedEntity);
            await writer.WriteLineAsync(JsonSerializer.Serialize(record));
        }
        return outputPath;
    }

    private static string? MapHeader(string value, string entityType)
    {
        var normalized = new string(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        // Apps 专用映射优先: engine_brand 列名在客户 Excel 中实际表示 machine_brand
        if (entityType == "apps" && normalized == "enginebrand") return "machine_brand";
        if (HeaderMap.TryGetValue(normalized, out var mapped)) return mapped;
        return normalized.Replace(" ", "_");
    }

    private static void ApplyCompatibilityFields(Dictionary<string, string?> record, string entityType)
    {
        if (entityType == "products")
        {
            record.TryAdd("oem_no_normalized", record.GetValueOrDefault("mr_1") ?? record.GetValueOrDefault("oem_2"));
            record.TryAdd("oem_no_display", record.GetValueOrDefault("oem_2") ?? record.GetValueOrDefault("mr_1"));
            record.TryAdd("type", "others");
            PreserveRawValue(record, "no_check_valves");
            PreserveRawValue(record, "no_bypass_valves");
            PreserveRawValue(record, "bypass_valve_lr");
            PreserveRawValue(record, "bypass_valve_hr");
            PreserveRawValue(record, "bypass_pressure");
            PreserveRawValue(record, "collapse_pressure_bar");
            // 🔧 fix(数据导入漏项): 尺寸列同样保留原文 (如 "178.0 mm" → d1_mm=178.00 + d1_mm_raw="178.0 mm")，
            //   便于溯源且 GetDecimalOrNull 可成功剥单位解析。
            PreserveRawValue(record, "d1_mm");
            PreserveRawValue(record, "d2_mm");
            PreserveRawValue(record, "d3_mm");
            PreserveRawValue(record, "d4_mm");
            PreserveRawValue(record, "h1_mm");
            PreserveRawValue(record, "h2_mm");
            PreserveRawValue(record, "h3_mm");
            PreserveRawValue(record, "h4_mm");
        }
    }

    private static void PreserveRawValue(Dictionary<string, string?> record, string numericField)
    {
        if (record.TryGetValue(numericField, out var value) && !string.IsNullOrWhiteSpace(value))
            record.TryAdd($"{numericField}_raw", value);
    }
}
