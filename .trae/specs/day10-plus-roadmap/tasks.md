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

## Phase 3: P3 搜索+展示 (4 任务) ⏳ 待开始

> **规格依据**: 新思路.xlsx → "后台搜索统筹" / "对比界面" / "前端展示内容" / "各分区管理界面" 5 个 sheet 严格对应

### Task 9: P3.1 搜索容差 UI (±5mm 固定) (0.5 session)

> **规格纠正**: 新思路.xlsx R9-R18 明确"**搜索范围支持±5mm**" (H1-H4/D1-D4/D7/D8 共 10 个尺寸字段),**不是 ±1/±5/±10 三档**

- [ ] SubTask 9.1: 后端确认 tolerance 默认 5 (Day 8.4 已实现,无需改)
  - 验证: `grep -n "tolerance" backend/.../AdminSearchController.cs` 应见 `int? tolerance = 5`
  - 若默认 1 或 10 → 改为 5
- [ ] SubTask 9.2: 前端 `AdminSearchView.vue` **删除容差下拉**,尺寸字段直接传 `tolerance=5`
  - **不**加 `<el-select>` 三档选择
  - 请求参数固定: `{ tolerance: 5, h1, h2, d1, d2, ... }`
- [ ] SubTask 9.3: `PublicSearchView.vue` (P3.4) 同步实现
- [ ] SubTask 9.4: (可选) popover 提示: 鼠标悬停尺寸字段时显示"搜索范围 ±5mm"
  - `<el-popover trigger="hover">` 包裹尺寸 input
- [ ] 验证: 尺寸字段输入 → 搜索结果符合 ±5mm 范围
  - E2E: `spike-test/_test_tolerance_ui.py` (新)
  - Case 1: H1=100 → 约 20 条 (H1 ∈ 95-105)
  - Case 2: H1=100 + H2=200 → 5-10 条 (双字段 AND 收窄)

**复用模式**: Day 8.4 后端 `(h BETWEEN lo AND hi) AND (d BETWEEN lo AND hi)` 已实现 `lo = value-5, hi = value+5`,前端无需改逻辑。

### Task 10: P3.2 Excel 多行复制粘贴查询 (1 session)

> **规格来源**: 新思路.xlsx → "后台搜索统筹" R6/R8 "**是否可是支持Excel多行复制黏贴查询**" 明确针对 OEM 2 / OEM 3

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

> **规格来源**: 新思路.xlsx → "前端展示内容" R1 "**域名格式:product name 1+product name2+OEM BRAND+OEM NO.**" + "后台新增产品格式" 7 分区字段 + "前端展示内容" R5 "图片名称需要同一命名为对应的OEM号码"

- [ ] SubTask 11.1: `PublicProductView.vue` 路由 `/product/{name1}-{name2}-{oemBrand}-{oemNo}` (URL 编码)
  - 文件: `frontend/src/views/public/PublicProductView.vue`
  - 路由: `router/index.ts` 加 `{ path: '/product/:slug', component: PublicProductView, meta: { public: true } }`
  - slug 解析: name1 + name2 + oemBrand + oemNo 用 `-` 连接
- [ ] SubTask 11.2: 7 分区折叠展示 (按"后台新增产品格式"规格)
  - **分区 1 基础**: Product Name 1, Product Name 2, Type, MR.1, OEM 2, 上架
  - **分区 2 替代**: OEM Brand, OEM 3, 上架, Remark, 排序
  - **分区 3 尺寸**: H1-H4, D1-D4, D7, D8, No. Check Valves, No. Bypass Valves
  - **分区 4 图片**: 图片 1-6 (主图 + 5 副图)
  - **分区 5 性能**: Media Name, Media Model, Bypass Valve LR/HR, Efficiency 1/2, Δ Collapse Pressure, Seal Material, Temperature Range, Bypass Pressure
  - **分区 6 包装**: QTY, Weight/KGS, Length/Wide/Height, Volume/m³ (自动计算)
  - **分区 7 适配**: machine brand/model/name, Engine brand/type/energy, Production date, Power, Serial number (from/to), Car body type, Series, CO₂ emission, Transmission, Engine displacement, Number of cylinders, GVWR, Tonnage, Geographic area, Chassis type, Engine model, Cabin type, Capacity, Engine serial number
  - `<el-collapse v-model="activeNames">` + `<el-collapse-item>` (默认全展开)
