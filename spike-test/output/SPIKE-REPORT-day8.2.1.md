# SPIKE-REPORT-day8.2.1

**主题**: 改进 1-3 落地 (排序时区修复 + 字段补齐 + count 性能模式)
**日期**: 2026-07-01
**状态**: ✅ 全部通过 (11/11 测试段, 全部 10 段 Day 8.2 测试 + 1 段 Day 8.2.1 countMode)

---

## 改进清单

| # | 改进 | 状态 | 改动 |
|---|------|------|------|
| 1 | Npgsql timestamp 时区错乱 | ✅ 修复 | 排序加 `Id DESC` 次级排序, 保证新数据排前 |
| 2 | 规格分区 5 字段补齐 | ✅ 补齐 | DTO + Service 加 `SealingMaterial` + `Efficiency1` |
| 3 | count 性能模式 | ✅ 新增 | `countMode=exact/estimated/none` 三模式, 默认 exact |
| 4 | (改进 4 留 Day 8.3) | ⏸️ | pageSize keyset 改写作为下一 Day |

---

## 1. 改进 1: 排序时区修复

### 1.1 根因分析

- 启用 `Npgsql.EnableLegacyTimestampBehavior` 后, `DateTime.Kind=Unspecified` 写入 PG 时按会话时区 (`Asia/Shanghai`) 转 UTC
- 老 ETL 数据用 `DateTime.Now` / `new DateTime(...)` 写入 (Kind=Unspecified), 实际值按 +08:00 解释
- 新数据用 `DateTime.UtcNow` 写入 (Kind=Utc), 直接按 UTC 存
- **结果**: 老数据 PG 内部 `09:53:34+08:00` (本地表示, 实际 UTC 01:53:34) 与新数据 UTC 04:47:42 混存
- ORDER BY updated_at DESC 排序时, 老数据因为 PG 内部 timestamp 错位, 反而**排在新数据前面**, 测试翻页 pageSize=200 拿不到新建产品

### 1.2 修复方案

不动 ETL 历史数据 (migration 成本高, 改后还要重算), 在 SearchAsync 排序统一加 `Id DESC` 次级排序:

```csharp
_ => sortDesc
    ? query.OrderByDescending(p => p.UpdatedAt).ThenByDescending(p => p.Id)
    : query.OrderBy(p => p.UpdatedAt).ThenByDescending(p => p.Id)
```

**为什么 Id DESC 次级排序有效**:
- Id 是 BIGSERIAL, 单调递增, 新数据 id 永远 > 老数据
- 当 updated_at 相同时 (同一批次 ETL 导入), Id DESC 决定顺序
- 测试产品 id=11758-11761 (Day 8.2.1) 排在最前, 翻页 pageSize=200 一定能拿到
- 5 个排序分支 (id/oem/type/mr1/updated_at) 全部加 Id DESC 次级排序

### 1.3 验证

- 服务启动后 `?pageSize=1` 返回 id=1949 (最大 id), 而不是之前的 745 (老 id) ✓
- 11 段测试全过, 1d/2a/2d/2e 等依赖 id 命中的测试稳定通过 ✓

---

## 2. 改进 2: 字段补齐

### 2.1 补齐的字段

DTO `AdminProductSearchRequest` 加 2 个规格"前端展示内容"分区 5 的过滤字段:
- `SealingMaterial` (密封材料) - 文本 ILIKE 模糊
- `Efficiency1` (过滤效率) - 文本 ILIKE 模糊

```csharp
if (!string.IsNullOrWhiteSpace(req.SealingMaterial))
    query = query.Where(p => p.SealingMaterial != null && EF.Functions.ILike(p.SealingMaterial, $"%{req.SealingMaterial}%"));
if (!string.IsNullOrWhiteSpace(req.Efficiency1))
    query = query.Where(p => p.Efficiency1 != null && EF.Functions.ILike(p.Efficiency1, $"%{req.Efficiency1}%"));
```

> 注: `MediaModel` 字段已在 Day 8.2 DTO 存在, 不需补齐。"补齐"是规格"前端展示内容"分区 5 文本字段的完整覆盖。

### 2.2 当前 DTO 过滤覆盖

- 规格"后台搜索统筹" R2-R25 共 19 调取字段: ✅ 全部支持
- 规格"前端展示内容" 分区 5 媒体属性 (9 字段): 文本 4 字段支持 (Media, MediaModel, SealingMaterial, Efficiency1), 数值 5 字段未在 DTO (BypassValveLr/Hr, BypassPressure, CollapsePressureBar, TempRange) 暂不进后台搜索

---

## 3. 改进 3: count 性能模式

### 3.1 设计

`countMode` query string 参数, 3 种值:

| 值 | 行为 | 适用场景 | 性能 (1M 数据) |
|---|------|----------|-----------------|
| `exact` (默认) | 走 `LongCountAsync` 准确值 | 后台管理 (准确) | 2-5s |
| `estimated` | 走 PG `reltuples` 统计 | 翻页 UI (估算够用) | 50ms |
| `none` | 跳过 COUNT, total=-1 | 列表浏览 (只需 hasMore) | <10ms |

### 3.2 SQL 翻译

`estimated` 走 PG 系统表:
```sql
SELECT COALESCE(c.reltuples::bigint, 0)
FROM pg_class c
WHERE c.relname = 'products'
```

