# OEM NO 1 Clean Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 PostgreSQL staging schema 中按 OEM NO 1 生成可审计、可验证的三表 clean 层。

**Architecture:** 原始 staging 表继续作为唯一事实来源。新增 SQL 迁移创建 clean 表、冲突表和刷新函数；CLI 子命令执行指定批次的刷新。规格使用 JSONB 聚合补全，不静默处理多值冲突；OEM 交叉号码按规范化复合键去重；应用表逐行复制。

**Tech Stack:** .NET 8、Npgsql 8、PostgreSQL 18、xUnit、FluentAssertions。

**Spec:** docs/superpowers/specs/2026-09-28-oem-clean-layer-design.md

## Global Constraints

- 注释、文档、CLI 文案使用简体中文。
- 不写正式业务表，不修改 MR.1 的业务含义。
- OEM NO 1 只作为锚点；应用和交叉号码保持一对多。
- 所有清洗结果必须可按 batch_id 和来源行号追溯。

---

### Task 1: 定义 clean schema 契约

**Files:**
- Create: backend/tests/SakuraFilter.Api.Tests/OemCleanSchemaContractTests.cs
- Create: backend/migrations/031_oem_no1_clean.sql

**Interfaces:**
- Produces: staging.clean_runs、三个 clean 表、规格冲突表和 staging.refresh_oem_clean(bigint)。

- [x] **Step 1: 写入失败的迁移契约测试**

```csharp
migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.product_specs_clean");
migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.oem_numbers_clean");
migration.Should().Contain("CREATE TABLE IF NOT EXISTS staging.applications_clean");
migration.Should().Contain("CREATE OR REPLACE FUNCTION staging.refresh_oem_clean");
```

- [x] **Step 2: 运行契约测试并确认因迁移文件不存在而失败**

运行：dotnet test backend/tests/SakuraFilter.Api.Tests/SakuraFilter.Api.Tests.csproj --filter FullyQualifiedName~OemCleanSchemaContractTests

- [x] **Step 3: 创建最小迁移，定义对象和刷新函数**

迁移中的刷新函数必须删除指定批次的旧 clean 结果，再从 raw 表重建，写入 clean_runs 状态与统计信息。

- [x] **Step 4: 运行契约测试并确认通过**

### Task 2: 增加 CLI clean 命令

**Files:**
- Create: backend/src/SakuraFilter.Etl/Staging/StagingCleanOptions.cs
- Create: backend/src/SakuraFilter.Etl/Staging/StagingCleanCommandParser.cs
- Create: backend/src/SakuraFilter.Etl/Staging/StagingCleanService.cs
- Create: backend/src/SakuraFilter.Cli/StagingCleanCommand.cs
- Modify: backend/src/SakuraFilter.Cli/Program.cs
- Test: backend/tests/SakuraFilter.Etl.Tests/StagingCleanCommandParserTests.cs

**Interfaces:**
- Consumes: --pg-conn <connection-string> --batch-id <positive-bigint>。
- Produces: Task<StagingCleanReport> RefreshAsync(StagingCleanOptions, CancellationToken)。

- [x] **Step 1: 写 parser 失败测试，验证合法参数和缺少 batch-id 的错误**
- [x] **Step 2: 运行测试确认失败**
- [x] **Step 3: 实现最小参数解析和 SQL 函数调用服务**
- [x] **Step 4: 运行 parser 与 ETL 测试确认通过**

### Task 3: 运行测试库清洗和验证

**Files:**
- Modify: docs/data-import/oem-no1-staging-runbook.md

**Interfaces:**
- Consumes: spike_test_v3 的批次 1 raw 数据。
- Produces: clean 层实测数量、冲突统计和 SH 56212 保留校验。

- [x] **Step 1: 在 spike_test_v3 执行迁移 031**
- [x] **Step 2: 使用 CLI 刷新批次 1 clean 层**
- [x] **Step 3: 运行 SQL 对账：规格、交叉号、应用和冲突表**
- [x] **Step 4: 更新运行手册中的 clean 命令和验证 SQL**
- [x] **Step 5: 运行完整构建和 ETL 测试集**
