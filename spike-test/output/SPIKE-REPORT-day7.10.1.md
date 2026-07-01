# SPIKE-REPORT-day7.10.1 — 死信恢复元数据持久化 Bug Fix

**日期**: 2026-07-01
**作者**: SakuraFilter spike
**触发**: Advisor 评审 Day 7.10 报告时发现严重设计缺陷
**严重度**: 🔴 Critical — `recovery_count` 限位完全失效, max_recovery_count 形同虚设

---

## TL;DR

| 修复项 | 状态 | 关键证据 |
|--------|------|---------|
| 死信永不删除, 改 status 列 | ✅ | 步骤 4: 跨循环 id 保持 15661, status 正确切换 |
| IndexReplayWorker 复用死信行 | ✅ | 步骤 4: payload 匹配, 复用同一行不新建 |
| recovery_count 跨循环保留 | ✅ | rc=0→recover→1→入死信→recover→2→入死信→recover→3 |
| max_recovery_count 限位有效 | ✅ | 步骤 9: batch maxRc=3 过滤 rc=3 (matched=0) |
| PostgreSQL advisory lock | ✅ | TryWithAdvisoryLockAsync, 串行化 worker + admin |
| 回归测试 | ✅ | Day 7.10 旧测试 全部通过 |

---

## 1. Bug 描述

### 1.1 触发场景
Day 7.10 完成后, Advisor 评审代码时发现:
- `DeadLetterRecoveryService.RunOnceAsync()` 中,代码先设置 `dead.RecoveryCount += 1`、`dead.LastRecoveryAt = now2`,**然后**调用 `db.SearchIndexDeadLetters.Remove(dead)` 删除该行
- `/api/admin/dead-letter/{id}/recover` 和 `/recover-batch` 端点**也犯了同样错误**

### 1.2 根因 (EF Core 行为)
EF Core 在 `SaveChangesAsync()` 时, 对 `EntityState.Deleted` 的实体会**清空所有其他属性变更**。
- 原代码: `dead.RecoveryCount += 1; dead.Status = "recovered"; db.Remove(dead); SaveChanges();`
- 实际 SQL: `DELETE FROM search_index_dead_letter WHERE id = ?` (recovery_count 等字段变更被丢弃)

### 1.3 后果
1. **recovery_count 永远不会被持久化** — 限位逻辑完全失效
2. **若恢复的条目再次失败,会以 `recovery_count=0` 重新入队** — 历史彻底丢失
3. **后台 worker 与 admin 端点存在竞争条件** — 无显式锁,可能并发改写

### 1.4 实际复现
在 advisor 评审前, Day 7.10 的 `_test_day710_recovery.py` 通过了 — 因为它只测了**单次**恢复场景,没有跨循环验证。

---

## 2. 修复方案 (用户选定)

**方案 A: 加 status 列 + PostgreSQL advisory lock**

### 2.1 关键设计

