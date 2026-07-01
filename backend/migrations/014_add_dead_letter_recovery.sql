-- Day 7.10 Item 4: 死信自动恢复字段
-- WHY: 当前 dead_letter 一旦进入就永久不动,只能人工 /api/admin/dead-letter/{id}/recover
--   新增: 当 last_error 属于"瞬时错误" (Meili 5xx / connection refused / timeout)
--         且 entry 已 stable 冷却 >= 10min,自动移回 pending 重试
--   限位: recovery_count >= max_recovery_count (默认 3) 后不再自动恢复,需人工
--   可观测: last_recovery_at / last_recovery_error 记录自动恢复轨迹,排查用

ALTER TABLE search_index_dead_letter
    ADD COLUMN IF NOT EXISTS recovery_count     INT          NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS last_recovery_at   TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS last_recovery_error TEXT;

-- 诊断索引: worker 扫描 "cooling 完成 + recovery_count < max" 的候选
--   WHERE last_recovery_at IS NULL OR last_recovery_at < now() - INTERVAL '10 min'
--   + recovery_count < 3
CREATE INDEX IF NOT EXISTS idx_dead_letter_recovery
    ON search_index_dead_letter (recovery_count, last_recovery_at);

COMMENT ON COLUMN search_index_dead_letter.recovery_count IS
    '自动恢复次数,超过 max_recovery_count (默认 3) 后不再自动重试,需人工 recover';
COMMENT ON COLUMN search_index_dead_letter.last_recovery_at IS
    '最近一次自动恢复时间,用于冷却 (默认 10min)';
COMMENT ON COLUMN search_index_dead_letter.last_recovery_error IS
    '自动恢复过程中遇到的错误,例如 Meili 仍不可用; 排查"为什么自动恢复不工作"用';
