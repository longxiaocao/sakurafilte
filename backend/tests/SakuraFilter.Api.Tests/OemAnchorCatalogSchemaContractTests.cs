using FluentAssertions;
using Xunit;

namespace SakuraFilter.Api.Tests;

public class OemAnchorCatalogSchemaContractTests
{
    [Fact]
    public void Migration_DefinesOemCatalogTablesAndPublishFunction()
    {
        var migration = File.ReadAllText(FindRepoFile("backend", "migrations", "033_oem_anchor_catalog.sql"));

        migration.Should().Contain("CREATE SCHEMA IF NOT EXISTS catalog");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS catalog.oem_import_runs");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS catalog.oem_products");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS catalog.oem_cross_references");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS catalog.oem_machine_applications");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS catalog.oem_mr1_mappings");
        migration.Should().Contain("CREATE OR REPLACE FUNCTION catalog.publish_oem_clean_batch");
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
