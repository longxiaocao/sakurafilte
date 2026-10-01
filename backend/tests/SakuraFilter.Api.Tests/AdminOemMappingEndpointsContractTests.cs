using FluentAssertions;
using Xunit;

namespace SakuraFilter.Api.Tests;

public class AdminOemMappingEndpointsContractTests
{
    [Fact]
    public void EndpointModule_ProvidesProtectedSummaryAndCandidateRoutes()
    {
        var endpoint = File.ReadAllText(FindRepoFile("backend", "src", "SakuraFilter.Api", "Endpoints", "AdminOemMappingEndpoints.cs"));
        var registration = File.ReadAllText(FindRepoFile("backend", "src", "SakuraFilter.Api", "Extensions", "EndpointRouteBuilderExtensions.cs"));

        endpoint.Should().Contain("MapGroup(\"/api/admin/oem-mapping\")");
        endpoint.Should().Contain("RequireAuthorization(\"Admin\")");
        endpoint.Should().Contain("MapGet(\"/batches/{batchId:long}/summary\"");
        endpoint.Should().Contain("MapGet(\"/batches/{batchId:long}/candidates\"");
        registration.Should().Contain("app.MapAdminOemMappingEndpoints()");
    }

    private static string FindRepoFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException($"未找到仓库文件: {Path.Combine(parts)}");
    }
}
