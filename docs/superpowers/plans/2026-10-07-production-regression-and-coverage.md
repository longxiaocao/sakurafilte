# 生产回归验收与关键测试补强 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改变 OEM NO 1 锚点和生产数据的前提下，完成当前生产线可复现回归验收，并为高级搜索、后台产品检索和令牌广播补齐高价值测试护栏。

**Architecture:** 验收分为本地门禁、生产只读验收和真实 PostgreSQL 回归测试三层。公开八字段搜索保持控制器直达 PostgreSQL；令牌广播保持 Channel 串行处理 NOTIFY；本轮不重构既有复杂搜索链路。

**Tech Stack:** .NET 8、xUnit、FluentAssertions、Npgsql、PostgreSQL 16、Vue 3、Vite、PowerShell、Docker Compose。

**Spec:** .ai/index.md、.ai/decisions.md、.ai/context.md、方案梳理.md

## Global Constraints

- OEM NO 1 是目录数据锚点；当前不引入或伪造 MR.1 映射。
- 生产验证只能只读；不可恢复生产 db-init 或 db-migrate 服务。
- 生产 API 只能通过 https://localhost/api/... 或容器访问，禁止误用压测栈 localhost:5148。
- 不提交 .env、令牌、密码或其他密钥；不删除现有未跟踪文件。
- 所有新增注释、文档、提交信息使用简体中文。

---

### Task 1: 执行本地回归门禁

**Files:**
- Modify: docs/superpowers/plans/2026-10-07-production-regression-and-coverage.md
- Read: scripts/ci-local.ps1、backend/SakuraFilter.sln、frontend/package.json

**Interfaces:**
- Consumes: 当前分支源码和本机依赖。
- Produces: 后端构建/单测、前端类型检查/单测/生产构建的可复现结果。

- [x] **Step 1: 检查工作区与脚本副作用**

Run: git status --short; Get-Content -Raw scripts/ci-local.ps1

Expected: 脚本不写生产数据库；仅在 localhost:5148 就绪时跑契约测试。

- [x] **Step 2: 运行本地 CI 等价门禁**

Run: pwsh -NoProfile -File scripts/ci-local.ps1

Expected: build、后端单测、前端 type-check、Vitest、Vite 生产构建通过；契约测试若后端不可用则标记跳过。

- [x] **Step 3: 定位失败但不先改生产代码**

Run: dotnet test backend/tests/SakuraFilter.Api.Tests/SakuraFilter.Api.Tests.csproj --filter Category!=Integration

Expected: 若失败，保留失败用例与日志，完成根因分析后才进入 TDD 修复。

### Task 2: 校验迁移与生产只读状态

**Files:**
- Read: scripts/check-migration-uniqueness.ps1、scripts/preflight-schema-check.ps1、docker-compose.prod.yml
- Modify: docs/superpowers/plans/2026-10-07-production-regression-and-coverage.md

**Interfaces:**
- Consumes: Docker 生产服务和 public.__sakura_migrations。
- Produces: 迁移编号唯一性、DDL 落地与公开 API 健康的只读证据。

- [x] **Step 1: 检查迁移编号唯一性**

Run: pwsh -NoProfile -File scripts/check-migration-uniqueness.ps1

Expected: exit 0，所有迁移序号唯一。

- [x] **Step 2: 运行 schema 预检闸门**

Run: pwsh -NoProfile -File scripts/preflight-schema-check.ps1

Expected: SQL 迁移均已登记，CREATE TABLE 和 ADD COLUMN 均已落地。

- [x] **Step 3: 只读验收公开主链路**

Run: curl.exe -k -sS -o NUL -w %{http_code} https://localhost/health/ready

Expected: 200；聚合搜索和公开详情均无 5xx。

### Task 3: 补高级搜索八字段 PostgreSQL 契约测试

**Files:**
- Modify: backend/tests/SakuraFilter.Api.Tests/Integration/PostgresSearchProviderIntegrationTests.cs
- Read: backend/src/SakuraFilter.Api/Controllers/PublicSearchController.cs、backend/src/SakuraFilter.Search/PostgresSearchProvider.cs

**Interfaces:**
- Consumes: AggregateSearchRequest 的 OEM 品牌、OEM 2/3、机型/发动机五字段。
- Produces: 字段独立过滤、OEM 与机型条件组合为 AND 的集成测试。

- [x] **Step 1: 写失败测试；包含 OEM 品牌、OEM 3、机型品牌和机型型号组合，并断言仅一条命中。**
- [x] **Step 2: 运行指定用例确认 RED。**

Run: dotnet test backend/tests/SakuraFilter.Api.Tests/SakuraFilter.Api.Tests.csproj --filter FullyQualifiedName~AggregateSearchAsync_EightFieldFilters

- [x] **Step 3: 仅在 RED 暴露行为缺陷时最小修复。**

保持 PostgresSearchProvider.AggregateSearchAsync 的 OEM/机型各自 EXISTS 与同组 AND 语义；不得变更 Meili 文档结构。

- [x] **Step 4: 运行 Category=Integration 验证 GREEN（当前无 PG_TEST_CONNECTION_STRING，按既有基类跳过数据库连接）。**

### Task 4: 补后台检索组合测试

**Files:**
- Modify: backend/tests/SakuraFilter.Api.Tests/Integration/AdminProductServiceIntegrationTests.cs
- Read: backend/src/SakuraFilter.Api/Services/AdminProductService.cs

**Interfaces:**
- Consumes: AdminProductSearchRequest 的分页、关键字、类别和关联筛选。
- Produces: 不跨产品串联、分页总数正确的组合测试。

- [x] **Step 1: 写油滤类别、Bosch 品牌、每页一条的组合筛选/分页测试。**
- [x] **Step 2: 运行 RED；必要时最小修复；运行 Category=Integration 验证 GREEN（当前无 PG_TEST_CONNECTION_STRING，按既有基类跳过数据库连接）。**

### Task 5: 补 AuthTokenBroadcaster LISTEN/NOTIFY 回归测试

**Files:**
- Create: backend/tests/SakuraFilter.Api.Tests/Integration/AuthTokenBroadcasterIntegrationTests.cs
- Read: backend/src/SakuraFilter.Api/Services/AuthTokenBroadcaster.cs

**Interfaces:**
- Consumes: PostgreSQL 通道 auth_token_rotated、IAuthTokenStore.ReloadFromDbAsync、IHostedServiceStatus。
- Produces: NOTIFY 后 Reload 被调用、循环可取消、无 Connection is busy 的回归测试。

- [ ] **Step 1: 建立可观察替身并写失败测试。**
- [ ] **Step 2: 运行 RED，必要时最小修复，运行完整相关测试。**

Run: dotnet test backend/tests/SakuraFilter.Api.Tests/SakuraFilter.Api.Tests.csproj

### Task 6: 收尾与提交

**Files:**
- Modify: .ai/context.md（仅在证据完整时）
- Modify: .ai/suggestions.md（仅记录新发现且未修复的 P1/P2）

**Interfaces:**
- Consumes: 全部门禁与测试日志。
- Produces: 中文提交和可审计的验收结论。

- [x] **Step 1: 复跑受影响门禁。**

Run: pwsh -NoProfile -File scripts/ci-local.ps1

- [x] **Step 2: 检查差异和敏感信息。**

Run: git diff --check; git status --short

- [ ] **Step 3: 按可独立验收单元提交。**

Run: git add docs/superpowers/plans backend/tests && git commit -m 测试：补充生产回归关键测试

Expected: 无经过验证的代码改动时，只提交计划或说明原因。
