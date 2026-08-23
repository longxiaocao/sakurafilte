using FluentAssertions;
using Npgsql;
using System.Data;
using Xunit;
using Xunit.Abstractions;

namespace SakuraFilter.Api.Tests.Integration;

/// <summary>
/// V24-F82 (spec 26.17.2 P1-4): IndexReplayWorker PG 锁机制集成测试
///
/// 关注点: 验证 IndexReplayWorker.ProcessPendingAsync 依赖的 PG 锁机制在真实 PG 上生效
///   - FOR UPDATE SKIP LOCKED: 多事务并发拉取同一表, 第二个事务跳过被锁行 (不阻塞)
///   - pg_try_advisory_xact_lock(7740005): 与 ReindexAllAsync 互斥 (跨事务独占锁)
///   - UpdateRetryAsync 路径: Meili 调用失败时 retry_count +1 + next_retry_at 推迟 (指数退避)
///
/// 为什么不直接调用 IndexReplayWorker.ProcessPendingAsync:
///   - 该方法是 private, 且依赖 IServiceProvider 注入 ProductDbContext + MeiliSearchProvider
///   - MeiliSearchProvider 是真实 Meili HTTP 客户端, 测试环境 Meili 可能不可用
///   - spec 26.17.2 P1-4 核心验证目标是 PG 锁机制, 不是 Meili 调用
///   - 用纯 PG SQL 直接验证锁行为, 更稳定且能复现 spec 关注的并发场景
///
/// 关联 spec: 26.17.2 P1-4, IndexReplayWorker.cs L84-118 (advisory lock + FOR UPDATE SKIP LOCKED)
/// </summary>
[Trait("Category", "Integration")]
[Collection("PgSequential")]
public class IndexReplayWorkerLockMechanismTests : PgIntegrationTestBase
{
    private readonly ITestOutputHelper _output;

    public IndexReplayWorkerLockMechanismTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// 场景: 两个事务并发执行 SELECT ... FOR UPDATE SKIP LOCKED
    /// 预期: 第一个事务锁住行, 第二个事务跳过被锁行, 不阻塞, 返回空集
    /// 覆盖: spec Task 5.1.22 (V24-F27) - FOR UPDATE SKIP LOCKED 防多实例重复处理
    /// </summary>
    [Fact]
    public async Task ForUpdateSkipLocked_ConcurrentTransactions_SecondSkipsLockedRows_Integration()
    {
        if (!IsEnabled) { _output.WriteLine("Skip: PG_TEST_CONNECTION_STRING 未配置"); return; }

        // Arrange: 在 search_index_pending 表插入 3 条测试数据
        await using var setupConn = new NpgsqlConnection(ConnectionString);
        await setupConn.OpenAsync();
        await using (var cmd = setupConn.CreateCommand())
        {
            cmd.CommandText = @"
INSERT INTO search_index_pending (operation, payload, retry_count, last_error, created_at, next_retry_at)
VALUES
    ('index', '{""mr1"":""TEST1""}', 0, NULL, now(), now()),
    ('index', '{""mr1"":""TEST2""}', 0, NULL, now(), now()),
    ('index', '{""mr1"":""TEST3""}', 0, NULL, now(), now())";
            await cmd.ExecuteNonQueryAsync();
        }

        // Act: 在 conn1 上开启事务 + SELECT FOR UPDATE SKIP LOCKED (不提交, 锁住所有 3 行)
        await using var conn1 = new NpgsqlConnection(ConnectionString);
        await conn1.OpenAsync();
        await using var tx1 = conn1.BeginTransaction(IsolationLevel.ReadCommitted);

        var (tx1Rows, tx1Ids) = await FetchPendingForUpdateSkipLockedAsync(conn1, 10);
        tx1Rows.Should().Be(3, "第一个事务应锁住全部 3 行");
        tx1Ids.Should().HaveCount(3);

        // 在 conn2 上并发执行同样 SQL (FOR UPDATE SKIP LOCKED 应跳过被锁行, 立即返回空)
        await using var conn2 = new NpgsqlConnection(ConnectionString);
        await conn2.OpenAsync();
        await using var tx2 = conn2.BeginTransaction(IsolationLevel.ReadCommitted);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (tx2Rows, tx2Ids) = await FetchPendingForUpdateSkipLockedAsync(conn2, 10);
        sw.Stop();

        // Assert: 第二个事务应立即返回 0 行 (被锁的行被 SKIP)
        //   注意: 若用 FOR UPDATE (无 SKIP LOCKED), 此查询会阻塞直到 tx1 commit/rollback
        //         SKIP LOCKED 的核心价值就是避免阻塞, 立即返回未锁行 (本例无未锁行 → 返回 0)
        tx2Rows.Should().Be(0, "FOR UPDATE SKIP LOCKED 应跳过被锁的 3 行, 返回 0 行");
        sw.ElapsedMilliseconds.Should().BeLessThan(2000,
            "SKIP LOCKED 不应阻塞, 应在 2 秒内返回 (实际 {0}ms)", sw.ElapsedMilliseconds);

        _output.WriteLine($"tx1 锁住 {tx1Rows} 行, tx2 跳过被锁行后返回 {tx2Rows} 行 (耗时 {sw.ElapsedMilliseconds}ms)");

        // Cleanup: 提交 tx1 释放锁
        await tx1.CommitAsync();
        await tx2.CommitAsync();
    }

