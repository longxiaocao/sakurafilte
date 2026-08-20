-- idempotent 可重跑: 023 为 typeahead 建全量 distinct 字典表 (typeahead_dict) 并填充
-- WHY: 8 字段 typeahead 在 1550 万行明细表 ILIKE, machine-brand/engine-brand 命中 45 万行 → 2-4s;
--      字典表存全量 distinct 值 + GIN trgm 索引, 查询只扫字典 (万行级) → 毫秒级。ETL 后需重建刷新。
CREATE TABLE IF NOT EXISTS typeahead_dict (
    field  TEXT NOT NULL,
    value  TEXT NOT NULL,
    PRIMARY KEY (field, value)
);
CREATE INDEX IF NOT EXISTS ix_typeahead_dict_value_trgm ON typeahead_dict USING gin (value gin_trgm_ops);

-- 全量重建 (幂等: 清空重填, 字典表万行级秒级完成)
TRUNCATE typeahead_dict;
INSERT INTO typeahead_dict (field, value)
SELECT 'oem-brand', oem_brand FROM cross_references WHERE oem_brand IS NOT NULL AND oem_brand <> '' GROUP BY oem_brand
UNION ALL
SELECT 'oem-no2', oem_2 FROM products WHERE oem_2 IS NOT NULL AND oem_2 <> '' GROUP BY oem_2
UNION ALL
SELECT 'oem-no3', oem_no_3 FROM cross_references WHERE oem_no_3 IS NOT NULL AND oem_no_3 <> '' GROUP BY oem_no_3
UNION ALL
SELECT 'machine-brand', machine_brand FROM machine_applications WHERE machine_brand IS NOT NULL AND machine_brand <> '' GROUP BY machine_brand
UNION ALL
SELECT 'machine-model', machine_model FROM machine_applications WHERE machine_model IS NOT NULL AND machine_model <> '' GROUP BY machine_model
UNION ALL
SELECT 'model-name', model_name FROM machine_applications WHERE model_name IS NOT NULL AND model_name <> '' GROUP BY model_name
UNION ALL
SELECT 'engine-brand', engine_brand FROM machine_applications WHERE engine_brand IS NOT NULL AND engine_brand <> '' GROUP BY engine_brand
UNION ALL
SELECT 'engine-type', engine_type FROM machine_applications WHERE engine_type IS NOT NULL AND engine_type <> '' GROUP BY engine_type;
