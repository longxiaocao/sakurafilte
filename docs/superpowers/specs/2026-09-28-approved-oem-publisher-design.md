# 已审核 OEM 映射发布器设计

## 目标

将某一 completed staging 批次中人工审核为 approved 的 OEM NO 1 到 MR.1 映射，受控发布到既有正式产品。发布前必须预检；发布在演练库中验证后，生产执行仍需单独维护窗口确认。

## 边界

- 只读取 `staging.oem_mr1_mapping_reviews.review_status = 'approved'` 的记录，绝不把 candidate 自动当作 approved。
- 每个映射在同一事务内按 `target_mr1` 锁定一个未下架的 `products` 记录；不存在、重复或已下架均拒绝发布。
- `product_specs_clean` 只补充目标产品当前为空的规格字段。规格冲突和 `product_name_candidates` 多值不覆盖正式字段，写入预检告警。
- `oem_numbers_clean` 按完整业务键去重后写入交叉号码；`applications_clean` 只保留现有 clean 层的精确去重结果并写入机型适配。
- 第一阶段发布器先提供 dry-run 预检与发布审计基础，不清空、不删除、不重建搜索索引，也不创建新产品。

## 数据流

1. `stage-publish-approved --dry-run` 读取 batch、审批记录和目标产品，产生每类待写入行数与阻断原因。
2. 预检失败时退出非零，不写入正式表；没有 approved 映射时成功返回零写入报告。
3. 非 dry-run 仅允许对通过预检的 batch 执行，在一个数据库事务中重新校验目标产品，再写入正式表及 staging 发布审计记录。
4. 成功后由独立的搜索重建步骤处理检索，不在发布事务内调用 Meilisearch。

## 安全约束

- 命令必须显式携带 `--batch-id`、`--pg-conn`；真正写入还必须携带 `--apply`。
- 预检及发布报告保存 batch ID、执行时间、审批数量、阻断数量与各表写入数量。
- 正式 `sakurafilter` 在演练签字前不运行 `--apply`；当前只操作 `sakurafilter_oem_rehearsal`。
- 11,889 条空 OEM 来源行及 245 个规格冲突继续保留在 staging，绝不静默丢弃或覆盖。

## 验收

- 无 approved 映射时 dry-run 报告为零写入且不修改正式表。
- 审核目标不存在或已下架时预检列出阻断项并拒绝 apply。
- approved 映射在同一事务内重新解析 MR.1，不能信任已保存的 target_product_id。
- 演练发布后能按 MR.1 对账每个 approved OEM 的规格、交叉号码和机型适配来源数量。
