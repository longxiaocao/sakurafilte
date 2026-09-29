using FluentAssertions;
using SakuraFilter.Etl.Staging;
using Xunit;

namespace SakuraFilter.Etl.Tests;

public class StagingImportCommandParserTests
{
    [Fact]
    public void Parse_RequiresAllThreeExcelFiles()
    {
        var result = StagingImportCommandParser.Parse(new[]
        {
            "--specs", "specs.xlsx",
            "--oem-numbers", "oem.xlsx",
            "--pg-conn", "Host=localhost"
        });

        result.Options.Should().BeNull();
        result.Error.Should().Contain("--applications");
    }

    [Fact]
    public void Parse_ReturnsOptionsAndDryRun()
    {
        var result = StagingImportCommandParser.Parse(new[]
        {
            "--specs", "specs.xlsx",
            "--oem-numbers", "oem.xlsx",
            "--applications", "apps.xlsx",
            "--pg-conn", "Host=localhost",
            "--dry-run"
        });

        result.Error.Should().BeNull();
        result.Options.Should().BeEquivalentTo(new StagingImportOptions(
            "specs.xlsx", "oem.xlsx", "apps.xlsx", "Host=localhost", true));
    }
}
