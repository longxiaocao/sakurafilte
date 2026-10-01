-- idempotent 可重跑: DDL 全部带 IF NOT EXISTS, 重复执行不报错、不产生重复对象。
-- OEM NO 1 暂存导入区
-- WHY: 客户 MR.1 尚未定稿，先保留三份 Excel 的原始粒度，避免直接写入正式业务表造成不可逆合并。

CREATE SCHEMA IF NOT EXISTS staging;

CREATE TABLE IF NOT EXISTS staging.import_batches (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    status              VARCHAR(20) NOT NULL DEFAULT 'running'
                        CHECK (status IN ('running', 'completed', 'failed', 'cancelled')),
    specs_file_name     TEXT,
    oem_numbers_file_name TEXT,
    applications_file_name TEXT,
    specs_source_rows   BIGINT NOT NULL DEFAULT 0,
    oem_numbers_source_rows BIGINT NOT NULL DEFAULT 0,
    applications_source_rows BIGINT NOT NULL DEFAULT 0,
    specs_staged_rows   BIGINT NOT NULL DEFAULT 0,
    oem_numbers_staged_rows BIGINT NOT NULL DEFAULT 0,
    applications_staged_rows BIGINT NOT NULL DEFAULT 0,
    duplicate_rows      BIGINT NOT NULL DEFAULT 0,
    blank_oem_rows      BIGINT NOT NULL DEFAULT 0,
    issue_count         BIGINT NOT NULL DEFAULT 0,
    error_message       TEXT,
    started_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    finished_at         TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS staging.oem_anchors (
    oem_no_1_normalized VARCHAR(100) PRIMARY KEY,
    oem_no_1_display    VARCHAR(100) NOT NULL,
    specs_row_count     BIGINT NOT NULL DEFAULT 0,
    oem_numbers_row_count BIGINT NOT NULL DEFAULT 0,
    applications_row_count BIGINT NOT NULL DEFAULT 0,
    first_seen_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_seen_at        TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS staging.product_specs_raw (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    batch_id            BIGINT NOT NULL REFERENCES staging.import_batches(id) ON DELETE CASCADE,
    source_row_no       INTEGER NOT NULL,
    oem_no_1_raw        TEXT,
    oem_no_1_normalized VARCHAR(100),
    product_name_1      TEXT,
    remark              TEXT,
    row_hash            CHAR(64) NOT NULL,
    raw_payload         JSONB NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS staging.oem_numbers_raw (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    batch_id            BIGINT NOT NULL REFERENCES staging.import_batches(id) ON DELETE CASCADE,
    source_row_no       INTEGER NOT NULL,
    oem_no_1_raw        TEXT,
    oem_no_1_normalized VARCHAR(100),
    product_name_1      TEXT,
    oem_brand           TEXT,
    oem_no_3            TEXT,
    row_hash            CHAR(64) NOT NULL,
    raw_payload         JSONB NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS staging.applications_raw (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    batch_id            BIGINT NOT NULL REFERENCES staging.import_batches(id) ON DELETE CASCADE,
    source_row_no       INTEGER NOT NULL,
    oem_no_1_raw        TEXT,
    oem_no_1_normalized VARCHAR(100),
    machine_brand       TEXT,
    machine_model       TEXT,
    product_name_2      TEXT,
    product_name_1      TEXT,
    model_name          TEXT,
    engine_brand        TEXT,
    engine_type         TEXT,
    engine_energy       TEXT,
    production_date     TEXT,
    power               TEXT,
    engine_model        TEXT,
    row_hash            CHAR(64) NOT NULL,
    raw_payload         JSONB NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_staging_applications_batch_row_hash UNIQUE (batch_id, row_hash)
);

CREATE TABLE IF NOT EXISTS staging.validation_issues (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    batch_id            BIGINT NOT NULL REFERENCES staging.import_batches(id) ON DELETE CASCADE,
    source_kind         VARCHAR(30) NOT NULL,
    source_row_no       INTEGER,
    issue_code          VARCHAR(50) NOT NULL,
    issue_message       TEXT NOT NULL,
    raw_payload         JSONB,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_staging_specs_oem
    ON staging.product_specs_raw (oem_no_1_normalized);
CREATE INDEX IF NOT EXISTS idx_staging_oem_numbers_oem
    ON staging.oem_numbers_raw (oem_no_1_normalized);
CREATE INDEX IF NOT EXISTS idx_staging_applications_oem
    ON staging.applications_raw (oem_no_1_normalized);
CREATE INDEX IF NOT EXISTS idx_staging_issues_batch
    ON staging.validation_issues (batch_id, source_kind, issue_code);

