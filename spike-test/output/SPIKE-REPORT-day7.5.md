# Day 7.5 改进建议落地报告 (运维可见性 + 错误原因拆分)

**目标**: 实施 Day 7 末尾提出的 3 项改进建议 + advisor 推荐的 skipped 原因拆分。

---

## 1. 完成情况

| # | 建议 | 状态 | 验证 |
|---|------|------|------|
| 1 | EtlProgress skipped 拆分为 missing_oem / null_field | ✅ 完成 | 单元测试 6 行 ETL read=6 skipped=4 拆分正确 |
| 2 | etl_clean.py 启动时列名严格校验 | ✅ 完成 | 严格/非严格两种模式 + 正负向测试 |
| 3 | dead_letter HTTP 端点 (查询 + 恢复) | ✅ 完成 | 端到端测试通过 |
| 4 | Meilisearch 端到端消化 | ⏸ 暂缓 | 离线环境无法安装,需 Linux 部署时补做 |

---

## 2. EtlProgress skipped 拆分

### 2.1 修改
[EtlProgress.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Etl/EtlImportService.cs#L14-L102) 增加 2 个计数:
```csharp
private long _skippedMissingOem;  // product_oem 在 products 集中找不到
private long _skippedNullField;   // 必填字段 (brand/model/oem_brand/oem_no_3) 为 null
public long SkippedMissingOem => Interlocked.Read(ref _skippedMissingOem);
public long SkippedNullField => Interlocked.Read(ref _skippedNullField);
public void IncrSkippedMissingOem() { Interlocked.Increment(ref _skipped); Interlocked.Increment(ref _skippedMissingOem); }
public void IncrSkippedNullField() { Interlocked.Increment(ref _skipped); Interlocked.Increment(ref _skippedNullField); }
```

进度 JSON 自动包含新字段:
```json
{
  "skipped": 4,
  "skippedMissingOem": 2,
  "skippedNullField": 2
}
```

### 2.2 ETL 调用点更新

[xrefs ETL](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Etl/EtlImportService.cs#L509): OEM 找不到 → `IncrSkippedMissingOem()`

[apps ETL](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Etl/EtlImportService.cs#L645-L662) **增加必填字段预检**:
```csharp
// Day 7.5: 必填字段预检 (SQL 的 WHERE machine_brand IS NOT NULL 静默过滤在这里显式化)
var brand = GetStringOrNull(doc, "machine_brand");
var model = GetStringOrNull(doc, "machine_model");
if (brand is null || model is null)
{
    Progress.IncrSkippedNullField();
    _logger.LogDebug("apps 行 {LineNo} 必填字段空: brand={Brand}, model={Model}", lineNo, ...);
    continue;
}
```
WHY: apps 之前 apps 列名 bug 导致 brand/model 全为 None,SQL WHERE 静默丢行,只能从 final count=0 反推。预检后 skipped_null_field 计数直接暴露问题。

### 2.3 单元测试结果 [_test_skipped_split.py](file:///d:/projects/sakurafilter/spike-test/_test_skipped_split.py)

| 场景 | 数据 | 期望 skipped 原因 | 实际 |
|------|------|------------------|------|
| 2 行有效 (brand+model 都有,OEM 真实) | TEST_A/M1, TEST_A/M2 | inserted/updated | updated=2 ✓ |
| 2 行 OEM 不存在 | FAKEXXX, FAKEYYY | skippedMissingOem=2 | 2 ✓ |
| 1 行 brand=null, 1 行 model=null | TEST/M3, TEST_B/null | skippedNullField=2 | 2 ✓ |

进度报告:
```
read: 6, inserted: 0, updated: 2, skipped: 4
skippedMissingOem: 2, skippedNullField: 2  ← 拆分生效
elapsedSec: 5.04
```

---

## 3. etl_clean.py 列名严格校验

### 3.1 实现 [etl_clean.py](file:///d:/projects/sakurafilter/spike-test/etl_clean.py#L60-L89)

```python
REQUIRED_COLUMNS = {
    '产品区': ['OEM NO.2', 'product name 3', 'Dimension 1 (D1)'],
    'OEM区': ['OEM NO.2', ' OEM Brand', 'OEM NO.3'],
    '机型区': ['OEM NO.2', 'Machine Brand', 'Machine Model', 'Engine Brand', 'Engine Type', 'Engine Energy'],
}

def validate_columns(sheet_name: str, actual_cols: list[str], strict: bool = True) -> list[str]:
    """启动时校验必填列 (大小写不敏感, 空格归一化)"""
    required = REQUIRED_COLUMNS.get(sheet_name, [])
    actual_norm = {str(c).strip().lower(): str(c).strip() for c in actual_cols}
    missing = [req for req in required if req.strip().lower() not in actual_norm]
    if missing and strict:
        raise ValueError(f"[列名校验失败] sheet '{sheet_name}' 缺少必填列: {missing}\n   实际列: {list(actual_cols)}")
    elif missing:
        log.warning(f"sheet '{sheet_name}' 缺少必填列: {missing}")
    else:
        log.info(f"sheet '{sheet_name}' 列名校验通过 ({len(required)} 项必填列)")
    return missing
```

### 3.2 启动日志(实际数据)
```
[INFO] 产品区: 2132 行, 23 列
[INFO] sheet '产品区' 列名校验通过 (3 项必填列)
[INFO] OEM区: 2017 行, 4 列
[INFO] sheet 'OEM区' 列名校验通过 (3 项必填列)
[INFO] 机型区: 1650 行, 27 列
[INFO] sheet '机型区' 列名校验通过 (6 项必填列)
```

### 3.3 负向测试(故意删列)
```
[WARNING] sheet '机型区' 缺少必填列: ['Machine Brand']
PASS 严格模式正确抛错:
   [列名校验失败] sheet '机型区' 缺少必填列: ['Machine Brand']
      实际列: ['Machine Model', 'OEM NO.2', 'Engine Brand', 'Engine Type', 'Engine Energy']
PASS 非严格模式:警告不抛错
```

---

## 4. dead_letter HTTP 端点

### 4.1 GET /api/admin/dead-letter 查询
[Program.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Program.cs#L147-L174)

**请求**:`GET /api/admin/dead-letter?limit=2&operation=index`
- `limit` (默认 50,最大 500)
- `operation` (可选,过滤 index/delete)

**响应**:
```json
{
  "total": 3898,
  "returned": 2,
  "limit": 2,
  "items": [{
    "id": 3501, "originalId": 186399, "operation": "index",
    "retryCount": 5, "lastError": "CommunicationError",
    "createdAt": "2026-06-30T16:30:35.553931Z",
    "movedAt": "2026-06-30T16:50:50.168944Z",
    "payloadPreview": "{\"Id\": 1552, \"D1Mm\": 152.00, ...}"
  }]
}
```

### 4.2 POST /api/admin/dead-letter/{id}/recover 恢复
[Program.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Program.cs#L176-L195)

**流程**:dead_letter → pending (retry=0, next_retry_at=now) → IndexReplayWorker 自动重试

**测试**:
```bash
POST /api/admin/dead-letter/3501/recover
→ {"recovered":true, "newPendingId":186797, "originalId":186399}
```
DB 验证:
- search_index_dead_letter: 3898 → 3897 (-1) ✓
- search_index_pending: +1 (id 186797, retry=0) ✓

### 4.3 已知坑(Day 7.5 修复)
PostgreSQL jsonb 不支持 `substring(jsonb, int, int)`,必须先 `::text` 转换:
```csharp
// 错误写法 (生成 500):
d.Payload.Substring(0, 200)
// SqlState: 42883, 函数 substring(jsonb, integer, integer) 不存在

// 正确写法 (Day 7.5):
d.Payload.ToString().Substring(0, 200)
```

---

## 5. Meilisearch 端到端测试 (暂缓)

**原因**: 当前离线环境无法下载 Meilisearch (github.com 不可达),但 nuget.org 可用。

**生产 Linux 部署时补做**:
1. `docker run -p 7700:7700 getmeili/meilisearch:v1.10`
2. 启动 API 后,IndexReplayWorker 应自动消化 search_index_pending
3. 验证项:
   - pending 队列从 3898 递减
   - dead_letter 不再增长
   - API 搜索响应能查到已索引产品

---

## 6. 修改文件清单

| 文件 | 改动 |
|------|------|
| [spike-test/etl_clean.py](file:///d:/projects/sakurafilter/spike-test/etl_clean.py) | 列名严格校验 + 大小写归一化 |
| [spike-test/_test_skipped_split.py](file:///d:/projects/sakurafilter/spike-test/_test_skipped_split.py) | skipped 拆分单测(6 行 4 skipped 验证通过) |
| [backend/src/SakuraFilter.Etl/EtlImportService.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Etl/EtlImportService.cs#L14-L102) | EtlProgress 加 skippedMissingOem / skippedNullField 双计数器 |
| [backend/src/SakuraFilter.Etl/EtlImportService.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Etl/EtlImportService.cs#L645-L666) | apps ETL COPY 阶段必填字段预检 |
| [backend/src/SakuraFilter.Api/Program.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Program.cs#L147-L195) | dead_letter GET/POST 端点 |
| [spike-test/output/SPIKE-REPORT-day7.5.md](file:///d:/projects/sakurafilter/spike-test/output/SPIKE-REPORT-day7.5.md) | 本报告 |

---

## 7. 下一轮待办

| 任务 | 优先级 | 备注 |
|------|--------|------|
| Meili 端到端验证(生产 Linux 部署时) | 高 | 需 meilisearch 进程运行,验证 pending→0 |
| 1M 合成数据 < 40s 压测 | 高 | 用户硬约束 |
| ETL 进度 WebSocket/SSE 推送 | 中 | 当前轮询可工作但延迟感强 |
| 前端添加 ETL 监控页 (pending / dead_letter 计数) | 中 | 数据已就绪,需 UI |
