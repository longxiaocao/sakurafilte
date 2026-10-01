-- idempotent 可重跑: 唯一语句为 ALTER COLUMN DROP NOT NULL, 重复执行无副作用。
-- 允许 machine_applications.product_id 为 NULL，支持孤儿机型数据导入
-- 孤儿行: oem_2 在 products 表找不到匹配，但仍需保留机型字段供 typeahead 搜索和客户后续关联
ALTER TABLE machine_applications ALTER COLUMN product_id DROP NOT NULL;
