using FluentAssertions;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// 暂存区迁移契约测试，防止 staging 表结构与导入服务约定漂移。
/// </summary>
public class StagingSchemaContractTests
{
    [Fact]
    public void Migration_DefinesOemAnchorAndAllRawTables()
    {
        var migration = File.ReadAllText(FindRepoFile("backend", "migrations", "030_oem_no1_staging.sql"));

        migration.Should().Contain("CREATE SCHEMA IF NOT EXISTS staging");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.import_batches");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.oem_anchors");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.product_specs_raw");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.oem_numbers_raw");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.applications_raw");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.validation_issues");
        migration.Should().Contain("row_hash");
        migration.Should().Contain("oem_no_1_normalized");
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
