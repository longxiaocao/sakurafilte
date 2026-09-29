using FluentAssertions;
using SakuraFilter.Etl.Staging;
using Xunit;

namespace SakuraFilter.Etl.Tests;

public class StagingKeyNormalizerTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  sh   56212 ", "SH 56212")]
    [InlineData("sh 56212", "SH 56212")]
    public void NormalizeOemNo1_FoldsWhitespaceAndCase(string? input, string? expected)
    {
        StagingKeyNormalizer.NormalizeOemNo1(input).Should().Be(expected);
    }

    [Fact]
    public void ComputeRowHash_IsStableForSameOrderedValues()
    {
        var first = StagingKeyNormalizer.ComputeRowHash(new[] { "SH 56212", "Bus", null });
        var second = StagingKeyNormalizer.ComputeRowHash(new[] { "SH 56212", "Bus", null });

        first.Should().Be(second).And.HaveLength(64);
    }

    [Fact]
    public void ComputeRowHash_DiffersWhenAnyValueChanges()
    {
        var first = StagingKeyNormalizer.ComputeRowHash(new[] { "SH 56212", "Bus" });
        var second = StagingKeyNormalizer.ComputeRowHash(new[] { "SH 56212", "Truck" });

        first.Should().NotBe(second);
    }
}
