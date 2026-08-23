using System.Text.Json;
using ClosedXML.Excel;
using FluentAssertions;
using SakuraFilter.Etl;
using Xunit;

namespace SakuraFilter.Etl.Tests;

/// <summary>
/// 项目规划V2 XLSX 适配器测试。
/// </summary>
public class EtlSpreadsheetAdapterTests
{
    /// <summary>
    /// 覆盖: 项目规划V2 分区3/5 - XLSX 特殊字符值应同时保留原值，供后续 ETL 解析数值检索列。
    /// </summary>
    [Fact]
    public async Task ConvertAsync_Products_PreservesRawSpecialParameterValues()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"sakurafilter-adapter-{Guid.NewGuid():N}.xlsx");
        string? outputPath = null;
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("products");
                sheet.Cell(1, 1).Value = "MR.1";
                sheet.Cell(1, 2).Value = "OEM 2";
                sheet.Cell(1, 3).Value = "No. Check Valves";
                sheet.Cell(1, 4).Value = "Bypass Pressure";
                sheet.Cell(2, 1).Value = "MRX001";
                sheet.Cell(2, 2).Value = "OEM-X-001";
                sheet.Cell(2, 3).Value = "1/2";
                sheet.Cell(2, 4).Value = "1.2 bar";
                workbook.SaveAs(sourcePath);
            }

            outputPath = await EtlSpreadsheetAdapter.ConvertAsync(sourcePath, "products", CancellationToken.None);
            var json = await File.ReadAllTextAsync(outputPath);
            using var doc = JsonDocument.Parse(json);

            doc.RootElement.GetProperty("no_check_valves").GetString().Should().Be("1/2");
            doc.RootElement.GetProperty("no_check_valves_raw").GetString().Should().Be("1/2");
            doc.RootElement.GetProperty("bypass_pressure").GetString().Should().Be("1.2 bar");
            doc.RootElement.GetProperty("bypass_pressure_raw").GetString().Should().Be("1.2 bar");
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
            if (!string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
}
