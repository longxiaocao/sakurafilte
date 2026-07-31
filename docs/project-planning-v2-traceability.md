# 项目规划 V2 需求追溯矩阵

需求唯一基线：`项目规划V2.docx`、`项目规划V2.xlsx`。本矩阵把规划条目映射到当前代码、自动化测试和运行态证据。状态定义如下：

- `已完成`：实现与相应验证均已存在。
- `本轮待回归`：实现已存在，本轮修改后需要重新执行的验证。
- `已确认不做`：客户已确认不进入交付范围，不计为缺口。
- `明确暂缓`：已有 ADR 的非规划核心改进，不计为项目规划 V2 缺口。

## 章节追溯

| 规划章节 | 验收要求 | 实现文件 | 验证证据 | 状态 |
|---|---|---|---|---|
| 一、架构总览 | MR.1 串联产品、OEM、机型、参数、图片；Meilisearch 聚合检索 | `Product.cs`、`PostgresSearchProvider.cs`、`MeiliSearchProvider.cs` | 后端解决方案测试；Meilisearch 本地运行态 | 本轮待回归 |
| 二、分区 1-5、7 | 后台六模块、MR.1/OEM3 关联、七分区数据管理 | `AdminProductService.cs`、`DictionaryEndpoints.cs`、`AdminProductFormView.vue` | 后端 677 项测试 | 本轮待回归 |
| 二、分区 6 | 不纳入前台和主链路 | 无公开路由或业务实体依赖 | 客户确认结论 | 已确认不做 |
| 三、全局框架 | About/Product/News/Contact、全局搜索、实时联想 | `AppHeader.vue`、`PublicInfoView.vue`、`PublicTypeaheadEndpoints.cs` | 公开流程 E2E | 本轮待回归 |
| 三、列表与详情 | 双维筛选、OEM3 卡片、公开详情、询盘 | `AggregateSearchView.vue`、`PublicProductView.vue`、`InquiryApp.vue` | 公开流程 E2E | 本轮待回归 |
| 四、六个后台模块 | 产品、OEM、尺寸、图片、技术参数、机型与 ETL 导入 | `AdminProductFormView.vue`、`AdminXrefReorderView.vue`、`AdminEtlView.vue` | 后端与前端契约测试 | 本轮待回归 |
| 五、检索联动 | 模糊、目录级联、尺寸范围、OEM 双向、上架排序 | `PublicSearchController.cs`、`PublicMachineBrandsController.cs`、`AggregateSearchView.vue` | 集成测试、公开流程 E2E | 本轮待回归 |
| 六、架构图 | 数据链路与前后端职责可追溯 | 本矩阵和上述模块边界 | 代码静态核对 | 已完成 |
| 七、落地注意事项 | 主键、品牌隔离、特殊字符、索引与导入校验 | `Product.cs`、`EtlImportService.cs`、`MeiliSearchProvider.cs` | 后端测试和 ETL 测试 | 本轮待回归 |
| 八、客户确认项 | Machine Type 双轨；OEM3 独立主图；分区 6 不入主链路 | `MachineApplication.cs`、`ProductImage`、`CrossReference` | 客户确认结论与实现核对 | 已完成 |

## Excel 属性逐项追溯（65 条）