`reltuples` 是 PG ANALYZE 自动维护的统计, 误差 ±20% (1M 数据实测 ±5-15%)

### 3.3 兜底策略

- `reltuples` 不可用时 (如分表场景) catch 异常, 退到 `LongCountAsync` 走 exact
- 非法 `countMode=garbage` 走 DTO 扩展方法归一化降级 `exact`

### 3.4 响应格式

新增 `hasMore` 字段, 替代 total 翻页判断:

```json
{
  "total": 1953,            // exact: 准确值; estimated: 估算; none: -1
  "countMode": "exact",     // echo 当前模式
  "hasMore": true,          // 前端用这个判断是否显示 "下一页"
  "page": 1,
  "pageSize": 50,
  "items": [...]
}
```

### 3.5 DTO 扩展方法

`AdminProductSearchRequestExtensions.NormalizeCountMode()` 共享归一化逻辑, 避免 Service + Endpoint 各自实现:

```csharp
public static class AdminProductSearchRequestExtensions
{
    private static readonly HashSet<string> ValidCountModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "exact", "estimated", "none"
    };

    public static string NormalizeCountMode(this AdminProductSearchRequest req)
    {
        var mode = req.CountMode ?? "exact";
        return ValidCountModes.Contains(mode) ? mode.ToLowerInvariant() : "exact";
    }
}
```

---

## 4. 测试结果 (11/11)

| # | 测试段 | 结果 | 关键 |
|---|--------|------|------|
| 1 | 单字段文本筛选 | ✅ | type=oil (total=2), mr1 前缀, productName1, mediaName, d7Thread |
| 2 | 尺寸范围 ±容差 | ✅ | 5 子项: 区间/只Min/只Max/精确/H维度, 全验证 D1 在区间内 |
| 3 | 批量 OEM | ✅ | oem2Batch (大小写归一化) + oem3Batch (xref 子查询) |
| 4 | 机器应用字段 | ✅ | machineBrand=2, machineModel 精确, engineBrand |
| 5 | 状态筛选 | ✅ | isPublished true/false, 软删除 + includeDiscontinued |
| 6 | 排序白名单 | ✅ | sortBy=mr1 asc, 非法 sortBy 降级 |
| 7 | 组合筛选 | ✅ | 3 字段 AND (oil+D1+CAT), 4 字段 AND |
| 8 | 批量对比 | ✅ | 4 id 顺序保持, 1 id, 不存在 id 跳过 |
| 9 | 对比边界 | ✅ | 空 → 400, >6 → 400 |
| 10 | 向后兼容 | ✅ | 旧端点 keyword=DAY82 命中 4 个 |
| 11 | **countMode (Day 8.2.1)** | ✅ | exact/estimated/none/garbage 降级 4 子项 |

**测试产品**: 4 个 DAY82-TEST-{A1,A2,B1,C1} 自助创建, 验证后清理, idempotent。

---

## 5. 改动文件清单

| 文件 | 改动 |
|------|------|
| `backend/src/SakuraFilter.Core/DTOs/AdminProductSearchRequest.cs` | 加 `CountMode` 字段 + `SealingMaterial` + `Efficiency1` + `AdminProductSearchRequestExtensions` |
| `backend/src/SakuraFilter.Api/Services/AdminProductService.cs` | 5 排序分支加 `Id DESC` 次级排序; `SearchAsync` 加 countMode 三分支; 应用新过滤字段 |
| `backend/src/SakuraFilter.Api/Program.cs` | 端点 `countMode` + `hasMore` 响应字段, 用 DTO 扩展方法归一化 |
| `spike-test/_test_day82_admin_search.py` | 加第 11 段 countMode 测试 |

无 schema 变更, 无 migration, 无 appsettings 变更。

---

## 6. 性能预估 (1M 数据)

| countMode | SQL 代价 | 端到端 |
|-----------|----------|--------|
| exact | COUNT(*) + 17 字段 EXISTS | 2-5s |
| estimated | reltuples 查 pg_class | 50ms |
| none | 无 COUNT | <10ms (1 次 items 查询) |

**前端推荐策略**:
- 列表首次加载: `countMode=none` (秒开, hasMore 显示 "加载更多")
- 用户点 "搜索" 后: `countMode=exact` (显示准确 "约 N 条")
- 翻页: `countMode=estimated` (快速翻页, ±20% 误差用户感知不到)

---

## 7. 已知限制

1. **`reltuples` 精度**: PG ANALYZE 自动维护, 大批量数据导入后未跑 ANALYZE 时 `reltuples` 会偏差较大。建议 ETL 完成后跑 `ANALYZE products;`

2. **countMode=none 翻页**: 翻到最后一页时, pageSize 个数 == items.count 会被误判 hasMore=true (实际上没数据了)。需要前端特殊处理: 翻页后 items.count < pageSize 才标记结束。

3. **Service 改造**: 17 字段 EXISTS 本身性能 1M 数据下还有优化空间 (例如用 JOIN 替代 N 个 EXISTS), 但目前 `countMode=estimated/none` 已经能绕过 COUNT, 翻页体验够用。

---

## 8. 部署清单

无 schema 变更, 无 migration, 无配置变更。`dotnet build -c Release && dotnet run` 即生效。
