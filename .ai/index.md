# 项目知识索引

> 维护：随 `.ai/decisions.md` 同步更新；门控通过后做路径比对（规则 §5.2.1 第 6 步）。
> 更新时间：2026-10-01

## 技术栈

- 后端: .NET 8 (Minimal API + Razor Pages SSR) + EF Core + Npgsql (raw SQL)
- 前端: Vue 3 + Vite + TypeScript + Element Plus + Tailwind + vue-i18n
- 数据: PostgreSQL 16、Meilisearch v1.12、MinIO / R2 / OSS
- 部署: Docker Compose（开发 `docker-compose.yml`；生产 `docker-compose.prod.yml`，project `sakura-prod`）

## 核心模块

- `backend/src/SakuraFilter.Api/`: `Endpoints/`（minimal API，统一在 `Extensions/EndpointRouteBuilderExtensions.cs` 注册）、`Services/`、`Controllers/`（MVC/Razor）、`Pages/`（SEO 详情页）
- `backend/src/SakuraFilter.Core/`: `Entities/`、`DTOs/`、`Interfaces/`、`Validation/`
- `backend/src/SakuraFilter.Infrastructure/`: `Data/ProductDbContext.cs`、`Storage/`（MinIO 实现）
- `backend/src/SakuraFilter.Etl/`: Excel 导入 + `Staging/`（OEM staging 清洗/映射，030–034 迁移建立）
- `backend/src/SakuraFilter.Search/`: `MeiliSearchProvider`（主）、`PostgresSearchProvider`（fallback）、`ResilientSearchProvider`（弹性包装）
- `backend/src/SakuraFilter.Cli/`: 运维 CLI（孤儿图片清理、OEM staging 导入/清洗/映射/发布）
- `backend/migrations/`: SQL 迁移，按文件名顺序；`run-migrations.sh` 以 `__sakura_migrations` 幂等登记。OEM 切库相关：`035_oem_catalog_serving.sql`（`oem_key`/派生分类）、`036_catalog_to_public_cutover.sql`（catalog → public 投影）、`037_products_meili_safe_ids.sql`（`mr_1` 净化为 Meili 安全 ID + `oem_2` 回填）、`038_supplement_product_category.sql`（派生分类规则 v2：交叉引用名主判据 + OEM 前缀兜底）
- `frontend/src/api/`: `types.ts` + `index.ts`（契约层）；`utils/http.ts`（axios 拦截器）
- `frontend/src/views/public/`: 搜索/详情/对比；`frontend/src/views/admin/`: 后台各管理页

## 数据库 Schema

- `public`: 正式业务表（products / cross_references / machine_applications 等）。**2026-10-01 起由 `catalog` 投影重建**（旧数据已清除）：`mr_1 = catalog.oem_key`、`oem_no_normalized = oem_key`、`type = catalog.product_category`；行数 48,733 / 529,499 / 709,843。分类由 `catalog.refresh_product_categories()` 计算（交叉引用名关键词 ×2 + 候选名关键词 ×1 打分，全未命中按 OEM NO 1 首段前缀兜底），`catalog.oem_products.product_category` 与 `public.products.type` 及 Meili `type` facet 三层已对齐
- `staging`: OEM 导入暂存（raw → clean → mapping candidates）
- `catalog`: OEM 锚点目录（oem_products / oem_cross_references / oem_machine_applications / oem_mr1_mappings / oem_import_runs）
- 关键约定：`mr_1` 为 Meili 文档主键，字符集受限 `^[A-Za-z0-9_-]{1,50}$`；`oem_no_1_normalized` 保留空格，`oem_key`/`oem_no_normalized` 去空格大写

## 关键接口（公开搜索，2026-10-01 切库后）