| 属性行 | 分区与字段 | 实现文件 | 验证证据 | 实现状态 |
|---|---|---|---|---|
| 1 | 分区 1 / Product Name 1 | `Product.cs`、`AdminProductFormView.vue`、产品名字典 | `ProductFormMr1Rules.test.ts` | 本轮待回归 |
| 2 | 分区 1 / Product Name 2 | `Product.cs`、`AdminProductFormView.vue`、产品名字典 | 后端 DTO 与前端类型检查 | 本轮待回归 |
| 3 | 分区 1 / Type | `Product.cs`、`AdminProductFormView.vue`、类型字典 | 公开类型快捷筛选 E2E | 本轮待回归 |
| 4 | 分区 1 / MR.1 | `Product.cs`、`AdminProductService.cs` | `ProductFormMr1Rules.test.ts`；公开 DTO 不暴露 MR.1 | 本轮待回归 |
| 5 | 分区 1 / OEM 2 | `Product.cs`、`CrossReference`、`AdminProductService.cs` | 聚合搜索与公开详情测试 | 本轮待回归 |
| 6 | 分区 1 / 上架 | `Product.IsPublished`、`PublicSearchController.cs` | 公开产品发布状态测试 | 本轮待回归 |
| 7 | 分区 2 / OEM Brand | `CrossReference`、OEM 品牌字典 | 字典契约测试 | 本轮待回归 |
| 8 | 分区 2 / OEM 3 | `CrossReference`、`build-product-url.ts` | URL、详情与搜索 E2E | 本轮待回归 |
| 9 | 分区 2 / 上架 | `CrossReference.IsPublished` | 公开产品发布状态测试 | 本轮待回归 |
| 10 | 分区 2 / Remark | `Product.Remark`、`AdminProductFormView.vue` | 后端 DTO 测试 | 本轮待回归 |
  > **位置说明 (2026-07-30 核实)**: Excel 原文"对应产品区 remark 栏"暗示 remark 属产品级。代码实现将 `remark` 落在 `Product` 主表 (分区 1)，`CrossReference` 实体无 remark 字段。经用户确认 (Task 4 选 A)，现状符合 spec 意图，产品级 remark 即可，不新增 `cross_references.remark` 列。
