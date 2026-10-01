# OEM NO 1 MR.1 Review Mapping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** 新增 OEM NO 1 到 MR.1 的 staging 审核映射层，不写正式业务表。

**Architecture:** PostgreSQL migration 创建候选、审核和运行表。候选刷新函数从 batch clean 数据生成 OEM 集合，动态检查正式 products 表是否存在；不存在时全部保持 pending。CLI 提供刷新与人工审核写入，审核记录与候选记录分离，刷新不会覆盖审核结论。

**Tech Stack:** .NET 8、Npgsql 8、PostgreSQL 18、xUnit、FluentAssertions。

**Spec:** docs/superpowers/specs/2026-09-28-oem-mr1-review-mapping-design.md

## Global Constraints

- 绝不在此阶段写 products、cross_references、machine_applications 或搜索索引。
- OEM NO 1 是来源锚点；MR.1 是审核后的目标标识，不能因候选匹配而自动批准。
- 迁移、CLI 和审核记录均使用简体中文说明。
- 映射必须保留 batch_id、OEM NO 1、时间和审核人。

---

### Task 1: 映射迁移契约与 SQL 刷新函数

**Files:**
- Create: backend/tests/SakuraFilter.Api.Tests/OemMr1MappingSchemaContractTests.cs
- Create: backend/migrations/032_oem_mr1_review_mapping.sql

**Interfaces:**
- Produces: staging.refresh_oem_mapping_candidates(bigint)。

- [x] 写失败契约测试，检查三个 mapping 表和刷新函数名称。
- [x] 运行测试确认迁移文件缺失。
- [x] 创建幂等迁移。函数处理 products 表不存在、唯一精确匹配和多个匹配。
- [x] 运行契约测试确认通过。

### Task 2: 映射 CLI 参数和服务

**Files:**
- Create: backend/src/SakuraFilter.Etl/Staging/StagingMappingOptions.cs
- Create: backend/src/SakuraFilter.Etl/Staging/StagingMappingCommandParser.cs
- Create: backend/src/SakuraFilter.Etl/Staging/StagingMappingService.cs
- Create: backend/src/SakuraFilter.Cli/StagingMappingCommand.cs
- Modify: backend/src/SakuraFilter.Cli/Program.cs
- Test: backend/tests/SakuraFilter.Etl.Tests/StagingMappingCommandParserTests.cs

**Interfaces:**
- stage-map-refresh --batch-id <id> --pg-conn <conn>
- stage-map-review --batch-id <id> --oem-no-1 <oem> --status <approved|rejected> [--mr1 <mr1>] [--reason <text>] --reviewed-by <name> --pg-conn <conn>

- [x] 写 parser 失败测试。
- [x] 运行测试确认失败。
- [x] 实现刷新和审核服务，审核写入必须参数化。
- [x] 运行 parser 与 ETL 测试确认通过。

### Task 3: 测试库执行与质量对账

**Files:**
- Modify: docs/data-import/oem-no1-staging-runbook.md
- Modify: docs/data-import/oem-no1-batch-1-quality-report.md

- [x] 在 spike_test_v3 执行 032。
- [x] 运行 batch 1 候选刷新。
- [x] 验证 49,391 个非空 OEM 都有候选，全部是 pending，审核数为零。
- [x] 更新手册和质量报告。
- [x] 运行 solution build、ETL 测试和 staging 契约测试。