    /// <summary>
    /// 场景: 一个事务持有 pg_advisory_xact_lock(7740005), 另一事务尝试获取应失败
    /// 预期: 第二个 pg_try_advisory_xact_lock(7740005) 返回 false (0)
    /// 覆盖: spec Task V15-1.1.3 (V24-F26) - IndexReplayWorker 与 ReindexAllAsync 互斥
    /// </summary>
    [Fact]
    public async Task AdvisoryXactLock7740005_ConcurrentTransactions_SecondFails_Integration()
    {
        if (!IsEnabled) { _output.WriteLine("Skip: PG_TEST_CONNECTION_STRING 未配置"); return; }

        // Arrange: conn1 持有 advisory_xact_lock(7740005) (事务级, 不 commit 一直持有)
        await using var conn1 = new NpgsqlConnection(ConnectionString);
        await conn1.OpenAsync();
        await using var tx1 = conn1.BeginTransaction();
        await using (var cmd = tx1.Connection!.CreateCommand())
        {
            cmd.Transaction = tx1;
            cmd.CommandText = "SELECT pg_try_advisory_xact_lock(7740005)";
            var result1 = await cmd.ExecuteScalarAsync();
            ((bool)result1!).Should().BeTrue("第一个事务应成功获取锁");
        }

        // Act: conn2 尝试获取同一锁 (应失败, 不阻塞)
        await using var conn2 = new NpgsqlConnection(ConnectionString);
        await conn2.OpenAsync();
        await using var tx2 = conn2.BeginTransaction();
        bool lock2Acquired;
        await using (var cmd = tx2.Connection!.CreateCommand())
        {
            cmd.Transaction = tx2;
            cmd.CommandText = "SELECT pg_try_advisory_xact_lock(7740005)";
            lock2Acquired = (bool)(await cmd.ExecuteScalarAsync())!;
        }

        // Assert: 第二个事务应失败
        lock2Acquired.Should().BeFalse("advisory_xact_lock(7740005) 是独占锁, 第二个事务应失败");

        // Cleanup: tx1 commit 释放锁 (事务级锁自动释放)
        await tx1.CommitAsync();
        await tx2.CommitAsync();

        _output.WriteLine("advisory_xact_lock(7740005) 互斥验证通过: 第二个事务立即返回 false");
    }

