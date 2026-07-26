# 项目规划 V2 追溯矩阵

需求基线：`项目规划V2.docx`、`项目规划V2.xlsx`。状态仅表示代码与自动化验证的当前证据，不把外部内容、生产环境配置或客户确认误标为完成。

| 规划章节 | 验收要点 | 实现证据 | 状态 |
|---|---|---|---|
| 数据总览 | MR.1 聚合产品、OEM、机型与图片 | `Product`、`CrossReference`、`MachineApplication`、`ProductImage` 实体与 `ProductDbContext` | 已实现 |
| 分区 1 | MR.1、OEM2、产品名、类型、上架 | `AdminProductService`、`AdminProductFormView.vue` | 已实现 |
| 分区 2 | OEM Brand/OEM3、排序、Machine Type | `AdminXrefReorderView.vue`、`PublicMachineBrandsController`、公开聚合搜索 | 已实现 |
| 分区 3 | 尺寸原值+数值、范围检索 | D1-H4 原始列；`20260726014656_AddRawParameterValues` 补阀门原值；聚合搜索 tolerance | 已实现 |
| 分区 4 | OEM3 主图、MR.1 详情图、批量导入、替换删除 | `AdminProductImageService.ImportFolderAsync`、产品表单图片区、`GalleryApp.vue` | 已实现 |
| 分区 5 | 技术参数与特殊字符 | `*_raw` 原始列、管理表单、ETL 解析与公开详情/对比 DTO | 已实现 |
| 分区 6 | 不纳入主链路 | 无业务字段；未新增前端入口 | 已实现 |
| 分区 7 | 机型三级树与适配反查 | `MachineApplication`、公开目录 `/api/public/machine-brands/catalog`、`AggregateSearchView.vue` | 已实现 |
| 前端框架 | About/Product/News/Contact、全局搜索与实时下拉联想 | `AppHeader.vue`、`PublicInfoView.vue`、`/search/aggregate` | 已实现 |
| 产品列表 | 机型与类型交叉筛选、Air/Oil/Fuel/Hydraulic 快捷筛选、OEM 展示、MR.1 内部聚合不对外显示；`/search` 统一进入公开聚合页 | `AggregateSearchView.vue`、`router/index.ts`、`PublicMachineBrandsController` | 已实现 |
| 产品详情 | 主图/详情图、参数、适配、同 MR.1 推荐、询盘；公开界面不显示 MR.1 | `PublicProductView.vue`、Razor Detail、`GalleryApp.vue`、`InquiryApp.vue` | 已实现 |
| 检索规则 | Meilisearch 模糊命中、OEM 双向、范围检索 | `MeiliSearchProvider`、`AggregateSearch`、`IProductDetailService` | 已实现 |
| 批量导入 | XLSX 产品/OEM/机型、校验、进度 | `EtlSpreadsheetAdapter`、`EtlImportService`、`EtlSpreadsheetAdapterTests` | 已实现 |
| 缺图体验 | 回退资产与加载失败处理 | `/images/product-placeholder.svg`、`GalleryApp.vue`、`PublicProductView.vue` | 已实现 |

## 验证记录

- 后端：`dotnet test SakuraFilter.sln --no-restore --verbosity minimal`，677 项通过（API 639、ETL 38）。
- 前端：`npm run type-check` 与 `npm run build` 通过。
- 浏览器：本地 Vite 服务验证 `/about`、`/news`、`/contact` 与 `/images/product-placeholder.svg` 可达；导航可见 About us / Product / News / Contact us。
- 搜索运行态：本地 Windows Meilisearch v1.12.0 已完成全量重建，`products` 索引 49,990 条文档；筛选、排序、全文、停用词、容错与分词符配置均已完成，`POST /api/public/search/aggregate` 实测返回 `provider=meilisearch`。

## 运行态前置条件

- 后端需注入 `ConnectionStrings__Postgres` 与 `Jwt__SigningKey`；数据库迁移会在启动时检查并应用。
- 本地搜索服务需以 `localhost:7700` 提供 Meilisearch；生产环境应配置 API Key 并通过服务管理器托管进程。
- 当前本地后台 ETL 广播连接处于等待状态，导致第二次进程退出；需在干净进程环境中复跑完整 API 冒烟。
- 新闻内容、公司介绍和联系信息目前为可用的信息页骨架，正式业务文案和联系方式仍需业务方提供后替换。
