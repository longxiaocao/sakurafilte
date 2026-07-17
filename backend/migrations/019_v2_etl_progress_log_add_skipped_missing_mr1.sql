-- V2 改进 1: etl_progress_log 加 skipped_missing_mr1 列
-- 文件: 019_v2_etl_progress_log_add_skipped_missing_mr1.sql
-- 用途: V2 主键迁移后, xrefs/apps 关联 mr_1 失败的跳过计数需要持久化到历史日志
--   - V2 Task 5.1: IncrSkippedMissingMr1 在运行时 Progress 对象中已实现
--   - 但 ToLogSnapshot 未持久化到 etl_progress_log.skipped_missing_mr1 列
--   - 运维查"昨天 xrefs/apps 跳过了多少 mr_1 关联失败"无数据支撑
-- WHY idempotent: 用 IF NOT EXISTS 保证可重跑 (018 是一次性脚本, 本脚本可重复执行)
-- 执行前提: 018_v2_legacy_data_cleanup.sql 已执行 (或并行执行无依赖)

-- 1. 加列 (BIGINT NOT NULL DEFAULT 0, 历史日志无此列时填 0)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'etl_progress_log'
          AND column_name = 'skipped_missing_mr1'
    ) THEN
        ALTER TABLE etl_progress_log
            ADD COLUMN skipped_missing_mr1 BIGINT NOT NULL DEFAULT 0;
        RAISE NOTICE '已添加列: etl_progress_log.skipped_missing_mr1';
    ELSE
        RAISE NOTICE '列已存在, 跳过: etl_progress_log.skipped_missing_mr1';
    END IF;
END $$;

-- 2. 索引 (可选, 按 skipped_missing_mr1 > 0 筛选异常日志时加速)
--   WHY 不建索引: 此列筛选频率低 (仅运维排查), 默认不加索引避免写入开销
--   若需要可手动: CREATE INDEX idx_etl_log_skipped_mr1 ON etl_progress_log (skipped_missing_mr1) WHERE skipped_missing_mr1 > 0;

-- 3. 校验
DO $$
BEGIN
    DECLARE
        col_count INTEGER;
    BEGIN
        SELECT COUNT(*) INTO col_count
        FROM information_schema.columns
        WHERE table_name = 'etl_progress_log'
          AND column_name = 'skipped_missing_mr1';

        IF col_count = 0 THEN
            RAISE EXCEPTION '校验失败: etl_progress_log.skipped_missing_mr1 列未添加';
        END IF;
    END;
END $$;
