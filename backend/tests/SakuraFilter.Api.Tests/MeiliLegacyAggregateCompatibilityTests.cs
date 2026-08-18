using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SakuraFilter.Core.DTOs;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// 兼容旧 Meili 文档嵌套列表的回归测试。
/// </summary>
public class MeiliLegacyAggregateCompatibilityTests
{
    [Fact]
    public void LegacyOemList_IsParsedToAggregateContract()
    {
        // 覆盖: 聚合搜索在 PG 富化为空时仍交付旧索引中的 OEM 3。
        var result = Invoke<List<AggregateOemItem>>("ParseLegacyOemList", """
            [{"oem_brand":"BOSCH","oem_no_3":"F0001","is_published":true,"sort_order":2}]
            """);

        result.Should().ContainSingle()
            .Which.Should().Match<AggregateOemItem>(x =>
                x.OemBrand == "BOSCH" && x.OemNo3 == "F0001" && x.IsPublished && x.SortOrder == 2);
    }

    [Fact]
    public void InvalidLegacyMachineList_ReturnsEmptyList()
    {
        // 覆盖: 历史索引字段损坏时搜索仍可返回主结果，不因兼容解析抛错。
        var result = Invoke<List<AggregateMachineItem>>("ParseLegacyMachineList", "not-json");

        result.Should().BeEmpty();
    }

    private static T Invoke<T>(string methodName, string json)
    {
        var method = typeof(SakuraFilter.Search.MeiliSearchProvider)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var node = json == "not-json" ? JsonValue.Create(json)! : JsonNode.Parse(json)!;
        return (T)method.Invoke(null, new object[] { node })!;
    }
}
