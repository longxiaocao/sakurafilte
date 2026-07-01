-- 013_add_etl_alert_sent.sql
-- Day 7.9: etl_progress_log 加 alert_sent 列,避免 ETL 失败告警重复推送
--
-- WHY:
--   告警是 BackgroundService 轮询 etl_progress_log (status='failed' AND alert_sent=false)
--   推送后置 alert_sent=true,下次轮询不再处理
--   不在 EtlProgress.Fail() 内联告警:解耦告警可靠性与 ETL 业务逻辑,允许告警重试
--
-- 列属性:
--   - NOT NULL DEFAULT FALSE:老记录默认 false (满足"失败必告警"语义)
--   - WHERE status='failed' AND alert_sent=false 走 idx_etl_log_status 索引
--   - 不加单独索引,因 status 已有索引,加上也只过滤少量行
ALTER TABLE etl_progress_log
    ADD COLUMN alert_sent BOOLEAN NOT NULL DEFAULT FALSE;

-- 部分索引:只在 failed 状态建,告警查询更快
CREATE INDEX idx_etl_log_failed_unalerted
    ON etl_progress_log (id)
    WHERE status = 'failed' AND alert_sent = FALSE;
