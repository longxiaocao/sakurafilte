# OEM NO 1 暂存导入与校验设计

## 目标

在不改动现有 MR.1 正式业务模型和 API 的前提下，把三个 Excel 文件导入 PostgreSQL 暂存区，以 `OEM NO 1` 作为跨表关联锚点，并生成可审计的数据质量报告。

## 数据粒度

- `data0827`：产品规格候选数据。相同 `OEM NO 1` 的碎片行允许合并到规格快照，但暂存区保留每一条原始行。
- `OEM NUMBER0827`：OEM 品牌/号码交叉引用。相同 `OEM NO 1` 下的多个 OEM 品牌和 OEM NO.3 必须保留为多行。
- `dataa0827`：车型、发动机和应用明细。同一个 `OEM NO 1` 可有多条记录，只删除完全相同的整行重复，不按 OEM 压成一行。

## 数据库设计

新增 `staging` schema：

- `staging.import_batches`：导入批次、源文件、状态、行数、错误数和时间。
- `staging.oem_anchors`：规范化后的 OEM NO 1 锚点及各来源行数。
- `staging.product_specs_raw`：`data0827` 的宽表原始字段、源行号、行哈希和原始 JSON。
- `staging.oem_numbers_raw`：OEM 号码表原始字段、源行号、行哈希和原始 JSON。
- `staging.applications_raw`：车型应用表原始字段、源行号、行哈希和原始 JSON。
- `staging.validation_issues`：空键、规范化冲突、重复行和字段异常。

所有原始表都保留 `batch_id`、`source_row_no`、`oem_no_1_raw`、`oem_no_1_normalized`、`row_hash`、`raw_payload` 和 `created_at`。`OEM NO 1` 仅作为锚点/外键，不作为应用明细唯一键。

## 导入方式

新增 CLI 子命令 `stage-import`：

```text
dotnet run --project backend/src/SakuraFilter.Cli -- stage-import \
  --specs <data0827.xlsx> \
  --oem-numbers <OEM NUMBER0827.xlsx> \
  --applications <dataa0827.xlsx> \
  --pg-conn <connection-string>
```

导入使用批次事务和 PostgreSQL COPY/参数化批量写入；导入失败只回滚当前批次，不删除历史批次。Excel 解析沿用 ETL 项目已有的 ClosedXML 适配约定，后续可替换为流式解析器而不改变暂存契约。

## 校验规则

- 源文件数据行数与暂存表行数一致。
- 规范化 OEM NO 1 为空的行单独计数，不静默丢弃。
- 完全重复行按 `row_hash` 识别；`dataa0827` 只报告/排除完全重复行。
- 每个 OEM 锚点输出三个来源的行数。
- `SH 56212` 必须保留多条应用记录，且车型/类型集合不能被拼接成单一值。
- 所有异常写入 `staging.validation_issues` 并出现在 CLI 汇总报告中。

## 后续迁移边界

本阶段不写 `products`、`cross_references`、`machine_applications`、图片表或搜索索引；MR.1 字段保持现有设计。暂存验证通过后，再单独设计 OEM NO 1 到 MR.1 的映射与正式迁移。

