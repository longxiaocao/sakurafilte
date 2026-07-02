# Day 10+ Tasks (有序任务清单)

> 共 15 项任务,按 Phase 顺序编号。对照 `spec.md` 看完整需求,看 `checklist.md` 看验证点。
>
> **执行原则**: 每完成一项 SubTask 立即勾选 `[x]`,并写一句结果摘要(贴关键输出/commit 链接)。每完成一项 Task 立即 `git add -A && git commit -m "task-XX: [简述]"`。

---

## Phase 1: P0 地基 + P1 业务 (5 任务) ✅ 全部完成

### Task 1: P0.1 全局 ILIKE ESCAPE 修复 (0.5 session) ✅

- [x] SubTask 1.1: Grep `EF.Functions.ILike` 找所有 2 参调用位置
  - 命令: `rg -n "EF\.Functions\.ILike" backend/src/ --type cs`
  - 预期: 0 处 (Day 10.1 已替换完)
- [x] SubTask 1.2: 提取 `LikeEscapeExtensions.EscapeKeyword(string)` 帮助方法 (DRY)
  - 文件: `backend/src/SakuraFilter.Api/Common/LikeEscapeExtensions.cs`
- [x] SubTask 1.3: 替换所有 2 参为 3 参 `EF.Functions.ILike(col, $"%{escaped}%", "\\")`
  - 模式: 见 `spec.md` P0.1 章节
- [x] SubTask 1.4: 加 E2E `spike-test/_test_escape_underscore.py` 验证 `q=foo_bar` 不误命中
- [x] 验证: dotnet build 0 warning, E2E 1/1 全绿
  - 命令: `cd backend/src/SakuraFilter.Api && dotnet build --nologo`
  - 命令: `cd spike-test && python _test_escape_underscore.py`

### Task 2: P0.2 EF Core Migrations baseline 自动化 (0.5 session) ✅

- [x] SubTask 2.1: 创建 `spike-test/_ef_migrations_baseline.py` 参数化脚本 (接收 `migration_id: list[str]`)
  - 调用: `python _ef_migrations_baseline.py --migrations "M1" "M2" "M3" "M4"`
- [x] SubTask 2.2: `.github/workflows/e2e.yml` 加 `init-postgres` 步骤 (CI 全新 DB 跑 baseline)
- [x] SubTask 2.3: `scripts/db-baseline.sh` 一键执行 (本地开发用)
- [x] SubTask 2.4: 文档 `docs/ef-migrations-baseline.md` (使用流程 + 回滚)
- [x] 验证: 删 CI DB → 重跑 workflow → 全绿; 本地 baseline seed < 30s

### Task 3: P1.1 ETL 暂停恢复 (1.5 session) ✅

- [x] SubTask 3.1: `etl_progress_log` 加 `checkpoint_id` 列 (EF Core Migration `20260702085522_AddEtlCheckpointId`)
- [x] SubTask 3.2: `EtlImportService` 加 `PauseActiveTask` / `GetLastPausedCheckpointAsync` 方法 + `_pausedFlag` 标志
- [x] SubTask 3.3: Resume 时从 `lastCommittedBatchId` 续读, 跳过已 COMMIT 批次 (批次粒度 1000 行)
- [x] SubTask 3.4: API `POST /api/admin/etl/pause` + `POST /api/admin/etl/resume` (区别 Cancel, RateLimit 30/min)
- [x] SubTask 3.5: 前端 `AdminEtlView.vue` 加"暂停"/"恢复"按钮 + 状态显示
- [x] SubTask 3.6: E2E `spike-test/_test_pause_resume.py` 验证 3 万行 xref 中途暂停/恢复, count reconciliation 一致
- [x] 验证: 暂停/恢复 3 万行, 总行数 = 30,000, count reconciliation delta=0

### Task 4: P1.2 图片 CDN 切换 MinIO → Aliyun OSS (1.5 session) ✅

