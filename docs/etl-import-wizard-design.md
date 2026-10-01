# ETL 导入向导详细设计（P0 模板上传 / P1 策略口语化 / P2 行级错误）

> 2026-08-25 ｜ 目标：让交付后的**客户**能自助导入产品数据（当前 ETL 是技术工具，客户无法使用）
> 设计审核：多轮自审（字段契约/权限/安全/边界/兼容性），结论：可实施

---

## 0. 背景与目标

**现状痛点**（用户反馈确认）：
- ETL 触发要求 JSONL 文件路径（容器内路径），客户无容器概念，无法操作
- "拖拽 XLSX"实际**没有真正上传文件**（前端只把文件名填入 `jsonlPath`，假设文件已在服务器目录）
- 模式术语（全量重建/仅新增/增量更新）晦涩
- 错误只有汇总计数，不定位到行

**目标**：客户流程 = **下载模板 → 按说明填表 → 上传文件 → 选策略 → 看结果（成功/失败+行级原因）**

---

## 1. 现状分析（已核实代码）

| 项 | 现状 | 文件 |
|---|---|---|
| XLSX 解析 | ✅ 已有 `EtlSpreadsheetAdapter.ConvertAsync`（**表头行 = JSON 字段名**） | EtlSpreadsheetAdapter.cs L63-79 |
| XLSX 生成库 | ✅ ClosedXML 0.102.3（可直接生成模板，零新依赖） | SakuraFilter.Etl.csproj |
| 触发端点 | `POST /admin/etl/trigger`（body: jsonlPath/mode/entityType/dryRun） | AdminEtlEndpoints.cs L26 |
| 前端触发 | `etlApi.trigger`，拖拽只填路径**不传文件** | AdminEtlView.vue L66-88 |
| 字段契约 | products: mr_1* 必填；xrefs: oem_brand*+oem_no_3*；apps: mr_1*+machine_brand*+machine_model* | EtlImportService.cs |

**核心结论**：P0 只需新增 **2 个端点**（模板下载 + 文件上传）+ 前端上传改造，**trigger 链路完全复用**，风险低。

---

## 2. P0：模板下载 + 文件上传（客户"能导入"）

### 2.1 模板字段契约（表头 = JSON 字段名，与 ConvertAsync 严格一致）

**products 模板（19 列）**：
| 列（JSON key） | 必填 | 说明/示例 |
|---|---|---|
| mr_1 | **\*** | 自有产品编码（唯一主键）示例 `MR00000001` |
| oem_no_display | | 展示用 OEM 号 |
| type | | 产品类型（OIL FILTER / AIR FILTER / FUEL FILTER / HYDRAULIC FILTER / OTHER） |
| product_name_1 | | 产品名称 1（示例 OIL FILTER） |
| product_name_2 | | 产品名称 2（可选） |
| product_name_3 | | 产品名称 3（可选） |
| oem_2 | | OEM 二级号（可选） |
| is_published | | 是否上架，填 `true` 或 `false`（默认 true） |
| remark | | 备注（可选） |
| d1_mm ~ d4_mm | | 直径 1-4（mm，数字） |
| h1_mm ~ h4_mm | | 高度 1-4（mm，数字） |
| d7_thread / d8_thread | | 螺纹规格（如 `M36x1.5`，可选） |

**xrefs 模板（7 列）**：
| 列 | 必填 | 说明 |
|---|---|---|
| mr_1 | **\*** | 关联的自有产品编码（导入时按 mr_1 反查产品，**必填**；缺失/找不到 → 该行跳过并计入失败明细） |
| oem_brand | **\* ** | OEM 品牌（如 MAHLE / BOSCH / FRAM） |
| oem_no_3 | **\*** | OEM 三级号（如 S1002390） |
| oem_2 | | OEM 二级号（可选） |
| sort_order | | 排序（数字，越小越靠前，可选） |
| machine_type | | 机型类型标签（可选，合法值 agriculture/commercial/construction/industrial/others） |
| is_published | | 是否上架 true/false |

**apps 模板（10 列）**：
| 列 | 必填 | 说明 |
|---|---|---|
| mr_1 | **\*** | 关联的自有产品编码 |
| machine_brand | **\*** | 机器品牌（如 DEUTZ / KUBOTA） |
| machine_model | **\*** | 机器型号（如 D5297） |
| model_name | | 型号全称（可选） |
| engine_brand | | 发动机品牌（可选） |
| engine_type | | 发动机型号（可选） |
| engine_energy | | 能源类型（可选） |
| production_date_start | | 生产起始日期（`yyyy-MM-dd`，可选） |
| is_ongoing | | 是否在产 true/false |
| machine_category | | 机型分类（agriculture/commercial/industrial/others） |

### 2.2 模板 XLSX 结构（3 行式，客户友好）

```
Row1 表头：mr_1 | oem_no_display | type | ...
Row2 说明：必填* 自有产品编码 | 展示用OEM号 | 产品类型 | ...（灰字，含格式示例）
Row3 示例：MR00000001 | 000001 | OIL FILTER | ...（示例数据行，橙色标注"示例行，导入前删除"）
```
- 单元格批注（comment）补充字段格式说明
- Sheet 名 = 实体名（products / xrefs / apps）

### 2.3 后端端点

**`GET /api/admin/etl/template?entity=products|xrefs|apps`**
- 返回 `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`
- 文件名：`sakurafilter-products-template.xlsx`
- 鉴权：Admin 策略（仅后台用户）

