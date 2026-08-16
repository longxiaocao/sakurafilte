-- 一次性脚本,不可重跑 (迁移序列已应用后禁止再次执行)
-- ============================================================
-- ============================================================
-- 021: products.mr_1 加长 varchar(10) → varchar(50)
-- WHY: V2 主键 mr_1 原始设计 10 字符 (演练数据 MR000001 8 字符), 客户真实数据无 MR.1 列,
--      ETL 按 OEM NO.2 确定性派生 MR+OEM规范化 (如 MRSA42359), 客户 OEM 最长 14 字符
--      → 派生值 16 字符超 10, 导入报 22001 value too long (2026-08-04 真实数据导入实证)
-- 影响面: mr_1 非主键 (主键是 id), 唯一性由部分唯一索引 idx_products_mr_1_unique 保证 (类型变更自动跟随),
--      xrefs/apps 无 mr_1 列 (关联走 product_id FK) → 仅 products 单表变更, 风险可控
-- 幂等: ALTER COLUMN TYPE 对已变更列是 no-op (PG 自动跳过同类型), CHECK 约束先删后建

ALTER TABLE products DROP CONSTRAINT IF EXISTS chk_mr_1_format;
ALTER TABLE products ALTER COLUMN mr_1 TYPE varchar(50);
ALTER TABLE products ADD CONSTRAINT chk_mr_1_format CHECK (mr_1 IS NULL OR mr_1 ~ '^[A-Za-z0-9]{1,50}$');
