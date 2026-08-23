-- idempotent 可重跑: 022 为 admin 商品检索默认排序补齐联合索引
-- WHY: AdminProductService.SearchAsync 默认按 (updated_at DESC, id DESC) 排序。
--      此前 products 表无 updated_at 索引, 任何带 machine_brand/machine_model 等
--      过滤的 admin 检索都会先对全量 products 做 Parallel Seq Scan +
--      external-merge Sort (排序集 16MB 落盘), 再嵌套循环探测 machine_applications。
--      实测(百万级): machineBrand=CUMMINS 这类高命中量过滤 → SQL 1481ms / API ~1.7s。
--      根因是"排序发生在过滤之前", 与 machine_brand 上没有 trigram 索引无关
--      (017 的 GIN 索引对此路径无效: 代码用 = 等值, 且过滤在相关子查询内、
--       作为排序之后的嵌套循环探测条件求值)。
-- 修复: 补 (updated_at DESC, id DESC) 联合索引, 让外层 products 扫描改为索引扫描,
--      省去全表排序 + 落盘。实测: 同查询 SQL 0.27ms / API 5~25ms (降 ~6000 倍)。
-- 收益: 所有按 updated_at 排序的 admin 列表/检索页 (默认排序) 均受益, 不止机型过滤。
-- 风险: 索引约 +~30MB (100 万行), 写入时多一棵树维护; 对导入吞吐影响可忽略。
-- IF NOT EXISTS: 重复执行安全 (perf 环境已手动建过同名索引, 不会冲突)。

CREATE INDEX IF NOT EXISTS ix_products_updated_at_id
    ON products (updated_at DESC, id DESC);