- [ ] SubTask 11.3: SEO `<title>` + OG meta tags
  - 用 `useHead` (vueuse) 或直接 `document.title = ...`
  - `<meta property="og:title">` / `og:image` / `og:description` / `og:type>`
  - title 格式: `{{ name1 }} {{ name2 }} {{ oemBrand }} {{ oemNo }} - SakuraFilter`
- [ ] SubTask 11.4: 图片按 OEM 编号命名 (imageKey 验证)
  - 主图: `oem2/{OEM}.jpg` (slot 1)
  - 副图: `oem2/{OEM}_{slot}.jpg` (slot 2-6)
  - 缺图回退: `static/logo.png`
  - OSS 预签名 URL 1h 有效
- [ ] SubTask 11.5: 公开搜索 `/search` 路由 (P3.4 独立 Task)
  - 路由: `/search` (公开, 无 token)
- [ ] SubTask 11.6: Playwright 截图测试
  - `tests/visual/public-product.spec.ts`
  - 截图存 `tests/visual/baselines/public-product.png`
- [ ] 验证: `/product/oil-filter-of100-mann-w950` 公开访问, 首屏 < 1.5s, title/OG 正确
  - lighthouse 性能跑分

**复用模式**:
- 7 分区数据模型: `Product` Entity (Day 5 已建, 23 字段已具备)
- 字段映射: 见 `spec.md` P3.3 7 分区字段表
- SEO: 见 `spec.md` P3.3 HTML 模板

### Task 11.5: P3.4 公开搜索页 (8 字段多框) (0.5 session) — 新增独立 Task

> **规格来源**: 新思路.xlsx → "前端展示内容" R2 "**都多框支持所有 oem brand, oem 2 no, oem 3 no, machine brand, machine model, model name, engine brand, engine type 模糊搜索**" + R8 示例 URL

- [ ] SubTask 11.5.1: `PublicSearchView.vue` 8 字段多框布局
  - 字段 (按规格顺序): oem brand / oem 2 no / oem 3 no / machine brand / machine model / model name / engine brand / engine type
  - 8 个 `<el-input>` 并排 (2 行 4 列 grid)
  - 任一框输入触发搜索 (debounce 300ms)
- [ ] SubTask 11.5.2: 后端 `GET /api/public/search` 接 8 字段
  - 参数: `oemBrand, oemNo2, oemNo3, machineBrand, machineModel, modelName, engineBrand, engineType` (全部可选 string)
  - 走 P0.1 ILIKE ESCAPE 模糊 (前后通配)
  - 多个字段 = AND 关系
  - 走 Meili 索引
- [ ] SubTask 11.5.3: URL 路由与查询参数同步
  - 路由: `/search` 公开
  - 字段值变化时同步到 URL query (可分享)
  - 例: `?oemBrand=CAT&machineModel=320D`
- [ ] SubTask 11.5.4: E2E 验证规格 R8 例子
  - `?oemBrand=CAT` → 模糊返回 oem_brand 含 "CAT"
  - `?oemNo3=207-60` → 模糊返回 oem_no_3 以 "207-60" 开头
- [ ] 验证: 8 字段任一输入触发搜索, 多字段 AND 收窄, URL 可分享

**复用模式**:
- Meili 索引: Day 9.3 SearchService 已封装
- ILIKE 转义: P0.1 `LikeEscapeExtensions.EscapeKeyword`

### Task 12: P3.5 对比 UI 完整版 (0.5 session)

> **规格来源**: 新思路.xlsx → "对比界面" sheet 严格列出 23 字段,与"后台搜索统筹" R27 "查询之后需要显示的内容及顺序" **完全一致**

- [ ] SubTask 12.1: `AdminCompareView.vue` 6 列布局 (23 字段)
  - 字段顺序 (与"对比界面"规格严格一致):
    1. 图片 1 (主图缩略图)
    2. MR. 1
    3. OEM 2 NO.
    4. OEM 3 NO.
    5-8. H1, H2, H3, H4
    9-12. D1, D2, D3, D4
    13-14. D7, D8 (螺纹)
    15. Media Name
    16. Media Model
    17. remark
    18. Each Carton QTY
    19. Each Carton Weight/KGS
    20-22. Length, Wide, Height (mm)
    23. **Volume/CTN (m³) 自动计算**
  - `<div class="grid grid-cols-7">` (1 字段名 + 6 产品)
