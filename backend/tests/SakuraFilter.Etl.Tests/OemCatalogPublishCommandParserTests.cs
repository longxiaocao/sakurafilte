using FluentAssertions;
using SakuraFilter.Etl.Staging;
using Xunit;

namespace SakuraFilter.Etl.Tests;

public class OemCatalogPublishCommandParserTests
{
    [Fact]
    public void PublishService_UsesUnlimitedTimeout_ForLargeCatalogBatch()
    {
        OemCatalogPublishService.PublishCommandTimeoutSeconds.Should().Be(0);
    }

    [Fact]
    public void Parse_ReturnsOptions_ForValidArguments()
    {
        var result = OemCatalogPublishCommandParser.Parse(new[]
        {
            "--batch-id", "12",
            "--pg-conn", "Host=127.0.0.1;Database=test"
        });

        result.Error.Should().BeNull();
        result.Options.Should().BeEquivalentTo(
            new OemCatalogPublishOptions(12, "Host=127.0.0.1;Database=test"));
    }

    [Fact]
    public void Parse_RejectsMissingBatchId()
    {
        var result = OemCatalogPublishCommandParser.Parse(new[]
        {
            "--pg-conn", "Host=127.0.0.1;Database=test"
        });

        result.Options.Should().BeNull();
        result.Error.Should().Be("缺少 --batch-id <positive-bigint>");
    }

    [Fact]
    public void Parse_RejectsUnknownArgument()
    {
        var result = OemCatalogPublishCommandParser.Parse(new[]
        {
            "--batch-id", "12",
            "--pg-conn", "Host=127.0.0.1;Database=test",
            "--apply"
        });

        result.Options.Should().BeNull();
        result.Error.Should().Be("未知参数: --apply");
    }
}
