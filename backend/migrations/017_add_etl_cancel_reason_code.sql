-- Day 9.5: 取消原因标准化枚举
--   在 cancel_reason (自由文本) 旁加 reason_code (有限枚举)
--   WHY: 文本分析困难, 运营审计需要按码聚合
--   枚举: USER_REQUEST/TIMEOUT/SYSTEM_SHUTDOWN/ADMIN_OVERRIDE/OTHER
--   兼容: 老记录的 reason_code 留 NULL (前台默认 "OTHER")
ALTER TABLE etl_progress_log
    ADD COLUMN IF NOT EXISTS reason_code VARCHAR(32);

COMMENT ON COLUMN etl_progress_log.reason_code IS 'Day 9.5: 取消原因枚举码, NULL 表示非取消或旧记录';

-- 兼容字段: 旧 cancel_reason NULL 的行, reason_code 自然为 NULL
