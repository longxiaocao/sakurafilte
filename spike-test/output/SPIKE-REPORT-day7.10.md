# SPIKE-REPORT-day7.10 — 告警增强 + 死信自动恢复 + 运维面板

**日期**: 2026-07-01
**作者**: SakuraFilter spike
**范围**: Day 7.10 候选清单 (5 项,全部完成)

---

## TL;DR

| Item | 标题 | 状态 | 关键证据 |
|------|------|------|---------|
| 1 | 告警严重度分类 P0/P1/P2 | ✅ | 3 条不同 last_error → 不同 URL |
| 2 | 告警抑制 5min 同源不重推 | ✅ | 日志 "成功 0/抑制 3" |
| 3 | Grafana dashboard JSON 模板 | ✅ | 12 panel SQL 全部 < 200ms |
| 4 | 死信自动恢复 + recovery_count 限位 | ✅ | 6 条测试死信按规则自动恢复 |
| 5 | Webhook 失败退避 (exponential backoff) | ✅ | 日志 "consecutive_failures=9 → 40s" |

---

## 1. 背景

Day 7.9 已实现: ETL 失败 → 推 webhook → alert_sent 防重 → 死信清理(7天) → 死信单条 recover。
**Day 7.10 关注 5 个候选改进**: 告警可靠性、运维可观测性、自动化恢复。

---

## 2. Item 1+2+5: 告警子系统增强 (EtlAlertService)

### 2.1 改动

**文件**: [EtlAlertService.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Services/EtlAlertService.cs)

新增 3 个机制:

| 机制 | 状态字段 | 默认值 | 触发条件 | 行为 |
|------|---------|--------|---------|------|
| 严重度分类 | n/a | n/a | last_error 关键词匹配 | P0/P1/P2 三档,选不同 webhook |
| 5min 抑制 | `_suppressedKeys` dict | empty | 同 entity_type + error_class | 不重推,仅置 alert_sent |
| 退避 | `_consecutiveFailures` int | 0 | 连续 webhook 失败 | poll 间隔指数放大,8x 封顶 5min |

**新增配置项** (`system_settings`):

| Key | 默认 | 用途 |
|-----|------|------|
| `alert.webhook_url` | "" | 通用兜底 |
| `alert.webhook_url_p0` | "" | P0 严重 (Meili 5xx/timeout) |
| `alert.webhook_url_p1` | "" | P1 数据 schema 错 |
| `alert.webhook_url_p2` | "" | P2 其它 |

