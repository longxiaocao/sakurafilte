-- OEM NO 1 正式目录层
-- WHY: 客户尚未提供 MR.1 时，仍须完整保存 OEM 规格、交叉号码和机型适配；因此与旧 public 产品层隔离。

CREATE SCHEMA IF NOT EXISTS catalog;

CREATE TABLE IF NOT EXISTS catalog.oem_import_runs (
    batch_id                    BIGINT PRIMARY KEY REFERENCES staging.import_batches(id),
    status                      VARCHAR(20) NOT NULL CHECK (status IN ('running', 'completed', 'failed')),
    oem_product_rows            BIGINT NOT NULL DEFAULT 0,
    cross_reference_rows        BIGINT NOT NULL DEFAULT 0,
    machine_application_rows    BIGINT NOT NULL DEFAULT 0,
    error_message               TEXT,
    started_at                  TIMESTAMPTZ NOT NULL DEFAULT now(),
    finished_at                 TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS catalog.oem_products (
    id                          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    oem_no_1_normalized         VARCHAR(100) NOT NULL UNIQUE,
    oem_no_1_display            VARCHAR(100) NOT NULL,
    product_name_candidates     JSONB NOT NULL DEFAULT '[]'::jsonb,
    spec_payload                JSONB NOT NULL DEFAULT '{}'::jsonb,
    spec_conflict_field_count   INTEGER NOT NULL DEFAULT 0,
    source_batch_id             BIGINT NOT NULL REFERENCES staging.import_batches(id),
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at                  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS catalog.oem_cross_references (
    id                          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    oem_product_id              BIGINT NOT NULL REFERENCES catalog.oem_products(id) ON DELETE CASCADE,
    source_batch_id             BIGINT NOT NULL REFERENCES staging.import_batches(id),
    product_name_key            TEXT NOT NULL,
    product_name_1              TEXT,
    oem_brand_key               TEXT NOT NULL,
    oem_brand                   TEXT,
    oem_no_3_key                TEXT NOT NULL,
    oem_no_3                    TEXT,
    source_row_nos              INTEGER[] NOT NULL,
    merged_source_row_count     INTEGER NOT NULL,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (oem_product_id, product_name_key, oem_brand_key, oem_no_3_key)
);

CREATE TABLE IF NOT EXISTS catalog.oem_machine_applications (
    id                          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    oem_product_id              BIGINT NOT NULL REFERENCES catalog.oem_products(id) ON DELETE CASCADE,
    source_batch_id             BIGINT NOT NULL REFERENCES staging.import_batches(id),
    source_application_id       BIGINT NOT NULL UNIQUE,
    source_row_no               INTEGER NOT NULL,
    machine_brand               TEXT,
    machine_model               TEXT,
    product_name_2              TEXT,
    product_name_1              TEXT,
    model_name                  TEXT,
    engine_brand                TEXT,
    engine_type                 TEXT,
    engine_energy               TEXT,
    production_date             TEXT,
    power                       TEXT,
    engine_model                TEXT,
    row_hash                    CHAR(64) NOT NULL,
    raw_payload                 JSONB NOT NULL,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS catalog.oem_mr1_mappings (
    id                          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    oem_product_id              BIGINT NOT NULL REFERENCES catalog.oem_products(id) ON DELETE CASCADE,
    mr1                         VARCHAR(50) NOT NULL,
    effective_at                TIMESTAMPTZ NOT NULL DEFAULT now(),
    ended_at                    TIMESTAMPTZ,
    assigned_by                 VARCHAR(100) NOT NULL,
    change_reason               TEXT,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT now(),
    CHECK (ended_at IS NULL OR ended_at >= effective_at)
);

CREATE INDEX IF NOT EXISTS idx_catalog_oem_xrefs_product
    ON catalog.oem_cross_references (oem_product_id);
CREATE INDEX IF NOT EXISTS idx_catalog_oem_apps_product
    ON catalog.oem_machine_applications (oem_product_id);
CREATE INDEX IF NOT EXISTS idx_catalog_oem_apps_machine
    ON catalog.oem_machine_applications (machine_brand, machine_model);
CREATE UNIQUE INDEX IF NOT EXISTS uq_catalog_oem_mr1_active_product
    ON catalog.oem_mr1_mappings (oem_product_id) WHERE ended_at IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_catalog_oem_mr1_active_value
    ON catalog.oem_mr1_mappings (mr1) WHERE ended_at IS NULL;

CREATE OR REPLACE FUNCTION catalog.publish_oem_clean_batch(p_batch_id BIGINT)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_oem_product_rows BIGINT := 0;
    v_cross_reference_rows BIGINT := 0;
    v_machine_application_rows BIGINT := 0;
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM staging.clean_runs
        WHERE batch_id = p_batch_id AND status = 'completed'
    ) THEN
        RAISE EXCEPTION '批次 % 尚未完成 clean 层，不能发布 OEM 目录', p_batch_id;
    END IF;

    PERFORM pg_advisory_xact_lock(hashtext('catalog.publish_oem_clean_batch'), p_batch_id::INTEGER);

    INSERT INTO catalog.oem_import_runs (batch_id, status, started_at, finished_at, error_message)
    VALUES (p_batch_id, 'running', now(), NULL, NULL)
    ON CONFLICT (batch_id) DO UPDATE
    SET status = 'running', started_at = now(), finished_at = NULL, error_message = NULL;

    WITH anchors AS (
        SELECT oem_no_1_normalized, MIN(oem_no_1_display) AS oem_no_1_display
        FROM (
            SELECT oem_no_1_normalized, oem_no_1_display
            FROM staging.product_specs_clean WHERE batch_id = p_batch_id
            UNION ALL
            SELECT oem_no_1_normalized, oem_no_1_display
            FROM staging.oem_numbers_clean WHERE batch_id = p_batch_id
            UNION ALL
            SELECT oem_no_1_normalized,
                   COALESCE(NULLIF(btrim(oem_no_1_raw), ''), oem_no_1_normalized)
            FROM staging.applications_clean
            WHERE batch_id = p_batch_id AND oem_no_1_normalized IS NOT NULL
        ) source_anchors
        GROUP BY oem_no_1_normalized
    )
    INSERT INTO catalog.oem_products (
        oem_no_1_normalized, oem_no_1_display, product_name_candidates, spec_payload,
        spec_conflict_field_count, source_batch_id, updated_at)
    SELECT
        a.oem_no_1_normalized,
        a.oem_no_1_display,
        COALESCE(s.product_name_candidates, '[]'::jsonb),
        COALESCE(s.spec_payload, '{}'::jsonb),
        COALESCE(s.conflict_field_count, 0),
        p_batch_id,
        now()
    FROM anchors a
    LEFT JOIN staging.product_specs_clean s
      ON s.batch_id = p_batch_id AND s.oem_no_1_normalized = a.oem_no_1_normalized
    ON CONFLICT (oem_no_1_normalized) DO UPDATE
    SET oem_no_1_display = EXCLUDED.oem_no_1_display,
        product_name_candidates = EXCLUDED.product_name_candidates,
        spec_payload = EXCLUDED.spec_payload,
        spec_conflict_field_count = EXCLUDED.spec_conflict_field_count,
        source_batch_id = EXCLUDED.source_batch_id,
        updated_at = now();

    GET DIAGNOSTICS v_oem_product_rows = ROW_COUNT;

    DELETE FROM catalog.oem_cross_references x
    USING catalog.oem_products p
    WHERE x.oem_product_id = p.id AND p.source_batch_id = p_batch_id;

    DELETE FROM catalog.oem_machine_applications a
    USING catalog.oem_products p
    WHERE a.oem_product_id = p.id AND p.source_batch_id = p_batch_id;

    INSERT INTO catalog.oem_cross_references (
        oem_product_id, source_batch_id, product_name_key, product_name_1,
        oem_brand_key, oem_brand, oem_no_3_key, oem_no_3, source_row_nos, merged_source_row_count)
    SELECT
        p.id, p_batch_id, x.product_name_key, x.product_name_1,
        x.oem_brand_key, x.oem_brand, x.oem_no_3_key, x.oem_no_3,
        x.source_row_nos, x.merged_source_row_count
    FROM staging.oem_numbers_clean x
    JOIN catalog.oem_products p ON p.oem_no_1_normalized = x.oem_no_1_normalized
    WHERE x.batch_id = p_batch_id;

    GET DIAGNOSTICS v_cross_reference_rows = ROW_COUNT;

    INSERT INTO catalog.oem_machine_applications (
        oem_product_id, source_batch_id, source_application_id, source_row_no,
        machine_brand, machine_model, product_name_2, product_name_1, model_name,
        engine_brand, engine_type, engine_energy, production_date, power, engine_model,
        row_hash, raw_payload)
    SELECT
        p.id, p_batch_id, a.source_application_id, a.source_row_no,
        a.machine_brand, a.machine_model, a.product_name_2, a.product_name_1, a.model_name,
        a.engine_brand, a.engine_type, a.engine_energy, a.production_date, a.power, a.engine_model,
        a.row_hash, a.raw_payload
    FROM staging.applications_clean a
    JOIN catalog.oem_products p ON p.oem_no_1_normalized = a.oem_no_1_normalized
    WHERE a.batch_id = p_batch_id;

    GET DIAGNOSTICS v_machine_application_rows = ROW_COUNT;

    UPDATE catalog.oem_import_runs
    SET status = 'completed',
        oem_product_rows = v_oem_product_rows,
        cross_reference_rows = v_cross_reference_rows,
        machine_application_rows = v_machine_application_rows,
        finished_at = now()
    WHERE batch_id = p_batch_id;
EXCEPTION WHEN OTHERS THEN
    UPDATE catalog.oem_import_runs
    SET status = 'failed', error_message = SQLERRM, finished_at = now()
    WHERE batch_id = p_batch_id;
    RAISE;
END;
$$;
