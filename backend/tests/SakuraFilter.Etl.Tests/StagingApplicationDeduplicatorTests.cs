using FluentAssertions;
using SakuraFilter.Etl.Staging;
using Xunit;

namespace SakuraFilter.Etl.Tests;

public class StagingApplicationDeduplicatorTests
{
    [Fact]
    public void TryAccept_ReturnsFalseOnlyForRepeatedExactRowHash()
    {
        var deduplicator = new StagingApplicationDeduplicator();

        deduplicator.TryAccept("hash-1").Should().BeTrue();
        deduplicator.TryAccept("hash-2").Should().BeTrue();
        deduplicator.TryAccept("hash-1").Should().BeFalse();
        deduplicator.AcceptedCount.Should().Be(2);
        deduplicator.DuplicateCount.Should().Be(1);
    }
}
