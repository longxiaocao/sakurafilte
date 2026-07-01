-- 007_add_unique_constraint_oem_normalized.sql
-- Day 5 修复: ON CONFLICT (oem_no_normalized) 需要 UNIQUE 约束
-- WHY: 错误 42P10 "没有匹配ON CONCRLICT说明的唯一或者排除约束"

-- 删除可能重复的旧 unique 索引(若有)
DROP INDEX IF EXISTS idx_products_oem_normalized;

-- 创建 UNIQUE 索引(同时支持快速查询和 ON CONFLICT)
CREATE UNIQUE INDEX IF NOT EXISTS uq_products_oem_normalized
    ON products (oem_no_normalized);

ANALYZE products;
