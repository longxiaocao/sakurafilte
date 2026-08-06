# SakuraFilter — 滤清器产品目录系统

以 MR.1（自有产品编码）为唯一主键的滤清器产品目录：串联 OEM 三级体系、机型体系、尺寸参数、图片资源；前端做双维度分类导航 + 全局检索 + 详情展示。技术栈：.NET 8 + Vue 3 + PostgreSQL + Meilisearch + MinIO。

## Project

- 业务基线文档：`方案梳理.md`（客户需求基准，前后端统一依据）
- 后端入口：`backend/SakuraFilter.sln`，web 入口 `backend/src/SakuraFilter.Api/Program.cs`（minimal API + Razor Pages SSR）
- 前端入口：`frontend/`（Vue 3 + Vite，dev 端口 **5175**，`/api` 代理到 `localhost:5148`）
- 部署：`docker-compose.yml`（postgres16 / minio / meilisearch / backend:5148 / frontend:80 / grafana:3000）
- 架构记忆：`.ai/decisions.md`（ADR）、`.ai/context.md`（个人上下文，已 gitignore）、`.ai/suggestions.md`（建议归档）

## Commands

后端（在 `backend/` 下）：

```bash
dotnet build SakuraFilter.sln                      # 构建
dotnet test tests/SakuraFilter.Api.Tests/SakuraFilter.Api.Tests.csproj   # 单元测试
dotnet test tests/SakuraFilter.Api.Tests/SakuraFilter.Api.Tests.csproj --filter "Category=Integration"  # PG 集成测试（需 PG_TEST_CONNECTION_STRING）
```

启动：`.\start-dev.ps1`（自动生成 `.env` + 启后端 `http://localhost:5148`）；停止 `.\kill_stuck.ps1`。

前端（在 `frontend/` 下）：

```bash
npm run dev          # dev server :5175
npm run type-check   # vue-tsc --noEmit
npm run build        # vue-tsc -b && vite build
npm run lint         # eslint src
npx vitest run tests/unit/        # 单元测试
npx vitest run tests/contract/    # 契约测试（字典 schema 等）
npx playwright test tests/e2e/    # E2E（需后端 + PG 运行）
npm run test:visual  # 视觉回归（快照）
```

全栈一键：`docker compose up -d --build`（生产配置 `docker-compose.prod.yml`）。

## Architecture

- `backend/src/SakuraFilter.Api/` — web API：`Endpoints/`（minimal API 按功能拆分，在 `Extensions/EndpointRouteBuilderExtensions.cs` 注册）、`Services/`、`Controllers/`（MVC/Razor）、`Pages/`（SEO 详情页 Razor SSR）、`Extensions/`（服务注册 + 中间件管道）
- `SakuraFilter.Core/` — 领域层：`Entities/`、`DTOs/`、`Interfaces/`（含 `IObjectStorage`）、`Validation/`
- `SakuraFilter.Infrastructure/` — `Data/ProductDbContext.cs`（EF Core）+ `Storage/`（MinIO 实现）
- `SakuraFilter.Etl/` — Excel 导入（`EtlImportService.cs`，按 MR.1 关联 7 分区）
- `SakuraFilter.Search/` — 检索：`ISearchProvider`、`MeiliSearchProvider`（主）、`PostgresSearchProvider`（fallback，CTE UNION + GIN trgm）、`ResilientSearchProvider`（包装降级）
- `SakuraFilter.Cli/` — 运维 CLI（孤儿图片清理等）
- `backend/migrations/*.sql` — SQL 迁移脚本，按文件名顺序执行（CI 中 psql 逐个执行）
- `frontend/src/api/` — 契约层：`types.ts`（TypeScript 接口）+ `index.ts`（按业务域拆分的 API 方法）；`utils/http.ts` 为 axios 拦截器（错误码映射、X-Client-Version 头）
- `frontend/src/views/` — `public/`（搜索/详情/对比）+ `admin/`（产品、ETL、字典、OEM 排序、用户、告警、监控等）
- 后台鉴权：JWT Bearer + `X-Admin-Token`（`Auth:DevStaticToken`，存 `localStorage.sakurafilter_admin_token`），端点策略名 `Admin`；后台路由 `requireAuth`

## Conventions

- 注释、文档、Git 提交信息一律简体中文（含 WHY 注释解释非直觉代码）
- 后端：minimal API 端点集中在 `Endpoints/` 目录；错误统一 ProblemDetails + 业务错误码；输入校验 FluentValidation；并发写用 xmin 乐观锁；raw SQL 用 Npgsql；单元测试 xunit + FluentAssertions + Moq，集成测试连本地 PG 独立库（`sakurafilter_int_tests`，TRUNCATE CASCADE 重置）
- 前端：严格 TypeScript；API 契约变更必须同步 `api/types.ts` + `index.ts` + Mock；UI 用 Element Plus + Tailwind，Musk 极简风格（黑白 + 单一蓝色强调、无阴影、1px hairline、8px 网格）；文案用 vue-i18n；异步请求必须有 loading + error 兜底；`v-for` 必须稳定 key（禁 index）；E2E 选择器优先 `data-testid`
- 关键架构决策写入 `.ai/decisions.md`（ADR 格式）；P1/P2 未采纳建议追加 `.ai/suggestions.md`；会话状态覆盖写入 `.ai/context.md`
- 严禁硬编码密钥；`.env` 不入库（参考 `.env.example`）

## Notes

## 工具优先级（强制）
1. 探索代码结构、追调用链、评估改动影响面时，
   必须先调用 codegraph_context / codegraph_search / codegraph_callers；
   仅当 codegraph 无结果时才回退 grep/glob。
2. 涉及第三方库（Spring Boot、MyBatis-Plus、Vue 等）写代码前，
   必须先 resolve-library-id 定位库，再 query-docs 拉当前版本文档。
3. 每次任务结束时，汇报本次使用了哪些工具。
