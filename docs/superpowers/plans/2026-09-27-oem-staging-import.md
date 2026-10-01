# OEM NO 1 Staging Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立以 OEM NO 1 为锚点、保留三张 Excel 原始粒度的 PostgreSQL 暂存导入与校验链路。

**Architecture:** 新增 staging SQL schema 和 CLI 导入服务，原始字段宽表化保存并附带 JSON、行号、规范化 OEM 锚点与哈希；导入后只生成校验报告，不写现有正式业务表。dataa0827 按整行哈希去重，保持一个 OEM 下多车型、多发动机、多应用记录。

**Tech Stack:** .NET 8, Npgsql, ClosedXML, PostgreSQL, xUnit, FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-09-27-oem-staging-import-design.md`

## Global Constraints

- 注释、文档、Git 提交信息使用简体中文。
- 暂不修改 `products`、`cross_references`、`machine_applications` 和现有 API。
- `OEM NO 1` 是关联锚点，不代表每个锚点只能有一行。
- `dataa0827` 只删除完全重复整行，不能把多条应用值拼接到一行。
- 不硬编码数据库密码或 Excel 路径。

---

### Task 1: 定义 staging SQL 结构

**Files:**
- Create: `backend/migrations/030_oem_no1_staging.sql`
- Test: `backend/tests/SakuraFilter.Api.Tests/StagingSchemaContractTests.cs`

**Interfaces:**
- Produces: `staging.import_batches`, `staging.oem_anchors`, 三张 raw 表和 `staging.validation_issues`。

- [x] **Step 1: 写迁移契约测试**

断言迁移文本包含 schema、批次表、锚点表、三张 raw 表、校验表、row_hash 唯一索引和 OEM 规范化索引。

- [x] **Step 2: 运行测试确认失败**

运行：`dotnet test backend/tests/SakuraFilter.Api.Tests/SakuraFilter.Api.Tests.csproj --filter FullyQualifiedName~StagingSchemaContractTests`

预期：FAIL，因为迁移文件尚不存在。

- [x] **Step 3: 写最小 SQL 迁移**

使用 `CREATE SCHEMA IF NOT EXISTS staging`、批次状态 CHECK、raw 表公共审计列、JSONB 原始载荷和索引；不添加正式表外键。

- [x] **Step 4: 运行测试确认通过**

运行同一命令，预期 PASS。

### Task 2: 实现 OEM NO 1 规范化和行哈希

**Files:**
- Create: `backend/src/SakuraFilter.Etl/Staging/StagingKeyNormalizer.cs`
- Create: `backend/tests/SakuraFilter.Etl.Tests/StagingKeyNormalizerTests.cs`

**Interfaces:**
- Produces: `NormalizeOemNo1(string?)` 和 `ComputeRowHash(IReadOnlyList<string?>)`。

- [x] **Step 1: 写失败测试**

覆盖 null/空白、连续空格、大小写归一化，以及同一行值顺序固定后哈希一致。

- [x] **Step 2: 运行测试确认失败**

运行：`dotnet test backend/tests/SakuraFilter.Etl.Tests/SakuraFilter.Etl.Tests.csproj --filter FullyQualifiedName~StagingKeyNormalizerTests`

- [x] **Step 3: 实现最小规范化器**

OEM 锚点规范化规则为 Trim、连续空白折叠、InvariantUpper；行哈希使用 UTF-8 SHA-256 和稳定分隔符。

- [x] **Step 4: 运行测试确认通过**

运行同一命令，预期 PASS。

### Task 3: 实现三类 Excel staging 读取器

**Files:**
- Create: `backend/src/SakuraFilter.Etl/Staging/StagingWorkbookReader.cs`
- Create: `backend/src/SakuraFilter.Etl/Staging/StagingRow.cs`
- Create: `backend/tests/SakuraFilter.Etl.Tests/StagingWorkbookReaderTests.cs`

**Interfaces:**
- Consumes: `.xlsx` path and expected source kind `specs|oem-numbers|applications`。
- Produces: `IAsyncEnumerable<StagingRow>`，包含源行号、标准化表头、OEM 原值/规范值、字段字典和哈希。

- [x] **Step 1: 写小型 ClosedXML fixture 的失败测试**

覆盖三张表的表头映射、未知列保留在 `raw_payload`、空 OEM 不抛异常、`SH 56212` 两个不同机型生成两条独立行。

- [x] **Step 2: 运行测试确认失败**

运行 `dotnet test ... --filter FullyQualifiedName~StagingWorkbookReaderTests`，预期 FAIL。

- [x] **Step 3: 实现读取器**

沿用 `EtlSpreadsheetAdapter` 的表头规范化；不做按 OEM 聚合；每行保留所有非空和空字段，生成 JSONB payload。

- [x] **Step 4: 运行测试确认通过**

运行同一测试过滤器，预期 PASS。

### Task 4: 实现 PostgreSQL staging 导入服务

**Files:**
- Create: `backend/src/SakuraFilter.Etl/Staging/StagingImportService.cs`
- Create: `backend/src/SakuraFilter.Etl/Staging/StagingImportOptions.cs`
- Modify: `backend/src/SakuraFilter.Etl/SakuraFilter.Etl.csproj`
- Test: `backend/tests/SakuraFilter.Etl.Tests/StagingImportServiceTests.cs`

**Interfaces:**
- Produces: `Task<StagingImportReport> ImportAsync(StagingImportOptions, CancellationToken)`。
- Uses: Npgsql batch/COPY，按 source kind 写入对应 raw 表，更新 `oem_anchors` 和 `validation_issues`。

- [x] **Step 1: 写服务契约失败测试**

用测试连接字符串/可替换数据库接口断言：每批创建 import batch；dataa 完全重复按 hash 去重；同 OEM 不同机型均保留；空 OEM 写 issue；失败状态可读。

- [x] **Step 2: 运行测试确认失败**

运行 ETL 测试过滤器，预期 FAIL。

- [x] **Step 3: 实现导入服务**

使用事务、参数化 SQL 和 COPY；重复行只对 applications raw 表按 `(batch_id,row_hash)` 去重；其他来源保留原始行并通过 issue 报告重复；不触碰正式表。

- [x] **Step 4: 运行测试确认通过**

运行 ETL 测试项目全量。

### Task 5: 增加 CLI stage-import 和校验报告

**Files:**
- Modify: `backend/src/SakuraFilter.Cli/Program.cs`
- Create: `backend/src/SakuraFilter.Cli/StagingImportCommand.cs`
- Test: `backend/tests/SakuraFilter.Etl.Tests/StagingImportCommandTests.cs`

**Interfaces:**
- CLI: `stage-import --specs <path> --oem-numbers <path> --applications <path> [--pg-conn <conn>] [--batch-id <id>]`。
- Produces: 控制台 JSON 汇总及数据库批次记录。

- [x] **Step 1: 写参数解析失败测试**

缺少任一文件参数时返回非零；`--dry-run` 不写数据库；正常参数传递到 `StagingImportService`。

- [x] **Step 2: 运行测试确认失败**

运行 CLI/ETL 相关测试过滤器，预期 FAIL。

- [x] **Step 3: 实现子命令**

复用 CLI 现有连接串解析，输出每个文件的 source/read/staged/duplicate/blank_oem/issue 计数和重点 OEM 查询结果。

- [x] **Step 4: 运行测试确认通过**

运行 CLI 和 ETL 测试项目。

### Task 6: 集成验证与文档

**Files:**
- Create: `docs/data-import/oem-no1-staging-runbook.md`
- Test: `backend/tests/SakuraFilter.Api.Tests/Integration/StagingImportIntegrationTests.cs`

- [ ] **Step 1: 写集成验证**

验证三张表行数对账、`SH 56212` 多应用保留、重复行处理、空 OEM issue、批次失败回滚。

- [ ] **Step 2: 运行失败测试**

在本地 PostgreSQL 测试库执行 integration filter，确认缺少实现时失败。

- [ ] **Step 3: 接入迁移和导入服务**

按 runbook 执行迁移、导入和报告查询；不改正式业务表。

- [ ] **Step 4: 运行完整验证**

运行：

```text
dotnet build backend/SakuraFilter.sln
dotnet test backend/tests/SakuraFilter.Etl.Tests/SakuraFilter.Etl.Tests.csproj
dotnet test backend/tests/SakuraFilter.Api.Tests/SakuraFilter.Api.Tests.csproj --filter Category=Integration
```

- [ ] **Step 5: 清理并提交**

只提交本阶段新增迁移、staging/CLI/测试和文档；不提交 Excel、临时输出、连接串或密钥。
