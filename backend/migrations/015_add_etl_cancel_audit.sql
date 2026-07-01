-- Day 9.4: etl_progress_log 加取消审计字段
-- WHY: 取消 ETL 任务时 (DELETE /api/admin/etl/task),需要记录操作原因 + 时间戳
--      用于运维审计: 谁在什么时间取消了哪次 ETL, 为什么取消
--      之前 cancel 不落库, 历史中只看到 "cancelled" 状态, 但没有上下文
-- ALTER 而非重建: 历史数据保留, NULL 表示非取消任务

ALTER TABLE etl_progress_log
    ADD COLUMN IF NOT EXISTS cancel_reason TEXT,
    ADD COLUMN IF NOT EXISTS cancelled_at  TIMESTAMPTZ;

COMMENT ON COLUMN etl_progress_log.cancel_reason IS
    'Day 9.4: 取消 ETL 时记录的原因, NULL 表示非取消';
COMMENT ON COLUMN etl_progress_log.cancelled_at IS
    'Day 9.4: 取消时间, NULL 表示非取消';