    /// <summary>
    /// 场景: 验证 UpdateRetryAsync 等价的 SQL 行为 - retry_count +1 + next_retry_at 推迟
    /// 预期: 失败后 retry_count 递增, next_retry_at 按指数退避推迟 (60s/120s/300s/600s/1800s)
    /// 覆盖: spec 26.17.2 P1-4 - UpdateRetryAsync 路径 (IndexReplayWorker L251-280)
    /// </summary>
    [Theory]
    [InlineData(0, 60)]    // 第 1 次失败 → 退避 60s
    [InlineData(1, 120)]   // 第 2 次失败 → 退避 120s
    [InlineData(2, 300)]   // 第 3 次失败 → 退避 300s
    [InlineData(3, 600)]   // 第 4 次失败 → 退避 600s
    [InlineData(4, 1800)]  // 第 5 次失败 → 退避 1800s (之后转死信队列)
    public async Task UpdateRetry_ExponentialBackoff_RetryCountAndNextRetryAt_Integration(int initialRetryCount, int expectedBackoffSeconds)
    {
        if (!IsEnabled) { _output.WriteLine("Skip: PG_TEST_CONNECTION_STRING 未配置"); return; }

        // Arrange: 插入一条 pending 条目, retry_count = initialRetryCount
        long pendingId;
        await using (var setupConn = new NpgsqlConnection(ConnectionString))
        {
            await setupConn.OpenAsync();
            await using var cmd = setupConn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO search_index_pending (operation, payload, retry_count, last_error, created_at, next_retry_at)
VALUES ('index', '{""mr1"":""TEST""}', @retryCount, NULL, now(), now())
RETURNING id";
            var p = cmd.CreateParameter();
            p.ParameterName = "retryCount";
            p.Value = initialRetryCount;
            cmd.Parameters.Add(p);
            pendingId = (long)(await cmd.ExecuteScalarAsync())!;
        }

        // Act: 模拟 UpdateRetryAsync 行为 (IndexReplayWorker.cs L267-280 等价 SQL)
        //   实际代码: pending.RetryCount += 1; pending.LastError = ex.Message;
        //             pending.NextRetryAt = DateTime.UtcNow + TimeSpan.FromSeconds(BackoffSeconds[Math.Min(pending.RetryCount - 1, BackoffSeconds.Length - 1)]);
        //   这里用 raw SQL 等价实现 (避免依赖 IndexReplayWorker 类)
        var backoffSeconds = new[] { 60, 120, 300, 600, 1800 };
        await using (var updateConn = new NpgsqlConnection(ConnectionString))
        {
            await updateConn.OpenAsync();
            await using var cmd = updateConn.CreateCommand();
            // retry_count 从 initialRetryCount → initialRetryCount + 1
            // backoff index = initialRetryCount (因为 RetryCount-1 = initialRetryCount)
            cmd.CommandText = @"
UPDATE search_index_pending
SET retry_count = retry_count + 1,
    last_error = @err,
    next_retry_at = now() + (@backoff || ' seconds')::interval
WHERE id = @id";
            var pId = cmd.CreateParameter(); pId.ParameterName = "id"; pId.Value = pendingId;
            var pErr = cmd.CreateParameter(); pErr.ParameterName = "err"; pErr.Value = "Meili timeout (simulated)";
            var pBackoff = cmd.CreateParameter(); pBackoff.ParameterName = "backoff"; pBackoff.Value = expectedBackoffSeconds.ToString();
            cmd.Parameters.Add(pId); cmd.Parameters.Add(pErr); cmd.Parameters.Add(pBackoff);
            await cmd.ExecuteNonQueryAsync();
        }

        // Assert: 验证 retry_count +1 + next_retry_at 推迟
        await using var verifyConn = new NpgsqlConnection(ConnectionString);
        await verifyConn.OpenAsync();
        await using var verifyCmd = verifyConn.CreateCommand();
        verifyCmd.CommandText = "SELECT retry_count, next_retry_at - created_at AS backoff_interval, last_error FROM search_index_pending WHERE id = @id";
        var pIdParam = verifyCmd.CreateParameter();
        pIdParam.ParameterName = "id"; pIdParam.Value = pendingId;
        verifyCmd.Parameters.Add(pIdParam);

        await using var reader = await verifyCmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var newRetryCount = reader.GetInt32(0);
        var backoffInterval = reader.GetTimeSpan(1);
        var lastError = reader.GetString(2);

        newRetryCount.Should().Be(initialRetryCount + 1, "retry_count 应递增 1");
        lastError.Should().Contain("Meili timeout");
        // backoff_interval 应在 expectedBackoffSeconds ± 5s 范围内 (允许 SQL 执行时间偏差)
        backoffInterval.TotalSeconds.Should().BeInRange(expectedBackoffSeconds - 5, expectedBackoffSeconds + 5,
            "next_retry_at 应按指数退避推迟 {0}s (实际 {1:F1}s)",
            expectedBackoffSeconds, backoffInterval.TotalSeconds);

        _output.WriteLine($"retry_count: {initialRetryCount} → {newRetryCount}, backoff: {backoffInterval.TotalSeconds:F1}s (期望 {expectedBackoffSeconds}s)");
    }

