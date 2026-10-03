-- idempotent 可重跑 (V2 客户数据导入: etl_progress_log 加 auto_generated_mr1 列)
-- 文件: 041_etl_progress_log_add_auto_generated_mr1.sql
-- 改名: 原编号 026 → 041 (2026-10-03 消除与 026_product_images_show_dimension.sql 的编号冲突)
-- 用途: 记录 products 导入时 mr_1 为空自动生成的行数
--   - 与 skipped_missing_mr1 区分: 前者是系统生成, 后者是关联失败跳过

-- 1. 加列 (BIGINT NOT NULL DEFAULT 0)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'etl_progress_log'
          AND column_name = 'auto_generated_mr1'
    ) THEN
        ALTER TABLE etl_progress_log
            ADD COLUMN auto_generated_mr1 BIGINT NOT NULL DEFAULT 0;
        RAISE NOTICE '已添加列: etl_progress_log.auto_generated_mr1';
    ELSE
        RAISE NOTICE '列已存在, 跳过: etl_progress_log.auto_generated_mr1';
    END IF;
END $$;

-- 2. 校验
DO $$
BEGIN
    DECLARE
        col_count INTEGER;
    BEGIN
        SELECT COUNT(*) INTO col_count
        FROM information_schema.columns
        WHERE table_name = 'etl_progress_log'
          AND column_name = 'auto_generated_mr1';

        IF col_count = 0 THEN
            RAISE EXCEPTION '校验失败: etl_progress_log.auto_generated_mr1 列未添加';
        END IF;
    END;
END $$;
