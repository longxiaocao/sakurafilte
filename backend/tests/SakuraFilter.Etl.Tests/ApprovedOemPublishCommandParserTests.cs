using FluentAssertions;
using SakuraFilter.Etl.Staging;
using Xunit;

namespace SakuraFilter.Etl.Tests;

public class ApprovedOemPublishCommandParserTests
{
    [Fact]
    public void Parse_ReturnsDryRunOptions_ForValidArguments()
    {
        var result = ApprovedOemPublishCommandParser.Parse(new[]
        {
            "--batch-id", "12",
            "--pg-conn", "Host=127.0.0.1;Database=test"
        });

        result.Error.Should().BeNull();
        result.Options.Should().BeEquivalentTo(new ApprovedOemPublishOptions(
            12, "Host=127.0.0.1;Database=test", false));
    }

    [Fact]
    public void Parse_RejectsMissingBatchId()
    {
        var result = ApprovedOemPublishCommandParser.Parse(new[]
        {
            "--pg-conn", "Host=127.0.0.1;Database=test"
        });

        result.Options.Should().BeNull();
        result.Error.Should().Be("缺少 --batch-id <positive-bigint>");
    }

    [Fact]
    public void Parse_RejectsUnknownArgument()
    {
        var result = ApprovedOemPublishCommandParser.Parse(new[]
        {
            "--batch-id", "12",
            "--pg-conn", "Host=127.0.0.1;Database=test",
            "--force"
        });

        result.Options.Should().BeNull();
        result.Error.Should().Be("未知参数: --force");
    }

    [Fact]
    public void Parse_RequiresExplicitApplyFlag()
    {
        var result = ApprovedOemPublishCommandParser.Parse(new[]
        {
            "--batch-id", "12",
            "--pg-conn", "Host=127.0.0.1;Database=test",
            "--apply"
        });

        result.Error.Should().BeNull();
        result.Options!.Apply.Should().BeTrue();
    }
}