- [x] SubTask 4.1: 安装 Aliyun OSS SDK NuGet: `dotnet add package Aliyun.OSS.SDK.NetCore`
- [x] SubTask 4.2: `Storage/AliyunOssStorage : IObjectStorage` 实现 (Upload/GetUrl/Remove)
  - 模板: 复用 `MinioStorage.cs` 接口签名
- [x] SubTask 4.3: `Program.cs` DI 按 `Storage:Provider` 配置注入
  ```csharp
  builder.Services.AddSingleton<IObjectStorage>(sp =>
      builder.Configuration["Storage:Provider"] == "aliyun-oss"
          ? ActivatorUtilities.CreateInstance<AliyunOssStorage>(sp, builder.Configuration)
          : ActivatorUtilities.CreateInstance<MinioStorage>(sp, builder.Configuration));
  ```
- [x] SubTask 4.4: 预签名 URL (GetObject) 用于前台产品页直接 OSS 读图
  - API: `ossClient.GeneratePresignedUri(bucket, key, expiry)` 1h
- [x] SubTask 4.5: 文档 `docs/cdn-switch.md` (切换流程 + 回滚)
- [x] SubTask 4.6: E2E 模拟两种 provider 启动 → 上传/下载/删除
- [x] 验证: 切换 provider 后图片 URL 可访问, 前台产品页不 404

### Task 5: P1.4 Search 性能基准 (1 session) ✅

- [x] SubTask 5.1: `spike-test/_bench_search.py` 50 个典型查询
  - 50 关键词池: `SELECT DISTINCT oem_no FROM products LIMIT 50`
- [x] SubTask 5.2: 并发 10 / 50 / 100 测试, 输出 P50/P95/P99 延迟表
  - 库: `aiohttp` 异步并发
- [x] SubTask 5.3: CI 加 `bench` 步骤, 阈值: P95 < 200ms 搜索, P95 < 100ms typeahead
  - CI 因 Meili 未启放宽到 3000ms
- [x] SubTask 5.4: 文档 `docs/bench-baseline.md` 记录基线 + 退化告警
- [x] 验证: CI 报告 P95 < 200ms, 文档记录 baseline 数字

---

## Phase 2: P2 字典扩展 (4 任务) — ✅ 全部完成

### Task 6: P2.1 字典抽象层 IDictService + BaseDictService (1 session) ✅

- [x] SubTask 6.1: 设计 `IDictService<TItem>` 通用接口 (List/Typeahead/Create/Update/Delete/Restore/Reorder)
  - 文件: `backend/src/SakuraFilter.Api/Services/IDictService.cs`
  - 7 方法, 见 `spec.md` P2.1 章节
- [x] SubTask 6.2: `BaseDictService<TItem>` 抽象基类 (软删/排序/UNIQUE/xrefCount 统一)
  - 文件: `backend/src/SakuraFilter.Api/Services/BaseDictService.cs`
  - 必读陷阱: 4 条, 见 `spec.md` 附录 B
- [x] SubTask 6.3: 重构 `OemBrandDictService` 继承 `BaseDictService<XrefOemBrand>` (Day 10 E2E 10/10 仍通过)
- [x] SubTask 6.4: 用 `IEntityTypeConfiguration<TItem>` 分文件配置, DbContext 集中注册
  - 文件: `backend/src/SakuraFilter.Api/Data/Configurations/`
- [x] 验证: Day 10 E2E 10/10 通过, 后续字典 P2.2 实现量 < 100 行/字典

### Task 7: P2.2 7 个新字典 (复用 P2.1 抽象) (2 session) ✅

- [x] SubTask 7.1: `dict_product_name1` Entity + Migration + Service + View + E2E (0.5 session)
  - Entity: `Entities/DictProductName1.cs` (继承 IDictItem)
  - Service: `Services/ProductName1DictService : BaseDictService<DictProductName1>`
  - View: `frontend/src/views/admin/AdminProductName1View.vue`
  - E2E: `spike-test/_test_p22_seven_dicts.py` Case 2
