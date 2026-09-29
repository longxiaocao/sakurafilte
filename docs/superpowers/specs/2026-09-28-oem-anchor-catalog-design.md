# OEM NO 1 锚点目录设计

## 目标

在客户尚未提供 MR.1 的阶段，将 OEM NO 1 作为正式目录数据的唯一锚点，完整承载规格、交叉号码和机型适配。MR.1 保留为后续可选关联，不阻塞当前数据发布。

## 选择

新增 `catalog` schema，不直接改写现有 `public.products`、`cross_references`、`machine_applications`。

理由：现有产品表和 ETL 仍将 type、MR.1 视为目录前置字段。49,391 个 OEM 锚点中只有 10,020 个有规格，直接导入旧表会让其余锚点被跳过或被填入虚假类型。独立 OEM 层能保留所有来源事实，并让旧系统在迁移期间继续可用。

## 数据模型

- `catalog.oem_import_runs`：记录每次从 staging clean 发布的批次、状态、行数和错误。
- `catalog.oem_products`：每个规范化 OEM NO 1 一条；保存展示值、规格 JSON、产品名称候选、规格冲突数和来源 batch。OEM 规范化值唯一。
- `catalog.oem_cross_references`：关联 `oem_product_id`，保存 clean 层每一条已去重 OEM 交叉号码。
- `catalog.oem_machine_applications`：关联 `oem_product_id`，保存 clean 层每一条已去重机型适配，不折叠多车型。
- `catalog.oem_mr1_mappings`：后续由业务补录；一条 OEM 当前最多一条生效 MR.1，保留历史开始、结束、操作人与理由。MR.1 当前保持可选且全局唯一，避免一个内部产品编码被静默分配给多个 OEM。

## 发布规则

1. 只允许已完成的 staging import 与 clean batch 发布。
2. 本阶段不读取、不要求、不自动生成 `staging.oem_mr1_mapping_reviews`。
3. 每个 batch 在一个事务内执行：先 upsert OEM 主记录，再删除该 batch 涉及 OEM 的旧目录子记录，最后从 clean 层 set-based 插入交叉号码和机型适配。
4. 机型适配使用 clean 层已有的整行去重结果；不按 OEM 合并为单一机型记录。
5. 空 OEM 来源行继续留在 staging validation issues；245 个规格字段冲突保留在 OEM 主记录的冲突计数与 staging 明细中，不随机选择名称。
6. 发布器只写 `catalog` schema；不写 public 正式表、不清库、不触发搜索索引。

## MR.1 后续更新

后续提供按 OEM NO 1 更新 MR.1 的专用命令或后台接口。更新在事务中锁定 OEM 主记录，关闭旧生效映射并创建新映射；MR.1 为空时没有映射行。目录规格、交叉号、机型适配不因 MR.1 更新而重导。

## 验收

- batch 1 发布后有 49,391 条 OEM 主记录、529,499 条交叉号码和 709,843 条有 OEM 锚点的机型适配；另有 11,696 条空 OEM clean 机型行继续保留在 staging。原始来源的 11,760 条空 OEM 行中有 64 条已作为完全重复行在 clean 前去重。
- `SH 56212` 在 OEM 目录下保留 10,515 条机型适配。
- 无 MR.1 时发布成功，`catalog.oem_mr1_mappings` 为空。
- 重复运行同一 batch 后三个目录表行数不增加。
- public 旧正式表行数保持不变。
