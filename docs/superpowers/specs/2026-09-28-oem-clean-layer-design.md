# OEM NO 1 清洗合并层设计

## 目标

在 staging 原始层已完成导入和可追溯去重的基础上，按批次生成可审计的 clean 层。OEM NO 1 是关联锚点，不是应用或交叉号码的一行唯一键。

## 不可变边界

- 不写入正式 products、cross_references、machine_applications 或搜索索引。
- 不删除 staging 原始行；clean 层每条记录必须能追溯到导入批次和原始行。
- MR.1 保留现有设计，当前不建立 OEM NO 1 到 MR.1 的正式映射。

## 三张来源表的清洗规则

### 产品规格（data0827）

- 每个非空 OEM NO 1 生成一条产品规格快照。
- 对每个非空规格字段，若该 OEM 只有一个不同的非空值，则写入快照，实现碎片字段补全。
- 若一个字段存在多个不同的非空值，保留全部候选值，写入字段冲突明细，不选择任意一个值。
- 本批次实测除 product_name_1 外的规格字段没有冲突；product_name_1 有 245 个 OEM 存在两个候选值，须标记为待复核。

### OEM 交叉号码（OEM NUMBER0827）

- 同一 OEM NO 1 下保留多个品牌和 OEM NO.3。
- clean 层只移除规范化键完全一致的交叉号码记录，不按 OEM NO 1 压缩为一行。
- 规范化键为 OEM NO 1、产品名称、OEM 品牌、OEM NO.3 的首尾空格压缩和大小写无关表示；展示值保留首个原始非空值。

### 车型应用（dataa0827）

- staging 已只排除完全相同的整行。
- clean 层逐行保留 staging 中全部应用明细，不按 OEM NO 1 合并或拼接车型、发动机、产品类型字段。

## 目标对象

- staging.product_specs_clean：每批次每 OEM 一条规格快照，包含 JSONB 规格字段、产品名称候选和冲突字段数。
- staging.product_spec_field_conflicts：冲突 OEM、字段和候选值。
- staging.oem_numbers_clean：每批次每个规范化交叉号码一条记录，并保留来源行号数组。
- staging.applications_clean：每批次逐行保留已经精确去重的车型应用。
- staging.clean_runs：清洗运行状态、行数、冲突数和时间。

## 验收规则

- 每个规格 clean 记录的所有无冲突字段都来自同批次同 OEM 的原始行。
- 产品规格候选冲突不得被静默覆盖。
- OEM 交叉号码 clean 行数不超过 raw 行数，且每行拥有至少一个来源行号。
- applications clean 行数与 applications raw 行数完全相等。
- SH 56212 在 applications clean 中保留 10,515 条应用明细。
- clean 层不得出现空 OEM NO 1 锚点记录。
