# OEM NO 1 Batch 1 数据质量报告

## 范围

- 测试数据库：spike_test_v3
- 暂存批次：staging.import_batches.id = 1
- 原始来源：data0827、OEM NUMBER0827、dataa0827
- 清洗边界：不写入正式业务表；MR.1 保留现有设计。

## 已验证结果

| 项目 | 结果 | 结论 |
| --- | ---: | --- |
| 产品规格 raw 行数 | 22,229 | 完整保留在 raw 层 |
| 产品规格 clean OEM 主记录 | 10,020 | 每个非空 OEM NO 1 一条规格快照 |
| OEM 交叉号码 raw 行数 | 529,630 | 原始行完整保留 |
| OEM 交叉号码 clean 行数 | 529,499 | 仅合并 2 组规范化重复记录，共覆盖 4 条来源行 |
| 车型应用源行数 | 738,086 | 原始 Excel 计数 |
| 车型应用 raw / clean 行数 | 721,539 / 721,539 | 只删除 16,547 条完全相同的整行；clean 层逐行保留 |
| SH 56212 车型应用 | 10,515 | 已完整保留，不再被压缩为单一车型 |

## 关键验证

- product_specs_clean 的 10,020 条记录与规格 raw 表的非空 OEM 去重计数一致。
- oem_numbers_clean 的行数加上其来源行合并差额后，等于 529,501 条非空 OEM 交叉号码 raw 行。
- applications_clean 与 applications_raw 都是 721,539 行。
- SH 51281 V 的五条规格碎片已补全为一条快照，包含 d1 = 78.0 mm、d3 = 43.2 mm、h1 = 229.0 mm、thread = 3 1/2 inch BSP。

## 需要治理的例外

| 类型 | 数量 | 当前处理 | 后续处理 |
| --- | ---: | --- | --- |
| applications 缺失 OEM NO 1 | 11,760 | 保留 raw 行并写入 validation_issues，不进入 OEM 规格主快照 | 建立人工匹配或回填队列，不能自动猜测锚点 |
| OEM 交叉号码缺失 OEM NO 1 | 129 | 保留 raw 行并写入 validation_issues | 同上 |
| 产品类型候选冲突 | 245 个 OEM | 保留全部候选值并写入 product_spec_field_conflicts | 审核后制定产品类型映射规则，不静默选值 |

目前规格字段的多值冲突只出现在 product_name_1；尺寸、介质、效率、密封材料、压力和螺纹等字段没有发现同一 OEM 的多个不同非空值。

## 跨表覆盖情况

共有 49,391 个非空 OEM NO 1 锚点：

- 10,020 个出现在产品规格来源；
- 46,729 个出现在 OEM 交叉号码来源；
- 18,646 个出现在车型应用来源；
- 3,491 个同时出现在三张来源表；
- 6,528 个规格 OEM 暂无车型应用；
- 15,154 个车型应用 OEM 暂无产品规格。

这些覆盖差异是待补充数据或待匹配范围，不是去重失败；正式系统导入时必须保留来源覆盖状态，不能用空值补成虚假的完整产品。

## 结论

批次 1 的 clean 层可以作为后续系统改造的受控输入。正式迁移前，应先将缺失 OEM 锚点和 245 个产品类型冲突纳入审核队列，再定义 MR.1 与 OEM NO 1 的正式映射。

## OEM 到 MR.1 审核队列

批次 1 已生成 49,391 个 OEM NO 1 审核候选。当前测试库不含正式 products 表，因此所有候选均为 pending，没有自动批准或写入任何正式表。

候选算法已在测试库临时模拟一条正式产品记录：SH 51281 V 唯一精确匹配到产品 900001 和 MR51281 时，正确生成 candidate；删除模拟记录并重新刷新后，队列恢复为 49,391 条 pending。模拟产品已删除。

下一阶段必须由业务人员在审核表中批准或驳回映射。仅 approved 映射可被未来的正式发布器读取，发布器仍需再次校验 MR.1 对应产品。

## 演练库映射与发布预检

2026-09-28 已将生产库备份恢复到 `sakurafilter_oem_rehearsal`，并导入 batch 1 的 staging、clean 与审核映射数据。对演练库刷新候选后，结果如下：

| 项目 | 数量 |
| --- | ---: |
| OEM NO 1 总锚点 | 49,391 |
| 唯一精确正式产品候选 | 669 |
| 待人工指定 MR.1 | 48,722 |
| 多产品歧义候选 | 0 |
| approved 审核记录 | 0 |

随后执行 `stage-publish-approved` 的只读预检：approved、可发布、阻断、受影响规格、交叉号码、机型适配及规格冲突均为 0，`IsReadyToApply = false`。

预检前后演练库正式表行数均为 products 9,376、cross_references 149,342、machine_applications 150,111；因此确认当前发布预检不写入正式表。生产库 `sakurafilter` 未执行任何写入。

## OEM 锚点目录演练

在 `sakurafilter_oem_rehearsal` 新建独立 `catalog` schema 后，batch 1 已按 OEM NO 1 发布，不要求 MR.1。发布结果如下：

| 项目 | 行数 |
| --- | ---: |
| catalog OEM 主记录 | 49,391 |
| catalog 交叉号码 | 529,499 |
| catalog 有锚点机型适配 | 709,843 |
| 留在 staging 的空 OEM clean 机型行 | 11,696 |
| SH 56212 catalog 机型适配 | 10,515 |
| catalog MR.1 映射 | 0 |

原始来源的空 OEM 机型异常为 11,760 条；其中 64 条与其他来源行完全重复，已在 clean 前按整行规则去重，因此 clean 层的无锚点行数为 11,696。

同一 batch 已连续发布两次，目录三张数据表行数不增加。演练前后旧 public 表保持 products 9,376、cross_references 149,342、machine_applications 150,111。生产库未执行迁移或写入。
