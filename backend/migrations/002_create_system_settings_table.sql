-- 002_create_system_settings_table.sql
-- Day 4 补充: system_settings 表 (HistoryCleanupService 依赖)
-- 默认永久保留策略,通过此表可由客户在后台配置
-- WHY: value 用 TEXT 而非 JSONB,允许存任意字符串 (cron 表达式、纯数字、布尔语义)

CREATE TABLE IF NOT EXISTS system_settings (
    key         VARCHAR(100) PRIMARY KEY,
    value       TEXT,
    description TEXT,
    updated_at  TIMESTAMPTZ DEFAULT now()
);

-- 种子数据
INSERT INTO system_settings (key, value, description) VALUES
    ('history.retention_enabled', 'true', '历史清理全局开关'),
    ('history.retention_days', '0', '保留天数 (0=永久)'),
    ('history.cleanup_batch_size', '10000', '单批删除上限'),
    ('history.cleanup_cron', '0 3 * * *', '执行时间 (Cron)')
ON CONFLICT (key) DO NOTHING;