| 11 | 分区 2 / 排序 | `CrossReference.SortOrder`、`AdminXrefReorderView.vue` | 公开聚合排序测试 | 本轮待回归 |
| 12 | 分区 2 / Machine Type | `CrossReference.MachineType`、`MachineApplication.MachineCategory` | 目录与聚合筛选集成测试 | 本轮待回归 |
| 13 | 分区 3 / H1 | `Product.H1Mm`、`H1MmRaw` | 尺寸范围搜索测试 | 本轮待回归 |
| 14 | 分区 3 / H2 | `Product.H2Mm`、`H2MmRaw` | 尺寸范围搜索测试 | 本轮待回归 |
| 15 | 分区 3 / H3 | `Product.H3Mm`、`H3MmRaw` | 尺寸范围搜索测试 | 本轮待回归 |
| 16 | 分区 3 / H4 | `Product.H4Mm`、`H4MmRaw` | DTO 与 ETL 测试 | 本轮待回归 |
| 17 | 分区 3 / D1 | `Product.D1Mm`、`D1MmRaw` | Meili 和 PostgreSQL 范围检索测试 | 本轮待回归 |
| 18 | 分区 3 / D2 | `Product.D2Mm`、`D2MmRaw` | Meili 和 PostgreSQL 范围检索测试 | 本轮待回归 |
| 19 | 分区 3 / D3 | `Product.D3Mm`、`D3MmRaw` | Meili 和 PostgreSQL 范围检索测试 | 本轮待回归 |
| 20 | 分区 3 / D4 | `Product.D4Mm`、`D4MmRaw` | DTO 与 ETL 测试 | 本轮待回归 |
| 21 | 分区 3 / D7 | `Product.D7Thread` | DTO 与公开详情测试 | 本轮待回归 |
| 22 | 分区 3 / D8 | `Product.D8Thread` | DTO 与公开详情测试 | 本轮待回归 |
| 23 | 分区 3 / No. Check Valves | `Product.NoCheckValves`、原始值列 | ETL 原值解析测试 | 本轮待回归 |
| 24 | 分区 3 / No. Bypass Valves | `Product.NoBypassValves`、原始值列 | ETL 原值解析测试 | 本轮待回归 |
| 25 | 分区 4 / 图片 1 | `ProductImage`、`AdminProductImageService.cs` | 图片服务测试与公开主图 E2E | 本轮待回归 |
| 26 | 分区 4 / 图片 2 | `ProductImage` slot 2 | 图片服务测试 | 本轮待回归 |
| 27 | 分区 4 / 图片 3 | `ProductImage` slot 3 | 图片服务测试 | 本轮待回归 |
| 28 | 分区 4 / 图片 4 | `ProductImage` slot 4 | 图片服务测试 | 本轮待回归 |
| 29 | 分区 4 / 图片 5 | `ProductImage` slot 5 | 图片服务测试 | 本轮待回归 |
| 30 | 分区 4 / 图片 6 | `ProductImage` slot 6 | 图片服务测试 | 本轮待回归 |
| 31 | 分区 5 / Media Name | `Product.Media`、`MediaDictService.cs` | 字典契约测试 | 本轮待回归 |
| 32 | 分区 5 / Media Model | `Product.MediaModel`、`MediaDictService.cs` | 字典契约测试 | 本轮待回归 |
| 33 | 分区 5 / Bypass Valve Setting LR | `Product.BypassValveLr`、原始值列 | DTO 与 ETL 测试 | 本轮待回归 |
| 34 | 分区 5 / Bypass Valve Setting HR | `Product.BypassValveHr`、原始值列 | DTO 与 ETL 测试 | 本轮待回归 |
| 35 | 分区 5 / Efficiency 1 | `Product.Efficiency1` | DTO 与公开详情测试 | 本轮待回归 |
| 36 | 分区 5 / Efficiency 2 | `Product.Efficiency2` | DTO 与公开详情测试 | 本轮待回归 |
| 37 | 分区 5 / Collapse Pressure | `Product.CollapsePressureBar`、原始值列 | DTO 与 ETL 测试 | 本轮待回归 |
| 38 | 分区 5 / Seal Material | `Product.SealingMaterial` | DTO 与公开详情/对比测试 | 本轮待回归 |
| 39 | 分区 5 / Temperature Range | `Product.TempRange` | DTO 与公开详情/对比测试 | 本轮待回归 |
| 40 | 分区 5 / Bypass Pressure | `Product.BypassPressure`、原始值列 | DTO 与 ETL 测试 | 本轮待回归 |
| 41 | 分区 6 / 无需逻辑关系 | 不建业务主链路实体，不提供公开入口 | 客户确认结论 | 已确认不做 |
| 42 | 分区 7 / machine brand | `MachineApplication.MachineBrand`、机型字典 | 目录接口与聚合搜索测试 | 本轮待回归 |
| 43 | 分区 7 / machine model | `MachineApplication.MachineModel`、机型字典 | 目录接口与聚合搜索测试 | 本轮待回归 |
| 44 | 分区 7 / model name | `MachineApplication.ModelName` | 产品表单 DTO 测试 | 本轮待回归 |
| 45 | 分区 7 / Engine Brand | `MachineApplication.EngineBrand`、发动机字典 | 产品表单 DTO 测试 | 本轮待回归 |
| 46 | 分区 7 / Engine Type | `MachineApplication.EngineType`、发动机字典 | 产品表单 DTO 测试 | 本轮待回归 |
| 47 | 分区 7 / Engine Energy | `MachineApplication.EngineEnergy` | 产品表单 DTO 测试 | 本轮待回归 |
| 48 | 分区 7 / Production date | `ProductionDateStart`、`ProductionDateEnd` | 产品表单 DTO 测试 | 本轮待回归 |
| 49 | 分区 7 / Power | `MachineApplication.Power` | 产品表单 DTO 测试 | 本轮待回归 |
| 50 | 分区 7 / Serial number (from) | `MachineApplication.SerialNumberFrom` | 产品表单 DTO 测试 | 本轮待回归 |
| 51 | 分区 7 / Car body type | `MachineApplication.CarBodyType` | 产品表单 DTO 测试 | 本轮待回归 |
| 52 | 分区 7 / Series | `MachineApplication.Series` | 产品表单 DTO 测试 | 本轮待回归 |
| 53 | 分区 7 / Serial number (to) | `MachineApplication.SerialNumberTo` | 产品表单 DTO 测试 | 本轮待回归 |
| 54 | 分区 7 / CO2 emission standard | `MachineApplication.Co2EmissionStandard` | 产品表单 DTO 测试 | 本轮待回归 |
| 55 | 分区 7 / Transmission type | `MachineApplication.TransmissionType` | 产品表单 DTO 测试 | 本轮待回归 |
| 56 | 分区 7 / Engine displacement | `MachineApplication.EngineDisplacement` | 产品表单 DTO 测试 | 本轮待回归 |
| 57 | 分区 7 / Number of cylinders | `MachineApplication.NumberOfCylinders` | 产品表单 DTO 测试 | 本轮待回归 |
| 58 | 分区 7 / GVWR | `MachineApplication.Gvwr` | 产品表单 DTO 测试 | 本轮待回归 |
| 59 | 分区 7 / Tonnage | `MachineApplication.Tonnage` | 产品表单 DTO 测试 | 本轮待回归 |
| 60 | 分区 7 / Geographic area | `MachineApplication.GeographicArea` | 产品表单 DTO 测试 | 本轮待回归 |
| 61 | 分区 7 / Chassis type | `MachineApplication.ChassisType` | 产品表单 DTO 测试 | 本轮待回归 |
| 62 | 分区 7 / Engine model | `MachineApplication.EngineModel` | 产品表单 DTO 测试 | 本轮待回归 |
| 63 | 分区 7 / Cabin type | `MachineApplication.CabinType` | 产品表单 DTO 测试 | 本轮待回归 |
| 64 | 分区 7 / Capacity | `MachineApplication.Capacity` | 产品表单 DTO 测试 | 本轮待回归 |
| 65 | 分区 7 / Engine serial number | `MachineApplication.EngineSerialNumber`；适配通过 `ProductId` 多对多反查 | PostgreSQL 聚合筛选测试、目录联动 E2E | 本轮待回归 |

