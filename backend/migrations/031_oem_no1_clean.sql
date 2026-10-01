-- idempotent 可重跑: DDL 全部带 IF NOT EXISTS, 重复执行不报错、不产生重复对象。
-- OEM NO 1 清洗合并层
-- WHY: raw 暂存层保留原始粒度；clean 层只生成可追溯快照和逻辑去重明细，绝不直接覆盖正式业务表。

CREATE TABLE IF NOT EXISTS staging.clean_runs (
    batch_id                     BIGINT PRIMARY KEY REFERENCES staging.import_batches(id) ON DELETE CASCADE,
    status                       VARCHAR(20) NOT NULL
                                 CHECK (status IN ('running', 'completed', 'failed')),
    product_specs_rows           BIGINT NOT NULL DEFAULT 0,
    product_spec_conflict_rows   BIGINT NOT NULL DEFAULT 0,
    oem_numbers_rows             BIGINT NOT NULL DEFAULT 0,
    oem_numbers_merged_rows      BIGINT NOT NULL DEFAULT 0,
    applications_rows            BIGINT NOT NULL DEFAULT 0,
    error_message                TEXT,
    started_at                   TIMESTAMPTZ NOT NULL DEFAULT now(),
    finished_at                  TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS staging.product_specs_clean (
    batch_id                  BIGINT NOT NULL REFERENCES staging.import_batches(id) ON DELETE CASCADE,
    oem_no_1_normalized       VARCHAR(100) NOT NULL,
    oem_no_1_display          VARCHAR(100) NOT NULL,
    product_name_candidates   JSONB NOT NULL DEFAULT '[]'::jsonb,
    spec_payload              JSONB NOT NULL DEFAULT '{}'::jsonb,
    source_row_nos            INTEGER[] NOT NULL,
    conflict_field_count      INTEGER NOT NULL DEFAULT 0,
    created_at                TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (batch_id, oem_no_1_normalized)
);

CREATE TABLE IF NOT EXISTS staging.product_spec_field_conflicts (
    id                       BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    batch_id                 BIGINT NOT NULL REFERENCES staging.import_batches(id) ON DELETE CASCADE,
    oem_no_1_normalized      VARCHAR(100) NOT NULL,
    field_name               TEXT NOT NULL,
    candidate_values         JSONB NOT NULL,
    source_row_count         INTEGER NOT NULL,
    created_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (batch_id, oem_no_1_normalized, field_name)
);

CREATE TABLE IF NOT EXISTS staging.oem_numbers_clean (
    batch_id                   BIGINT NOT NULL REFERENCES staging.import_batches(id) ON DELETE CASCADE,
    oem_no_1_normalized        VARCHAR(100) NOT NULL,
    oem_no_1_display           VARCHAR(100) NOT NULL,
    product_name_key           TEXT NOT NULL,
    product_name_1             TEXT,
    oem_brand_key              TEXT NOT NULL,
    oem_brand                  TEXT,
    oem_no_3_key               TEXT NOT NULL,
    oem_no_3                   TEXT,
    source_row_nos             INTEGER[] NOT NULL,
    merged_source_row_count    INTEGER NOT NULL,
    created_at                 TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (batch_id, oem_no_1_normalized, product_name_key, oem_brand_key, oem_no_3_key)
);

CREATE TABLE IF NOT EXISTS staging.applications_clean (
    source_application_id      BIGINT PRIMARY KEY REFERENCES staging.applications_raw(id) ON DELETE CASCADE,
    batch_id                   BIGINT NOT NULL REFERENCES staging.import_batches(id) ON DELETE CASCADE,
    source_row_no              INTEGER NOT NULL,
    oem_no_1_raw               TEXT,
    oem_no_1_normalized        VARCHAR(100),
    machine_brand              TEXT,
    machine_model              TEXT,
    product_name_2             TEXT,
    product_name_1             TEXT,
    model_name                 TEXT,
    engine_brand               TEXT,
    engine_type                TEXT,
    engine_energy              TEXT,
    production_date            TEXT,
    power                      TEXT,
    engine_model               TEXT,
    row_hash                   CHAR(64) NOT NULL,
    raw_payload                JSONB NOT NULL,
    created_at                 TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_clean_product_specs_oem
    ON staging.product_specs_clean (oem_no_1_normalized);
CREATE INDEX IF NOT EXISTS idx_clean_oem_numbers_oem
    ON staging.oem_numbers_clean (oem_no_1_normalized);
CREATE INDEX IF NOT EXISTS idx_clean_applications_batch_oem
    ON staging.applications_clean (batch_id, oem_no_1_normalized);
CREATE INDEX IF NOT EXISTS idx_clean_product_conflicts_batch
    ON staging.product_spec_field_conflicts (batch_id, oem_no_1_normalized);

CREATE OR REPLACE FUNCTION staging.refresh_oem_clean(p_batch_id BIGINT)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_product_specs_rows BIGINT := 0;
    v_product_spec_conflict_rows BIGINT := 0;
    v_oem_numbers_rows BIGINT := 0;
    v_oem_numbers_merged_rows BIGINT := 0;
    v_applications_rows BIGINT := 0;
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM staging.import_batches WHERE id = p_batch_id AND status = 'completed'
    ) THEN
        RAISE EXCEPTION '批次 % 不存在或尚未完成导入', p_batch_id;
    END IF;

    DELETE FROM staging.product_spec_field_conflicts WHERE batch_id = p_batch_id;
    DELETE FROM staging.product_specs_clean WHERE batch_id = p_batch_id;
    DELETE FROM staging.oem_numbers_clean WHERE batch_id = p_batch_id;
    DELETE FROM staging.applications_clean WHERE batch_id = p_batch_id;
    DELETE FROM staging.clean_runs WHERE batch_id = p_batch_id;

    INSERT INTO staging.clean_runs (batch_id, status) VALUES (p_batch_id, 'running');

    WITH field_values AS (
        SELECT
            p.oem_no_1_normalized,
            e.key AS field_name,
            ARRAY_AGG(DISTINCT btrim(e.value) ORDER BY btrim(e.value))
                FILTER (WHERE NULLIF(btrim(e.value), '') IS NOT NULL) AS candidate_values,
            COUNT(DISTINCT p.source_row_no)::INTEGER AS source_row_count
        FROM staging.product_specs_raw p
        CROSS JOIN LATERAL jsonb_each_text(p.raw_payload) e(key, value)
        WHERE p.batch_id = p_batch_id
          AND p.oem_no_1_normalized IS NOT NULL
        GROUP BY p.oem_no_1_normalized, e.key
        HAVING COUNT(*) FILTER (WHERE NULLIF(btrim(e.value), '') IS NOT NULL) > 0
    )
    INSERT INTO staging.product_spec_field_conflicts (batch_id, oem_no_1_normalized, field_name, candidate_values, source_row_count)
    SELECT p_batch_id, oem_no_1_normalized, field_name, to_jsonb(candidate_values), source_row_count
    FROM field_values
    WHERE cardinality(candidate_values) > 1;

    GET DIAGNOSTICS v_product_spec_conflict_rows = ROW_COUNT;

    WITH spec_rows AS (
        SELECT
            p.oem_no_1_normalized,
            MIN(p.oem_no_1_raw) FILTER (WHERE NULLIF(btrim(p.oem_no_1_raw), '') IS NOT NULL) AS oem_no_1_display,
            ARRAY_AGG(p.source_row_no ORDER BY p.source_row_no) AS source_row_nos
        FROM staging.product_specs_raw p
        WHERE p.batch_id = p_batch_id
          AND p.oem_no_1_normalized IS NOT NULL
        GROUP BY p.oem_no_1_normalized
    ),
    field_values AS (
        SELECT
            p.oem_no_1_normalized,
            e.key AS field_name,
            ARRAY_AGG(DISTINCT btrim(e.value) ORDER BY btrim(e.value))
                FILTER (WHERE NULLIF(btrim(e.value), '') IS NOT NULL) AS candidate_values
        FROM staging.product_specs_raw p
        CROSS JOIN LATERAL jsonb_each_text(p.raw_payload) e(key, value)
        WHERE p.batch_id = p_batch_id
          AND p.oem_no_1_normalized IS NOT NULL
        GROUP BY p.oem_no_1_normalized, e.key
        HAVING COUNT(*) FILTER (WHERE NULLIF(btrim(e.value), '') IS NOT NULL) > 0
    )
    INSERT INTO staging.product_specs_clean (
        batch_id, oem_no_1_normalized, oem_no_1_display, product_name_candidates,
        spec_payload, source_row_nos, conflict_field_count)
    SELECT
        p_batch_id,
        s.oem_no_1_normalized,
        s.oem_no_1_display,
        COALESCE((JSONB_AGG(to_jsonb(f.candidate_values)) FILTER (WHERE f.field_name = 'product_name_1'))->0, '[]'::jsonb),
        COALESCE(JSONB_OBJECT_AGG(f.field_name, to_jsonb(f.candidate_values[1]))
            FILTER (WHERE f.field_name NOT IN ('oem_no_1', 'product_name_1') AND cardinality(f.candidate_values) = 1), '{}'::jsonb),
        s.source_row_nos,
        COUNT(*) FILTER (WHERE cardinality(f.candidate_values) > 1)::INTEGER
    FROM spec_rows s
    LEFT JOIN field_values f ON f.oem_no_1_normalized = s.oem_no_1_normalized
    GROUP BY s.oem_no_1_normalized, s.oem_no_1_display, s.source_row_nos;

    GET DIAGNOSTICS v_product_specs_rows = ROW_COUNT;

    WITH normalized_rows AS (
        SELECT
            oem_no_1_normalized,
            oem_no_1_raw,
            product_name_1,
            oem_brand,
            oem_no_3,
            source_row_no,
            REGEXP_REPLACE(UPPER(COALESCE(NULLIF(btrim(product_name_1), ''), '')), '\s+', ' ', 'g') AS product_name_key,
            REGEXP_REPLACE(UPPER(COALESCE(NULLIF(btrim(oem_brand), ''), '')), '\s+', ' ', 'g') AS oem_brand_key,
            REGEXP_REPLACE(UPPER(COALESCE(NULLIF(btrim(oem_no_3), ''), '')), '\s+', ' ', 'g') AS oem_no_3_key
        FROM staging.oem_numbers_raw
        WHERE batch_id = p_batch_id
          AND oem_no_1_normalized IS NOT NULL
    )
    INSERT INTO staging.oem_numbers_clean (
        batch_id, oem_no_1_normalized, oem_no_1_display, product_name_key, product_name_1,
        oem_brand_key, oem_brand, oem_no_3_key, oem_no_3, source_row_nos, merged_source_row_count)
    SELECT
        p_batch_id,
        oem_no_1_normalized,
        MIN(oem_no_1_raw) FILTER (WHERE NULLIF(btrim(oem_no_1_raw), '') IS NOT NULL),
        product_name_key,
        MIN(product_name_1) FILTER (WHERE NULLIF(btrim(product_name_1), '') IS NOT NULL),
        oem_brand_key,
        MIN(oem_brand) FILTER (WHERE NULLIF(btrim(oem_brand), '') IS NOT NULL),
        oem_no_3_key,
        MIN(oem_no_3) FILTER (WHERE NULLIF(btrim(oem_no_3), '') IS NOT NULL),
        ARRAY_AGG(source_row_no ORDER BY source_row_no),
        COUNT(*)::INTEGER
    FROM normalized_rows
    GROUP BY oem_no_1_normalized, product_name_key, oem_brand_key, oem_no_3_key;

    GET DIAGNOSTICS v_oem_numbers_rows = ROW_COUNT;

    SELECT COUNT(*) - v_oem_numbers_rows INTO v_oem_numbers_merged_rows
    FROM staging.oem_numbers_raw
    WHERE batch_id = p_batch_id
      AND oem_no_1_normalized IS NOT NULL;

    INSERT INTO staging.applications_clean (
        source_application_id, batch_id, source_row_no, oem_no_1_raw, oem_no_1_normalized,
        machine_brand, machine_model, product_name_2, product_name_1, model_name, engine_brand,
        engine_type, engine_energy, production_date, power, engine_model, row_hash, raw_payload)
    SELECT
        id, batch_id, source_row_no, oem_no_1_raw, oem_no_1_normalized,
        machine_brand, machine_model, product_name_2, product_name_1, model_name, engine_brand,
        engine_type, engine_energy, production_date, power, engine_model, row_hash, raw_payload
    FROM staging.applications_raw
    WHERE batch_id = p_batch_id;

    GET DIAGNOSTICS v_applications_rows = ROW_COUNT;

    UPDATE staging.clean_runs
    SET status = 'completed',
        product_specs_rows = v_product_specs_rows,
        product_spec_conflict_rows = v_product_spec_conflict_rows,
        oem_numbers_rows = v_oem_numbers_rows,
        oem_numbers_merged_rows = v_oem_numbers_merged_rows,
        applications_rows = v_applications_rows,
        finished_at = now()
    WHERE batch_id = p_batch_id;
EXCEPTION WHEN OTHERS THEN
    UPDATE staging.clean_runs
    SET status = 'failed', error_message = SQLERRM, finished_at = now()
    WHERE batch_id = p_batch_id;
    RAISE;
END;
$$;