- [ ] SubTask 12.2: 高亮差异 (相同灰底/不同黄底)
  - 算法: `const allEqual = values.every(v => v === values[0])`
  - CSS: `.same { background: #f5f5f5; } .diff { background: #fffbe6; }`
- [ ] SubTask 12.3: 拖拽**列产品**调顺序 (字段顺序固定,不能拖)
  - `vuedraggable` 包 6 列
  - 注: 字段行**不**可拖,只可拖产品列
- [ ] SubTask 12.4: 打印优化 CSS `@media print`
  ```css
  @media print {
    .el-button, .el-toolbar { display: none !important; }
    .grid { grid-template-columns: 200px repeat(6, 1fr); }
  }
  ```
- [ ] 验证: 6 产品对比页一次性展示 23 字段, 差异高亮, 截图 E2E
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

---

# Next 任务执行手册 (Day 11 启动用)

> **使用场景**: 任何新 session 启动时,翻到本节直接执行。本节为"照着做"的最小可执行清单。

## 🟢 Task 11: P3.3 前台产品详情页 (1 session) — Next

### 启动检查 (5 分钟)

```bash
# 0. 备份 (高危: 新建 controller + 改路由, 需备份)
cd d:\projects\sakurafilter
git status  # 必须 clean
# 若有未提交 → git add -A && git commit -m "backup: pre Task-11 启动"

# 1. 数据库
psql -h localhost -U postgres -d spike_test_v3 -c "SELECT count(*) FROM products"
# 预期: ~10000 (Day 1-10 已有)

# 2. 编译
cd backend/src/SakuraFilter.Api && dotnet build --nologo
# 预期: 0 Error

# 3. 启动后端 (新窗口)
dotnet run --urls "http://localhost:5148"
# 预期: Now listening on http://localhost:5148

# 4. E2E 回归 (P2 必须仍绿)
cd d:\projects\sakurafilter\spike-test
python _test_day10_oem_brands.py   # 10/10
python _test_p22_seven_dicts.py     # 9/9
python _test_type_ordering.py       # 5/5
```

### SubTask 详细步骤

#### SubTask 11.1: 后端 `PublicProductController` (15 分钟)

```bash
# 1. 创建文件
touch backend/src/SakuraFilter.Api/Controllers/PublicProductController.cs
```

**代码模板** (复制后改):
```csharp
// 参考 AdminProductController.cs, 但 [AllowAnonymous] 且只读
[ApiController]
[Route("api/public")]
[AllowAnonymous]  // 关键: 无需 admin token
public class PublicProductController : ControllerBase
{
    [HttpGet("product/{oem}")]
    public async Task<ActionResult<ProductDetailDto>> GetByOem(string oem) { ... }

    [HttpGet("products/by-type")]
    public async Task<ActionResult<List<TypeGroupDto>>> ByType() { ... }
}
```

**验证**:
```bash
# 1. 编译
dotnet build --nologo  # 0 Error

# 2. 启动后 curl
curl http://localhost:5148/api/public/product/11427622448
# 预期: 200 + JSON (无 token)
# 预期: { oem, name1, name2, type, dimensions, machines, xrefs, images }
```

#### SubTask 11.2: 前台 `PublicProductView.vue` (30 分钟)

```bash
# 1. 创建文件
mkdir -p frontend/src/views/public
touch frontend/src/views/public/PublicProductView.vue
```

**7 分区模板** (复制 Day 9 AdminProductView.vue, 删鉴权 + 加 SEO):
```vue
<template>
  <div class="public-product">
    <el-collapse v-model="activeNames">
      <el-collapse-item title="1. 图片" name="1">
        <el-carousel :interval="4000"><el-carousel-item v-for="img in product.images" :key="img.url">
          <img :src="img.url" :alt="product.oem" />
        </el-carousel-item></el-carousel>
      </el-collapse-item>
      <el-collapse-item title="2. 基础" name="2">
        <dl><dt>Product Name 1</dt><dd>{{ product.name1 }}</dd>...</dl>
      </el-collapse-item>
      <!-- 3-7 分区同上 -->
    </el-collapse>
  </div>
</template>

<script setup lang="ts">
import { useHead } from '@vueuse/head'  // SEO
useHead({
  title: `${product.name1} ${product.name2} ${product.oemBrand} ${product.oem} - SakuraFilter`,
  meta: [
    { property: 'og:title', content: `${product.name1} ${product.name2}` },
    { property: 'og:image', content: product.images[0]?.url },
    { property: 'og:description', content: `${product.oemBrand} ${product.oem}` },
    { property: 'og:type', content: 'product' }
  ]
})
</script>
```