- [x] SubTask 7.2: `dict_product_name2` (同上结构)
- [x] SubTask 7.3: `dict_type` + 默认值 seed (oil/fuel/air/cabin/others, 5 行)
  - Seed 脚本: `spike-test/_seed_dict_type.py`
- [x] SubTask 7.4: `dict_oem_no3` Entity + Migration + Service + View + E2E (5.27M 行 seed)
  - Seed 脚本: `spike-test/_seed_dict_oem_no3.py` (耗时 30+ 分钟, 建议后台跑)
- [x] SubTask 7.5: `dict_media` (Media Name + Model 二合一) + `dict_machine` (3 字段) + `dict_engine` (2 字段) (并行, 共 1 session)
  - 关键: `BuildSearchPredicate` override (多字段 OR)
- [x] SubTask 7.6: 数据迁移脚本 (从 products/cross_references/machine_applications 提取 distinct)
  - 6 个 `_seed_dict_*.py` 全部跑通
- [x] SubTask 7.7: typeahead 接入产品表单 (AdminProductFormView 7 分区全覆盖)
  - 文件: `frontend/src/views/admin/AdminProductFormView.vue` 7 个 `<el-autocomplete>`
- [x] 验证: 7 个字典管理页 + 7 个 typeahead 全部联动, 拖拽排序全部 OK

### Task 8: P2.3 Type 字典排序 + 机器分类 (0.5 session) ✅

- [x] SubTask 8.1: `spike-test/_seed_dict_defaults.py` (Type: oil/fuel/air/cabin/others; Machine 4 大类自动归类)
  - 实现: dict_type 5 行 seed (oil=1, fuel=2, air=3, cabin=4, others=99), ON CONFLICT DO UPDATE SET sort_order 幂等
  - 实现: dict_machine 按 brand 关键词归类到 Agriculture/Commercial/Construction/others 4 大类
- [x] SubTask 8.2: 前台公开端点 `GET /api/public/products/by-type` 按 `dict_type.sort_order` 排序
  - 文件: `backend/src/SakuraFilter.Api/Controllers/PublicProductController.cs`
  - 返 `{ total, groups: [{ type, sortOrder, productCount, products: [...] }] }` 按 sort_order 升序
- [x] SubTask 8.3: machine brand 4 大类聚合 (`GET /api/public/machine-brands/aggregated`)
  - EF Migration: `20260702133148_AddMachineCategory` (machine_category varchar(50) NOT NULL DEFAULT 'others' + idx_dict_machine_category 索引)
  - Entity: `DictMachine.MachineCategory` + `DictMachineConfiguration.HasDefaultValue("others")`
  - 文件: `backend/src/SakuraFilter.Api/Controllers/PublicMachineBrandsController.cs`
  - 返 `{ byCategory: { Agriculture: [...], Commercial: [...], Construction: [...], others: [...] }, totalCount }`
- [x] SubTask 8.4: `MachineDictService` 加 `ListMachinesByCategoryAsync` + `UpdateMachineCategoryAsync` 方法 (4 大类白名单校验)
  - `MachineUpdateRequest` 扩展 `MachineCategory` 字段
- [x] SubTask 8.5: `AdminMachinesView.vue` 加 category 编辑 (el-select 4 大类 + 列表 tag 显示)
- [x] SubTask 8.6: E2E `spike-test/_test_type_ordering.py` 5/5 全绿
- [x] 验证: dotnet build 0 错误 (19 warning 均为已存在), P2.3 E2E 5/5 PASS, P2.2 回归 9/9, Day 10 回归 10/10

---

## Phase 3: P3 搜索+展示 (3 任务) ⏳ 待开始

### Task 9: P3.1 搜索容差 UI (±1/±5/±10mm) (0.5 session)

