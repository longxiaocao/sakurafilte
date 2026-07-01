-- Day 7.7: ETL 运行历史表
-- WHY: EtlProgress 是单例,进程重启后丢失,无法回溯"昨天 14:00 的 ETL 跑成啥样"
--      ETL Finish()/Fail() 时 INSERT 一行,所有计数快照保存
--      运维查"今天跑了哪些 ETL / 成功失败 / 读入多少"用
-- 索引: 按 entity_type + finished_at 倒序,按 status 过滤

CREATE TABLE etl_progress_log (
    id                  BIGSERIAL PRIMARY KEY,
    entity_type         VARCHAR(20) NOT NULL,        -- products / xrefs / apps
    mode                VARCHAR(20) NOT NULL,        -- full-load / insert-only / upsert
    file_path           TEXT NOT NULL,
    status              VARCHAR(20) NOT NULL,        -- completed / failed
    read_count          BIGINT NOT NULL DEFAULT 0,
    inserted_count      BIGINT NOT NULL DEFAULT 0,
    updated_count       BIGINT NOT NULL DEFAULT 0,
    skipped_count       BIGINT NOT NULL DEFAULT 0,
    skipped_missing_oem BIGINT NOT NULL DEFAULT 0,
    skipped_null_field  BIGINT NOT NULL DEFAULT 0,
    skipped_duplicate   BIGINT NOT NULL DEFAULT 0,
    error_count         BIGINT NOT NULL DEFAULT 0,
    indexed_count       BIGINT NOT NULL DEFAULT 0,
    index_pending_count BIGINT NOT NULL DEFAULT 0,
    last_error          TEXT,
    started_at          TIMESTAMPTZ NOT NULL,
    finished_at         TIMESTAMPTZ NOT NULL,
    duration_sec        DOUBLE PRECISION NOT NULL
);

-- 常用查询索引
CREATE INDEX idx_etl_log_entity_finished ON etl_progress_log (entity_type, finished_at DESC);
CREATE INDEX idx_etl_log_status ON etl_progress_log (status);
CREATE INDEX idx_etl_log_finished ON etl_progress_log (finished_at DESC);

COMMENT ON TABLE etl_progress_log IS
    'ETL 运行历史快照:Finish()/Fail() 时落库,用于运维回溯进程重启后的历史';
