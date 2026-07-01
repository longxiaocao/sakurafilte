# SPIKE-REPORT-day7.10.2 — Advisory Lock 修复 + Grafana SQL 校正

**日期**: 2026-07-01
**作者**: SakuraFilter spike
**触发**: Day 7.10.1 修复后, Advisor 二次评审发现 3 个关键缺陷

---

## TL;DR

| 修复项 | 状态 | 关键证据 |
|--------|------|---------|
| Advisory lock 显式事务 | ✅ | 5 并发: 1 成功 + 4 锁忙 409 |
| RecoveredToPendingId 直接读 Id | ✅ | DB 中 recovered_to_pending_id 正确 |
| Grafana SQL 加 status='active' | ✅ | 12 panel SQL 全部通过 |
| 集成测试回归 | ✅ | Day 7.10.1 集成测试仍全部通过 |
| 并发安全验证 | ✅ | rc=1, 无重复恢复 |

---

## 1. Day 7.10.1 残留问题

Advisor 二次评审发现 Day 7.10.1 还有 3 个未解决问题:

### 1.1 🔴 Critical: Advisory lock 失效
- **原实现**: `pg_try_advisory_xact_lock` 通过 `ExecuteScalarAsync` 调用,无显式事务
- **根因**: PostgreSQL 默认 auto-commit, 单语句执行后立即释放
- **后果**: 锁只保护了 SELECT 一瞬间, SaveChanges 时已无锁
- **修复**: 显式 `db.Database.BeginTransactionAsync` + `tx.GetDbTransaction()` 绑定到 cmd

### 1.2 🟡 High: RecoveredToPendingId 重查错配
- **原实现**: `db.SearchIndexPending.Where(Payload+CreatedAt+RetryCount==0).FirstOrDefault()`
- **问题**: 并发下可能匹配到其他 worker 创建的 pending
- **修复**: 用 `Dictionary<long, SearchIndexPending>` 跟踪, SaveChanges 后从 `pending.Id` 直接读

### 1.3 🟡 Medium: Grafana SQL 统计包含 recovered
- **原 SQL**: `SELECT count(*) FROM search_index_dead_letter`
- **问题**: 死信行永不删除, recovered 行会持续累积, 统计虚高
- **修复**: panel 1/2/4/10/20 加 `WHERE status='active'`, panel 3 改用 `status='recovered'`

---

## 2. 改动文件

### 修改
- [backend/src/SakuraFilter.Api/Services/DeadLetterRecoveryService.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Services/DeadLetterRecoveryService.cs)
  - `TryWithAdvisoryLockAsync` 加显式事务
  - `RecoverInternalAsync` 用 Dictionary 跟踪 pending
