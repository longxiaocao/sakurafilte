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

        // Arrange: 确保线上表存在 (CI int 库由 EF migrations 建, 不含手工 SQL 023 的 typeahead_dict;
        //   测试自包含建表, 与 023_typeahead_dict.sql 幂等语义一致) + 插入少量源数据
        await EnsureTypeaheadDictTableAsync();
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

    [Fact]
    public async Task ConcurrentRequestRebuilds_Merged_AllSucceed_NoResidue_Integration()
    {
        if (!IsEnabled) { _output.WriteLine("Skip: PG_TEST_CONNECTION_STRING 未配置"); return; }

        // Arrange: ETL 路径使用合并式入口 RequestRebuildAsync — 短时间多个 ETL 完成事件
        //   只保留必要的重建次数 (首个执行 + pending 补跑), 全部并发触发必须收敛且状态正确
        await EnsureTypeaheadDictTableAsync();
        await SeedSourceRowsAsync();

        var ds = new NpgsqlDataSourceBuilder(ConnectionString).Build();
        var rebuild = new TypeaheadDictRebuildService(ds);

        // Act: 并发触发 3 次 RequestRebuildAsync (模拟三 ETL 几乎同时完成)
        var tasks = Enumerable.Range(0, 3)
            .Select(_ => rebuild.RequestRebuildAsync(CancellationToken.None));
        await Task.WhenAll(tasks);

        // Assert 1: 全部完成无异常 (无死锁/无互删临时表)
        _output.WriteLine($"3 次并发 RequestRebuildAsync 全部收敛 (合并式触发无死锁)");

        // Assert 2: 最终状态正确 — typeahead_dict 有数据 + 无残留临时表
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT count(*) FROM typeahead_dict";
        var rowCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        rowCount.Should().BeGreaterThanOrEqualTo(3, "typeahead_dict 应包含重建结果");

        await using var tmpCmd = conn.CreateCommand();
        tmpCmd.CommandText = "SELECT count(*) FROM pg_tables WHERE tablename = 'typeahead_dict_new'";
        var tmpCount = Convert.ToInt32(await tmpCmd.ExecuteScalarAsync());
        tmpCount.Should().Be(0, "重建完成后不应残留 typeahead_dict_new 临时表");
    }

    [Fact]
    public async Task StressMixedRebuildRequests_Converges_NoDeadlock_Integration()
    {
        if (!IsEnabled) { _output.WriteLine("Skip: PG_TEST_CONNECTION_STRING 未配置"); return; }

        // 🔧 fix(2026-08-23 Codex 审查 #11 回归防护): 交接窗口压力测试 —
        //   Codex 发现 RequestRebuildAsync 在 pending 检查与 running 释放之间存在丢触发窗口
        //   (执行者读到 pending=0 后、清 running 前新请求只置 pending 无人接手)。该窗口极窄,
        //   精确时序难以确定性复现, 用高并发随机交替调用 RebuildAsync/RequestRebuildAsync
        //   压力暴露 (锁内原子交接后应无死锁/无异常/无残留/状态收敛)。
        await EnsureTypeaheadDictTableAsync();
        await SeedSourceRowsAsync();

        var ds = new NpgsqlDataSourceBuilder(ConnectionString).Build();
        var rebuild = new TypeaheadDictRebuildService(ds);

        // Act: 12 线程 × 随机调用 (手动端点 + ETL 合并式混用), 全部并发
        var rnd = new Random(42);
        var tasks = Enumerable.Range(0, 12).Select(t =>
            Task.Run(async () =>
            {
                for (var i = 0; i < 5; i++)
                {
                    if (rnd.Next(2) == 0)
                        await rebuild.RebuildAsync(CancellationToken.None);
                    else
                        await rebuild.RequestRebuildAsync(CancellationToken.None);
                    await Task.Yield();
                }
            }));
        await Task.WhenAll(tasks);

        // Assert: 无死锁 (Task.WhenAll 完成即证明) + 最终状态正确
        _output.WriteLine("12 线程 × 5 次混合并发调用全部收敛 (无死锁/无竞态崩溃)");

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT count(*) FROM typeahead_dict";
        var rowCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        rowCount.Should().BeGreaterThanOrEqualTo(3, "typeahead_dict 应包含重建结果");

        await using var tmpCmd = conn.CreateCommand();
        tmpCmd.CommandText = "SELECT count(*) FROM pg_tables WHERE tablename = 'typeahead_dict_new'";
        var tmpCount = Convert.ToInt32(await tmpCmd.ExecuteScalarAsync());
        tmpCount.Should().Be(0, "重建完成后不应残留 typeahead_dict_new 临时表");
    }

    /// <summary>
    /// 确保 typeahead_dict 线上表存在 + pg_trgm 扩展可用 (与 023_typeahead_dict.sql 幂等语义一致)。
    /// WHY: CI 集成测试库由 EF migrations 创建, 不含手工 SQL 023/024 的 typeahead_dict 表,
    ///    且 postgres service 未启用 pg_trgm 扩展 — RebuildAsync 第 3 步建 GIN trgm 索引会报
    ///    42704 (operator class gin_trgm_ops does not exist), 第 4 步 DROP typeahead_dict 会
    ///    报 relation does not exist → 测试自包含建扩展+建表。
    /// </summary>
    private async Task EnsureTypeaheadDictTableAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE EXTENSION IF NOT EXISTS pg_trgm;
            CREATE TABLE IF NOT EXISTS typeahead_dict (
                field  TEXT NOT NULL,
                value  TEXT NOT NULL,
                PRIMARY KEY (field, value)
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>插入少量源数据: products(1) + cross_references(1) + machine_applications(1)</summary>
    private async Task SeedSourceRowsAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        // products: oem_2 (typeahead oem-no2 来源)
        //   显式插入全部 NOT NULL 字段 (不依赖默认值): CI 库由 EF migrations 建,
        //   020/021 等手工 SQL 的默认值未生效 → 靠默认值会 23502 (2026-08-22 CI 实测)
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO products (mr_1, oem_2, oem_no_display, product_name_1, type,
                                      is_published, image_status, is_discontinued, created_at, updated_at) VALUES
                ('MRTST001', 'TEST-OEM2-A', 'TST-001', 'Test Filter', 'OIL FILTER',
                 true, 'pending', false, now(), now())
                ON CONFLICT DO NOTHING;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        // cross_references: oem_brand + oem_no_3 (oem-brand / oem-no3 来源)
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO cross_references (oem_brand, oem_no_3, product_id,
                                              is_discontinued, is_published, sort_order, created_at) VALUES
                ('TEST-BRAND-A', 'TEST-OEM3-A', 1,
                 false, true, 0, now())
                ON CONFLICT DO NOTHING;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        // machine_applications: machine_brand/model_name/engine_* 来源
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO machine_applications (machine_brand, machine_model, model_name, engine_brand, engine_type,
                                                  product_id, is_ongoing, is_discontinued, created_at) VALUES
                ('TEST-MACHINE-A', 'M1000', 'Test Model', 'TEST-ENG-A', 'Diesel',
                 1, false, false, now())
                ON CONFLICT DO NOTHING;
                """;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
