-- 005_add_efficiency2_to_products.sql
-- Day 5 修复: Python ETL 输出 efficiency_2 但 products 表缺该列
-- WHY: 错误 42703 "关系 products 的 efficiency_2 字段不存在"

ALTER TABLE products ADD COLUMN IF NOT EXISTS efficiency_2 VARCHAR(100);
