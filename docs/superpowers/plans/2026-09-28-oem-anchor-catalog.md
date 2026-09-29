# OEM NO 1 锚点目录 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 staging clean 批次按 OEM NO 1 发布到独立 catalog 正式层，不要求 MR.1。

**Architecture:** 迁移创建 catalog OEM 主表、子表、MR.1 历史映射和发布运行记录。CLI 先提供严格参数校验，再调用数据库函数，以一个事务按 clean snapshot 幂等刷新 catalog 数据；旧 public 业务表完全隔离。

**Tech Stack:** .NET 8、Npgsql 8、PostgreSQL 16/18、xUnit、FluentAssertions。

**Spec:** docs/superpowers/specs/2026-09-28-oem-anchor-catalog-design.md

## Global Constraints

- OEM NO 1 规范化值是目录唯一身份；MR.1 可为空且不阻塞发布。
- 发布器只写 catalog schema，禁止写 public 正式表、搜索索引和 staging clean/raw 表。
- 没有 OEM 的来源行和规格冲突必须保持可追溯，不能静默丢弃或猜测字段。
- 只在 `sakurafilter_oem_rehearsal` 演练，生产库不得执行迁移或发布。

---

### Task 1: catalog 数据库契约

**Files:**
- Create: `backend/tests/SakuraFilter.Api.Tests/OemAnchorCatalogSchemaContractTests.cs`
- Create: `backend/migrations/033_oem_anchor_catalog.sql`

**Interfaces:** `catalog.publish_oem_clean_batch(bigint)`。

- [x] 写失败契约测试，检查 catalog 的五张表、OEM 唯一索引和发布函数名称。
- [x] 运行测试确认迁移文件缺失。
- [x] 创建幂等 SQL 迁移及事务性发布函数。
- [x] 运行契约测试确认通过。

### Task 2: OEM 目录发布 CLI

**Files:**
- Create: `backend/tests/SakuraFilter.Etl.Tests/OemCatalogPublishCommandParserTests.cs`
- Create: `backend/src/SakuraFilter.Etl/Staging/OemCatalogPublishOptions.cs`
- Create: `backend/src/SakuraFilter.Etl/Staging/OemCatalogPublishCommandParser.cs`
- Create: `backend/src/SakuraFilter.Etl/Staging/OemCatalogPublishService.cs`
- Create: `backend/src/SakuraFilter.Cli/OemCatalogPublishCommand.cs`
- Modify: `backend/src/SakuraFilter.Cli/Program.cs`

**Interfaces:** `stage-publish-oem-catalog --batch-id <id> --pg-conn <conn>`。

- [x] 写 parser 失败测试，覆盖缺 batch、缺连接串与未知参数。
- [x] 运行测试确认失败。
- [x] 实现命令与服务，调用 catalog 发布函数并打印发布计数。
- [x] 运行 ETL 测试确认通过。

### Task 3: 演练发布和质量对账

**Files:**
- Modify: `docs/data-import/oem-no1-staging-runbook.md`
- Modify: `docs/data-import/oem-no1-batch-1-quality-report.md`
- Modify: `.ai/decisions.md`

- [x] 在演练库执行 033 和 batch 1 发布。
- [x] 对账 49,391 OEM、529,499 交叉号码、709,843 有锚点机型适配、SH 56212 的 10,515 条机型，以及 public 表行数未变。
- [x] 重跑发布验证幂等。
- [x] 运行 solution build、ETL/API 测试并提交。
