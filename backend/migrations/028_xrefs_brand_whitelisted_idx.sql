-- idempotent 脚本, 可重复执行: 028 cross_references 白名单精准索引
-- 028: cross_references 白名单精准索引 (2026-08-24)
--   WHY: AdminXrefReorderEndpoints 列表端点 (?oemBrand=X) 查询
--     `WHERE oem_brand=? AND is_discontinued=false AND is_whitelisted=true`
--     现有 uq_xrefs_brand_oem3 (oem_brand, oem_no_3) WHERE is_discontinued=false
--     命中后需对 is_whitelisted 做 heap filter (5W+ 行浪费, 查询 734ms × 多次 ≈ 2s)
--   V3 方案: partial index 仅索引白名单条目 (少量), 查询走 index scan + nested loop
--     白名单维护后条目数 几十~几百, 索引极小, 查询毫秒级
-- **idempotent 脚本, 可重复执行**

CREATE INDEX IF NOT EXISTS ix_xrefs_brand_whitelisted
    ON cross_references (oem_brand, sort_order)
    WHERE is_whitelisted = true AND is_discontinued = false;