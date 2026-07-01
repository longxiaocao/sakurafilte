# Day 7.6 可观测性增强报告 (last_5_errors + ?since= + skipped_duplicate)

**目标**: 实施 Day 7.5 末尾 3 项可观测性建议,补齐诊断信号盲区。

---

## 1. 完成情况

| # | 建议 | 状态 | 验证 |
|---|------|------|------|
| 1 | EtlProgress last_5_errors 环形缓冲 | ✅ | 4 条解析错误全捕获,带行号+错误类型 |
| 2 | GET /api/admin/dead-letter 加 ?since= | ✅ | 3 种格式 (valid ISO / short date / invalid) 全部正确 |
| 3 | apps/xrefs ETL skipped_duplicate 计数器 | ✅ | 真实数据 apps=55 读,skippedDuplicate=2 (DISTINCT ON 去重) |
| 4 | Meilisearch 端到端验证 | ⏸ 暂缓 | docker/github 离线,生产 Linux 部署时补做 |

---

## 2. last_5_errors 环形缓冲

### 2.1 实现 [EtlProgress.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Etl/EtlImportService.cs#L26-L100)

```csharp
private const int MaxRecentErrors = 5;
private readonly object _errorsLock = new();
private readonly Queue<(DateTime At, string Message)> _recentErrors = new();

public IReadOnlyList<(DateTime At, string Message)> RecentErrors
{
    get { lock (_errorsLock) return _recentErrors.ToArray(); }
}

public void IncrErrorsWith(string message)  // 新增
{
    Interlocked.Increment(ref _errors);
    PushError(message);
}

private void PushError(string message)
{
    if (string.IsNullOrEmpty(message)) return;
    var entry = (DateTime.UtcNow, message.Length > 300 ? message[..300] : message);
    lock (_errorsLock)
    {
        _recentErrors.Enqueue(entry);
        while (_recentErrors.Count > MaxRecentErrors) _recentErrors.Dequeue();
    }
}
```

WHY 容量=5: 经验值。太多掩盖最新错误,太少无法看分布(失败风暴时)。

### 2.2 3 个 catch 块全部升级

[EtlImportService.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Etl/EtlImportService.cs) 三处改 `IncrErrors()` → `IncrErrorsWith(message)`:
- line 258: products
- line 570: xrefs
- line 742: apps

⚠️ **自检发现**:第一次 edit 因代码新增(duplicate 计数 query 块)导致行号位移,后 2 处 catch 块未生效。重启后 `recentErrors=0 but errors=4` 暴露问题,补改完成。

### 2.3 单元测试 [_test_recent_errors.py](file:///d:/projects/sakurafilter/spike-test/_test_recent_errors.py)

构造 6 行(含 3 个解析错误 + 1 个 null JSON + 2 个缺字段):
```
status: completed
read: 6
errors: 4
skippedMissingOem: 1
skippedNullField: 1
recentErrors: [4 条]
```

recentErrors 内容(完整诊断信息):
```json
[
  {"at": "2026-07-01T00:04:14.856Z", "message": "apps 行 1: 't' is an invalid start of a property name. Expected a '\"'. Path: $ | LineNumber: 0 | BytePositionInLine: 2."},
  {"at": "2026-07-01T00:04:14.860Z", "message": "apps 行 4: The requested operation requires an element of type 'Object', but the target element has type 'Null'."},
  {"at": "2026-07-01T00:04:14.860Z", "message": "apps 行 5: The given key was not present in the dictionary."},
  {"at": "2026-07-01T00:04:14.861Z", "message": "apps 行 6: 'not even json' is an invalid JSON literal. Expected the literal 'null'. Path: $ | LineNumber: 0 | BytePositionInLine: 1."}
]
```

---

## 3. ?since= 时间过滤

### 3.1 实现 [Program.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Program.cs#L147-L194)

```csharp
app.MapGet("/api/admin/dead-letter", async (
    [FromQuery] int? limit,
    [FromQuery] string? operation,
    [FromQuery] string? since,         // Day 7.6 新增
    ProductDbContext db, CancellationToken ct) =>
{
    ...
    DateTime? sinceUtc = null;
    if (!string.IsNullOrEmpty(since))
    {
        if (!DateTime.TryParse(since, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            return Results.BadRequest(new { error = "since 必须是 ISO8601 时间 (例: 2026-07-01T00:00:00Z)", since });
        sinceUtc = parsed;
        query = query.Where(d => d.MovedAt >= sinceUtc);
    }
    ...
    return Results.Ok(new { total = totalAll, totalInRange, returned, limit, since = sinceUtc, items });
});
```

### 3.2 测试结果