**验证**:
```bash
# 1. 前端编译
cd frontend && npm run build  # 0 Error

# 2. 浏览器手动
# 访问 http://localhost:5173/product/11427622448
# 预期: 看到 7 个折叠面板, 标题正确
```

#### SubTask 11.3: 路由配置 (5 分钟)

```typescript
// frontend/src/router/index.ts 加 2 行
{
  path: '/product/:oem',
  component: () => import('@/views/public/PublicProductView.vue'),
  meta: { public: true, title: '产品详情' }
},
{
  path: '/search',
  component: () => import('@/views/public/PublicSearchView.vue'),
  meta: { public: true, title: '产品搜索' }
}
```

#### SubTask 11.4: Playwright 截图 (20 分钟)

```bash
cd frontend
npx playwright test tests/visual/public-product.spec.ts
# 首次跑会创建 baseline, 后续跑比对
```

**spec 模板**:
```typescript
import { test, expect } from '@playwright/test'
test('public product page', async ({ page }) => {
  await page.goto('http://localhost:5148/product/11427622448')
  await expect(page).toHaveTitle(/SakuraFilter/)
  await expect(page.locator('.el-collapse-item')).toHaveCount(7)
  await page.screenshot({ path: 'tests/visual/baselines/public-product.png', fullPage: true })
})
```

#### SubTask 11.5: E2E (20 分钟)

```bash
touch spike-test/_test_public_product.py
```

**模板** (复制 `_test_type_ordering.py`):
```python
import requests, sys
BASE = "http://localhost:5148"

def test_get_product_no_token():
    """前台产品页无需 token"""
    r = requests.get(f"{BASE}/api/public/product/11427622448")
    assert r.status_code == 200, f"need 200, got {r.status_code}: {r.text}"
    data = r.json()
    assert "oem" in data, "must have oem field"
    print("[OK] public product accessible without token")

def test_get_product_with_images():
    """产品页含 imageKey (OSS URL)"""
    r = requests.get(f"{BASE}/api/public/product/11427622448")
    data = r.json()
    if data.get("images"):
        assert "url" in data["images"][0]
        print("[OK] images have url field")

if __name__ == "__main__":
    test_get_product_no_token()
    test_get_product_with_images()
    print("2/2 PASS")
```

#### 11.6 提交 + 推送

```bash
git add -A
git commit -m "task-11: P3.3 前台产品页 (公开) - 7 分区 + SEO + OSS 图片"
# GFW 阻断时: 后台重试循环
for i in {1..30}; do
  git push origin master && break
  echo "push failed, retry $i in 8s..."
  sleep 8
done
```

### Task 11 回滚方案

```bash
# 若 SubTask 11.2 前端编译失败
git revert HEAD  # 撤销最后一次 commit
git push origin master

# 若 SubTask 11.1 后端编译失败
# 检查 [AllowAnonymous] 是否漏, AdminProductController 鉴权是否被误改
grep -n "X-Admin-Token" backend/src/SakuraFilter.Api/Controllers/AdminProductController.cs
```

---

## 🟡 Task 12: P3.5 对比 UI (0.5 session) — 备选 Next

### 启动检查

```bash
# 0. 备份 (前端单文件, 可不备份, 但仍建议)
git status  # clean

# 1. 复用 Day 9.4 后端接口
curl -H "X-Admin-Token: $TOKEN" http://localhost:5148/api/admin/compare?ids=1,2,3
# 预期: 200 + JSON (3 产品 + 字段)
```

### SubTask 详细步骤

#### SubTask 12.1: 6 列 grid 布局 (15 分钟)

```bash
# 1. 创建/改造
touch frontend/src/views/admin/AdminCompareView.vue
```

**模板**:
```vue
<template>
  <div class="compare-view">
    <div class="grid grid-cols-7 gap-2">
      <!-- 1 列字段名 + 6 列产品 -->
      <div v-for="field in fields" :key="field.name" class="field-row">
        <div class="field-name">{{ field.label }}</div>
        <div v-for="(product, idx) in products" :key="product.id"
             :class="['field-value', getHighlightClass(field, idx)]">
          {{ product[field.name] || '—' }}
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
// 差异高亮算法
const getHighlightClass = (field, colIdx) => {
  const values = products.value.map(p => p[field.name])
  const allEqual = values.every(v => v === values[0])
  return allEqual ? 'same' : 'diff'
}
</script>

<style scoped>
.same { background: #f5f5f5; }
.diff { background: #fffbe6; }

@media print {
  .el-button, .el-toolbar { display: none !important; }
  .grid { grid-template-columns: 200px repeat(6, 1fr); }
}
</style>
```

