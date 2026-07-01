-- 003_add_missing_product_columns.sql
-- Day 4 补充: 补齐 Product 实体声明但 DB 缺失的列
-- WHY: ETL 直接 COPY 进 DB 没建 updated_at,EF 实体声明了导致 SELECT 失败

ALTER TABLE products ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT now();
ALTER TABLE products ADD COLUMN IF NOT EXISTS product_name_3 VARCHAR(100);

-- 复合索引 (Step 1 重复声明以防 002 漏跑)
CREATE INDEX IF NOT EXISTS idx_products_type_d1 ON products (type, d1_mm);
CREATE INDEX IF NOT EXISTS idx_products_type_d2 ON products (type, d2_mm);
CREATE INDEX IF NOT EXISTS idx_products_type_h1 ON products (type, h1_mm);

ANALYZE products;
