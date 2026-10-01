using FluentAssertions;
using Xunit;

namespace SakuraFilter.Api.Tests;

public class AdminOemCatalogEndpointsContractTests
{
    [Fact]
    public void EndpointModule_ProvidesProtectedReadOnlyCatalogRoutes()
    {
        var endpoint = File.ReadAllText(FindRepoFile("backend", "src", "SakuraFilter.Api", "Endpoints", "AdminOemCatalogEndpoints.cs"));
        var registration = File.ReadAllText(FindRepoFile("backend", "src", "SakuraFilter.Api", "Extensions", "EndpointRouteBuilderExtensions.cs"));

        endpoint.Should().Contain("MapGroup(\"/api/admin/oem-catalog\")");
        endpoint.Should().Contain("RequireAuthorization(\"Admin\")");
        endpoint.Should().Contain("MapGet(\"/summary\"");
        endpoint.Should().Contain("MapGet(\"/products\"");
        endpoint.Should().Contain("MapGet(\"/products/{**oemNo1}\"");
        endpoint.Should().Contain("MapGet(\"/products/xrefs/{**oemNo1}\"");
        endpoint.Should().Contain("MapGet(\"/products/applications/{**oemNo1}\"");
        endpoint.Should().Contain("Uri.UnescapeDataString(oemNo1)");
        endpoint.Should().Contain("catalog.oem_products");
        endpoint.Should().Contain("WITH filtered AS");
        endpoint.Should().NotContain("INSERT INTO catalog");
        endpoint.Should().NotContain("UPDATE catalog");
        endpoint.Should().NotContain("DELETE FROM catalog");
        registration.Should().Contain("app.MapAdminOemCatalogEndpoints()");
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
