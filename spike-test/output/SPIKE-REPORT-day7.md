# Day 7 改进建议落地报告 (apps 数据修复 + dead_letter)

**目标**: 修复 Day 6 遗留的 apps 写入 0 行问题,补齐 Meili 写入死信队列,完成 ETL 三件套端到端验证。

---

## 1. 改进清单与完成情况

| # | 建议 | 优先级 | 状态 | 交付 |
|---|------|--------|------|------|
| 1 | 修复 apps ETL 0 行 (大小写列名 + 数据对齐) | 高 | ✅ 完成 | etl_clean.py + 验证 apps=53 行 |
| 2 | dead_letter 表 + Worker 超限转移 | 中 | ✅ 完成 | migration 011 + IndexReplayWorker.ProcessDeadLetterAsync |
| 3 | 验证 dead_letter 自动转移 | 中 | ✅ 完成 | 500 条 retry=5 已自动转入 |
| 4 | apps/xrefs/products 三件套全链路验证 | 高 | ✅ 完成 | 1949/36/53 行入库 |
| 5 | Day 6 改进建议全部完成 | - | ✅ 100% | 见上 |

---

## 2. 根因分析:apps 写入 0 行

### 2.1 表象
apps ETL 进度报告 `read=55, inserted=0, updated=0, skipped=0, errors=0`,但 DB 中 `machine_applications` 仍为 0 行。

### 2.2 排查过程
1. **数据层**: `_check_apps.py` 显示 apps 1602 行中 1547 行的 product_oem 在 products 中找不到 (源数据问题)
2. **ETL 层**: `etl_clean.py` 在清洗时未丢弃无对应产品的行
3. **清洗层**: `clean_apps_sheet` 输出的 55 行全部 `machine_brand=None`/`machine_model=None`,被 SQL `WHERE machine_brand IS NOT NULL` 全部过滤

### 2.3 真因(2 层 bug)

**Bug 1 - 列名大小写不匹配**:
- 实际 Excel 列名(机型区 sheet):`machine brand`、`machine model`(小写)
- 原脚本查询:`row.get('Machine Brand')`、`row.get('Machine Model')`(大写)
- 结果:所有 brand/model 返回 None

**Bug 2 - 数据未对齐**:
- 机型区引用了产品区不存在的 OEM(SO/SN/SA 前缀 vs 产品的 SH 前缀)
- ETL 时 `oemMap.TryGetValue` 失败 → skip,真实进度被掩盖