- [ ] SubTask 9.1: `AdminSearchView.vue` 加"尺寸容差"下拉
  - 组件: `<el-select v-model="tolerance"><el-option label="±1mm" :value="1"/>...`
  - 默认值: 5mm
- [ ] SubTask 9.2: `PublicSearchView.vue` (P3.4) 同步加
- [ ] SubTask 9.3: 选 5mm → 搜索请求带 `tolerance=5` (后端 Day 8.4 已实现)
  - 拦截器: `http.get('/api/products/search', { params: { tolerance, h1, h2, ... } })`
- [ ] SubTask 9.4: popover 提示"切换容差会显著影响搜索速度 (10mm 比 1mm 慢 5-10 倍)"
  - `<el-popover>` 包裹容差下拉
- [ ] 验证: 容差切换后, 搜索结果数量变化符合预期
  - E2E: `spike-test/_test_tolerance_ui.py`
  - Case: 1mm → 3 条, 10mm → 50+ 条

**复用模式**: Day 8.4 后端已实现 `(diameter BETWEEN lo AND hi)`,前端只改 UI + 请求参数。

### Task 10: P3.2 Excel 多行复制粘贴查询 (1 session)

- [ ] SubTask 10.1: 搜索输入框加"批量粘贴"模式
  - `el-tabs` 切换: 单条 / 批量
  - 批量模式: `<el-input type="textarea" :rows="10">`
- [ ] SubTask 10.2: 解析 tab/换行/逗号分隔, 拆成多个 OEM 2 或 OEM 3
  - 正则: `[\t\n,;]+` 分隔, `filter(Boolean)` 去空, `new Set()` 去重
- [ ] SubTask 10.3: 后端 `POST /api/search/batch-oem` 接 `oems: string[]`
  - DTO: `BatchOemRequest { oems: string[] }`
  - SQL: `WHERE oem_no_2 = ANY(@oems) OR oem_no_3 = ANY(@oems)` (走 Meili 索引)
  - 返: `List<BatchOemResult>` (每个 OEM 最佳匹配)
- [ ] SubTask 10.4: 前端结果表 1 行 = 1 个查询 OEM
  - 列: OEM 编号 / 命中状态 (✓/✗) / 最佳匹配产品 / OEM Brand
- [ ] SubTask 10.5: E2E 验证粘贴 100 个 OEM → 100 行结果 < 1s
  - E2E: `spike-test/_test_batch_oem.py`
  - 性能: 走 Meili 索引, 100 行 < 1s
- [ ] 验证: 100 OEM 1 秒内返回, 特殊字符 (中文/斜杠/引号) 不破坏解析

**必测边界**:
- 中文: "滤清器 1142" → 正确解析
- 斜杠: "AB/CD/123" → 不被当作分隔符
- 引号: `"OEN-123"` → 保留引号在结果中
- 空行 / 重复 / 前后空格 → 健壮处理

### Task 11: P3.3 前台产品详情页 (公开) (1 session)

- [ ] SubTask 11.1: `PublicProductView.vue` 路由 `/product/:oem`
  - 文件: `frontend/src/views/public/PublicProductView.vue`
  - 路由: `router/index.ts` 加 `{ path: '/product/:oem', component: PublicProductView, meta: { public: true } }`
- [ ] SubTask 11.2: 7 分区折叠展示
  - 1 图片 / 2 基础 / 3 尺寸 / 4 性能 / 5 包装 / 6 车型 / 7 Cross Ref
  - `<el-collapse v-model="activeNames">` + `<el-collapse-item>`
- [ ] SubTask 11.3: SEO `<title>` + OG meta tags
  - 用 `useHead` (vueuse) 或直接 `document.title = ...`
  - `<meta property="og:title">` / `og:image` / `og:description` / `og:type`
- [ ] SubTask 11.4: 图片按 OEM 编号命名验证 (imageKey 前缀 = `oem2` 编码)
  - `oem2/11427622448.jpg` / `oem2/11427622448_2.jpg`
  - OSS 预签名 URL 1h 有效
