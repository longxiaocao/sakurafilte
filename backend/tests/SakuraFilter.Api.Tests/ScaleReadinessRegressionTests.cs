using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SakuraFilter.Api.Extensions;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Search;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// 百万级索引瘦身后的关键回归：转发头限流安全与机型字段回填完整性。
/// </summary>
public class ScaleReadinessRegressionTests
{
    [Fact]
    public void RateLimitPartitionKey_MustIgnoreUntrustedXForwardedFor()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("172.20.0.8");
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.99";

        var method = typeof(ServiceCollectionExtensions).GetMethod(
            "GetClientIp", BindingFlags.NonPublic | BindingFlags.Static);

        var result = method!.Invoke(null, new object[] { ctx });

        result.Should().Be("172.20.0.8");
    }

    [Fact]
    public void EnrichedMachineList_MustPreserveEngineBrand()
    {
        const string json = "[{\"machine_brand\":\"CATERPILLAR\",\"machine_model\":\"320D\",\"machine_category\":\"excavator\",\"engine_brand\":\"CUMMINS\"}]";
        var method = typeof(MeiliSearchProvider).GetMethod(
            "ParseEnrichedMachineList", BindingFlags.NonPublic | BindingFlags.Static);

        var result = (List<AggregateMachineItem>)method!.Invoke(null, new object[] { json })!;

        result.Should().ContainSingle();
        result[0].MachineBrand.Should().Be("CATERPILLAR");
        result[0].MachineModel.Should().Be("320D");
        result[0].EngineBrand.Should().Be("CUMMINS");
    }
}