### 2.4 修复
[etl_clean.py](file:///d:/projects/sakurafilter/spike-test/etl_clean.py) 修改两处:

1. **大小写不敏感列查找**:
```python
col_map = {str(c).strip().lower(): str(c).strip() for c in df.columns}
def get(row, name: str):
    real = col_map.get(name.lower())
    return row.get(real) if real else None
```

2. **apps/xrefs 与 products OEM 集对齐**:
```python
product_oem_set = {p['oem_no_normalized'] for p in products}
xrefs = [x for x in xrefs if x['product_oem'] in product_oem_set]
apps = [a for a in apps if a['product_oem'] in product_oem_set]
# 统计丢弃数: xrefs_aligned_dropped, apps_aligned_dropped
```

---

## 3. ETL 三件套端到端验证 (真实数据 1949 OEM)

### 3.1 清洗结果

| 实体 | 读入 | 输出 (原始) | 对齐后输出 | 丢弃 |
|------|------|------------|-----------|------|
| products | 2132 | 2132 | 2132 | 0 |
| xrefs | 2017 | 2017 | 36 | 1981 (OEM 不在产品集) |
| apps | 1650 | 1602 | 55 | 1547 (OEM 不在产品集) |

### 3.2 ETL 导入结果(全部成功)

| ETL | mode | read | inserted | updated | skipped | errors | 耗时 |
|-----|------|------|----------|---------|---------|--------|------|
| products | full-load | 2132 | 1949 | 0 | 0 | 0 | 12.15s |
| xrefs | upsert | 36 | 0 | 36 | 0 | 0 | 8.46s |
| apps | upsert | 55 | 0 | 53 | 0 | 0 | 6.03s |

### 3.3 DB 终态验证
```sql
SELECT count(*) FROM products;             -- 1949
SELECT count(*) FROM cross_references;     -- 36
SELECT count(*) FROM machine_applications; -- 53
```

apps 样本(已正确填充 machine_brand/machine_model):
```
(163, 'ACF', 'AL 7')
(163, 'ACF', 'AL 7 D')
(163, 'ACF', 'H 7 C')
```

### 3.4 ETL 关键设计

- **mode 三态统一**:`full-load` (TRUNCATE+INSERT) / `insert-only` (ON CONFLICT DO NOTHING) / `upsert` (ON CONFLICT DO UPDATE)
- **DISTINCT ON + ctid DESC**:处理 xrefs/apps 源数据内部重复,取最新一行
- **advisory lock**:products=7740001, xrefs=7740002, apps=7740003 防多实例并发
- **幂等性**:UNIQUE 索引 + ON CONFLICT,重复跑 ETL 行数稳定

---

## 4. dead_letter 表 + Worker 转移

### 4.1 表设计 [011_add_search_index_dead_letter.sql](file:///d:/projects/sakurafilter/backend/migrations/011_add_search_index_dead_letter.sql)

```sql
CREATE TABLE search_index_dead_letter (
    id          BIGSERIAL PRIMARY KEY,
    original_id BIGINT NOT NULL,           -- 来源 pending.id
    operation   VARCHAR(20) NOT NULL,
    payload     JSONB NOT NULL,
    retry_count INT NOT NULL DEFAULT 5,
    last_error  TEXT,
    created_at  TIMESTAMPTZ NOT NULL,      -- 原入队时间
    moved_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_dead_letter_moved_at ON search_index_dead_letter (moved_at DESC);
CREATE INDEX idx_dead_letter_operation ON search_index_dead_letter (operation);
```

### 4.2 Worker 改造 [IndexReplayWorker.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Services/IndexReplayWorker.cs)

新增 `ProcessDeadLetterAsync`:每 10s 轮询把 `retry_count >= 5` 的 pending 条目复制到 dead_letter 并从 pending 删除。

```csharp
private async Task ProcessDeadLetterAsync(CancellationToken ct)
{
    var exhausted = await db.SearchIndexPending
        .Where(p => p.RetryCount >= MaxRetryCount)
        .OrderBy(p => p.Id)
        .Take(BatchSize)
        .ToListAsync(ct);
    if (exhausted.Count == 0) return;

    var deadLetters = exhausted.Select(p => new SearchIndexDeadLetter
    {
        OriginalId = p.Id,
        Operation = p.Operation,
        Payload = p.Payload,
        RetryCount = p.RetryCount,
        LastError = p.LastError,
        CreatedAt = p.CreatedAt,
        MovedAt = DateTime.UtcNow
    }).ToList();
    await db.SearchIndexDeadLetters.AddRangeAsync(deadLetters, ct);
    db.SearchIndexPending.RemoveRange(exhausted);
    await db.SaveChangesAsync(ct);
    _logger.LogWarning("已转死信: {Count} 条", deadLetters.Count);
}
```

### 4.3 实测效果(Meili 未运行场景)

| 阶段 | pending | dead_letter | 总数 |
|------|---------|-------------|------|
| 启动前 (Day 6 累计) | 3898 (retry=3) | 0 | 3898 |
| 启动 12s 后 | 3398 (retry=4) | 500 (retry=5) | 3898 |

**结论**:
- 500 条 retry=5 已自动转入 dead_letter
- pending+dead 总数 3898 不变(无数据丢失)
- 死信条目含 last_error="CommunicationError"(Meili 不可达)
- moved_at 字段记录转入时间,可按时间窗口排查

### 4.4 人工恢复流程
```sql
-- 1) 查看死信
SELECT id, original_id, operation, retry_count, last_error, moved_at
FROM search_index_dead_letter ORDER BY moved_at DESC LIMIT 50;

-- 2) 排查后,移回 pending 重试 (retry_count 重置)
INSERT INTO search_index_pending (operation, payload, retry_count, created_at, next_retry_at)
SELECT operation, payload, 0, created_at, now()
FROM search_index_dead_letter WHERE id = ?;
DELETE FROM search_index_dead_letter WHERE id = ?;
```

---

## 5. 待办(下一轮)

| 任务 | 优先级 | 备注 |
|------|--------|------|
| Meili 启动后,验证 IndexReplayWorker 自动消化 pending(应看到 indexed 增长,indexPending 降为 0) | 高 | 需 Meilisearch 进程运行 |
| 1M 合成数据端到端压测 (products < 40s) | 高 | 复现 Power-law 行为 |
| 1M 端到端:apps/xrefs/pro 三表同步导入 (现仅单表串行) | 中 | 评估真实场景耗时 |
| ETL 进度 WebSocket/SSE 推送 | 低 | 当前轮询可工作 |

---

## 6. 修改文件清单

| 文件 | 改动 |
|------|------|
| [spike-test/etl_clean.py](file:///d:/projects/sakurafilter/spike-test/etl_clean.py) | 大小写不敏感查列 + apps/xrefs 对齐过滤 |
| [backend/migrations/011_add_search_index_dead_letter.sql](file:///d:/projects/sakurafilter/backend/migrations/011_add_search_index_dead_letter.sql) | 新建 dead_letter 表 |
| [backend/src/SakuraFilter.Core/Entities/Product.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Core/Entities/Product.cs) | 新增 SearchIndexDeadLetter 实体 |
| [backend/src/SakuraFilter.Infrastructure/Data/ProductDbContext.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Infrastructure/Data/ProductDbContext.cs) | 注册 SearchIndexDeadLetters DbSet + OnModelCreating |
| [backend/src/SakuraFilter.Api/Services/IndexReplayWorker.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Services/IndexReplayWorker.cs) | 新增 ProcessDeadLetterAsync,每 10s 转移 retry=5 条目 |
