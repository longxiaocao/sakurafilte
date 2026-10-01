-- idempotent 可重跑: DDL 全部带 IF NOT EXISTS, 重复执行不报错、不产生重复对象。
-- OEM NO 1 到 MR.1 的审核映射层
-- WHY: OEM NO 1 是当前来源锚点，MR.1 仍是正式系统内部产品标识；任何正式表写入前必须有可追溯的审核结论。

CREATE TABLE IF NOT EXISTS staging.oem_mapping_candidate_runs (
    batch_id             BIGINT PRIMARY KEY REFERENCES staging.import_batches(id) ON DELETE CASCADE,
    status               VARCHAR(20) NOT NULL
                         CHECK (status IN ('running', 'completed', 'failed')),
    total_oem_count      BIGINT NOT NULL DEFAULT 0,
    candidate_count      BIGINT NOT NULL DEFAULT 0,
    pending_count        BIGINT NOT NULL DEFAULT 0,
    ambiguous_count      BIGINT NOT NULL DEFAULT 0,
    error_message        TEXT,
    started_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    finished_at          TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS staging.oem_mapping_candidates (
    batch_id                  BIGINT NOT NULL REFERENCES staging.import_batches(id) ON DELETE CASCADE,
    oem_no_1_normalized       VARCHAR(100) NOT NULL,
    oem_no_1_display          VARCHAR(100) NOT NULL,
    candidate_status          VARCHAR(20) NOT NULL
                              CHECK (candidate_status IN ('candidate', 'pending', 'ambiguous')),
    candidate_product_id      BIGINT,
    candidate_mr1             VARCHAR(50),
    exact_product_match_count INTEGER NOT NULL DEFAULT 0,
    match_reason              TEXT NOT NULL,
    generated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (batch_id, oem_no_1_normalized),
    CHECK (
        (candidate_status = 'candidate' AND candidate_product_id IS NOT NULL AND candidate_mr1 IS NOT NULL AND exact_product_match_count = 1)
        OR (candidate_status = 'pending' AND candidate_product_id IS NULL AND candidate_mr1 IS NULL AND exact_product_match_count = 0)
        OR (candidate_status = 'ambiguous' AND candidate_product_id IS NULL AND candidate_mr1 IS NULL AND exact_product_match_count > 1)
    )
);

CREATE TABLE IF NOT EXISTS staging.oem_mr1_mapping_reviews (
    batch_id                  BIGINT NOT NULL REFERENCES staging.import_batches(id) ON DELETE CASCADE,
    oem_no_1_normalized       VARCHAR(100) NOT NULL,
    review_status             VARCHAR(20) NOT NULL
                              CHECK (review_status IN ('approved', 'rejected')),
    target_product_id         BIGINT,
    target_mr1                VARCHAR(50),
    review_reason             TEXT,
    reviewed_by               VARCHAR(100) NOT NULL,
    reviewed_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at                TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (batch_id, oem_no_1_normalized),
    CHECK (
        (review_status = 'approved' AND NULLIF(btrim(target_mr1), '') IS NOT NULL)
        OR (review_status = 'rejected' AND NULLIF(btrim(review_reason), '') IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS idx_oem_mapping_candidates_status
    ON staging.oem_mapping_candidates (batch_id, candidate_status);
CREATE INDEX IF NOT EXISTS idx_oem_mapping_reviews_status
    ON staging.oem_mr1_mapping_reviews (batch_id, review_status);

CREATE OR REPLACE FUNCTION staging.refresh_oem_mapping_candidates(p_batch_id BIGINT)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_total_oem_count BIGINT := 0;
    v_candidate_count BIGINT := 0;
    v_pending_count BIGINT := 0;
    v_ambiguous_count BIGINT := 0;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM staging.clean_runs
        WHERE batch_id = p_batch_id AND status = 'completed'
    ) THEN
        RAISE EXCEPTION '批次 % 尚未完成 clean 层，不能生成 OEM 到 MR.1 候选映射', p_batch_id;
    END IF;

    DELETE FROM staging.oem_mapping_candidates WHERE batch_id = p_batch_id;
    DELETE FROM staging.oem_mapping_candidate_runs WHERE batch_id = p_batch_id;
    INSERT INTO staging.oem_mapping_candidate_runs (batch_id, status)
    VALUES (p_batch_id, 'running');

    IF to_regclass('public.products') IS NULL THEN
        INSERT INTO staging.oem_mapping_candidates (
            batch_id, oem_no_1_normalized, oem_no_1_display, candidate_status,
            exact_product_match_count, match_reason)
        SELECT
            p_batch_id,
            a.oem_no_1_normalized,
            MIN(a.oem_no_1_display),
            'pending',
            0,
            '正式 products 表不存在，等待人工指定 MR.1'
        FROM (
            SELECT oem_no_1_normalized, oem_no_1_display
            FROM staging.product_specs_clean
            WHERE batch_id = p_batch_id
            UNION ALL
            SELECT oem_no_1_normalized, oem_no_1_display
            FROM staging.oem_numbers_clean
            WHERE batch_id = p_batch_id
            UNION ALL
            SELECT oem_no_1_normalized, COALESCE(NULLIF(btrim(oem_no_1_raw), ''), oem_no_1_normalized)
            FROM staging.applications_clean
            WHERE batch_id = p_batch_id
              AND oem_no_1_normalized IS NOT NULL
        ) a
        GROUP BY a.oem_no_1_normalized;
    ELSE
        EXECUTE $sql$
            WITH anchors AS (
                SELECT oem_no_1_normalized, MIN(oem_no_1_display) AS oem_no_1_display
                FROM (
                    SELECT oem_no_1_normalized, oem_no_1_display
                    FROM staging.product_specs_clean
                    WHERE batch_id = $1
                    UNION ALL
                    SELECT oem_no_1_normalized, oem_no_1_display
                    FROM staging.oem_numbers_clean
                    WHERE batch_id = $1
                    UNION ALL
                    SELECT oem_no_1_normalized, COALESCE(NULLIF(btrim(oem_no_1_raw), ''), oem_no_1_normalized)
                    FROM staging.applications_clean
                    WHERE batch_id = $1
                      AND oem_no_1_normalized IS NOT NULL
                ) source_anchors
                GROUP BY oem_no_1_normalized
            ),
            matches AS (
                SELECT
                    a.oem_no_1_normalized,
                    a.oem_no_1_display,
                    COUNT(p.id)::INTEGER AS exact_product_match_count,
                    MIN(p.id) AS candidate_product_id,
                    MIN(p.mr_1) AS candidate_mr1
                FROM anchors a
                LEFT JOIN public.products p
                    ON upper(regexp_replace(btrim(p.oem_no_normalized), '\s+', ' ', 'g')) = a.oem_no_1_normalized
                   AND p.is_discontinued = false
                   AND NULLIF(btrim(p.mr_1), '') IS NOT NULL
                GROUP BY a.oem_no_1_normalized, a.oem_no_1_display
            )
            INSERT INTO staging.oem_mapping_candidates (
                batch_id, oem_no_1_normalized, oem_no_1_display, candidate_status,
                candidate_product_id, candidate_mr1, exact_product_match_count, match_reason)
            SELECT
                $1,
                oem_no_1_normalized,
                oem_no_1_display,
                CASE
                    WHEN exact_product_match_count = 1 THEN 'candidate'
                    WHEN exact_product_match_count = 0 THEN 'pending'
                    ELSE 'ambiguous'
                END,
                CASE WHEN exact_product_match_count = 1 THEN candidate_product_id END,
                CASE WHEN exact_product_match_count = 1 THEN candidate_mr1 END,
                exact_product_match_count,
                CASE
                    WHEN exact_product_match_count = 1 THEN '唯一精确匹配正式产品，等待人工确认'
                    WHEN exact_product_match_count = 0 THEN '未找到精确匹配正式产品，等待人工指定 MR.1'
                    ELSE '匹配到多个正式产品，必须人工裁决'
                END
            FROM matches;
        $sql$ USING p_batch_id;
    END IF;

    SELECT
        COUNT(*),
        COUNT(*) FILTER (WHERE candidate_status = 'candidate'),
        COUNT(*) FILTER (WHERE candidate_status = 'pending'),
        COUNT(*) FILTER (WHERE candidate_status = 'ambiguous')
    INTO v_total_oem_count, v_candidate_count, v_pending_count, v_ambiguous_count
    FROM staging.oem_mapping_candidates
    WHERE batch_id = p_batch_id;

    UPDATE staging.oem_mapping_candidate_runs
    SET status = 'completed',
        total_oem_count = v_total_oem_count,
        candidate_count = v_candidate_count,
        pending_count = v_pending_count,
        ambiguous_count = v_ambiguous_count,
        finished_at = now()
    WHERE batch_id = p_batch_id;
EXCEPTION WHEN OTHERS THEN
    UPDATE staging.oem_mapping_candidate_runs
    SET status = 'failed', error_message = SQLERRM, finished_at = now()
    WHERE batch_id = p_batch_id;
    RAISE;
END;
$$;
