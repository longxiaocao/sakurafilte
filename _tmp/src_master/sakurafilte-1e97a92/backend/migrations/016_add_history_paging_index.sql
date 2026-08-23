-- 一次性脚本,不可重跑 (P2-7.1 标注)
-- 原用途: product_history 加 (ProductId, ChangedAt DESC, Id DESC) 复合索引 (Day 9.4 keyset pagination 性能优化)
-- Day 9.4: product_history 加 (ProductId, ChangedAt DESC, Id DESC) 复合索引
-- WHY: keyset pagination 走 (ChangedAt, Id) 范围扫描, 不加 DESC 索引时
--      PostgreSQL 仍可用索引但需要反向扫描, 大数据量性能下降
-- IF NOT EXISTS: 重复执行安全
CREATE INDEX IF NOT EXISTS ix_product_history_paging
    ON product_history (product_id, changed_at DESC, id DESC);
