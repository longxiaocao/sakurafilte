-- 一次性脚本,不可重跑 (P2-7.1 标注)
-- 原用途: machine_applications 表去重 (DELETE 重复行) + 加部分唯一索引 (Day 6 ETL 幂等导入)
-- 010_add_apps_unique_constraint.sql
-- Day 6: machine_applications 加 UNIQUE(product_id, brand, model)
-- WHY: ETL 重复跑需要幂等去重

DELETE FROM machine_applications m
USING (
    SELECT MIN(id) AS keep_id, product_id, machine_brand, machine_model
    FROM machine_applications
    WHERE machine_brand IS NOT NULL AND machine_model IS NOT NULL
    GROUP BY product_id, machine_brand, machine_model
    HAVING COUNT(*) > 1
) d
WHERE m.product_id = d.product_id
  AND m.machine_brand IS NOT DISTINCT FROM d.machine_brand
  AND m.machine_model IS NOT DISTINCT FROM d.machine_model
  AND m.id <> d.keep_id;

CREATE UNIQUE INDEX IF NOT EXISTS uq_apps_product_brand_model
    ON machine_applications (product_id, machine_brand, machine_model)
    WHERE machine_brand IS NOT NULL AND machine_model IS NOT NULL;

ANALYZE machine_applications;
