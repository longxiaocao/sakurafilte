using FluentAssertions;
using SakuraFilter.Etl.Staging;
using Xunit;

namespace SakuraFilter.Etl.Tests;

public class ApprovedOemPublishServiceTests
{
    [Fact]
    public void PreflightReport_IsNotReady_WhenNoMappingsAreApproved()
    {
        var report = new ApprovedOemPublishPreflightReport(
            1, 0, 0, 0, 0, 0, 0, 0);

        report.IsReadyToApply.Should().BeFalse();
        report.HasNoApprovedMappings.Should().BeTrue();
    }

    [Fact]
    public void PreflightReport_IsNotReady_WhenAnyTargetIsBlocked()
    {
        var report = new ApprovedOemPublishPreflightReport(
            1, 2, 1, 1, 3, 4, 5, 0);

        report.IsReadyToApply.Should().BeFalse();
        report.HasNoApprovedMappings.Should().BeFalse();
    }

    [Fact]
    public void PreflightReport_IsReady_WhenEveryApprovedMappingResolves()
    {
        var report = new ApprovedOemPublishPreflightReport(
            1, 2, 2, 0, 3, 4, 5, 0);

        report.IsReadyToApply.Should().BeTrue();
    }
}
