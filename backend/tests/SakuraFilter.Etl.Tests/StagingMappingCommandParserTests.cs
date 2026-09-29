using FluentAssertions;
using SakuraFilter.Etl.Staging;
using Xunit;

namespace SakuraFilter.Etl.Tests;

public class StagingMappingCommandParserTests
{
    [Fact]
    public void ParseRefresh_ReturnsOptions_ForValidArguments()
    {
        var result = StagingMappingCommandParser.ParseRefresh(new[]
        {
            "--batch-id", "12",
            "--pg-conn", "Host=127.0.0.1;Database=test"
        });

        result.Error.Should().BeNull();
        result.Options.Should().BeEquivalentTo(new StagingMappingRefreshOptions(12, "Host=127.0.0.1;Database=test"));
    }

    [Fact]
    public void ParseReview_RejectsApprovedReviewWithoutMr1()
    {
        var result = StagingMappingCommandParser.ParseReview(new[]
        {
            "--batch-id", "12",
            "--oem-no-1", "SH 51281 V",
            "--status", "approved",
            "--reviewed-by", "tester",
            "--pg-conn", "Host=127.0.0.1;Database=test"
        });

        result.Options.Should().BeNull();
        result.Error.Should().Be("批准映射必须提供 --mr1 <MR.1>");
    }

    [Fact]
    public void ParseReview_ReturnsApprovedOptions_WithMr1()
    {
        var result = StagingMappingCommandParser.ParseReview(new[]
        {
            "--batch-id", "12",
            "--oem-no-1", "SH 51281 V",
            "--status", "approved",
            "--mr1", "MR51281",
            "--reviewed-by", "tester",
            "--pg-conn", "Host=127.0.0.1;Database=test"
        });

        result.Error.Should().BeNull();
        result.Options.Should().BeEquivalentTo(new StagingMappingReviewOptions(
            12, "SH 51281 V", "approved", "MR51281", null, null, "tester", "Host=127.0.0.1;Database=test"));
    }
}
