# OEM NO 1 到 MR.1 审核映射设计

## 目标

在不改变 products、cross_references、machine_applications 现有关系的前提下，建立 OEM NO 1 到正式产品/MR.1 的独立审核映射层。该层使用 staging clean 数据生成候选，任何正式数据写入都必须依赖人工批准的映射。

## 数据流

1. 每个 completed staging batch 先生成 product_specs_clean、oem_numbers_clean、applications_clean。
2. 映射刷新函数从该 batch 的三张 clean 表收集所有非空 OEM NO 1。
3. 若正式库存在 products，且仅有一个未下架产品的 oem_no_normalized 与 OEM NO 1 完全规范化匹配，并且该产品有 MR.1，则生成 candidate 状态。
4. 其余 OEM 生成 pending 状态。多个正式产品匹配同一 OEM 时标为 ambiguous，绝不自动选择。
5. 审核人通过 CLI 提交 approved 或 rejected 记录，批准记录必须含 target_mr1；target_product_id 可在正式发布时重新解析和校验。
6. 后续正式发布任务只读取 approved 映射，并在同一数据库事务中二次核验 MR.1 对应产品存在且未下架。

## 新增对象

- staging.oem_mapping_candidate_runs：候选刷新状态与候选数量。
- staging.oem_mapping_candidates：按 batch + OEM 保存候选状态、候选产品和匹配原因；刷新可安全重建。
- staging.oem_mr1_mapping_reviews：审核结论和目标 MR.1；刷新候选时不覆盖审核记录。

## 状态与约束

- candidate：只有一个精确正式产品匹配，待人工确认。
- pending：没有自动匹配，需要人工补录。
- ambiguous：多个精确正式产品匹配，需要人工裁决。
- approved：审核结论，target_mr1 非空。
- rejected：审核结论，必须有理由。
- 映射刷新不能写 products、cross_references、machine_applications，也不能创建任何产品。

## 验收

- 测试库无 products 表时，刷新 49,391 个 OEM 候选且全部为 pending，过程成功。
- staging 中不存在空 OEM 候选。
- 无人工审核记录时 approved 数为零。
- 刷新候选不会删除此前的审核记录。
- 审核命令拒绝空 MR.1、未知 OEM、未知状态。

## 后续边界

本阶段不提供正式发布功能、不写正式业务表、不触发搜索索引。审核映射积累并确认后，单独实现“批准映射发布器”，并针对产品、交叉号、车型应用分别做事务性 upsert 和审计。