**`POST /api/admin/etl/upload`**（multipart/form-data: `file`, `entityType`）
- 校验：扩展名白名单 `.xlsx/.xls/.csv/.jsonl`；大小 ≤ 50MB
- 保存到容器内 `/tmp/etl-upload/{guid}.{ext}`（**guid 文件名，不信任原始名**）
- `.xlsx/.xls` → `EtlSpreadsheetAdapter.ConvertAsync` 转 JSONL（`/tmp/etl-upload/{guid}.jsonl`），成功后删除原 xlsx
- 返回 `{ jsonlPath, entityType, fileName, lineCount? }`
- 清理：上传时删除 >24h 的 `/tmp/etl-upload/*` 旧文件（防磁盘膨胀）
- 鉴权：Admin

### 2.4 前端改造（AdminEtlView）

1. **下载模板**：entity 旁加"下载模板"按钮（下拉选 3 实体）→ `GET /template` 触发浏览器下载（blob）
2. **文件上传**：原"拖拽填路径"改为**真正上传**：
   - `el-upload`（drag，accept `.xlsx,.xls,.csv,.jsonl`）
   - `on-change` → `FormData` POST `/upload` → 成功后自动：`form.jsonlPath = 返回路径`、`form.entity = 实体`
   - 上传中 loading + 失败 error
3. 保留"手动输入路径"输入框（高级用户）
4. i18n zh/en 补文案

---

## 3. P1：导入策略口语化

| 内部 mode | 现文案 | 新文案（zh） | 默认 |
|---|---|---|---|
| `upsert` | 增量更新（存在则覆盖） | **有则更新、无则新增（推荐）** | ✅ 默认 |
| `insert` | 仅新增（跳过已存在） | 仅新增（跳过已存在的） | |
| `full-load` | 全量重建（清空后重导） | 全量重建（清空后重新导入） | |

- 后端 mode 值不变（零后端改动），只改前端 i18n + 默认值确认（现有默认 `upsert`）
- en-US 同步：Upsert (update if exists, add if new - recommended) / Insert only / Full reload

---

## 4. P2：行级错误报告

**现状**：`Progress.Skipped*/Errors` 汇总 + `lastError`，不定位行。

**目标**：导入后展示失败明细表（行号 / 字段 / 原因）。

### 4.1 后端
- `EtlProgress` 新增 `RowErrors`（环形缓冲，上限 **100 条**）：`(LineNo, Field, Reason)`
- `RecordRowError(lineNo, field, reason)` 方法（线程安全）
- 在 `ImportProductsAsync/ImportXrefsAsync/ImportAppsAsync` 的 skip 分支埋点：
  - mr_1 为空 → `RecordRowError(lineNo, "mr_1", "必填字段为空")`
  - 必填字段缺失（oem_brand/oem_no_3/machine_brand/machine_model）→ 记录
  - 类型不匹配（尺寸/日期）→ 记录
  - 去重跳过（xrefs 品牌+OEM3 重复）→ 记录
- `GetActiveTaskInfo` 返回 `rowErrors`（最新 100 条）

### 4.2 前端
- "最近错误"区块增加"导入失败明细"表格：列 = 行号 / 字段 / 原因（无错误时隐藏）
- i18n 补文案

---

## 5. 安全与边界（自审清单）

| 项 | 处理 |
|---|---|
| 文件上传注入 | guid 文件名（不信任原始名）+ 扩展名白名单 |
| 大小限制 | ≤ 50MB（超限 413） |
| 临时目录膨胀 | 上传时清理 >24h 旧文件 |
| 模板列与解析一致性 | 表头=JSON key（ConvertAsync L64-79 行为），与 EtlImportService 读取 key 严格一致 |
| xrefs 关联 | **mr_1 必填**（ImportXrefsAsync L1947-1952：mr_1 缺失/找不到 → 跳过计 SkippedMissingMr1）——客户需先导 products 再导 xrefs |
| is_published 布尔 | 模板说明"填 true 或 false"，解析容错（"1/是/true"→true） |
| 日期格式 | 说明 `yyyy-MM-dd`，解析容错 |
| 空文件/无表头 | 上传转换报错返回明确 message |
| xlsx 多 Sheet | ConvertAsync 用 FirstRowUsed（首个 Sheet），模板单 Sheet |
| P2 内存 | 环形缓冲 100 条上限 |

---

## 6. 实施顺序与验证

1. **备份**：git 分支 `backup/pre-import-wizard`（当前 HEAD 快照）
2. **P0**：template + upload 端点 → 前端下载/上传改造 → 编译 0 错 → 部署 → 验证（下载模板→填 3 行→上传→trigger 成功）
3. **P1**：前端文案（零后端）→ 部署 → 验证下拉显示
4. **P2**：RowErrors 埋点 → GetActiveTaskInfo → 前端表格 → 部署 → 验证（构造错误行导入→表格显示行号/原因）
5. **推送**：每阶段完成后推送远端（git show 验证关键文件）
6. **回归**：现有 JSONL 路径触发 + dry-run + xlsx 拖拽（服务器路径）不受影响

---

## 7. 工作量与风险

- P0：0.5-1 天（模板生成 + 上传端点 + 前端改造）
- P1：0.5 天（纯文案）
- P2：0.5-1 天（埋点 + 报告）
- 风险：模板列名与 ConvertAsync 一致性（已核对）；上传并发/权限（容器 /tmp 可写，已验证）
