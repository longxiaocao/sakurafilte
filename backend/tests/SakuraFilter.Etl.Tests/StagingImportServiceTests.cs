using ClosedXML.Excel;
using FluentAssertions;
using SakuraFilter.Etl.Staging;
using Xunit;

namespace SakuraFilter.Etl.Tests;

public class StagingImportServiceTests
{
    [Fact]
    public async Task DryRun_ReportsRowsAndExactApplicationDuplicatesWithoutOpeningDatabase()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"staging-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var specs = Path.Combine(dir, "specs.xlsx");
        var oems = Path.Combine(dir, "oems.xlsx");
        var apps = Path.Combine(dir, "apps.xlsx");
        try
        {
            CreateWorkbook(specs, new[] { "OEM NO.1", "Product Name 1" }, new[] { "SH 56212", "Hydraulic Filter" });
            CreateWorkbook(oems, new[] { "OEM NO.1", "OEM Brand", "Oem NO.3" }, new[] { "SH 56212", "Brand A", "A-1" });
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Sheet1");
                sheet.Cell(1, 1).Value = "OEM NO 1";
                sheet.Cell(1, 2).Value = "Machine Model";
                sheet.Cell(2, 1).Value = "SH 56212";
                sheet.Cell(2, 2).Value = "ENVIRO 200";
                sheet.Cell(3, 1).Value = "SH 56212";
                sheet.Cell(3, 2).Value = "ENVIRO 200";
                sheet.Cell(4, 1).Value = "SH 56212";
                sheet.Cell(4, 2).Value = "ENVIRO 300";
                workbook.SaveAs(apps);
            }

            var report = await new StagingImportService().ImportAsync(
                new StagingImportOptions(specs, oems, apps, "Host=unused", DryRun: true));

            report.BatchId.Should().Be(0);
            report.Sources.Should().ContainSingle(x => x.SourceKind == "specs" && x.SourceRows == 1 && x.StagedRows == 1);
            report.Sources.Should().ContainSingle(x => x.SourceKind == "oem-numbers" && x.SourceRows == 1 && x.StagedRows == 1);
            report.Sources.Should().ContainSingle(x => x.SourceKind == "applications" && x.SourceRows == 3 && x.StagedRows == 2 && x.DuplicateRows == 1);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private static void CreateWorkbook(string path, string[] headers, string[] row)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, i + 1).Value = row[i];
        }
        workbook.SaveAs(path);
    }
}
