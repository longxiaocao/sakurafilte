using ClosedXML.Excel;
using FluentAssertions;
using SakuraFilter.Etl.Staging;
using Xunit;

namespace SakuraFilter.Etl.Tests;

public class StagingWorkbookReaderTests
{
    [Fact]
    public async Task ReadAsync_KeepsDifferentApplicationsUnderSameOemAsSeparateRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"staging-reader-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Sheet1");
                sheet.Cell(1, 1).Value = "OEM NO 1";
                sheet.Cell(1, 2).Value = "Machine Model";
                sheet.Cell(1, 3).Value = "productname1";
                sheet.Cell(1, 4).Value = "Engine brand";
                sheet.Cell(1, 5).Value = "Custom Field";
                sheet.Cell(2, 1).Value = " sh  56212 ";
                sheet.Cell(2, 2).Value = "ENVIRO 200";
                sheet.Cell(2, 3).Value = "Hydraulic Filter";
                sheet.Cell(2, 4).Value = "Cummins";
                sheet.Cell(2, 5).Value = "A";
                sheet.Cell(3, 1).Value = "SH 56212";
                sheet.Cell(3, 2).Value = "ENVIRO 300";
                sheet.Cell(3, 3).Value = "Hydraulic Filter";
                sheet.Cell(3, 4).Value = "Scania";
                sheet.Cell(3, 5).Value = "B";
                workbook.SaveAs(path);
            }

            var rows = new List<StagingRow>();
            await foreach (var row in StagingWorkbookReader.ReadAsync(path, StagingSourceKind.Applications))
                rows.Add(row);

            rows.Should().HaveCount(2);
            rows.Select(x => x.OemNo1Normalized).Distinct().Should().ContainSingle("SH 56212");
            rows.Select(x => x.Fields["machine_model"]).Should().BeEquivalentTo("ENVIRO 200", "ENVIRO 300");
            rows.Select(x => x.Fields["custom_field"]).Should().BeEquivalentTo("A", "B");
            rows.Select(x => x.RowHash).Distinct().Should().HaveCount(2);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAsync_RetainsNonEmptyRowsWithBlankOemAsValidationCandidates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"staging-reader-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Sheet1");
                sheet.Cell(1, 1).Value = "OEM NO.1";
                sheet.Cell(1, 2).Value = "Machine Model";
                sheet.Cell(2, 1).Value = "";
                sheet.Cell(2, 2).Value = "UNKNOWN";
                workbook.SaveAs(path);
            }

            var rows = new List<StagingRow>();
            await foreach (var item in StagingWorkbookReader.ReadAsync(path, StagingSourceKind.Applications))
                rows.Add(item);
            var row = rows.Single();

            row.OemNo1Normalized.Should().BeNull();
            row.Fields["machine_model"].Should().Be("UNKNOWN");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
