using FluentAssertions;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// OEM 到 MR.1 审核映射迁移契约，保证候选与审核结论不会直接污染正式业务表。
/// </summary>
public class OemMr1MappingSchemaContractTests
{
    [Fact]
    public void Migration_DefinesReviewMappingTablesAndCandidateRefresh()
    {
        var migration = File.ReadAllText(FindRepoFile("backend", "migrations", "032_oem_mr1_review_mapping.sql"));

        migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.oem_mapping_candidate_runs");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.oem_mapping_candidates");
        migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.oem_mr1_mapping_reviews");
        migration.Should().Contain("CREATE OR REPLACE FUNCTION staging.refresh_oem_mapping_candidates");
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
