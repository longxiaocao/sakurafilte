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

    /// <summary>
    /// 覆盖: 客户原始文件 data0827.xlsx 的列名 (D1/D2/H1/D7/Media/...) 应映射到规范字段，
    ///   修复"尺寸列因列名不在 HeaderMap 而被整列静默丢弃"的漏项问题。
    /// </summary>
    [Fact]
    public async Task ConvertAsync_Products_MapsCustomerRawDimensionHeaders()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"sakurafilter-adapter-dim-{Guid.NewGuid():N}.xlsx");
        string? outputPath = null;
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("products");
                sheet.Cell(1, 1).Value = "OEM NO.1";
                sheet.Cell(1, 2).Value = "Product Name 1";
                sheet.Cell(1, 3).Value = "D1";
                sheet.Cell(1, 4).Value = "D2";
                sheet.Cell(1, 5).Value = "H1";
                sheet.Cell(1, 6).Value = "D7";
                sheet.Cell(1, 7).Value = "Media";
                sheet.Cell(1, 8).Value = "Temperature Range";
                sheet.Cell(2, 1).Value = "SH 64999";
                sheet.Cell(2, 2).Value = "Return Filter";
                sheet.Cell(2, 3).Value = "202.5 mm";
                sheet.Cell(2, 4).Value = "42.0 mm";
                sheet.Cell(2, 5).Value = "300 mm";
                sheet.Cell(2, 6).Value = "3/4\"-16UNF";
                sheet.Cell(2, 7).Value = "Cellulose";
                sheet.Cell(2, 8).Value = "-30 - +100°C";
                workbook.SaveAs(sourcePath);
            }

            outputPath = await EtlSpreadsheetAdapter.ConvertAsync(sourcePath, "products", CancellationToken.None);
            var json = await File.ReadAllTextAsync(outputPath);
            using var doc = JsonDocument.Parse(json);

            doc.RootElement.GetProperty("d1_mm").GetString().Should().Be("202.5 mm");
            doc.RootElement.GetProperty("d1_mm_raw").GetString().Should().Be("202.5 mm");
            doc.RootElement.GetProperty("d2_mm").GetString().Should().Be("42.0 mm");
            doc.RootElement.GetProperty("h1_mm").GetString().Should().Be("300 mm");
            doc.RootElement.GetProperty("h1_mm_raw").GetString().Should().Be("300 mm");
            doc.RootElement.GetProperty("d7_thread").GetString().Should().Be("3/4\"-16UNF");
            doc.RootElement.GetProperty("media").GetString().Should().Be("Cellulose");
            doc.RootElement.GetProperty("temp_range").GetString().Should().Be("-30 - +100°C");
            doc.RootElement.GetProperty("oem_no_display").GetString().Should().Be("SH 64999");
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
            if (!string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
}