#### SubTask 12.2: 拖拽列调序 (15 分钟)

```bash
npm install vuedraggable@next
```

```vue
<draggable v-model="products" item-key="id" handle=".drag-handle">
  <template #item="{ element }">
    <div class="product-column" :data-id="element.id">
      <el-icon class="drag-handle"><Rank /></el-icon>
      <h3>{{ element.oem }}</h3>
    </div>
  </template>
</draggable>
```

#### SubTask 12.3: 验证 (5 分钟)

```bash
# 1. 浏览器
# 访问 http://localhost:5173/admin/compare?ids=1,2,3,4,5,6
# 预期: 6 列 grid, 差异高亮, 拖拽生效

# 2. E2E
cd spike-test && python _test_compare.py
# 预期: PASS

# 3. Playwright 截图
cd frontend && npx playwright test tests/visual/compare.spec.ts
# 预期: PASS
```

#### SubTask 12.4: 提交

```bash
git add -A && git commit -m "task-12: P3.5 对比 UI 完整版 - 6 列 + 差异高亮 + 拖拽 + 打印"
git push origin master
```

---

## 🔵 Task 9: P3.1 容差 UI (0.5 session) — 备选 Next

### SubTask 详细步骤

#### SubTask 9.1: 后端确认 (5 分钟, 必跑)

```bash
# Day 8.4 已实现, 确认 tolerance 参数存在
grep -n "tolerance" backend/src/SakuraFilter.Api/Controllers/AdminSearchController.cs
# 预期: 看到 public async Task<ActionResult> Search(int? tolerance, ...)
```

#### SubTask 9.2: 前端下拉 (15 分钟)

```bash
# 改造 AdminSearchView.vue
touch /tmp/diff.txt  # 占位
```

**代码片段**:
```vue
<template>
  <el-popover placement="bottom" :width="300" trigger="hover">
    <template #content>
      <p>切换容差会显著影响搜索速度</p>
      <p>10mm 比 1mm 慢 5-10 倍</p>
    </template>
    <el-select v-model="tolerance" placeholder="尺寸容差" style="width: 120px">
      <el-option label="±1mm" :value="1" />
      <el-option label="±5mm" :value="5" />
      <el-option label="±10mm" :value="10" />
    </el-select>
  </el-popover>
</template>

<script setup lang="ts">
const tolerance = ref(5)  // 默认 5mm
// 搜索请求自动带 tolerance
const search = async () => {
  const { data } = await http.get('/api/products/search', {
    params: { tolerance: tolerance.value, h1, h2, d1, d2 }
  })
}
</script>
```

#### SubTask 9.3: 验证 (10 分钟)

```bash
# 1. 浏览器手动
# 访问 /admin/search, 切换 ±1mm → 看到结果数减少

# 2. DevTools Network
# 看到请求 URL 含 ?tolerance=1

# 3. E2E
cd spike-test && python _test_tolerance_ui.py
# 预期: 3/3 PASS
```

---

## 🟣 Task 10: P3.2 Excel 粘贴 (1 session) — 备选 Next

### SubTask 详细步骤

#### SubTask 10.1: 后端 `POST /api/search/batch-oem` (30 分钟)

```bash
touch backend/src/SakuraFilter.Api/Controllers/BatchOemController.cs
```

**代码模板**:
```csharp
[ApiController]
[Route("api/search")]
public class BatchOemController : ControllerBase
{
    public record BatchOemRequest(List<string> Oems);
    public record BatchOemResult(string Oem, bool Found, long? ProductId, string? OemBrand);

    [HttpPost("batch-oem")]
    public async Task<ActionResult<List<BatchOemResult>>> BatchOem(
        [FromBody] BatchOemRequest req, CancellationToken ct)
    {
        if (req.Oems == null || req.Oems.Count == 0)
            return BadRequest("oems 不能为空");
        if (req.Oems.Count > 500)
            return BadRequest("oems 最多 500 个");

        // 走 Meili 索引 (Day 9.3 SearchService 已封装)
        var results = await _searchService.SearchByOemBatchAsync(req.Oems, ct);
        return Ok(results);
    }
}
```

