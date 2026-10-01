# OEM 生产切换前只读预检

日期：2026-09-28

本次只连接 Docker 中的 `sakurafilter` 生产数据库执行只读查询，未执行迁移、目录发布、清理或写入。

## 结构预检

| 检查项 | 结果 |
| --- | --- |
| `catalog` schema | 不存在 |
| 旧 `public.products` | 存在 |
| 旧 `public.cross_references` | 存在 |
| 旧 `public.machine_applications` | 存在 |

## 生产旧表基线

| 表 | 行数 |
| --- | ---: |
| `public.products` | 9,376 |
| `public.cross_references` | 149,342 |
| `public.machine_applications` | 150,111 |

旧 `public.products.mr_1` 当前空值或空白值数量为 `0`。

## 切换门禁

- 生产库尚未安装 `033_oem_anchor_catalog.sql` 和 `034_oem_mr1_mapping.sql`。
- 不得使用 `stage-publish-oem-catalog` 直接连接生产库；该命令会执行 catalog 发布，不是 dry-run。
- 正式迁移后必须重新执行旧表行数对账，三张旧表行数应与本基线一致。
- catalog 发布完成后必须执行演练验收脚本的同等检查，并抽查 `SH 56212` 的 281 条交叉号和 10,515 条机型适配。