## 已确认交付边界

- Machine Type 采用双轨：OEM3 的 `machine_type` 负责前台展示/排序标签，机型适配的 `machine_category` 负责三级目录；两者在公开检索中共同受发布边界约束。
- 图片 1 为每个 OEM3 独立主图；图片 2-6 为同 MR.1 共享详情图。
- 分区 6 不进入主链路、不在前台展示，按客户确认不额外建空业务表。
- About、News、Contact 的正式业务文案可为空，由 `VITE_PUBLIC_*_TEXT` 在部署时补充；询盘使用 `VITE_INQUIRY_EMAIL` 的 mailto，不实现工单、CRM、webhook 或后台询盘列表。
- **OemNoDisplay 派生逻辑 (2026-07-30 核实)**: 规划V2.docx 第四章 M1 描述"Product Name 1 自动同步关联 OEM3 展示名"，实际代码 `AdminProductService.CreateAsync/UpdateAsync` 的 `OemNoDisplay` 从 `Oem2` 派生 (非 ProductName1)。经用户确认 (Task 5 选 A)，OEM2 派生更符合业务 (OEM2 是产品自身编号，PN1 是产品类型名)，维持现状，标注"规划描述与实现偏差，实现更优"。
- **聚合搜索高亮净化 (2026-07-30 核实)**: spec F14 字面要求 DOMPurify (ALLOWED_TAGS: ['mark'])，实际 `frontend/src/utils/html-sanitizer.ts` 用 30 行正则等价实现 (先全量 HTML 转义再仅还原 `<mark>` 标签)。经用户确认 (Task 6 选 A)，维持现状，安全性更强 (比 DOMPurify 默认配置更严格) + 节省 22KB 包体积。决策详见 `.ai/decisions.md` ADR #21。

## 本轮回归结论（2026-07-28）

- 表格中标为“本轮待回归”的条目均已完成本轮验证：后端构建零警告零错误，`dotnet test` 共 677 项通过；前端 `npm run type-check` 和 `npm run build` 通过。
- Chromium 浏览器回归共 95 项：首轮 93 项通过，两个失败均为深度测试遗留的已删除 `data-testid` 定位器；将定位器改为当前公开聚合搜索框后，覆盖该流程的 `deep-flow.spec.ts` 34 项全部通过。因此 95 项回归已闭环。
- 新增目录联动 E2E 验证了场景、品牌、型号三级点击会同步关键词与 `machineCategory` URL 查询条件，并成功请求公开聚合搜索接口。
- 运行态：Meilisearch 为 `available`，`products` 索引 49,990 条文档，公开机型目录返回 5 个场景；本地 API 健康检查为 `healthy`。

## 非阻塞暂缓项

- 孤儿图片全量扫描清理由 ADR #3 明确暂缓，当前覆盖上传时的旧文件容错删除已经实现。
- PostgreSQL keyset 分页由 ADR #5 明确暂缓；既有压测不显示 OFFSET 深分页为当前主瓶颈，生产搜索主路径为 Meilisearch。

## 运行前置条件

- 后端需要 `ConnectionStrings__Postgres` 与 `Jwt__SigningKey`；Meilisearch 通过 `localhost:7700` 或等价部署地址可用。
- 正式公开文案与询盘邮箱由部署环境变量提供；未配置时应用仍可运行，但公开页显示中性空态，询盘按钮提示配置缺失。