**SearchService 扩展** (30 分钟):
```csharp
public async Task<List<BatchOemResult>> SearchByOemBatchAsync(
    List<string> oems, CancellationToken ct)
{
    // Meili 多值查询 (search engine 支持 ANY 语义)
    var filter = $"oem_no_2 IN [{string.Join(',', oems.Select(o => $"\"{o}\""))}]";
    var meiliResults = await _meili.Index("products").SearchAsync<MeiliProductDto>("", new SearchQuery
    {
        Filter = filter,
        Limit = oems.Count * 2  // 每个 OEM 最多 2 个候选
    });

    // 聚合: 每个 OEM 取 best match (按 sort_order)
    return oems.Select(oem => new BatchOemResult(
        Oem: oem,
        Found: meiliResults.Hits.Any(h => h.OemNo2 == oem || h.OemNo3 == oem),
        ProductId: meiliResults.Hits.FirstOrDefault(h => h.OemNo2 == oem)?.Id,
        OemBrand: meiliResults.Hits.FirstOrDefault(h => h.OemNo2 == oem)?.OemBrand
    )).ToList();
}
```

#### SubTask 10.2: 前端批量粘贴 (30 分钟)

**代码片段** (PublicSearchView.vue):
```vue
<template>
  <el-tabs v-model="mode">
    <el-tab-pane label="单条" name="single">
      <el-input v-model="singleOem" placeholder="OEM 编号" />
    </el-tab-pane>
    <el-tab-pane label="批量粘贴" name="batch">
      <el-input v-model="batchText" type="textarea" :rows="10"
                placeholder="支持 tab/换行/逗号分隔, 最多 500 个" />
      <el-button @click="searchBatch">查询 ({{ parsedOems.length }})</el-button>
    </el-tab-pane>
  </el-tabs>

  <el-table v-if="results.length" :data="results">
    <el-table-column prop="oem" label="OEM 编号" />
    <el-table-column label="状态">
      <template #default="{ row }">
        <el-tag :type="row.found ? 'success' : 'danger'">
          {{ row.found ? '✓ 命中' : '✗ 未命中' }}
        </el-tag>
      </template>
    </el-table-column>
    <el-table-column prop="productId" label="产品 ID" />
    <el-table-column prop="oemBrand" label="OEM Brand" />
  </el-table>
</template>

<script setup lang="ts">
const batchText = ref('')
const parsedOems = computed(() => {
  return [...new Set(
    batchText.value
      .split(/[\t\n,;]+/)  // tab/换行/逗号/分号
      .map(s => s.trim())
      .filter(Boolean)
  )]
})

const searchBatch = async () => {
  const { data } = await http.post('/api/search/batch-oem', { oems: parsedOems.value })
  results.value = data
}
</script>
```

#### SubTask 10.3: 验证 (20 分钟)

```bash
# 1. curl 测试
curl -X POST http://localhost:5148/api/search/batch-oem \
  -H "Content-Type: application/json" \
  -d '{"oems":["11427622448","11427622449"]}'
# 预期: 200 + JSON (2 行结果)

# 2. 边界测试
# 中文: "滤清器 1142" → 单元素, 正确解析
# 斜杠: "AB/CD/123" → 单元素, 正确解析
# 引号: '"OEN-123"' → 保留引号
# 空行 / 重复 / 前后空格 → 健壮处理

# 3. E2E
cd spike-test && python _test_batch_oem.py
# 预期: PASS + 耗时 < 1s
```

---

## 🔴 Task 13: P4.1 E2E 全量 (1 session) — Phase 4 入口

### 启动检查

```bash
# 1. 备份 (spike-test 增量, 建议备份)
git status  # clean

# 2. 现有 E2E 列表
ls spike-test/_test_*.py
# 预期: ~10 个 (Day 9-10 + P2 全部)

# 3. 缺哪些
ls spike-test/_test_dict_*.py 2>/dev/null  # 字典 E2E
# 预期: 6 个新文件待建
```

### SubTask 详细步骤

#### SubTask 13.1: 6 个新字典 E2E (30 分钟)

```bash
# 模板: 复制 spike-test/_test_day10_oem_brands.py
cp spike-test/_test_day10_oem_brands.py spike-test/_test_dict_product_name1.py
# 改 dict 名 → 跑 → 验证
for dict in product_name1 product_name2 type oem_no3 media machine engine; do
  sed "s/oem-brand/${dict//_/-}/g" spike-test/_test_day10_oem_brands.py \
    > spike-test/_test_dict_${dict}.py
done

# 跑全部
for f in spike-test/_test_dict_*.py; do
  echo "=== $f ==="
  python "$f" || echo "FAIL: $f"
done
```

