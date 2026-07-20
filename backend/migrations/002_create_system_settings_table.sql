-- 一次性脚本,不可重跑 (P2-7.1 标注)
-- 原用途: 创建 system_settings 表并插入种子数据 (Day 4 HistoryCleanupService 依赖)
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
-- v28-4 P0 修复: 显式传 updated_at=now()
--   根因: 后端 dotnet run 启动时 EF Core 已创建 system_settings 表 (含 updated_at TIMESTAMPTZ DEFAULT now()),
--         但部分 PG 版本/配置下 DEFAULT 不补 NOT NULL 列的缺失值 (CI ubuntu-latest PG 16 严格模式)
--   CI 失败日志: null value in column "updated_at" of relation "system_settings" violates not-null constraint
--   修复: INSERT 显式传 now(), 保留 ON CONFLICT DO NOTHING 保证幂等 (后端 seed 后再跑也不冲突)
INSERT INTO system_settings (key, value, description, updated_at) VALUES
    ('history.retention_enabled', 'true', '历史清理全局开关', now()),
    ('history.retention_days', '0', '保留天数 (0=永久)', now()),
    ('history.cleanup_batch_size', '10000', '单批删除上限', now()),
    ('history.cleanup_cron', '0 3 * * *', '执行时间 (Cron)', now())
ON CONFLICT (key) DO NOTHING;
