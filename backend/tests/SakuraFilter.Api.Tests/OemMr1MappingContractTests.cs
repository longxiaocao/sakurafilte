using FluentAssertions;
using Xunit;

namespace SakuraFilter.Api.Tests;

public class OemMr1MappingContractTests
{
    [Fact]
    public void MappingMigration_ProvidesTransactionalHistoryPreservingFunction()
    {
        var migration = File.ReadAllText(FindRepoFile("backend", "migrations", "034_oem_mr1_mapping.sql"));

        migration.Should().Contain("catalog.set_oem_mr1_mapping");
        migration.Should().Contain("FOR UPDATE");
        migration.Should().Contain("ended_at = now()");
        migration.Should().Contain("uq_catalog_oem_mr1_active_value");
    }

    [Fact]
    public void Endpoint_RequiresAdminAndUsesMappingFunction()
    {
        var endpoint = File.ReadAllText(FindRepoFile("backend", "src", "SakuraFilter.Api", "Endpoints", "AdminOemCatalogEndpoints.cs"));

        endpoint.Should().Contain("MapPut(\"/products/mr1/{**oemNo1}\"");
        endpoint.Should().Contain("catalog.set_oem_mr1_mapping");
        endpoint.Should().Contain("RequireAuthorization(\"Admin\")");
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