```
┌─────────────────────────────────────────────────────────────────┐
│ 死信生命周期 (修复后)                                            │
│                                                                 │
│  注入新死信 (retry>=5)  ──→  status='active', recovery_count=0  │
│         │                       (worker 候选)                   │
│         ▼                                                       │
│  /recover 或自动恢复    ──→  status='recovered', rc+=1          │
│         │                       (历史留痕, worker 跳过)          │
│         ▼                                                       │
│  IndexReplayWorker 失败  ──→  查找同 payload 的 recovered 行    │
│         │                       找到: 改 status='active' (复用) │
│         │                       找不到: 新建 status='active'     │
│         ▼                                                       │
│  DeadLetterCleanupService ──→ 只清 status='recovered'           │
│                              + moved_at < cutoff               │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 关键决策

| 决策 | 选择 | WHY |
|------|------|-----|
| 死信是否删除 | ❌ 不删除 | 删除导致 metadata 丢失 |
| 标记方式 | `status` 列 ('active'/'recovered') | 简单清晰, 可扩展 |
| 复用 vs 新建 | 同 payload 复用 | 跨循环 recovery_count 不丢失 |
| 锁机制 | `pg_try_advisory_xact_lock` | 事务结束自动释放, 非阻塞 |
| 索引 | `(status, recovery_count, last_recovery_at)` 部分索引 | worker 扫描仅命中 active |
| 清理 | 只清 `status='recovered'` | active 永不清, 防误删 |

---

## 3. 改动文件清单

### 新增
- [backend/migrations/015_dead_letter_status.sql](file:///d:/projects/sakurafilter/backend/migrations/015_dead_letter_status.sql)
  - 加 `status` + `recovered_at` + `recovered_to_pending_id` 3 列
  - 加 `idx_dead_letter_active_recovery` 部分索引 (WHERE status='active')
  - 加 `idx_dead_letter_recovered_at` 部分索引
  - 加 `idx_dead_letter_payload_hash` (oper + md5(payload) + status) 用于复用查找
  - 历史回填 15595 行 status='active'
- [spike-test/_apply_migration_015.py](file:///d:/projects/sakurafilter/spike-test/_apply_migration_015.py)
- [spike-test/_test_day7101_integration.py](file:///d:/projects/sakurafilter/spike-test/_test_day7101_integration.py)

### 修改
- [backend/src/SakuraFilter.Core/Entities/Product.cs#L138-L162](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Core/Entities/Product.cs#L138-L162)
  - `SearchIndexDeadLetter` 加 `Status` + `RecoveredAt` + `RecoveredToPendingId` 字段
- [backend/src/SakuraFilter.Infrastructure/Data/ProductDbContext.cs#L112-L130](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Infrastructure/Data/ProductDbContext.cs#L112-L130)
  - 实体配置加 status 索引, 改用 `idx_dead_letter_active_recovery`
- [backend/src/SakuraFilter.Api/Services/DeadLetterRecoveryService.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Services/DeadLetterRecoveryService.cs) — **重写**
  - `TryWithAdvisoryLockAsync` 静态方法 (`pg_try_advisory_xact_lock`)
  - `RecoverInternalAsync` 改为改 status 而非删行
  - 加 status='active' 过滤
  - 二阶段 SaveChanges: 第一轮让 pending.Id 生成, 第二轮回填 RecoveredToPendingId
- [backend/src/SakuraFilter.Api/Services/IndexReplayWorker.cs#L126-L196](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Services/IndexReplayWorker.cs#L126-L196)
  - `ProcessDeadLetterAsync` 改为查找同 payload 的 recovered 行复用
  - 找不到才新建 (保持原 active 行为)
- [backend/src/SakuraFilter.Api/Services/DeadLetterCleanupService.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Services/DeadLetterCleanupService.cs)
  - 加 `status='recovered'` 过滤, 双重保护防止误删 active
- [backend/src/SakuraFilter.Api/Program.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Program.cs)
  - `/api/admin/dead-letter/{id}/recover` 端点: 改 status, 不删行, 用 advisory lock
  - `/api/admin/dead-letter/recover-batch` 端点: 同上, 加 status='active' 过滤
  - `/api/admin/dead-letter` GET 端点: DeadLetterItem 暴露新字段
  - 死信已恢复时 `/recover` 返回 409 Conflict (不重复恢复)

---

## 4. 端到端集成测试 [_test_day7101_integration.py](file:///d:/projects/sakurafilter/spike-test/_test_day7101_integration.py)

### 4.1 场景设计

模拟完整恢复-重试-重新死信循环, 验证 recovery_count 跨循环保留:

| 步骤 | 操作 | 期望 | 实际 |
|------|------|------|------|
| 1 | 注入死信 (rc=0) | id=15661, status=active, rc=0 | ✅ |
| 2 | /recover | status=recovered, rc=1, pending_id=198501 | ✅ |
| 3 | 模拟 pending 失败 (retry=5) | pending.retry_count=5 | ✅ |
| 4 | 等待 IndexReplayWorker 15s | id 保持 15661 (复用), status=active, rc 保持 1 | ✅ |
| 5 | /recover | status=recovered, rc=2, pending_id=198502 | ✅ |
| 6 | 模拟 pending 失败 | status=active, rc 保持 2 (入死信不递增) | ✅ |
| 7 | /recover | rc=3 (达到 max) | ✅ |
| 8 | 模拟 pending 失败 | status=active, rc 保持 3 | ✅ |
| 9 | batch maxRc=3 | matched=0 (rc=3 < 3 = false, 过滤) | ✅ |
| 9' | batch maxRc=4 | matched=1 (运维强制) | ✅ |

### 4.2 关键证据

```
⭐ 步骤 4: 跨循环 id 保持不变, status 重置 active, recovery_count 保留
⭐ 步骤 6: 第 3 次入死信, recovery_count 保持 2 (符合设计: 入死信不递增)
⭐ 步骤 9: max_recovery_count 限位有效: rc=3 不再被自动恢复
```

### 4.3 回归测试

`_test_day710_recovery.py` (Day 7.10 原测试) 全部通过:
- 6 条测试死信按规则自动恢复 ✓
- max_recovery_count 限位生效 ✓

---

## 5. 关键代码片段

### 5.1 advisory lock (DeadLetterRecoveryService.cs)

```csharp
public static async Task<bool> TryWithAdvisoryLockAsync(
    ProductDbContext db, Func<Task> work, CancellationToken ct)
{
    var conn = (NpgsqlConnection)db.Database.GetDbConnection();
    if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT pg_try_advisory_xact_lock(@key)";
    cmd.Parameters.Add(new NpgsqlParameter("key", AdvisoryLockKey));  // 0x44524C44 = "DRLD"
    var result = await cmd.ExecuteScalarAsync(ct);
    if (result is bool got && got)
    {
        await work();
        return true;
    }
    return false;
}
```

### 5.2 死信复用 (IndexReplayWorker.cs)

```csharp
// 查找同 operation + payload 的最近 recovered 死信
var existingDead = await db.SearchIndexDeadLetters
    .Where(d => d.Operation == p.Operation
             && d.Payload == p.Payload
             && d.Status == "recovered")
    .OrderByDescending(d => d.RecoveredAt)
    .FirstOrDefaultAsync(ct);

