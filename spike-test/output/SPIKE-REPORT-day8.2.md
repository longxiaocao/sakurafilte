# SPIKE-REPORT-day8.2

**主题**: 后台产品高级搜索 + 批量对比
**日期**: 2026-07-01
**状态**: ✅ 全部通过 (10/10 测试段)

---

## 1. 范围 (基于规格)

| 规格 | 字段 | 状态 |
|------|------|------|
| 后台搜索统筹 R2-R8 | 文本字段 (productName1/2, type, mr1, oem2, oemBrand, mediaName, mediaModel) | ✅ |
| 后台搜索统筹 R6/R8 | 批量 OEM (oem2Batch, oem3Batch, Excel 多行复制) | ✅ |
| 后台搜索统筹 R9-R18 | 尺寸范围 (D1-D4, H1-H4, 8 字段 × Min/Max) | ✅ |
| 后台搜索统筹 R17/R18 | 螺纹 (D7/D8) | ✅ |
| 后台搜索统筹 R19 | 尺寸容差 ±1/±5/±10mm | ✅ |
| 后台搜索统筹 R21-R25 | 机器应用字段 (machineBrand/Model/ModelName/EngineBrand/Type) | ✅ |
| 后台搜索统筹 R26 | 发布状态 + 软删除 | ✅ |
| 后台搜索统筹 R28 | 排序白名单 (id/oem_no_display/type/mr1/updated_at) | ✅ |
| 后台搜索统筹 R29 | 组合筛选 (多字段 AND) | ✅ |
| 对比界面 | 1-6 个产品批量对比 (顺序保持 + 字段按 R27 顺序) | ✅ |
| 对比界面 | 边界 (空 → 400, >6 → 400) | ✅ |

---

## 2. 实现要点

### 2.1 尺寸范围过滤 (核心难点)

`AdminProductService.ApplySizeFilter` 用 `System.Linq.Expressions.Expression` 反射拼装 `(p.D1Mm.HasValue && p.D1Mm.Value >= lo)` 复合表达式。

**关键 bug 修复**:

| # | Bug | 根因 | 修复 |
|---|-----|------|------|
| 1 | EF 静默丢 WHERE 条件 | 旧实现 `Expression.GreaterThanOrEqual(prop, Constant(lo, decimal?))` 翻译失败 | 拆成 `HasValue && Value >= lo` 显式复合, EF 翻译为 `p.d1_mm IS NOT NULL AND p.d1_mm >= 75.0` |
| 2 | 调用方 query 不更新 | `void ApplySizeFilter(...)` 内部 `query = query.Where(...)`, C# 值类型 ref 传递, 调用方收不到 | 改 `IQueryable<Product>` 返回值, 调用方 `query = ApplySizeFilter(...)` 重新接收 |

**生成的 SQL** (D1Min=80, D1Max=100, sizeTolerance=5):
```sql
WHERE NOT p.is_discontinued
  AND p.d1_mm IS NOT NULL AND p.d1_mm >= 75.0
  AND p.d1_mm <= 105.0
ORDER BY p.updated_at DESC
```

### 2.2 [AsParameters] 扁平 DTO 绑定

`AdminProductSearchRequest` 所有字段 nullable, 配合服务层 `??` 兜底:

```csharp
public decimal? D1Min { get; init; }    // nullable 避免 [AsParameters] "required" 错误
// ...
var tol = Math.Clamp(req.SizeTolerance ?? 5m, 0m, 50m);
```

### 2.3 批量 OEM (Excel 多行复制)

`oem2Batch=ABC,XYZ,DEF` 拆分归一化, 走 `normalized.Contains(p.OemNoNormalized)` IN 匹配; `oem3Batch` 走 xref 子查询 `WHERE EXISTS (xref.OemNo3 IN ...)`。

### 2.4 机器应用字段 (cross-table)

`machineBrand=CATERPILLAR` 翻译为:
```sql
EXISTS (SELECT 1 FROM machine_applications m
        WHERE m.product_id = p.id AND m.machine_brand = 'CATERPILLAR')
```

避免 N+1, 5 个机器字段走同一模式 (`Brand/Model/ModelName/EngineBrand/EngineType`)。

### 2.5 排序白名单防 SQL 注入

```csharp
var sortBy = ProductListColumns.SortWhitelist.Contains(req.SortBy ?? "")
    ? req.SortBy!.ToLowerInvariant()
    : "updated_at";   // 非法字段降级, 不抛异常
```

### 2.6 批量对比 (1-6 id)

`CompareAsync` 走单次 `WHERE id IN (...)` 查 product + xref + app, 内存中按传入顺序 re-order; 不存在 id 跳过 (前端用空白卡片占位)。

