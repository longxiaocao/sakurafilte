# 已审核 OEM 映射发布器 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 OEM NO 1 staging 批次提供只发布人工 approved 映射的预检和演练发布入口。

**Architecture:** CLI 服务先从 staging 读取审批、clean 数据和目标产品，生成只读预检报告。apply 模式仅在预检通过后开启事务重新校验 MR.1，并写入发布审计；不触发生产清空或搜索索引。

**Tech Stack:** .NET 8、Npgsql 8、PostgreSQL 16/18、xUnit、FluentAssertions。

**Spec:** docs/superpowers/specs/2026-09-28-approved-oem-publisher-design.md

## Global Constraints

- 只能发布 `approved` 审核记录，候选匹配不等于批准。
- dry-run 不得写入任何正式业务表或 staging 审计表。
- apply 必须显式要求 `--apply`，且只能在事务内重新校验未下架 MR.1。
- 此阶段不清空正式表、不创建产品、不重建搜索索引。

---

### Task 1: 发布命令解析与只读预检

**Files:**
- Create: `backend/tests/SakuraFilter.Etl.Tests/ApprovedOemPublishCommandParserTests.cs`
- Create: `backend/src/SakuraFilter.Etl/Staging/ApprovedOemPublishOptions.cs`
- Create: `backend/src/SakuraFilter.Etl/Staging/ApprovedOemPublishCommandParser.cs`
- Modify: `backend/src/SakuraFilter.Cli/Program.cs`

**Interfaces:** `stage-publish-approved --batch-id <id> --pg-conn <conn> [--apply]`。

- [x] 写失败测试：缺少 batch、连接串，以及 apply 外未知参数。
- [x] 运行测试确认新解析器尚不存在。
- [x] 实现最小参数解析，默认 dry-run，只有 `--apply` 明示实际发布。
- [x] 运行解析器测试确认通过。

### Task 2: 预检服务和回归测试

**Files:**
- Create: `backend/src/SakuraFilter.Etl/Staging/ApprovedOemPublishService.cs`
- Create: `backend/tests/SakuraFilter.Etl.Tests/ApprovedOemPublishServiceTests.cs`
- Create: `backend/src/SakuraFilter.Cli/ApprovedOemPublishCommand.cs`
- Modify: `backend/src/SakuraFilter.Cli/Program.cs`

**Interfaces:** `PreflightAsync(ApprovedOemPublishOptions)` 返回 approved 数、可发布数、阻断数和按 clean 层统计的规格、交叉号码、机型适配数量。

- [x] 写服务测试：无 approved 映射得到零写入；无目标或下架目标被计为阻断。
- [x] 运行测试确认失败原因是服务未实现。
- [x] 用参数化 SQL 实现只读预检，MR.1 按 `FOR UPDATE` 规则相同的资格条件统计。
- [x] 运行 ETL 测试确认通过。

### Task 3: 演练库预检和文档对账

**Files:**
- Modify: `docs/data-import/oem-no1-staging-runbook.md`
- Modify: `docs/data-import/oem-no1-batch-1-quality-report.md`

- [x] 在 `sakurafilter_oem_rehearsal` 对 batch 1 执行 dry-run。
- [x] 核验正式表行数未变化，并记录 approved 为零、候选为 669、pending 为 48,722。
- [x] 运行 `dotnet build backend/SakuraFilter.sln` 和 ETL 测试。
- [x] 提交实现和演练文档。
