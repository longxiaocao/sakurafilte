-- 一次性脚本,不可重跑 (P2-7.1 标注)
-- 原用途: search_index_dead_letter 加 status 列 + UPDATE 回填 active + 加索引 (Day 7.10.1 死信状态持久化修复)
-- Day 7.10.1 Bug Fix: 死信恢复元数据持久化
--   WHY: Day 7.10 初版的 DeadLetterRecoveryService 删除了 dead_letter 行后再设置
--         recovery_count, EF Core SaveChanges 时清空变更, 导致恢复计数永远丢失。
--         若恢复的条目再次失败并以新 id 入队, recovery_count 永远从 0 开始,
--         max_recovery_count 限位完全失效。
--   FIX: 死信永不删除, 改 status 列标记 active/recovered
--        转入新死信时 (IndexReplayWorker), 若发现 payload 相同的最近 dead_letter
--        已 recovered, 递增其 recovery_count 而非新增行 (跨循环保留计数)

ALTER TABLE search_index_dead_letter
    -- status: 'active' = 在死信队列等待处理; 'recovered' = 已恢复到 pending, 历史留痕
    ADD COLUMN IF NOT EXISTS status              VARCHAR(20)  NOT NULL DEFAULT 'active',
    ADD COLUMN IF NOT EXISTS recovered_at        TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS recovered_to_pending_id BIGINT;

-- 历史数据回填: 旧 dead_letter 均为 active (Day 7.10 之前的版本没 status 概念)
UPDATE search_index_dead_letter SET status = 'active' WHERE status IS NULL OR status = '';

-- 索引: worker 扫描 "active + recovery_count < max" 的候选 (替代旧 idx_dead_letter_recovery)
CREATE INDEX IF NOT EXISTS ix_dead_letter_active_recovery
    ON search_index_dead_letter (status, recovery_count, last_recovery_at)
    WHERE status = 'active';

-- 索引: cleanup 仅清 recovered, 按 recovered_at 排序
CREATE INDEX IF NOT EXISTS ix_dead_letter_recovered_at
    ON search_index_dead_letter (recovered_at)
    WHERE status = 'recovered';

-- 索引: IndexReplayWorker 转入死信时, 查找同 payload 的最近 recovered 记录
--   WHY: 同一文档的多次死信 (原始id + payload 相同) 应共享 recovery_count
--   payload 是 jsonb, 哈希后建索引
CREATE INDEX IF NOT EXISTS ix_dead_letter_payload_hash
    ON search_index_dead_letter (operation, md5(payload::text), status);

COMMENT ON COLUMN search_index_dead_letter.status IS
    'active = 等待处理; recovered = 已恢复到 pending, 历史留痕, 不参与 worker 扫描';
COMMENT ON COLUMN search_index_dead_letter.recovered_at IS
    '最近一次恢复到 pending 的时间, 跨循环保留 recovery_count 关联';
COMMENT ON COLUMN search_index_dead_letter.recovered_to_pending_id IS
    '恢复到 search_index_pending 的新行 id, 便于链路追踪';