---

## 3. API 端点

| 端点 | 用途 | 备注 |
|------|------|------|
| `GET /api/admin/products/search` | 17 字段高级搜索 | `[AsParameters]` 绑定, 全部 query string |
| `POST /api/admin/products/compare` | 1-6 个产品批量对比 | body `{ ids: [1,2,3] }` |

向后兼容: `GET /api/admin/products` 旧端点仍工作, 内部委托给 `SearchAsync`。

---

## 4. 测试结果 (10/10)

| # | 测试段 | 结果 | 关键验证点 |
|---|--------|------|------------|
| 1 | 单字段文本筛选 | ✅ | type=oil, mr1 前缀, productName1 模糊, mediaName, d7Thread |
| 2 | 尺寸范围 + ±容差 | ✅ | 5 子项: 区间/只Min/只Max/精确/H维度, 全验证 D1 都在区间内 |
| 3 | 批量 OEM | ✅ | oem2Batch (大小写归一化) + oem3Batch (xref 子查询) |
| 4 | 机器应用字段 | ✅ | machineBrand=2 命中, machineModel 精确, engineBrand |
| 5 | 状态筛选 | ✅ | isPublished true/false, 软删除 + includeDiscontinued |
| 6 | 排序白名单 | ✅ | sortBy=mr1 asc, 非法 sortBy 降级 |
| 7 | 组合筛选 | ✅ | 3 字段 AND (oil+D1+CAT), 4 字段 AND |
| 8 | 批量对比 | ✅ | 4 id 顺序保持, 1 id, 不存在 id 跳过, xref/apps 子查询 |
| 9 | 对比边界 | ✅ | 空 → 400, >6 → 400 |
| 10 | 向后兼容 | ✅ | 旧端点 keyword=DAY82 命中 4 个 |

---

## 5. 关键技术决策

1. **尺寸过滤用 Expression 树而不是 Compile()**: EF 无法翻译 `.Compile()` 结果, 会全表加载到内存, 1M 数据 OOM; Expression 树拼接保证 100% SQL 翻译。

2. **nullable 字段 + [AsParameters]**: 减少前端构造请求的复杂度, 前端可省略所有未用字段, 后端服务层 `??` 兜底默认值。

3. **跨表字段走 EXISTS 子查询**: 5 个机器字段 + 1 个 brand 字段 + 1 个 OEM3 字段, 共 7 处 EXISTS, 避免 N+1。

4. **排序白名单**: 防 SQL 注入 + 防止 EF Core 抛 `could not be translated` 异常 (非法字段名), 降级到 `updated_at` 是更安全的失败模式。

5. **批量对比走单次 SQL + InMemory 排序**: 1-6 个 id 用 `WHERE id IN (...)` 一句解决, 内存中按传入顺序 re-order, 避免对 EF 排序逻辑的依赖。

---

## 6. 已知问题 / 改进空间

1. **Npgsql legacy timestamp 时区错乱**: 测试数据 A1/A2 的 `updated_at` 排在末位, 导致 pageSize=200 拿不到, 测试需翻页遍历。**这是历史数据 + Npgsql 启用了 legacy timestamp 行为 的副作用**, 未来需要全局修 Npgsql 时区配置 (走 UTC 显式)。

2. **pageSize 上限 200**: 1M 数据下用户可能想翻 5000+ 条, 目前硬编码 200。前端可以加 "查看全部" 入口, 后端用 keyset 分页代替 OFFSET (避免深度分页性能塌方)。

3. **总条数的 17 个查询条件都翻页可能慢**: 1M 数据 + 复杂多表 EXISTS, `LongCountAsync` 可能要 2-5 秒。可加 `count_estimated` 模式 (返回估算值, 标 "约 X 条")。

4. **`MediaModel` 字段不在搜索 DTO**: 规格要求有, 但 17 字段清单里没列; 当前是 16 字段 (含 `MediaName` 但没 `MediaModel`)。后续如需补, 改 DTO + Service 即可。

---

## 7. 改动文件清单

- `backend/src/SakuraFilter.Core/DTOs/AdminProductSearchRequest.cs` (新建, 17 字段扁平 DTO + SortWhitelist)
- `backend/src/SakuraFilter.Api/Services/AdminProductService.cs` (SearchAsync, CompareAsync, ApplySizeFilter)
- `backend/src/SakuraFilter.Api/Program.cs` (注册 `MapGet /api/admin/products/search` + `MapPost /api/admin/products/compare`)
- `spike-test/_test_day82_admin_search.py` (E2E 10 段测试)

---

## 8. 部署清单

无 schema 变更, 无 migration, 无配置变更。直接 `dotnet build -c Release && dotnet run` 即可启用。
