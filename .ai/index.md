# 项目知识索引

> 维护：随 `.ai/decisions.md` 同步更新；门控通过后做路径比对（规则 §5.2.1 第 6 步）。
> 更新时间：2026-09-30

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
- `backend/migrations/`: SQL 迁移，按文件名顺序；`run-migrations.sh` 以 `__sakura_migrations` 幂等登记
- `frontend/src/api/`: `types.ts` + `index.ts`（契约层）；`utils/http.ts`（axios 拦截器）
- `frontend/src/views/public/`: 搜索/详情/对比；`frontend/src/views/admin/`: 后台各管理页

## 数据库 Schema

- `public`: 正式业务表（products / cross_references / machine_applications 等）
- `staging`: OEM 导入暂存（raw → clean → mapping candidates）
- `catalog`: OEM 锚点目录（oem_products / oem_cross_references / oem_machine_applications / oem_mr1_mappings / oem_import_runs）

## 关键接口（OEM 目录，2026-09-30 上线）

- GET `/api/admin/oem-catalog/summary` → 目录概览计数
- GET `/api/admin/oem-catalog/products?q=&page=&pageSize=` → OEM NO 1 分页列表
- GET `/api/admin/oem-catalog/products/{**oemNo1}` → 单条详情（catch-all，支持含斜杠编号）
- GET `/api/admin/oem-catalog/products/xrefs/{**oemNo1}` → 交叉号分页
- GET `/api/admin/oem-catalog/products/applications/{**oemNo1}` → 机型适配分页
- PUT `/api/admin/oem-catalog/products/mr1/{**oemNo1}` → 设置 MR.1 映射（调 `catalog.set_oem_mr1_mapping`）
- 均要求 JWT `Admin` 策略（role=admin）

## 前端路由

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
- OEM catalog 的 MR.1 映射当前为空（`oem_mr1_mappings = 0`），待审核流程填充