if (existingDead != null)
{
    // 复用: 更新 retry_count + last_error + status 重置为 active
    // RecoveryCount 保持不变 (入死信不递增, 恢复时才 +1)
    existingDead.RetryCount = p.RetryCount;
    existingDead.LastError = p.LastError;
    existingDead.Status = "active";
    existingDead.MovedAt = now;
    existingDead.RecoveredAt = null;
    existingDead.RecoveredToPendingId = null;
}
```

### 5.3 死信恢复 (Program.cs /recover)

```csharp
// 改 status 而非删行
dead.Status = "recovered";
dead.RecoveryCount += 1;
dead.LastRecoveryAt = now;
dead.LastRecoveryError = null;
dead.RecoveredAt = now;
await db.SaveChangesAsync(ct);
dead.RecoveredToPendingId = pending.Id;
await db.SaveChangesAsync(ct);
```

---

## 6. 关键 SQL 索引

```sql
-- 部分索引: worker 扫描仅命中 active (节省 50% 空间)
CREATE INDEX idx_dead_letter_active_recovery
    ON search_index_dead_letter (status, recovery_count, last_recovery_at)
    WHERE status = 'active';

-- cleanup 索引: 只清已恢复
CREATE INDEX idx_dead_letter_recovered_at
    ON search_index_dead_letter (recovered_at)
    WHERE status = 'recovered';

-- 复用查找索引: op + payload hash + status
CREATE INDEX idx_dead_letter_payload_hash
    ON search_index_dead_letter (operation, md5(payload::text), status);
```

---

## 7. 生产部署清单

1. ✅ 执行 migration 015: `python spike-test/_apply_migration_015.py` (15595 行回填 status='active')
2. ✅ 部署新代码
3. ⚠️ 评估并开启 `dead_letter.auto_recovery_enabled=true`
4. ⚠️ 监控 `idx_dead_letter_active_recovery` 索引大小 (recovered 行累计可能很大)
5. ⚠️ 监控 DeadLetterCleanupService (确保只清 recovered 行)

---

## 8. 💡 后续改进建议 (Day 7.11+ 候选)

1. **dead_letter 软删除回收**: `recovered` 行 30 天后归档到 `search_index_dead_letter_archive` 表
2. **batch 端点限流**: `/recover-batch` 加 QPS 限制, 避免运维误操作打爆 Meili
3. **advisory lock 监控**: 锁等待 > 5s 时告警, 说明批量恢复拥堵
4. **payload 哈希索引改用 GIN**: `(payload_jsonb)` 替代 `md5(payload::text)`, 支持更复杂查询
5. **恢复原因字段**: 加 `recovery_reason` 列 (worker / admin / manual), 便于审计
6. **advisory lock 改用会话级**: 用 `pg_try_advisory_lock` + 显式释放, 跨事务可用
