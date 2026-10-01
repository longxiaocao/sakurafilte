-- 一次性脚本,不可重跑: 025 products 表 trgm GIN 索引 (融合搜索 fuzzy 性能)
-- 025: products 表 trgm GIN 索引 (融合搜索 fuzzy 性能)
-- 一次性脚本, 不可重跑 (CREATE INDEX IF NOT EXISTS 自身幂等, 但仍标一次性)
-- WHY (2026-08-23 走查): fuzzy 融合搜索 (全字段 ILIKE %kw%) 段1 (products 5 字段 OR)
--   在 1M 行上 Parallel Seq Scan 810ms → 加 trgm GIN 索引走 BitmapOr (目标 <50ms)。
--   oem_2 已有 ix_products_oem_2_trgm; 补齐 oem_no_display / product_name_1 / product_name_2 / type。
--   与 024_typeahead_dict_indexes.sql 风格一致 (IF NOT EXISTS + 幂等)。
CREATE INDEX IF NOT EXISTS ix_products_oem_no_display_trgm ON products USING gin (oem_no_display gin_trgm_ops);
CREATE INDEX IF NOT EXISTS ix_products_product_name_1_trgm ON products USING gin (product_name_1 gin_trgm_ops);
CREATE INDEX IF NOT EXISTS ix_products_product_name_2_trgm ON products USING gin (product_name_2 gin_trgm_ops);
CREATE INDEX IF NOT EXISTS ix_products_type_trgm ON products USING gin (type gin_trgm_ops);
