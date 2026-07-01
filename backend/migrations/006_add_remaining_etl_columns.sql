-- 006_add_remaining_etl_columns.sql
-- Day 5 修复: C# ETL staging 声明的列,但 products 表缺的字段
-- 一次性补齐,避免再次反复 ALTER

ALTER TABLE products ADD COLUMN IF NOT EXISTS bypass_valve_hr NUMERIC;
ALTER TABLE products ADD COLUMN IF NOT EXISTS bypass_pressure NUMERIC;

-- 复合索引 (兜底:某些环境下 001-003 漏跑)
CREATE INDEX IF NOT EXISTS idx_products_type_d1 ON products (type, d1_mm);
CREATE INDEX IF NOT EXISTS idx_products_type_d2 ON products (type, d2_mm);
CREATE INDEX IF NOT EXISTS idx_products_type_h1 ON products (type, h1_mm);
CREATE INDEX IF NOT EXISTS idx_products_oem_norm ON products (oem_no_normalized);

ANALYZE products;