- POST `/api/public/search/aggregate` → 聚合搜索；`type` 过滤值必须是短码 `air/oil/fuel/hydraulic/cabin/others`（传展示名恒 0 结果）；另支持 8 个高级字段 `oemBrand/oemNo2/oemNo3/machineBrand/machineModel/modelName/engineBrand/engineType`（各字段独立、互不融合、可叠加）与 6 个尺寸 `d1/d2/d3/h1/h2/h3`。**这 8 字段不在 Meili filterableAttributes**：任一非空时控制器 `PublicSearchController.Aggregate` 直接改走 `PostgresSearchProvider` 做 ILIKE 精确过滤，全空才走「Meili 主 + PG 兜底」（ADR #39）
- GET `/api/public/product/{**slug}`、`GET /product/{oem}` → 详情；反查优先级 OemNo3(1) → OemNoDisplay(2) → Oem2(3) → Mr1(4)
- POST `/api/public/search/batch-oem` → 批量 OEM 查询；前端按 `hit` 拆「已匹配 / 未匹配」两分区，未匹配行可快捷添加（复用 `POST /api/admin/products`，Operator 策略）
- POST `/api/admin/products` → 后台新建产品（策略 `Operator`）；前端「快捷添加」弹窗复用此端点，未登录先跳 `/login?redirect=`
- POST `/api/admin/etl/reindex-all` → 全量重建索引（需 Admin，限流 "etl"）

## 关键接口（OEM 目录，2026-09-30 上线）

- GET `/api/admin/oem-catalog/summary` → 目录概览计数
- GET `/api/admin/oem-catalog/products?q=&page=&pageSize=` → OEM NO 1 分页列表
- GET `/api/admin/oem-catalog/products/{**oemNo1}` → 单条详情（catch-all，支持含斜杠编号）
- GET `/api/admin/oem-catalog/products/xrefs/{**oemNo1}` → 交叉号分页
- GET `/api/admin/oem-catalog/products/applications/{**oemNo1}` → 机型适配分页
- PUT `/api/admin/oem-catalog/products/mr1/{**oemNo1}` → 设置 MR.1 映射（调 `catalog.set_oem_mr1_mapping`）
- 均要求 JWT `Admin` 策略（role=admin）

## 前端路由

- `/search/aggregate` → `views/public/AggregateSearchView.vue`：**公开搜索统一入口**（合并原高级搜索与高级筛选为「高级搜索与筛选」面板；顶部融合搜索框 / 中部高级搜索与筛选展开区 / 下部批量 OEM 查询结果分区）
- `/public/search` → `views/public/PublicSearchView.vue`：**保留路由不重定向**，承载「产品对比内嵌视图」(`?compare=<ids>`)；仅导航入口统一指向 `/search/aggregate`（ADR #42）
- `/admin/oem-catalog` → `views/admin/AdminOemCatalogView.vue`（后台菜单 key `oem-catalog`）
- 其余后台页 `requireAuth`；`/admin/ops?tab=etl` 为 ETL 入口

## 命名约定

- 注释/文档/提交信息: 简体中文
- C# JSON 契约默认 PascalCase（已反映到前端类型）
- 前端 i18n: `nav.*` / `admin.*` 命名空间，zh-CN + en-US 必须同步
- 迁移文件: `<序号>_<描述>.sql`；历史表 `__sakura_migrations(basename PK, applied_at)`

## 已知限制与风险

- 生产库迁移**人工执行**：编排中 `db-init` / `db-migrate` 已注释禁用，禁止随意取消注释（`018_v2_legacy_data_cleanup.sql` 为一次性 TRUNCATE）
- `products.mr_1` 为业务关联键，搜索索引主键是字符串 `mr_1`，不是 DB 自增 Id
- Meilisearch 文档 ID 只允许 `[A-Za-z0-9_-]`（≤511 字节）；含 `/ . " +` 或空格会整批 `invalid_document_id`
- 生产栈 `sakura-api` / `sakura-meili` **未映射宿主端口**（宿主 `:7700`/`:5148` 属压测栈）；生产只能经 `https://localhost/api/...`（`curl -k`）或 `docker exec`
- OEM catalog 的 MR.1 映射当前为空（`oem_mr1_mappings = 0`），属设计预期（客户 MR.1 编码未定稿），待编码规则就绪后按 OEM 回填
- 派生分类仍有 `others` 9,908（20.3%，如 Cyclone / Mist Purifier / Liquid / Breather / Gasket Kit，无 6 类归属）；新增类别需同步前端 `dict_type`、`AdminTypesView.FIXED_TYPES`、i18n 与契约测试
- 前端聚合搜索页有**两个**搜索输入框：页头全局框（placeholder「搜索产品 / OEM / 机型」）与页面搜索框（「输入关键词 …」）；自动化须定位后者
