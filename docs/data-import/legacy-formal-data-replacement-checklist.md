# 旧正式数据替换检查清单

## 当前结论

本机 PostgreSQL 中，只有 sakurafilter_perf 存在 products、cross_references、machine_applications 和 product_images 表。该库只有 1 条产品、1 条交叉号码、11,761 条车型应用，且没有 ETL 历史记录，应视为性能或测试库，不得作为旧生产数据清空目标。

spike_test_v3 是 OEM NO 1 staging、clean 和审核映射测试库，不包含正式业务表。

## 允许清除旧正式数据的前置条件

1. 明确实际正式数据库连接和库名，不能使用 sakurafilter_perf 或 spike_test_v3。
2. 停止该库对应的后端 ETL、后台写入任务和搜索索引重建任务。
3. 创建可恢复的数据库物理备份或逻辑备份，并校验备份文件可读取。
4. 导出正式业务表的行数、MR.1 覆盖、数据来源和最近 ETL 记录，作为替换前快照。
5. 业务方确认旧批量数据没有需要保留的人工修订；人工修订必须先单独导出。
6. 在副本数据库完整演练：清空旧数据、发布 approved 映射、重建索引、行数对账、抽样检索。
7. 只有上述演练通过，才能在正式库的维护窗口执行清空。

## 只读审计命令

在任何候选正式库上先执行：

```powershell
dotnet run --project backend/src/SakuraFilter.Cli -- stage-legacy-audit +  --pg-conn "<connection-string>"
```

命令只读取数据库名称、大小、正式业务表是否存在、各表行数、产品 MR.1/OEM 为空的行数和 ETL 历史行数。它不提供删除参数，也不会执行任何写入。

## 可清除范围

经确认的旧批量导入数据只包含以下业务表及其索引队列：

- products
- cross_references
- machine_applications
- product_images
- search_index_pending
- search_index_dead_letter

不得清除用户、JWT、字典、system_settings、staging 原始数据、clean 数据、审核映射或审计记录。

## 现阶段禁止操作

- 不执行 TRUNCATE 或 DELETE。
- 不把 staging 批次 1 发布到任何正式表。
- 不因候选匹配而自动批准 OEM 到 MR.1 映射。

## 下一步

取得实际正式数据库连接后，先运行只读审计和备份，再建立同版本副本库完成发布演练。