- [ ] SubTask 11.5: 公开搜索 `/search?q=`, 无 admin 鉴权
  - 路由: `/search` (公开, 无 token)
- [ ] SubTask 11.6: Playwright 截图测试
  - `tests/visual/public-product.spec.ts`
  - 截图存 `tests/visual/baselines/public-product.png`
- [ ] 验证: `/product/11427622448` 公开访问, 首屏 < 1.5s, title/OG 正确
  - lighthouse 性能跑分

**复用模式**:
- 7 分区数据模型: `Product` Entity (Day 5 已建)
- SEO: 见 `spec.md` P3.3 章节 HTML 模板

### Task 12: P3.5 对比 UI 完整版 (0.5 session)

- [ ] SubTask 12.1: `AdminCompareView.vue` 6 列布局
  - `<div class="grid grid-cols-7">` (1 字段名 + 6 产品)
- [ ] SubTask 12.2: 高亮差异 (相同灰底/不同黄底)
  - 算法: `const allEqual = values.every(v => v === values[0])`
  - CSS: `.same { background: #f5f5f5; } .diff { background: #fffbe6; }`
- [ ] SubTask 12.3: 拖拽列调整顺序
  - `vuedraggable` 包 6 列
- [ ] SubTask 12.4: 打印优化 CSS `@media print`
  ```css
  @media print {
    .el-button, .el-toolbar { display: none !important; }
    .grid { grid-template-columns: 200px repeat(6, 1fr); }
  }
  ```
- [ ] 验证: 6 产品对比页一次性展示, 差异高亮, 截图 E2E
  - E2E: `spike-test/_test_compare.py` + Playwright 截图

---

## Phase 4: P4 CI 闭环 + P5 打磨 (3 任务) ⏳ 待开始

### Task 13: P4.1 E2E 全量覆盖 (1 session)

- [ ] SubTask 13.1: 每个字典配 1 个 E2E (7 个: OEM Brand 已有, + 6 个新字典)
  - Day 10.5 已有 `_test_day10_oem_brands.py` 模板
  - 复制模板为 `_test_dict_product_name1.py` / `_test_dict_machine.py` 等
- [ ] SubTask 13.2: P3.1 尺寸容差 E2E (`_test_tolerance_ui.py`)
- [ ] SubTask 13.3: P3.2 Excel 粘贴 E2E (`_test_batch_oem.py`)
- [ ] SubTask 13.4: P3.3 前台产品页 Playwright 截图测试 (`_test_public_product.py`)
- [ ] SubTask 13.5: P3.4 对比 UI 截图测试 (`_test_compare.py`)
- [ ] SubTask 13.6: CI gate 全部并行跑, < 10 分钟
  - `.github/workflows/e2e.yml` 拆 matrix job
  - 单 job < 3 分钟, 总耗时 < 10 分钟
- [ ] 验证: CI 跑完整 15+ E2E < 10 分钟

**E2E 模板** (Day 10.5 复用):
```python
# spike-test/_test_dict_xxx.py
import requests, sys
BASE = "http://localhost:5148"
TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
H = {"X-Admin-Token": TOKEN}

def test_list():
    r = requests.get(f"{BASE}/api/admin/dict/xxx", headers=H)
    assert r.status_code == 200, f"list failed: {r.text}"
    return r.json()

def test_create():
    r = requests.post(f"{BASE}/api/admin/dict/xxx",
                      json={"value": "TEST", "sortOrder": 99}, headers=H)
    assert r.status_code == 201, f"create failed: {r.text}"
    return r.json()

# ... 9 case 一致
```

### Task 14: P4.2 + P4.3 字典契约 + 视觉回归 (1 session)

- [ ] SubTask 14.1: 后端 `GET /api/admin/dict/_schema` 暴露所有字典字段定义
  - 文件: `backend/src/SakuraFilter.Api/Controllers/AdminDictController.cs`
  - 反射 `DbContext.Model` 提取 entity 字段, 返 JSON
