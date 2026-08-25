-- 029_add_xrefs_product_brand_oem3_unique.sql
-- 2026-08-25: ETL 导入向导 P2 验证时发现 xrefs upsert 报 42P10
--   (there is no unique or exclusion constraint matching the ON CONFLICT specification)
--
-- 根因: EtlImportService ImportXrefsAsync 的 INSERT ... ON CONFLICT 目标是
--   (product_id, oem_brand, oem_no_3) WHERE oem_brand IS NOT NULL AND oem_no_3 IS NOT NULL
--   但 cross_references 现有唯一索引是 uq_xrefs_brand_oem3 = (oem_brand, oem_no_3) WHERE is_discontinued=false
--   ON CONFLICT 推断要求存在"完全匹配"的唯一索引 (列 + 谓词), 不匹配 → 42P10, 真实导入被阻断
--
-- 修复: 新增与 ON CONFLICT 目标完全一致的部分唯一索引
--   (product_id, oem_brand, oem_no_3) WHERE oem_brand IS NOT NULL AND oem_no_3 IS NOT NULL
-- 幂等: IF NOT EXISTS; 已确认无重复数据 (009 迁移去重过, 实测 0 组重复)
-- 注: 旧索引 uq_xrefs_brand_oem3 保留 (前缀 (oem_brand, oem_no_3) 查询用途, 3 列索引不覆盖此前缀)

CREATE UNIQUE INDEX IF NOT EXISTS uq_xrefs_product_brand_oem3
    ON cross_references (product_id, oem_brand, oem_no_3)
    WHERE oem_brand IS NOT NULL AND oem_no_3 IS NOT NULL;

ANALYZE cross_references;
