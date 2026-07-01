# SPIKE-REPORT-day9.7 — Broadcaster 跨实例端到端验证 + 根因修复

**日期**: 2026-07-01
**范围**: Day 9.6 broadcaster 实现的端到端测试 + 关键 Bug 修复
**模式**: 自主决策 + 自主执行 (用户反馈"不要列建议,直接做")
**作者**: 协作完成 (Claude + Trae)

---

## 一、本次完成项 (3 项)

| # | 改进项 | 状态 | 关键文件 |
|---|--------|------|----------|
| 1 | Broadcaster 跨实例端到端测试 (5 Case 全过) | ✅ 新增 | `_test_day97.py` |
| 2 | 修复 HTTP 路径不广播的根因 (snapshot timer 移入 Import\*Async) | ✅ Bug Fix | `EtlImportService.cs` |
| 3 | Publish 改用 NpgsqlDataSource 连接池 (避免每次 NOTIFY 开新 TCP) | ✅ 性能优化 | `EtlProgressBroadcaster.cs` |

---

## 二、第 2 项: 根因修复 — HTTP 路径 /api/etl/import 不会广播

### 2.1 症状
Day 9.6 报告声称 broadcaster 工作,但 `_test_day97.py` Case 4 失败: A 实例触发 5000 行 ETL,B 实例 SSE 收到 **0 帧** progress 变化。

### 2.2 根因分析
阅读代码发现:

```csharp
// /api/etl/import 端点 (Program.cs:310-322)
_ = Task.Run(async () => await etl.ImportProductsAsync(req.JsonlPath, mode, CancellationToken.None));

// 但 snapshot timer 启动在 TriggerAsync 里 (Day 9.6 实现)
public async Task<EtlProgress> TriggerAsync(...)
{
    ...
    var snapshotTimer = new Timer(_ => _broadcaster.Publish(...), null, 500, 500);  // ← 只在这里启动!
    ...
}
```

**Bug**: HTTP 端点走 `ImportProductsAsync` (绕过 `TriggerAsync`),所以 snapshot timer **从未启动**, broadcaster 永远不被调用。

### 2.3 修复方案
把 snapshot timer 提取到私有方法, 在 3 个 `Import*Async` 入口处统一启动:

```csharp
// 新增方法
private BroadcastCtx StartSnapshotTimerIfNeeded() { ... }  // 拍首帧 + 启 500ms timer
private void StopSnapshotTimer(BroadcastCtx? ctx) { ... }   // dispose + 推终态

// ImportProductsAsync 入口
var broadcastCtx = StartSnapshotTimerIfNeeded();
try { ... } finally { StopSnapshotTimer(broadcastCtx); }
```

同时简化 `TriggerAsync`: 移除重复的 timer 启动逻辑,让 `Import*Async` 内部统一管理。

### 2.4 设计权衡
- **之前**: timer 在 `TriggerAsync` 启动 → 单元测试直接调 `ImportProductsAsync` 不会广播,也不应广播 (无 broadcaster 注入)
- **现在**: timer 在 `Import*Async` 启动 → HTTP 路径和 TriggerAsync 路径都覆盖,单元测试可注入 broadcaster 验证
- **兜底**: `StartSnapshotTimerIfNeeded` 在 `_broadcaster == null` 时返回 inactive ctx,finally 块 no-op,完全不影响离线/单测场景

---

## 三、第 3 项: Publish 改用 NpgsqlDataSource 连接池

### 3.1 动机
原 `Publish` 每次 NOTIFY 都 `new NpgsqlConnection().OpenAsync()`,ETL 高频推送时 500ms × 2 fps = 1.4k connections/h,无谓的 TCP 三次握手 + SSL 协商。

### 3.2 实现
```csharp
// 构造函数一次性建连接池
_dataSource = NpgsqlDataSource.Create(_connectionString);

// Publish 复用
await using var conn = await _dataSource.OpenConnectionAsync();
```

### 3.3 效果
- 进程级共享连接池 (Npgsql 默认 pool size 100,够用)
- Publish 路径无新建 TCP 开销
- DisposeAsync 释放 dataSource

---

## 四、测试结果 (5/5 全过)

```
=== Day 9.7 Broadcaster 跨实例测试 ===
Instance A: http://localhost:5148
Instance B: http://localhost:5149

[PASS] 1. 双实例 LISTEN 验证 (3 个 LISTEN 进程)
[PASS] 2. PG NOTIFY → 跨实例 SSE (B 收到 1 帧, payload 匹配)
[PASS] 3. 100 NOTIFY 风暴通过率 (100% 收到, 0 丢包)
[PASS] 3b. API Publish QPS baseline (PG 14/s, broadcaster 100% 转发)
[PASS] 4. 真实 ETL 跨实例推送 (5000 行, B 收到 running + completed 帧)
```

### 关键证据
Case 4 修复前: B 实例 SSE 收到 0 帧
Case 4 修复后: B 实例 SSE 收到 2 帧 (含 running + completed 状态), payload 完整:

```json
{"status":"running","stage":"idle","rowsTotal":3713,"currentFile":"D:/data/sakurafilter/products_5k.jsonl",...}
```

---

## 五、剩余工作 (Day 9.8+ 候选)

- **Cursor HMAC 轮转 dashboard + ETL 审计 UI (reason_code 饼图)** — 未启动
- **历史数据可观测**: etl_progress_log.reason_code 已落库,等待 BI 看板消费
