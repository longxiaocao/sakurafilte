-- 004_create_search_index_pending.sql
-- Day 5: Meili 索引写入失败补偿队列
-- WHY: ETL/后台编辑时,Meili 写失败不应阻塞 PG 提交;入队后由后台 worker 每 60s 重试

CREATE TABLE IF NOT EXISTS search_index_pending (
    id              BIGSERIAL PRIMARY KEY,
    operation       VARCHAR(20) NOT NULL,   -- 'index' | 'delete'
    payload         JSONB NOT NULL,         -- 文档(JSON) 或 删除的 ID 列表
    retry_count     INT NOT NULL DEFAULT 0,
    last_error      TEXT,
    created_at      TIMESTAMPTZ DEFAULT now(),
    next_retry_at   TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_pending_retry ON search_index_pending (next_retry_at, retry_count);

COMMENT ON TABLE search_index_pending IS 'Meili 索引写入补偿队列,失败重试 60s 一次';
COMMENT ON COLUMN search_index_pending.operation IS 'index=写入/更新, delete=删除';
COMMENT ON COLUMN search_index_pending.payload IS 'JSON: index=ProductIndexDoc, delete=[id1,id2,...]';
COMMENT ON COLUMN search_index_pending.next_retry_at IS '下次重试时间,失败后指数退避 60s/120s/300s';