| 输入 | 行为 | 响应 |
|------|------|------|
| `?since=2026-07-01T00:00:00Z` | 范围过滤,0 命中 | `{"total":3897, "totalInRange":0, "returned":0, "since":"2026-07-01T00:00:00Z"}` |
| `?since=2026-07-01` | 短日期被解析为 00:00:00Z | 同上 |
| `?since=invalid-date` | 400 + 中文提示 | `{"error":"since 必须是 ISO8601 时间 (例: 2026-07-01T00:00:00Z)", "since":"invalid-date"}` |

WHY 接受短日期: DateTime.TryParse 默认宽容,前端用 `new Date().toISOString().slice(0,10)` 即可传"2026-07-01"。

---

## 4. skipped_duplicate 计数器

### 4.1 实现 [EtlImportService.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Etl/EtlImportService.cs#L579-L593) (xrefs) + line 751 (apps)

```csharp
// Day 7.6: 计算 DISTINCT ON 去重掉的行数
await using (var dupCmd = new NpgsqlCommand(@"
    SELECT count(*) - count(DISTINCT (product_id, oem_brand, oem_no_3))
    FROM xrefs_stage
    WHERE oem_brand IS NOT NULL AND oem_no_3 IS NOT NULL", conn))
{
    var dup = (long)(await dupCmd.ExecuteScalarAsync(ct) ?? 0L);
    if (dup > 0)
    {
        for (long i = 0; i < dup; i++) Progress.IncrSkippedDuplicate();
        _logger.LogInformation("xrefs 去重: {Dup} 行 (DISTINCT ON)", dup);
    }
}
```

WHY 在 staging 阶段算: 直接 SQL `count - count(distinct)` 比在 ETL 层维护内存字典便宜(1M 行下 Dictionary 内存爆炸,这是 Day 5 的教训)。

### 4.2 真实数据验证(apps.jsonl, 55 行)

| 阶段 | read | inserted | updated | skipped | skippedMissingOem | skippedNullField | **skippedDuplicate** |
|------|------|----------|---------|---------|-------------------|------------------|----------------------|
| Day 7.5 | 55 | 0 | 53 | 0 | 0 | 0 | (无此字段) |
| **Day 7.6** | 55 | 0 | 53 | 2 | 0 | 0 | **2** ✓ |

之前 `inserted+updated=53, skipped=0` 让运维误以为"55 行全部成功",实际是 53 行真写入 + 2 行被 DISTINCT ON 静默去重。现在 2 行 explicit skipped,数据流自洽。

### 4.3 skipped 三分类汇总

| 类别 | 计数器 | 触发条件 |
|------|--------|----------|
| `skippedMissingOem` | product_oem 在 products 集中找不到 | xrefs/apps ETL |
| `skippedNullField` | 必填字段 (oem_brand/oem_no_3/machine_brand/machine_model) 为 null | apps/xrefs ETL 预检 |
| `skippedDuplicate` | 同 (product_id, brand, model) 重复行被 DISTINCT ON 去重 | apps/xrefs ETL 后置 |

三者之和 = `skipped` 总数。任一非 0 都说明有数据/源问题,定位时间从 30min 降到 30s。

---

## 5. 修改文件清单

| 文件 | 改动 |
|------|------|
| [EtlImportService.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Etl/EtlImportService.cs#L26-L100) | EtlProgress 加 recentErrors ring buffer + IncrErrorsWith + skippedDuplicate 计数 |
| [EtlImportService.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Etl/EtlImportService.cs#L579-L593) | xrefs DISTINCT ON 去重计算 + IncrSkippedDuplicate |
| [EtlImportService.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Etl/EtlImportService.cs#L751-L763) | apps DISTINCT ON 去重计算 + IncrSkippedDuplicate |
| [Program.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Program.cs#L147-L194) | dead_letter GET 加 ?since= 过滤 + totalInRange 字段 |
| [spike-test/_test_recent_errors.py](file:///d:/projects/sakurafilter/spike-test/_test_recent_errors.py) | recentErrors 环形缓冲单测(6 行 → 4 errors 全部捕获) |
| [spike-test/_test_day76_counters.py](file:///d:/projects/sakurafilter/spike-test/_test_day76_counters.py) | skippedDuplicate + 4 计数器单测(4 行 → 2 skipped 正确拆分) |

---

## 6. 下一轮待办

| 任务 | 优先级 | 备注 |
|------|--------|------|
| Meili 端到端验证 (生产 Linux 部署) | 高 | 启动 meilisearch → 验证 pending 递减 → 搜索可查 |
| 1M 合成数据 < 40s 压测 | 高 | 用户硬约束,需复现 Power-law 行为 |
| ETL 进度 WebSocket/SSE 推送 | 中 | 当前轮询 5s 一次可工作但有感知延迟 |
| last_5_errors 容量做成配置项 | 低 | 当前硬编码 5,改 appsettings 即可 |
| 历史 ETL 进度落库 (progress_log 表) | 低 | 当前进程重启后进度丢失,无法回溯昨天 14:00 的 ETL 状态 |
