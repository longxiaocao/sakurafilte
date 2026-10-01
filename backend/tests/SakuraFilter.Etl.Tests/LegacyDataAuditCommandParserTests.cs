using FluentAssertions;
using SakuraFilter.Etl.Staging;
using Xunit;

namespace SakuraFilter.Etl.Tests;

public class LegacyDataAuditCommandParserTests
{
    [Fact]
    public void Parse_ReturnsOptions_ForConnectionString()
    {
        var result = LegacyDataAuditCommandParser.Parse(new[]
        {
            "--pg-conn", "Host=127.0.0.1;Database=test"
        });

        result.Error.Should().BeNull();
        result.Options.Should().BeEquivalentTo(new LegacyDataAuditOptions("Host=127.0.0.1;Database=test"));
    }

    [Fact]
    public void Parse_ReturnsError_WhenConnectionStringIsMissing()
    {
        var result = LegacyDataAuditCommandParser.Parse(Array.Empty<string>());

        result.Options.Should().BeNull();
        result.Error.Should().Be("缺少 --pg-conn <connection-string>");
    }
}