- [backend/src/SakuraFilter.Api/Program.cs](file:///d:/projects/sakurafilter/backend/src/Sakurafilter.Api/Program.cs)
  - `/recover` 和 `/recover-batch` 端点用 Dictionary 跟踪 pending
- [monitoring/grafana-dashboard-etl.json](file:///d:/projects/sakurafilter/monitoring/grafana-dashboard-etl.json)
  - 5 个 panel SQL 加 status 过滤

### 新增
- [spike-test/_test_day7102_advisory_lock.py](file:///d:/projects/sakurafilter/spike-test/_test_day7102_advisory_lock.py)
  - 5 并发 /recover 验证锁有效性

---

## 3. 关键代码

### 3.1 显式事务 advisory lock

```csharp
public static async Task<bool> TryWithAdvisoryLockAsync(
    ProductDbContext db, Func<Task> work, CancellationToken ct)
{
    // 显式事务 — lock 跟随事务生命周期
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var conn = (NpgsqlConnection)db.Database.GetDbConnection();
    if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

    bool got = false;
    using (var cmd = conn.CreateCommand())
    {
        cmd.Transaction = (NpgsqlTransaction)tx.GetDbTransaction();  // 绑定到事务
        cmd.CommandText = "SELECT pg_try_advisory_xact_lock(@key)";
        cmd.Parameters.Add(new NpgsqlParameter("key", AdvisoryLockKey));
        var result = await cmd.ExecuteScalarAsync(ct);
        got = result is bool b && b;
    }
    if (!got)
    {
        await tx.RollbackAsync(ct);
        return false;
    }
    await work();
    await tx.CommitAsync(ct);
    return true;
}
```

### 3.2 跟踪 pending entity

```csharp
// 用 Dictionary 关联死信和 pending, 避免重查错配
var addedPending = new Dictionary<long, SearchIndexPending>();
foreach (var dead in candidates)
{
    var pending = new SearchIndexPending { /* ... */ };
    db.SearchIndexPending.Add(pending);
    addedPending[dead.Id] = pending;  // 跟踪
    dead.Status = "recovered";
    dead.RecoveryCount += 1;
    // ...
}
await db.SaveChangesAsync(ct);  // EF Core 填充 pending.Id

foreach (var dead in candidates)
{
    if (addedPending.TryGetValue(dead.Id, out var p))
        dead.RecoveredToPendingId = p.Id;  // 直接从 instance 读
}
await db.SaveChangesAsync(ct);
```

---

## 4. 测试结果

### 4.1 集成测试 [_test_day7101_integration.py](file:///d:/projects/sakurafilter/spike-test/_test_day7101_integration.py)

✅ **全部通过** (修复后仍工作)

```
⭐ 步骤 4: 跨循环 id 保持不变, status 重置 active, recovery_count 保留
⭐ 步骤 6: 第 3 次入死信, recovery_count 保持 2 (符合设计)
⭐ 步骤 9: max_recovery_count 限位有效: rc=3 不再被自动恢复
```

### 4.2 并发锁测试 [_test_day7102_advisory_lock.py](file:///d:/projects/sakurafilter/spike-test/_test_day7102_advisory_lock.py)

✅ **advisory lock 真的有效**

```
5 个并发请求, 总耗时 0.02s
  [0] status=409 "advisory lock 被占用"
  [1] status=200 "recovered: true, recoveryCount: 1"
  [2] status=409 "advisory lock 被占用"
  [3] status=409 "advisory lock 被占用"
  [4] status=409 "advisory lock 被占用"

统计: 1 成功 + 4 锁忙, DB recovery_count=1 (无重复恢复) ✓
```

### 4.3 Grafana SQL [_test_day710_grafana_sql.py](file:///d:/projects/sakurafilter/spike-test/_test_day710_grafana_sql.py)

✅ **12/12 panel SQL 全部通过**

```
panel#  1 [stat] 死信总条数                       | 1 rows, 6ms [OK]
panel#  3 [stat] 近 24h 自动恢复                  | 1 rows, 3ms [OK]
panel#  4 [stat] 超限不可恢复                      | 1 rows, 0ms [OK]
panel# 10 [ts]  死信队列趋势                       | 7 rows, 6ms [OK]
panel# 20 [tbl]  Top 10 死信错误                  | 3 rows, 7ms [OK]
... (12 panel 全部 OK)
```

---

## 5. 系统现状

经过 Day 7.10 / 7.10.1 / 7.10.2 三轮修复, 系统已准备就绪:

- ✅ ETL 告警 P0/P1/P2 严重度路由
- ✅ 5min 同源告警抑制
- ✅ Webhook 失败指数退避
- ✅ 死信自动恢复 (后台 worker)
- ✅ 死信批量恢复 (admin 端点)
- ✅ recovery_count 跨循环保留 (Bug fix)
- ✅ advisory lock 真正生效 (并发安全)
- ✅ Grafana 运维面板 (12 panel, status 过滤正确)
- ✅ 死信清理 (只清 status='recovered')
- ✅ 历史永久保留 (product change history 配置)

**可以开始功能测试了。**
