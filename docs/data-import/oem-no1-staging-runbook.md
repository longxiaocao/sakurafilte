# OEM NO 1 暂存导入运行手册

## 适用范围

本手册将三份 Excel 导入并清洗到 `staging` schema，不写入正式 `products`、`cross_references` 或 `machine_applications` 表。

## 1. 创建暂存表

在测试数据库按迁移顺序执行：

```powershell
psql "$env:PG_TEST_CONNECTION_STRING" -f backend/migrations/030_oem_no1_staging.sql
psql "$env:PG_TEST_CONNECTION_STRING" -f backend/migrations/031_oem_no1_clean.sql
psql "$env:PG_TEST_CONNECTION_STRING" -f backend/migrations/032_oem_mr1_review_mapping.sql
```

## 2. 预览导入

```powershell
dotnet run --project backend/src/SakuraFilter.Cli -- stage-import `
  --specs "C:\Users\C\Desktop\data0827 - 副本.xlsx" `
  --oem-numbers "C:\Users\C\Desktop\OEM NUMBER0827 - 副本.xlsx" `
  --applications "C:\Users\C\Desktop\dataa0827 - 副本.xlsx" `
  --pg-conn "$env:PG_TEST_CONNECTION_STRING" `
  --dry-run
```

预览只解析文件和统计，不写数据库。

## 3. 正式写入测试 staging

去掉 `--dry-run` 后执行。命令输出批次号和三个来源的行数、去重数、空 OEM 数以及 issue 数。

## 4. 生成 clean 层

导入批次状态为 `completed` 后，按批次刷新 clean 层：

```powershell
dotnet run --project backend/src/SakuraFilter.Cli -- stage-clean +  --batch-id <batch-id> +  --pg-conn "$env:PG_TEST_CONNECTION_STRING"
```

该命令只重建指定批次的下列 staging 表：

- `product_specs_clean`：每个非空 OEM NO 1 一条规格快照；字段冲突写入 `product_spec_field_conflicts`。
- `oem_numbers_clean`：交叉号码按 OEM、产品名称、品牌、OEM NO.3 的规范化复合键去重。
- `applications_clean`：逐行保留 `applications_raw`，不按 OEM 合并车型。

## 5. 生成 OEM 到 MR.1 审核候选

clean 层完成后，生成每个 OEM NO 1 的候选审核记录：

```powershell
dotnet run --project backend/src/SakuraFilter.Cli -- stage-map-refresh \
  --batch-id <batch-id> \
  --pg-conn "$env:PG_TEST_CONNECTION_STRING"
```

候选刷新只会读取正式 products（如该表存在）做唯一精确匹配，不写入正式表：

- 一个精确且未下架的产品匹配：状态为 candidate，仍需人工批准。
- 没有匹配：状态为 pending。
- 多个匹配：状态为 ambiguous，必须人工裁决。

人工审核命令：

```powershell
dotnet run --project backend/src/SakuraFilter.Cli -- stage-map-review \
  --batch-id <batch-id> \
  --oem-no-1 "SH 51281 V" \
  --status approved \
  --mr1 "MR51281" \
  --reviewed-by "<reviewer>" \
  --pg-conn "$env:PG_TEST_CONNECTION_STRING"
```

驳回时使用 --status rejected --reason "<reason>"。该命令只写入 staging.oem_mr1_mapping_reviews，不会发布数据。

## 6. 核验 SQL

```sql
SELECT id, status,
       specs_source_rows, oem_numbers_source_rows, applications_source_rows,
       specs_staged_rows, oem_numbers_staged_rows, applications_staged_rows,
       duplicate_rows, blank_oem_rows, issue_count
FROM staging.import_batches
ORDER BY id DESC
LIMIT 1;

SELECT oem_no_1_normalized,
       specs_row_count, oem_numbers_row_count, applications_row_count
FROM staging.oem_anchors
WHERE oem_no_1_normalized = 'SH 56212';

SELECT COUNT(*) AS sh56212_application_rows,
       COUNT(DISTINCT machine_model) AS machine_models,
       COUNT(DISTINCT machine_brand) AS machine_brands
FROM staging.applications_raw
WHERE oem_no_1_normalized = 'SH 56212';

SELECT source_kind, issue_code, COUNT(*)
FROM staging.validation_issues
GROUP BY source_kind, issue_code
ORDER BY source_kind, issue_code;

SELECT batch_id, status,
       product_specs_rows, product_spec_conflict_rows,
       oem_numbers_rows, oem_numbers_merged_rows, applications_rows
FROM staging.clean_runs
WHERE batch_id = <batch-id>;

SELECT COUNT(*) AS sh56212_application_rows
FROM staging.applications_clean
WHERE batch_id = <batch-id>
  AND oem_no_1_normalized = 'SH 56212';

SELECT oem_no_1_normalized, field_name, candidate_values
FROM staging.product_spec_field_conflicts
WHERE batch_id = <batch-id>
ORDER BY oem_no_1_normalized, field_name;

SELECT batch_id, status, total_oem_count, candidate_count, pending_count, ambiguous_count
FROM staging.oem_mapping_candidate_runs
WHERE batch_id = <batch-id>;

SELECT candidate_status, COUNT(*)
FROM staging.oem_mapping_candidates
WHERE batch_id = <batch-id>
GROUP BY candidate_status
ORDER BY candidate_status;

SELECT review_status, COUNT(*)
FROM staging.oem_mr1_mapping_reviews
WHERE batch_id = <batch-id>
GROUP BY review_status;
```

## 7. 已审核映射发布预检

```powershell
dotnet run --project backend/src/SakuraFilter.Cli -- stage-publish-approved \
  --batch-id <batch-id> \
  --pg-conn "<connection-string>"
```

该命令当前只读。它只统计 `approved` 审核记录关联的 clean 数据、目标 MR.1 是否存在且未下架，以及规格冲突数量；不会写入 `products`、`cross_references`、`machine_applications` 或 staging 审计表。

`--apply` 已被保留为未来显式发布开关，但当前版本会拒绝执行并退出，确保审核映射尚未完善时不存在误发布路径。

## 8. OEM 锚点目录发布

```powershell
dotnet run --project backend/src/SakuraFilter.Cli -- stage-publish-oem-catalog \
  --batch-id <batch-id> \
  --pg-conn "<connection-string>"
```

该命令要求 batch 的 clean 层已完成，并且只写 `catalog` schema。它把有 OEM NO 1 锚点的规格、交叉号码和机型适配发布到 OEM 目录，不要求 MR.1，不修改旧 `products`、`cross_references`、`machine_applications` 或搜索索引。

## 验收原则

- `dataa0827` 的同一 OEM 下多机型行必须保留，不能出现把 `Bus`、`Truck` 等值拼为单行的结果。
- `applications_raw` 的去重只针对完全相同的整行哈希。
- 空 OEM 行进入 `validation_issues`，不能静默丢弃。
- 规格冲突进入 `product_spec_field_conflicts`，不能随机选择一个候选值覆盖。
- `applications_clean` 行数必须与 `applications_raw` 一致。
- OEM 到 MR.1 的唯一精确候选仍须人工批准；候选本身不能直接写入正式表。
- 本阶段验证通过前，不执行正式业务表迁移。
- OEM 目录发布后可重跑同一 batch，目录行数不得累计增加。