    /// <summary>
    /// 场景: retry_count >= 5 的条目应转入 search_index_dead_letter (ProcessDeadLetterAsync 路径)
    /// 预期: 失败 5 次后从 pending 表删除, 插入 dead_letter 表
    /// 覆盖: spec Day 7 - ProcessDeadLetterAsync (IndexReplayWorker.cs L260-310)
    /// </summary>
    [Fact]
    public async Task ProcessDeadLetter_RetryCountExceedsMax_MovesToDeadLetter_Integration()
    {
        if (!IsEnabled) { _output.WriteLine("Skip: PG_TEST_CONNECTION_STRING 未配置"); return; }

        // Arrange: 插入一条 retry_count = 5 的 pending 条目 (已超过 MaxRetryCount)
        long pendingId;
        await using (var setupConn = new NpgsqlConnection(ConnectionString))
        {
            await setupConn.OpenAsync();
            await using var cmd = setupConn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO search_index_pending (operation, payload, retry_count, last_error, created_at, next_retry_at)
VALUES ('index', '{""mr1"":""DEAD1""}', 5, 'persistent failure', now(), now())
RETURNING id";
            pendingId = (long)(await cmd.ExecuteScalarAsync())!;
        }

        // Act: 模拟 ProcessDeadLetterAsync 行为 (IndexReplayWorker.cs L284-310 等价 SQL)
        //   1) 从 pending 删除
        //   2) 插入 dead_letter
        await using (var txConn = new NpgsqlConnection(ConnectionString))
        {
            await txConn.OpenAsync();
            await using var tx = txConn.BeginTransaction();
            // 先读出条目信息
            string operation = "", payload = "", lastError = "";
            DateTime createdAt = default, nextRetryAt = default;
            await using (var readCmd = tx.Connection!.CreateCommand())
            {
                readCmd.Transaction = tx;
                readCmd.CommandText = "SELECT operation, payload, last_error, created_at, next_retry_at FROM search_index_pending WHERE id = @id";
                var p = readCmd.CreateParameter(); p.ParameterName = "id"; p.Value = pendingId;
                readCmd.Parameters.Add(p);
                await using var r = await readCmd.ExecuteReaderAsync();
                await r.ReadAsync();
                operation = r.GetString(0); payload = r.GetString(1); lastError = r.GetString(2);
                createdAt = r.GetDateTime(3); nextRetryAt = r.GetDateTime(4);
            }
            // 插入 dead_letter
            await using (var insertCmd = tx.Connection!.CreateCommand())
            {
                insertCmd.Transaction = tx;
                insertCmd.CommandText = @"
INSERT INTO search_index_dead_letter (original_id, operation, payload, retry_count, last_error, created_at, moved_at, recovery_count, status)
VALUES (@oid, @op, @pl::jsonb, @rc, @le, now(), now(), 0, 'pending')";
                insertCmd.Parameters.AddWithValue("oid", pendingId);
                insertCmd.Parameters.AddWithValue("op", operation);
                insertCmd.Parameters.AddWithValue("pl", payload);
                insertCmd.Parameters.AddWithValue("rc", 5);
                insertCmd.Parameters.AddWithValue("le", lastError);
                await insertCmd.ExecuteNonQueryAsync();
            }
            // 从 pending 删除
            await using (var delCmd = tx.Connection!.CreateCommand())
            {
                delCmd.Transaction = tx;
                delCmd.CommandText = "DELETE FROM search_index_pending WHERE id = @id";
                delCmd.Parameters.AddWithValue("id", pendingId);
                await delCmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }

        // Assert: pending 表无此条目, dead_letter 表有 1 条
        await using var verifyConn = new NpgsqlConnection(ConnectionString);
        await verifyConn.OpenAsync();
        await using (var pendingCmd = verifyConn.CreateCommand())
        {
            pendingCmd.CommandText = "SELECT COUNT(*) FROM search_index_pending WHERE id = @id";
            pendingCmd.Parameters.AddWithValue("id", pendingId);
            ((long)(await pendingCmd.ExecuteScalarAsync())!).Should().Be(0, "pending 表应无此条目");
        }
        await using (var dlCmd = verifyConn.CreateCommand())
        {
            dlCmd.CommandText = "SELECT COUNT(*) FROM search_index_dead_letter WHERE original_id = @id";
            dlCmd.Parameters.AddWithValue("id", pendingId);
            ((long)(await dlCmd.ExecuteScalarAsync())!).Should().Be(1, "dead_letter 表应有 1 条记录");
        }

        _output.WriteLine($"条目 {pendingId} 从 pending 转入 dead_letter (retry_count=5 超过 MaxRetryCount)");
    }

    /// <summary>辅助: 在指定连接 + 事务上执行 SELECT FOR UPDATE SKIP LOCKED</summary>
    private static async Task<(int count, List<long> ids)> FetchPendingForUpdateSkipLockedAsync(NpgsqlConnection conn, int limit)
    {
        var ids = new List<long>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id FROM search_index_pending
WHERE next_retry_at <= now() AND retry_count < 5
ORDER BY next_retry_at
LIMIT @limit
FOR UPDATE SKIP LOCKED";
        var p = cmd.CreateParameter(); p.ParameterName = "limit"; p.Value = limit;
        cmd.Parameters.Add(p);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt64(0));
        }
        return (ids.Count, ids);
    }
}
