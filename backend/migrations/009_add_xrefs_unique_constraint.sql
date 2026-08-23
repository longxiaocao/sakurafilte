-- 一次性脚本,不可重跑 (P2-7.1 标注)
-- 原用途: cross_references 表去重 (DELETE 重复行) + 加部分唯一索引 (Day 6 ETL 幂等导入)
-- 009_add_xrefs_unique_constraint.sql
-- Day 6: 为 xrefs/apps 加 UNIQUE,支持 ETL 幂等导入(去重)
-- WHY: 当前 ETL 重跑会无限膨胀 (xrefs 当前 12.5M,多次跑会更大)

-- 先去重,保留 id 最小行 (历史记录优先)
DELETE FROM cross_references x
USING (
    SELECT MIN(id) AS keep_id, product_id, oem_brand, oem_no_3
    FROM cross_references
    WHERE oem_brand IS NOT NULL AND oem_no_3 IS NOT NULL
    GROUP BY product_id, oem_brand, oem_no_3
    HAVING COUNT(*) > 1
) d
WHERE x.product_id = d.product_id
  AND x.oem_brand IS NOT DISTINCT FROM d.oem_brand
  AND x.oem_no_3 IS NOT DISTINCT FROM d.oem_no_3
  AND x.id <> d.keep_id;

CREATE UNIQUE INDEX IF NOT EXISTS uq_xrefs_product_brand_no
    ON cross_references (product_id, oem_brand, oem_no_3)
    WHERE oem_brand IS NOT NULL AND oem_no_3 IS NOT NULL;

ANALYZE cross_references;
