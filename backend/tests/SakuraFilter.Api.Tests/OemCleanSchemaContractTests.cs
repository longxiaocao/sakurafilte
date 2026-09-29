using FluentAssertions;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// 清洗层迁移契约，防止后续导入流程绕过可审计的 OEM NO 1 clean 层。
/// </summary>
public class OemCleanSchemaContractTests
{
    [Fact]
    public void Migration_DefinesCleanTablesAndRefreshFunction()
    {
        var migration = File.ReadAllText(FindRepoFile("backend", "migrations", "031_oem_no1_clean.sql"));

        migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.clean_runs");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.product_specs_clean");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.product_spec_field_conflicts");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.oem_numbers_clean");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.applications_clean");
        migration.Should().Contain("CREATE OR REPLACE FUNCTION staging.refresh_oem_clean");
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
