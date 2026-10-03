-- idempotent 可重跑 (原编号 020 → 040, 2026-10-03 为消除编号冲突而改名; 语句重复执行无副作用)
-- ============================================================
-- ============================================================
-- 040: machine_applications.is_discontinued 补默认值 false
-- WHY: EF 模型配置 HasDefaultValue(false) (ProductDbContext L136), 但 EF 迁移未生成列默认值,
--      ETL raw SQL INSERT (EtlImportService L2070/2079) 不含该列 → NOT NULL 冲突 (23502)
-- 对齐: 实体默认值与 DB 一致, ETL 缺列时由 DEFAULT 兜底
ALTER TABLE machine_applications
    ALTER COLUMN is_discontinued SET DEFAULT false;
