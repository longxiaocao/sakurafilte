using FluentAssertions;
using Npgsql;
using SakuraFilter.Etl;
using Xunit;
using Xunit.Abstractions;

namespace SakuraFilter.Api.Tests.Integration;

/// <summary>
/// 🔧 fix(2026-08-22 Codex 审查 P1): typeahead_dict 重建并发互斥集成测试。
///
/// 背景: ETL products/xrefs/apps 三流程导入完成各自 fire-and-forget 触发 RebuildAsync,
///   修复前无互斥 → 并发重建会互相 DROP typeahead_dict_new 临时表 (第 1 步 DROP IF EXISTS
///   删掉对方正在填充的表 → INSERT 到不存在表报错 / 切换混乱)。
///   修复: TypeaheadDictRebuildService 加 SemaphoreSlim(1,1) 串行化。
///
/// 覆盖:
///   1. 并发 3 次 RebuildAsync 全部成功 (无互斥时会抛异常/残留临时表)
///   2. 最终 typeahead_dict 有数据 + 无残留 typeahead_dict_new 临时表
///
/// 本测试只依赖少量数据 (重建 SQL 对空表也安全), CI 用 service container PG 可跑。
/// </summary>
[Collection("PgSequential")]
[Trait("Category", "Integration")]
public class TypeaheadDictRebuildConcurrencyTests : PgIntegrationTestBase
{
    private readonly ITestOutputHelper _output;

    public TypeaheadDictRebuildConcurrencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ConcurrentRebuilds_AllSucceed_NoTempTableResidue_Integration()
    {
        if (!IsEnabled) { _output.WriteLine("Skip: PG_TEST_CONNECTION_STRING 未配置"); return; }

        // Arrange: 插入少量源数据 (重建 SQL 对空表也安全, 有数据更能验证 INSERT 路径)
        await SeedSourceRowsAsync();

        var ds = new NpgsqlDataSourceBuilder(ConnectionString).Build();
        var rebuild = new TypeaheadDictRebuildService(ds);

        // Act: 并发触发 3 次重建 (模拟 products/xrefs/apps 三个 ETL 连续完成)
        var tasks = Enumerable.Range(0, 3)
            .Select(_ => rebuild.RebuildAsync(CancellationToken.None));
        await Task.WhenAll(tasks);

        // Assert 1: 全部完成无异常 (无互斥时 DROP 对方临时表 → 至少一个任务抛异常)
        _output.WriteLine($"3 次并发重建全部成功 (SemaphoreSlim 串行化生效)");

        // Assert 2: typeahead_dict 有数据 (重建已切换到线上表)
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT count(*) FROM typeahead_dict";
        var rowCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        rowCount.Should().BeGreaterThanOrEqualTo(3, "typeahead_dict 应包含 3 个 source 行的 distinct 字段值");

        // Assert 3: 无残留临时表 (最后一次重建完成应切换并删除 _new 表)
        await using var tmpCmd = conn.CreateCommand();
        tmpCmd.CommandText = "SELECT count(*) FROM pg_tables WHERE tablename = 'typeahead_dict_new'";
        var tmpCount = Convert.ToInt32(await tmpCmd.ExecuteScalarAsync());
        tmpCount.Should().Be(0, "重建完成后不应残留 typeahead_dict_new 临时表");
    }

    /// <summary>插入少量源数据: products(1) + cross_references(1) + machine_applications(1)</summary>
    private async Task SeedSourceRowsAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        // products: 需要 oem_2 (typeahead oem-no2 来源)
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO products (mr_1, oem_2, product_name_1, type) VALUES
                ('MR_TST_001', 'TEST-OEM2-A', 'Test Filter', 'OIL FILTER')
                ON CONFLICT DO NOTHING;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        // cross_references: oem_brand + oem_no_3 (oem-brand / oem-no3 来源)
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO cross_references (oem_brand, oem_no_3, product_id) VALUES
                ('TEST-BRAND-A', 'TEST-OEM3-A', 1)
                ON CONFLICT DO NOTHING;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        // machine_applications: machine_brand/model_name/engine_* 来源
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO machine_applications (machine_brand, machine_model, model_name, engine_brand, engine_type, product_id) VALUES
                ('TEST-MACHINE-A', 'M1000', 'Test Model', 'TEST-ENG-A', 'Diesel', 1)
                ON CONFLICT DO NOTHING;
                """;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