#### SubTask 13.2: P3 E2E (30 分钟)

```bash
# 已有: _test_tolerance_ui.py / _test_batch_oem.py / _test_public_product.py / _test_compare.py
# (从 Task 9/10/11/12 复制过来)

ls spike-test/_test_p3_*.py 2>/dev/null || echo "[TODO] 创建 P3 E2E"
```

#### SubTask 13.3: CI 拆分 matrix (30 分钟)

```yaml
# .github/workflows/e2e.yml
jobs:
  e2e-matrix:
    strategy:
      matrix:
        test: [day10, p22, type-ordering, pause-resume, cdn-switch,
               dict-pn1, dict-pn2, dict-type, dict-oem3, dict-media, dict-machine, dict-engine,
               tolerance-ui, batch-oem, public-product, compare]
    steps:
      - uses: actions/checkout@v4
      - name: Run E2E
        run: cd spike-test && python _test_${{ matrix.test }}.py
```

**预期**: 总耗时 < 10 分钟, 单 job < 3 分钟。

---

## 🟠 Task 14: P4.2+P4.3 契约 + 视觉 (1 session)

### SubTask 详细步骤

#### SubTask 14.1: `GET /api/admin/dict/_schema` (30 分钟)

```bash
touch backend/src/SakuraFilter.Api/Controllers/DictSchemaController.cs
```

**代码**:
```csharp
[ApiController]
[Route("api/admin/dict")]
public class DictSchemaController : ControllerBase
{
    [HttpGet("_schema")]
    public ActionResult<Dictionary<string, Dictionary<string, string>>> Schema()
    {
        // 反射 DbContext.Model 提取所有 entity 字段
        var dicts = new[] {
            typeof(DictOemBrand), typeof(DictProductName1), typeof(DictProductName2),
            typeof(DictType), typeof(DictOemNo3), typeof(DictMedia),
            typeof(DictMachine), typeof(DictEngine)
        };
        var schema = new Dictionary<string, Dictionary<string, string>>();
        foreach (var t in dicts)
        {
            var props = t.GetProperties()
                .ToDictionary(p => p.Name, p => p.PropertyType.Name);
            schema[t.Name] = props;
        }
        return Ok(schema);
    }
}
```

#### SubTask 14.2: 前端契约测试 (20 分钟)

```bash
cd frontend
npm install --save-dev zod
touch tests/contract/dict-schema.test.ts
```

```typescript
import { describe, expect, test } from 'vitest'
import { z } from 'zod'

const DictItemSchema = z.object({
  id: z.number(),
  value: z.string(),
  sortOrder: z.number(),
  isDeleted: z.boolean(),
  deletedAt: z.string().nullable()
})

describe('dict schema contract', () => {
  test('backend dict_item matches frontend', async () => {
    const res = await fetch('http://localhost:5148/api/admin/dict/_schema', {
      headers: { 'X-Admin-Token': process.env.ADMIN_TOKEN }
    })
    const schema = await res.json()
    // 校验每个字典有 5 个字段
    for (const [name, fields] of Object.entries(schema)) {
      expect(fields).toHaveProperty('id')
      expect(fields).toHaveProperty('value')
      expect(fields).toHaveProperty('sortOrder')
    }
  })
})
```

#### SubTask 14.3: Playwright 视觉回归 (30 分钟)

```bash
cd frontend
npm install --save-dev pixelmatch pngjs
touch tests/visual/dict-pages.spec.ts
```

```typescript
import { test, expect } from '@playwright/test'

const DICTS = ['oem-brand', 'product-name1', 'product-name2', 'type',
               'oem-no3', 'media', 'machine', 'engine']

for (const dict of DICTS) {
  test(`dict ${dict} visual baseline`, async ({ page }) => {
    await page.goto(`http://localhost:5173/admin/dict/${dict}`)
    await expect(page.locator('.dict-table')).toBeVisible()
    await page.screenshot({
      path: `tests/visual/baselines/dict-${dict}.png`,
      fullPage: true
    })
  })
}
```

**像素 diff** (5% 阈值):
```typescript
import pixelmatch from 'pixelmatch'
import { PNG } from 'pngjs'
import fs from 'fs'

