-- Day 7: 搜索索引写入死信队列
-- 触发条件: search_index_pending.retry_count >= 5 仍失败
-- 用途: 人工排查 + 手动恢复,不会自动重试
-- 表结构: 继承 pending 的所有诊断字段,加 original_id/moved_at 用于溯源

CREATE TABLE search_index_dead_letter (
    id            BIGSERIAL PRIMARY KEY,
    original_id   BIGINT NOT NULL,                       -- 来源 search_index_pending.id
    operation     VARCHAR(20) NOT NULL,                  -- index/delete
    payload       JSONB NOT NULL,
    retry_count   INT NOT NULL DEFAULT 5,
    last_error    TEXT,
    created_at    TIMESTAMPTZ NOT NULL,                  -- 原入队时间(溯源)
    moved_at      TIMESTAMPTZ NOT NULL DEFAULT now()     -- 转入死信时间
);

-- 诊断索引
CREATE INDEX idx_dead_letter_moved_at ON search_index_dead_letter (moved_at DESC);
CREATE INDEX idx_dead_letter_operation ON search_index_dead_letter (operation);

COMMENT ON TABLE search_index_dead_letter IS
    'Meili 索引写入死信队列:retry 5 次仍失败,IndexReplayWorker 自动转入,需人工排查';