- [ ] SubTask 14.2: 前端 `npm run test:contract` 验证 TS interface 一致
  - 文件: `frontend/tests/contract/dict-schema.test.ts`
  - 用 zod 校验前后端字段一致
- [ ] SubTask 14.3: Playwright 跑每个字典管理页截图
  - 文件: `frontend/tests/visual/dict-pages.spec.ts`
  - 基准截图存 `tests/visual/baselines/`
- [ ] SubTask 14.4: 视觉回归 (像素 diff > 5% → fail)
  - `pixelmatch` npm 包
  - 阈值 5%, 超标 → fail
- [ ] SubTask 14.5: CI 加 contract + 视觉步骤
  - `.github/workflows/e2e.yml` 加 `test:contract` + `test:visual` job
- [ ] 验证: 改后端字段不改前端, CI 必报; 拖拽前后截图差异 < 5%

**契约 schema JSON 示例**:
```json
{
  "OemBrand": {
    "id": "long", "value": "string", "sortOrder": "int",
    "isDeleted": "bool", "deletedAt": "DateTime?"
  },
  "DictType": {
    "id": "long", "value": "string", "sortOrder": "int",
    "isDeleted": "bool", "deletedAt": "DateTime?"
  }
}
```

### Task 15: P5 打磨 (Volume / Popover / 主题 / 帮助) (1 session)

- [ ] SubTask 15.1: P5.1 Volume 自动计算 (L×W×H/1e9 m³, 前端实时)
  - `AdminProductFormView.vue` 加 watcher:
    ```ts
    watch([() => form.cartonL, () => form.cartonW, () => form.cartonH], ([l, w, h]) => {
      if (l && w && h) form.cartonVolume = Number(((l * w * h) / 1e9).toFixed(4));
    });
    ```
- [ ] SubTask 15.2: P5.2 字段说明 popover (`dict_field_help` 表)
  - Entity: `DictFieldHelp { fieldName, description, unit, example, updatedAt }`
  - API: `GET /api/admin/dict/field-help/:fieldName`
  - 组件: `FieldHelpPopover.vue` (el-popover + async load)
  - 30+ 字段全部加 `?` 图标
- [ ] SubTask 15.3: P5.3 主题切换 (Pinia/Vue reactive store + localStorage)
  - `frontend/src/stores/theme.ts` Pinia store
  - `localStorage.theme` 持久化
  - CSS variables 覆盖 (深色/浅色)
  - AppHeader 切换按钮 (太阳/月亮图标)
- [ ] SubTask 15.4: P5.4 `/admin/help` 路由 (操作指南 + 字典规范)
  - `AdminHelpView.vue` 路由
  - Markdown 渲染: `markdown-it`
  - 内容: 快速开始 / 字典规范 / 批量导入 / 容差建议 / FAQ
- [ ] 验证: 输入长宽高 → Volume 自动显示; 主题切换刷新后保持
  - E2E: `_test_p5_volume.py` / `_test_p5_theme.py`

---

# Task Dependencies (任务依赖图)

