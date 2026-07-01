# SPIKE-REPORT-day9.3 — History Total 改造 + 静态检查 + Slot 校验

**日期**: 2026-07-01
**范围**: Day 9.2 后续建议的 3 项快速改进 (history total / vue-tsc / slot 校验) + localStorage 持久化
**作者**: 协作完成 (Claude + Trae)

---

## 一、本次完成项 (4 项)

| # | 改进项 | 状态 | 关键文件 |
|---|--------|------|----------|
| 1 | history 接口加 total (筛选后真实总数) | ✅ 完成 | `ProductHistoryDto.cs` + `AdminProductService.cs` + `Program.cs` + 前端 |
| 2 | vue-tsc 静态检查 (杀未声明变量类 bug) | ✅ 完成 (0 错误) | 前端 `vue-tsc --noEmit` |
| 3 | history 筛选条件 localStorage 持久化 | ✅ 完成 | `AdminProductsView.vue` |
| 4 | AdminProductFormView 图片 slot 1-6 范围校验 | ✅ 完成 | `AdminProductFormView.vue` |

---

## 二、关键改进: history total 改造

### 2.1 改造前/后对比

| 维度 | 改造前 | 改造后 |
|------|--------|--------|
| 后端返回值 | `List<ProductHistoryItemDto>` | `ProductHistoryPageDto { Total, Limit, ChangeType, Since, Until, Items }` |
| 端点 | `Results.Ok(new { total = items.Count, items })` (total 被 limit 截断) | `Results.Ok(page)` (total = 真实总数) |
| 前端显示 | "共 N 条" (N=本页条数) | "共 N 条 (本页 M / 限制 L)" |

### 2.2 后端实现要点

```csharp
// ProductHistoryPageDto (新 record)
public record ProductHistoryPageDto(
    int Total,
    int Limit,
    string? ChangeType,
    DateTime? Since,
    DateTime? Until,
    List<ProductHistoryItemDto> Items
);

// GetHistoryAsync 改造
IQueryable<ProductHistory> query = ...; // 累积式 Where 链
var total = await query.CountAsync(ct);   // Day 9.3: total 在 Take 前计算
var items = await query.OrderByDescending(h => h.ChangedAt).Take(limit)
    .Select(h => new ProductHistoryItemDto(...)).ToListAsync(ct);
return new ProductHistoryPageDto(total, limit, changeType, since, until, items);
```

### 2.3 前端实现要点

```typescript
// types.ts
export interface ProductHistoryPage {
  total: number
  limit: number
  changeType?: string
  since?: string
  until?: string
  items: ProductHistoryItem[]
}

// api/index.ts
history(id, options): Promise<ProductHistoryPage>

// AdminProductsView.vue
const result = await adminProductApi.history(productId, params)
historyItems.value = result.items
historyTotal.value = result.total
```

Template 显示: `共 <b>{{ historyTotal }}</b> 条 (本页 M / 限制 L)`

---

## 三、关键改进: vue-tsc 静态检查

### 3.1 接入方式

```bash
cd frontend && npx vue-tsc --noEmit
```

### 3.2 初次发现的问题

发现 2 个未声明/未使用错误, 都是上一轮 PowerShell 替换造成的:
- `import` 语句位置错误 (在 script 中段)
- `watch` 在 `historyFilter` 定义前被引用 (TS2448 / TS2454)

### 3.3 修复

- `import { ref, reactive, onMounted, watch } from 'vue'` 移回顶部
- `watch(historyFilter, ...)` 移至 `historyFilter` 定义之后
- 清理了因 replace_all 不慎产生的重复 watch

最终 `vue-tsc --noEmit` exit 0, 0 errors.

### 3.4 后续建议

接入 CI 防止再出现 `historyFilter` 未声明类 bug:
```yaml
# .github/workflows/ci.yml
- run: cd frontend && npm ci && npx vue-tsc --noEmit
```

