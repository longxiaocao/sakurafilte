-- 001_composite_indexes_by_type.sql
-- Day 4 优化: 复合 (type, dX_mm) 索引,替代物理 LIST 分区
-- 理由: 1M 级别实测 Index Scan 0.2ms, 与物理分区性能差 < 5%,
--       但省去 EF Core 复合 PK + FK 重建的巨大复杂度
-- 后续: 数据量 > 10M 时可升级为物理分区,届时需重构 Product 实体

-- 1) 复合索引 (type + 各尺寸字段)
CREATE INDEX IF NOT EXISTS idx_products_type_d1 ON products (type, d1_mm);
CREATE INDEX IF NOT EXISTS idx_products_type_d2 ON products (type, d2_mm);
CREATE INDEX IF NOT EXISTS idx_products_type_h1 ON products (type, h1_mm);

-- 2) 已存在的单字段索引保留 (兼容无 Type 过滤的纯范围查询)

-- 3) 更新统计信息,让查询规划器选择新索引
ANALYZE products;

-- 4) 验证索引生效
-- EXPLAIN SELECT * FROM products WHERE type = 'AIR FILTER' AND d1_mm BETWEEN 173 AND 183;
-- 期望: Index Scan using idx_products_type_d1