test('compare with baseline', async () => {
  const baseline = PNG.sync.read(fs.readFileSync('tests/visual/baselines/dict-type.png'))
  const current = PNG.sync.read(fs.readFileSync('tests/visual/current/dict-type.png'))
  const diff = new PNG({ width: baseline.width, height: baseline.height })
  const changed = pixelmatch(baseline.data, current.data, diff.data,
    baseline.width, baseline.height, { threshold: 0.05 })
  expect(changed / (baseline.width * baseline.height)).toBeLessThan(0.05)
})
```

---

## 🟤 Task 15: P5 打磨 (1 session)

### SubTask 详细步骤

#### SubTask 15.1: P5.1 Volume 自动计算 (15 分钟)

```bash
# 改造 AdminProductFormView.vue
grep -n "cartonLength\|cartonWidth\|cartonHeight" frontend/src/views/admin/AdminProductFormView.vue
# 预期: 3 个表单字段
```

**代码**:
```typescript
import { watch } from 'vue'

// 监听 L/W/H 变化自动计算 Volume
watch(
  [() => form.cartonLength, () => form.cartonWidth, () => form.cartonHeight],
  ([l, w, h]) => {
    if (l && w && h) {
      form.cartonVolume = Number(((l * w * h) / 1e9).toFixed(4))
    }
  }
)
// masterBox 同理
```

#### SubTask 15.2: P5.2 字段 popover (30 分钟)

```bash
# 1. 新建 Entity
touch backend/src/SakuraFilter.Core/Entities/DictFieldHelp.cs
```

**Entity**:
```csharp
public class DictFieldHelp
{
    public long Id { get; set; }
    public string FieldName { get; set; } = "";
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public string? Example { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

**Migration**:
```bash
cd backend/src/SakuraFilter.Api
dotnet ef migrations add AddDictFieldHelp
```

**API + 组件**: 略 (见 spec.md P5.2)

#### SubTask 15.3: P5.3 主题切换 (30 分钟)

```bash
mkdir -p frontend/src/stores
touch frontend/src/stores/theme.ts
```

```typescript
import { defineStore } from 'pinia'

export const useThemeStore = defineStore('theme', {
  state: () => ({
    current: (localStorage.getItem('theme') as 'light' | 'dark') || 'light'
  }),
  actions: {
    toggle() {
      this.current = this.current === 'light' ? 'dark' : 'light'
      localStorage.setItem('theme', this.current)
      document.documentElement.classList.toggle('dark', this.current === 'dark')
    }
  }
})
```

**CSS 变量**:
```css
/* frontend/src/styles/theme.css */
:root {
  --bg-primary: #ffffff;
  --text-primary: #000000;
  --border-color: #e5e5e5;
}
.dark {
  --bg-primary: #0a0a0a;
  --text-primary: #fafafa;
  --border-color: #262626;
}
```

#### SubTask 15.4: P5.4 帮助页 (20 分钟)

```bash
npm install markdown-it
touch frontend/src/views/admin/AdminHelpView.vue
```

**内容模块**:
```vue
<template>
  <div class="help-view">
    <h1>操作指南</h1>
    <div v-html="renderedMarkdown" />
  </div>
</template>

<script setup lang="ts">
import MarkdownIt from 'markdown-it'
const md = new MarkdownIt()
const content = `
# 快速开始
1. 登录后台
2. 选择字典管理
3. 拖拽排序

# 字典使用规范
- oem-brand: 替代品牌
- type: 滤芯类型
- ...

# 批量导入流程
1. 准备 XLSX
2. 拖到后台导入区
3. 等待 ETL 完成
`
const renderedMarkdown = computed(() => md.render(content))
</script>
```

---

# 总执行顺序 (优先级 + 依赖)

```
Day 11+ 推荐执行顺序:
  Step 1: 跑 Session 启动检查表 (5min) → 全绿
  Step 2: Task 11 (P3.3 前台页)        → 1 session
  Step 3: Task 12 (P3.5 对比 UI)        → 0.5 session (可与 Step 2 并行)
  Step 4: Task 9 (P3.1 容差)            → 0.5 session
  Step 5: Task 10 (P3.2 Excel)          → 1 session
  Step 6: Task 13 (P4.1 E2E)            → 1 session
  Step 7: Task 14 (P4.2+4.3 契约+视觉)  → 1 session
  Step 8: Task 15 (P5 打磨)             → 1 session
  Step 9: git push → CI 全绿 → 结项
```

**总剩余工作量**: 6-7 session