---

## 四、关键改进: history 筛选条件 localStorage 持久化

### 4.1 设计

| 维度 | 说明 |
|------|------|
| 存储 key | `sakura_admin_history_filter` |
| 存储内容 | `{ changeType, since, until, limit }` |
| 触发时机 | `watch(historyFilter, save, { deep: true })` |
| 加载时机 | 组件初始化时 (loadHistoryFilter) |
| 失败兜底 | try/catch 静默 (localStorage 满了也不影响功能) |

### 4.2 用户价值

- 在 A 产品筛选 `type=update` 后切到 B 产品, 抽屉打开时仍保持 `type=update` 筛选
- 关掉浏览器再开, 筛选条件还在
- 重置按钮同时清 localStorage

---

## 五、关键改进: 图片 slot 1-6 范围校验

### 5.1 后端 (Day 8.1 已实现, 本次无改动)

```csharp
// AdminProductImageService.cs L44, L123
if (slot < 1 || slot > 6) throw new ArgumentException("slot 必须在 1-6 之间");
```

### 5.2 前端 (Day 9.3 新增)

```typescript
// AdminProductFormView.vue
async function uploadImage(slot: number, e: Event) {
  // Day 9.3: 前端 slot 范围校验, 与后端 AdminProductImageService.UploadAsync 一致
  if (slot < 1 || slot > 6 || !Number.isInteger(slot)) {
    ElMessage.error('Slot 非法: ' + slot + ', 必须在 1-6 之间')
    return
  }
  ...
}
```

### 5.3 经验沉淀

后端已经校验, 前端再加一层是为了:
- 避免触发后端 500 + axios 拦截器弹窗
- 减少无意义网络请求
- 即时反馈 (ElMessage.error 立即显示)

---

## 六、变更清单

| 文件 | 变更 |
|------|------|
| `backend/src/SakuraFilter.Core/DTOs/ProductHistoryDto.cs` | 新增 ProductHistoryPageDto record |
| `backend/src/SakuraFilter.Api/Services/AdminProductService.cs` | GetHistoryAsync 返回 PageDto, 含 CountAsync |
| `backend/src/SakuraFilter.Api/Program.cs` | /history 端点直接返回 page |
| `frontend/src/api/types.ts` | 新增 ProductHistoryPage interface |
| `frontend/src/api/index.ts` | history() 返回 ProductHistoryPage |
| `frontend/src/views/admin/AdminProductsView.vue` | historyTotal ref + 显示 "共 N 条" + localStorage 持久化 + watch |
| `frontend/src/views/admin/AdminProductFormView.vue` | uploadImage / removeImage 加 slot 1-6 范围校验 |

---

## 七、验证

### 7.1 静态检查

```bash
cd frontend && npx vue-tsc --noEmit
# exit 0, 0 errors
```

### 7.2 dev server 启动

```bash
cd frontend && npm run dev
# VITE v6.4.3 ready in 490ms
# http://localhost:5174/
```

### 7.3 Playwright 验证

- /admin/products 加载成功 (无 console 错误, 除 vite reconnect 提示)

---

## 八、后续 Day 9.4+ 候选

1. **CI 接入 vue-tsc**: 防 historyFilter 类 bug 再发, GitHub Actions 每次 PR 跑
2. **ETL 取消 reason 端点**: 当前 cancel 不带 reason, 排查 "谁/什么时候取消" 困难
3. **SSE 替换 3s 轮询**: ETL 进度推送实时性
4. **AdminProductFormView 车型 section 校验**: 必填字段校验 (machineBrand/machineModel)
5. **history 接口加 cursor 分页**: 当前 limit 上限 200, 数据多时需要 keyset

---

## 九、Git 状态

- 提交: 待执行 (Day 9.3 完成后整体备份)
- 修改文件: 7 个
- 新增接口: 1 个 (ProductHistoryPageDto / ProductHistoryPage)