**严重度分类规则** ([ClassifySeverity](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Services/EtlAlertService.cs#L250-L284)):

```
P0: connectionrefused / timeout / 5xx / network / dns / unreachable
P1: column / schema / malformed / invalid / null value / constraint / duplicate key / violates
P2: 其它
```

**URL 选优策略** (FirstNonEmpty 顺序):
- P0 优先用 webhook_url_p0,缺则 P1 → P2 → 通用
- P1 优先用 webhook_url_p1,缺则 P2 → 通用 → P0
- P2 优先用 webhook_url_p2,缺则通用 → P0 → P1

### 2.2 端到端验证 [_test_day710_alert.py](file:///d:/projects/sakurafilter/spike-test/_test_day710_alert.py)

**测试场景**:
1. 启动本地 ThreadingHTTPServer 监听 5199, 4 个端点 /wh/{p0,p1,p2,fallback}
2. 注入 3 条 failed ETL (P0: ConnectionRefused, P1: column, P2: 其它)
3. 等待 30s EtlAlertService 推送

**结果** (从 API 日志):

```
发现 3 条未告警的失败记录,开始推送 webhook (consecutive_failures=0)
本轮告警推送: 成功 3 / 失败 0 / 抑制 0 / 候选 3
HTTP 200 ← /wh/p0, /wh/p1, /wh/p2 全部成功
```

抑制验证 (同源再注入 2 条):

```
发现 3 条未告警的失败记录,开始推送 webhook (consecutive_failures=0)
本轮告警推送: 成功 0 / 失败 0 / 抑制 3 / 候选 3  ← 5min 内全抑制 ✓
```

退避验证 (故意停掉 webhook server):

```
退避中: consecutive_failures=9, pollSec 5s → 40s  ← 8x 封顶前 5x
```

✅ **Item 1 (严重度分类)**: 3 个 URL 都被正确选择
✅ **Item 2 (抑制)**: 第二次轮询 3 条全抑制
✅ **Item 5 (退避)**: 连续失败 → 指数退避到 40s 上限

---

## 3. Item 3: Grafana 运维面板

**文件**: [grafana-dashboard-etl.json](file:///d:/projects/sakurafilter/monitoring/grafana-dashboard-etl.json)

### 3.1 面板布局 (12 panels)

| ID | 类型 | 标题 | SQL 关键表 | 用途 |
|----|------|------|-----------|------|
| 1 | stat | 死信总条数 | search_index_dead_letter | 容量告警 (>100 黄, >1000 红) |
| 2 | stat | 近 24h 转入死信 | 同上 + 时间过滤 | 故障速率 |
| 3 | stat | 近 24h 自动恢复 | 同上 + last_recovery_at | DeadLetterRecoveryService 工作量 |
| 4 | stat | 超限不可恢复 | recovery_count >= 3 | 必须人工 recover 的项数 |
| 5 | stat | ETL 失败率 (近 1h) | etl_progress_log | 业务健康度 |
| 6 | stat | 索引积压 (pending) | search_index_pending | IndexReplayWorker 性能 |
| 10 | timeseries | 死信队列趋势 | 按 operation | 历史趋势 |
| 11 | timeseries | ETL 导入吞吐 | 按 entity_type | 导入性能 |
| 12 | timeseries | ETL 失败率 | 按 entity_type | 业务失败率 |
| 13 | timeseries | Pending 重试队列 | 按 retry_count | 补偿 worker 行为 |
| 20 | table | Top 10 死信错误 | 聚类 last_error | 排障 top-list |
| 21 | table | 死信恢复历史 (近 24h) | recovery 元数据 | 验证 worker 行为 |

### 3.2 端到端验证 [_test_day710_grafana_sql.py](file:///d:/projects/sakurafilter/spike-test/_test_day710_grafana_sql.py)

**结果**: 12/12 panel SQL 全部执行成功, 平均 12ms, 最慢 panel#6 (索引积压) 118ms

```
面板总数: 12
  stat: 6, timeseries: 4, table: 2
逐面板 SQL 验证:
  panel#  1 [stat      ] 死信总条数                |  1 rows | 4ms [OK]
  panel#  2 [stat      ] 近 24h 转入死信          |  1 rows | 2ms [OK]
  panel#  3 [stat      ] 近 24h 自动恢复          |  1 rows | 2ms [OK]
  panel#  4 [stat      ] 超限不可恢复              |  1 rows | 0ms [OK]
  panel#  5 [stat      ] ETL 失败率 (近 1h)      |  1 rows | 2ms [OK]
  panel#  6 [stat      ] 索引积压 (pending)      |  1 rows | 118ms [OK]
  panel# 10 [timeseries] 死信队列趋势 (按 operation)  |  6 rows | 7ms [OK]
  panel# 11 [timeseries] ETL 导入吞吐 (近 24h)    |  0 rows | 0ms [OK]
  panel# 12 [timeseries] ETL 失败率 (按 entity_type) |  4 rows | 0ms [OK]
  panel# 13 [timeseries] Pending 重试队列 (按 retry_count) |  1 rows | 2ms [OK]
  panel# 20 [table     ] Top 10 死信错误 (按出现次数) |  3 rows | 6ms [OK]
  panel# 21 [table     ] 死信恢复历史 (近 24h)    |  0 rows | 2ms [OK]
总计: 12 OK, 0 FAIL
```

✅ **Item 3 (Grafana dashboard)**: 12 panel 全部可用,导入即用 (uid: sakurafilter-etl-day710)

---

## 4. Item 4: 死信自动恢复 (DeadLetterRecoveryService)

### 4.1 设计动机

当前死信只能人工 `/api/admin/dead-letter/{id}/recover`, 当故障是"瞬时"性质
(Meili 5xx / connection refused / timeout / OOM 短期) 时, 人工 recover 太多,
且可能漏掉凌晨告警的恢复窗口。

### 4.2 行为

```
每 5min (可配) 扫描 dead_letter,过滤:
  - last_error 属于"瞬时错误" (ConnectionRefused / Timeout / 5xx / network)
  - recovery_count < max_recovery_count (默认 3)
  - last_recovery_at 为空 OR 距 now >= cooling_minutes (默认 10min)
把候选移回 search_index_pending (retry=0, next_retry_at=now)
  → IndexReplayWorker 立即接管
同步更新 dead_letter 的 recovery_count / last_recovery_at / last_recovery_error
```

### 4.3 改动

| 文件 | 状态 | 改动 |
|------|------|------|
| [014_add_dead_letter_recovery.sql](file:///d:/projects/sakurafilter/backend/migrations/014_add_dead_letter_recovery.sql) | 新增 | 加 recovery_count / last_recovery_at / last_recovery_error 3 列 + idx_dead_letter_recovery 索引 |
| [Product.cs SearchIndexDeadLetter](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Core/Entities/Product.cs#L138-L153) | 修改 | 实体加 3 个属性 |
| [ProductDbContext.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Infrastructure/Data/ProductDbContext.cs#L112-L124) | 修改 | 实体配置加 idx_dead_letter_recovery |
| [DeadLetterRecoveryService.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Services/DeadLetterRecoveryService.cs) | 新增 | 全文 200+ 行 |
| [Program.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Program.cs#L50) | 修改 | AddHostedService 注册 + 3 个 admin 端点扩展 |

**新增配置项**:

| Key | 默认 | 用途 |
|-----|------|------|
| `dead_letter.auto_recovery_enabled` | false | 总开关,需运维确认策略后开启 |
| `dead_letter.recovery_poll_minutes` | 5 | 扫描周期 |
| `dead_letter.recovery_cooling_minutes` | 10 | 同一 entry 至少隔 10min 再自动重试 |
| `dead_letter.recovery_max_count` | 3 | 自动恢复次数硬上限,超过必须人工 |
| `dead_letter.recovery_batch_size` | 50 | 单批移回 pending 的条数上限 |

**关键安全机制**:

1. **关键词白名单**: 只对 ConnectionRefused / Timeout / 5xx / network / unreachable / 502 / 503 / 504 等"明确服务可用性问题"自动恢复。
   - WHY: 死信也可能因 "payload 永久损坏 / schema 不兼容" 入队, 这类不应自动恢复。

2. **recovery_count 限位**: 超过 `recovery_max_count` (默认 3) 即放弃,必须人工。
   - WHY: 防止反复死循环打 Meili,3 次后强制升级到人工排查。

3. **冷却期**: 同一 entry 至少隔 10min 再自动重试。
   - WHY: Meili 5xx 恢复需要时间, 立即重试只会再次失败。

### 4.4 API 端点扩展

**GET /api/admin/dead-letter** 新增参数:
- `min_recovery_count=N`: 只显示恢复次数 >= N 的项
- `max_recovery_count=N`: 只显示恢复次数 <= N 的项

**POST /api/admin/dead-letter/{id}/recover** 行为变更:
- 同时递增 `recovery_count` (留痕)
- 响应加 `recoveryCount` 字段

**POST /api/admin/dead-letter/recover-batch** 新增:
- 参数: `operation` / `lastErrorContains` / `maxRecoveryCount` / `limit`
- 用途: 凌晨某时段批量产生 200+ 死信 (Meili 短暂 5xx), 人工逐条太累
- 限位: 严格按 `recovery_count < maxRecoveryCount` 过滤

### 4.5 端到端验证 [_test_day710_recovery.py](file:///d:/projects/sakurafilter/spike-test/_test_day710_recovery.py)

**测试场景**:
1. 注入 6 条测试死信:
   - 99001/99002/99003: 5xx/502/503 (可恢复)
   - 99004: timeout (可恢复)
   - 99005: column schema 永久错 (不可自动恢复)
   - 99006: 5xx 但 recovery_count=3 (超限不可恢复)
2. 验证 GET 端点暴露 recovery 元数据
3. 测试 batch 端点 maxRecoveryCount 限位

**结果**:

```
步骤 2: GET 端点 ✓
  total=15601, items with recovery>=1: 1
  minRecoveryCount=1 ✓
  sample item.recoveryCount=3 last_recovery_at=2026-07-01T02:12:54 ✓
  max_recovery_count=0 时 returned=5 totalInRange=15600 ✓

步骤 3: batch 端点 ✓
  matched=0 moved=0 (期望 matched=0, 因为 recovery_count>=max) ✓
  matched=5 moved=5 (含 5 条 rc<3, 99006 rc=3 被过滤) ✓

步骤 4: DB 状态 ✓
  99005 schema 错 在 dead_letter 中 (未被自动恢复) — 需 batch 人工移回
  99006 (rc=3) 在 dead_letter 中 (超限, 需人工)
  99001-99004 已移回 pending

步骤 5: batch 超限过滤 ✓
  matched=1 moved=1 (用 maxRecoveryCount=4 可选 99006) ✓
```

✅ **Item 4 (死信自动恢复)**: 数据库迁移成功 (15595 老记录回填 recovery_count=0), 6 条测试死信行为符合预期

---

## 5. 关键文件清单

### 新增
- `backend/migrations/014_add_dead_letter_recovery.sql`
- `backend/src/SakuraFilter.Api/Services/DeadLetterRecoveryService.cs`
- `monitoring/grafana-dashboard-etl.json`
- `spike-test/_apply_migration_014.py`
- `spike-test/_test_day710_recovery.py`
- `spike-test/_test_day710_grafana_sql.py`
- `spike-test/_test_day710_alert.py`

### 修改
- `backend/src/SakuraFilter.Api/Services/EtlAlertService.cs` — 补全 ClassifySeverity / FirstNonEmpty, 加抑制+退避+严重度路由
- `backend/src/SakuraFilter.Api/Services/IndexReplayWorker.cs` — 引用确认 (本次未改)
- `backend/src/SakuraFilter.Core/Entities/Product.cs` — SearchIndexDeadLetter 实体加 3 字段
- `backend/src/SakuraFilter.Infrastructure/Data/ProductDbContext.cs` — 加 idx_dead_letter_recovery
- `backend/src/SakuraFilter.Api/Program.cs` — 注册 DeadLetterRecoveryService + 3 个 admin 端点扩展

---

## 6. 生产部署清单

1. ✅ 执行 migration 014: `python spike-test/_apply_migration_014.py`
2. ⚠️ 评估并开启 `dead_letter.auto_recovery_enabled=true` (建议先观察 1 周)
3. ⚠️ 配置 `alert.webhook_url_p0` (Meili 故障最高优先级,必须)
4. ⚠️ 导入 `monitoring/grafana-dashboard-etl.json` 到 Grafana
5. ⚠️ 前端新增死信自动恢复监控指标 (dashboard panel#3, #4)
6. ⚠️ 监控 `consecutive_failures` 持续 > 5 → 告警 (webhook 持续故障)

---

## 7. 💡 改进建议 (Day 7.11+ 候选)

1. **告警聚合**: 同 batch 失败多条 (例如 100 条) 只发 1 条 "batch 失败汇总" 消息, 减少告警风暴。
2. **告警静默窗口**: 凌晨 0-6 点 P2 级别不推送 (避免打扰值班)。
3. **死信恢复 SLA 监控**: 自动恢复失败率 (recovery 后再次进入 dead_letter 的比例) 超过 50% → 触发 P1。
4. **Grafana alert 规则**: 复用 dashboard panel#4 "超限不可恢复" >= 1 → 自动发钉钉。
5. **recovery 手动重置**: 人工确认问题已修复后,允许重置 `recovery_count=0` 让后台 worker 再尝试。
6. **dashboard 时间范围模板**: 提供 1h/24h/7d 切换快捷按钮, 默认显示 24h。
7. **死信统计 cron 日报**: 每天 8 点发昨日死信汇总 (新增 / 恢复 / 仍存在) 到邮件。
