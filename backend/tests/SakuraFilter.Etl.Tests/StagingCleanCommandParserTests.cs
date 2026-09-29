using FluentAssertions;
using SakuraFilter.Etl.Staging;
using Xunit;

namespace SakuraFilter.Etl.Tests;

public class StagingCleanCommandParserTests
{
    [Fact]
    public void Parse_ReturnsOptions_ForValidRequiredArguments()
    {
        var result = StagingCleanCommandParser.Parse(new[]
        {
            "--batch-id", "12",
            "--pg-conn", "Host=127.0.0.1;Database=test"
        });

        result.Error.Should().BeNull();
        result.Options.Should().BeEquivalentTo(new StagingCleanOptions(12, "Host=127.0.0.1;Database=test"));
    }

    [Fact]
    public void Parse_ReturnsError_WhenBatchIdIsMissing()
    {
        var result = StagingCleanCommandParser.Parse(new[]
        {
            "--pg-conn", "Host=127.0.0.1;Database=test"
        });

        result.Options.Should().BeNull();
        result.Error.Should().Be("缺少 --batch-id <positive-bigint>");
    }
}