```
Phase 1 (全部 ✅):
  Task 1 (P0.1 ILIKE)        → 无依赖, 优先
  Task 2 (P0.2 baseline)     → 无依赖
  Task 3 (P1.1 ETL pause)    → 依赖 Task 1 (ILIKE 安全)
  Task 4 (P1.2 CDN)          → 无依赖
  Task 5 (P1.4 bench)        → 无依赖

Phase 2 (Task 6/7 ✅, Task 8 🟡):
  Task 6 (P2.1 抽象层)        → 依赖 Task 1 (ILIKE 修复保证搜索)
  Task 7 (P2.2 7字典)        → 依赖 Task 6 (抽象层)
  Task 8 (P2.3 排序+机器)    → 依赖 Task 7 (Type/Machine 字典) 🟡

Phase 3 (⏳):
  Task 9  (P3.1 容差 UI)     → 依赖 Task 1 (后端已有)
  Task 10 (P3.2 Excel 粘贴)  → 无依赖
  Task 11 (P3.3 前台页)      → 依赖 Task 7 (字典 typeahead) + Task 8 (type 排序)
  Task 12 (P3.5 对比 UI)     → 无依赖 (后端 Day 9.4 已实现)

Phase 4 (⏳):
  Task 13 (P4.1 E2E)         → 依赖 Task 1-12 全部
  Task 14 (P4.2+P4.3)        → 依赖 Task 6+7 (字典)
  Task 15 (P5 打磨)          → 依赖 Task 11 (前台页样式基础)

并行机会 (提高效率):
  - Task 1, 2, 4, 5 可并行
  - Task 9, 10, 12 可并行 (Phase 3 内)
  - Phase 4 三个 Task 可并行
```

---

# 验收里程碑 (Milestone)

- **M1** (Phase 1 末) ✅: P0/P1 全绿, ILIKE 安全, ETL 暂停可恢复, CDN 可切换, 性能基线建立
- **M2** (Phase 2 末) 🟡: 7 字典全部上线, Type 排序生效, 机器分类生效
- **M3** (Phase 3 末) ⏳: 前台产品页公开可访问, 搜索容差可调, 对比 UI 完整
- **M4** (Phase 4 末) ⏳: 15+ E2E 全绿 < 10min, 字典契约测试, 视觉回归, P5 全部完成

---

# 工作量汇总

| 阶段 | 任务数 | session 数 | 状态 |
|------|--------|----------|------|
| Phase 1 (P0+P1) | 5 | 5 | ✅ 全部完成 |
| Phase 2 (P2) | 3 | 4 | ✅ 全部完成 (P2.1+P2.2+P2.3) |
| Phase 3 (P3) | 4 | 3 | ⏳ 待开始 |
| Phase 4 (P4+P5) | 3 | 3 | ⏳ 待开始 |
| **合计** | **15** | **15** | **8/15 完成 (53%)** |

> 按 Day 10 节奏 1 session = 1 次连续 push + 全绿。

---

# 执行检查表 (每次开始新 Task 前必读)

1. **当前进度**: 翻到对应 Phase, 看哪个 SubTask 是第一个 `[ ]`
2. **依赖检查**: 看 Task Dependencies, 确认所有依赖 Task 已完成
3. **复用模式**: 查 `spec.md` 附录 A, 找是否有现成模板
4. **踩坑提醒**: 查 `spec.md` 附录 B, 避坑
5. **文件路径**: 直接复制本文件中的文件路径 (已用绝对/相对路径双写)
6. **验证命令**: 本文件 Task 末"验证"段落已列具体命令
7. **完成后**: 勾选 `[x]`, 写结果摘要, `git commit`

---

# 优先级建议 (按 ROI 排序)

| 排名 | Task | 价值 | 风险 | 建议时机 |
|------|------|------|------|---------|
| 1 | Task 11 (前台产品页) | 高 (用户可见) | 中 | Phase 3 优先 |
| 2 | Task 12 (对比 UI) | 中 (用户可见) | 低 | Phase 3 优先 |
| 3 | Task 9 (容差 UI) | 中 (业务相关) | 低 | Phase 3 优先 |
| 4 | Task 10 (Excel 粘贴) | 中 (效率) | 中 | Phase 3 |
| 5 | Task 13 (E2E 全量) | 中 (CI) | 低 | Phase 4 |
| 6 | Task 14 (契约+视觉) | 中 (CI) | 中 | Phase 4 |
| 7 | Task 15 (P5 打磨) | 低 (锦上添花) | 低 | Phase 4 最后 |

最高优先级 (Next): **Task 11 (P3.3 前台产品页)** — 依赖 Task 7 (字典 typeahead) + Task 8 (type 排序) 全部完成, 可立即启动。
