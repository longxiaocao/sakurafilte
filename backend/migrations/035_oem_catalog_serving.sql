-- 035_oem_catalog_serving.sql
-- 目的: 让 OEM 目录层具备"对外唯一且稳定"的锚点键，并派生公开层需要的分类维度。
--
-- WHY: 公开层（搜索索引 / URL / 详情）需要一个单一、URL 友好的身份键。
--      catalog.oem_products 现有唯一键是 oem_no_1_normalized（保留内部空格，
--      如 'FS 104'），但同一产品的源数据存在 'FS 104' 与 'FS104' 两种写法，
--      去空白后同键。实测 49,391 行中存在 641 组重复（624 组 2 行 + 17 组 3 行，
--      共 658 行冗余）。若直接以去空白键对外，会造成前台出现重复锚点。
--
-- 口径: catalog.normalize_oem_no1 = 去全部空白字符 + 统一大写。
--      与旧 public.products.oem_no_normalized 的历史口径一致（'SH 56212' → 'SH56212'），
--      因此公开层从旧体系切到 catalog 时不需要改写 URL 规则。
--
-- 影响范围: 仅 catalog schema（表结构 + 数据合并）。不触及 public 业务表。
-- 幂等性: 使用 IF NOT EXISTS / 临时表 ON COMMIT DROP，可重复执行。
-- 前置: 已按运维要求对生产库做全量 pg_dump 备份。

BEGIN;

-- ============================================================
-- 1. 规范化函数
-- ============================================================
CREATE OR REPLACE FUNCTION catalog.normalize_oem_no1(p_raw text)
RETURNS text
LANGUAGE sql
IMMUTABLE
PARALLEL SAFE
AS $$
    SELECT upper(regexp_replace(coalesce(p_raw, ''), '\s+', '', 'g'))
$$;

COMMENT ON FUNCTION catalog.normalize_oem_no1(text) IS
    'OEM NO 1 规范化口径：去全部空白字符 + 统一大写（''SH 56212'' → ''SH56212''）。'
    '与旧 public.products.oem_no_normalized 口径一致，作为公开层唯一身份键。';

-- ============================================================
-- 2. 锚点键列 oem_key（生成列，先建列不加唯一约束，去重后再加）
-- ============================================================
ALTER TABLE catalog.oem_products
    ADD COLUMN IF NOT EXISTS oem_key text
    GENERATED ALWAYS AS (catalog.normalize_oem_no1(oem_no_1_normalized)) STORED;

COMMENT ON COLUMN catalog.oem_products.oem_key IS
    '公开层唯一身份键（去空白+大写）。由 oem_no_1_normalized 生成，勿手工写入。';

-- ============================================================
-- 3. 锚点去重：同一 oem_key 的多行合并到"信息最全"的保留行
--    保留优先级: 有规格 > 交叉号多 > 机型适配多 > id 小
-- ============================================================
CREATE TEMP TABLE tmp_oem_keep ON COMMIT DROP AS
WITH stats AS (
    SELECT p.id,
           p.oem_key,
           (p.spec_payload <> '{}'::jsonb) AS has_spec,
           (p.product_name_candidates <> '[]'::jsonb) AS has_name,
           (SELECT count(*) FROM catalog.oem_cross_references x WHERE x.oem_product_id = p.id) AS xref_count,
           (SELECT count(*) FROM catalog.oem_machine_applications a WHERE a.oem_product_id = p.id) AS app_count
    FROM catalog.oem_products p
), ranked AS (
    SELECT id,
           first_value(id) OVER (
               PARTITION BY oem_key
               ORDER BY has_spec DESC, has_name DESC, xref_count DESC, app_count DESC, id
           ) AS keep_id
    FROM stats
)
SELECT id, keep_id FROM ranked WHERE id <> keep_id;

-- 3.1 交叉号重新挂靠到保留行
UPDATE catalog.oem_cross_references x
SET oem_product_id = k.keep_id
FROM tmp_oem_keep k
WHERE x.oem_product_id = k.id;

-- 3.2 机型适配重新挂靠到保留行
UPDATE catalog.oem_machine_applications a
SET oem_product_id = k.keep_id
FROM tmp_oem_keep k
WHERE a.oem_product_id = k.id;

-- 3.3 删除被合并的冗余锚点行（子表已改挂，ON DELETE CASCADE 不会误删）
DELETE FROM catalog.oem_products p
USING tmp_oem_keep k
WHERE p.id = k.id;

-- ============================================================
-- 4. 合并后清理交叉号重复：同 (product, name, brand, oem3) 多行 → 并集 source_row_nos
--    WHY: catalog.oem_cross_references 的 UNIQUE 含 oem_product_id，
--         改挂后原本分属两行的同名交叉号会撞唯一约束。
-- ============================================================
CREATE TEMP TABLE tmp_xref_grp ON COMMIT DROP AS
SELECT oem_product_id, product_name_key, oem_brand_key, oem_no_3_key,
       min(id) AS keep_id,
       array_agg(source_row_nos) AS all_rows,
       sum(merged_source_row_count) AS total_merged,
       count(*) AS row_count
FROM catalog.oem_cross_references
GROUP BY oem_product_id, product_name_key, oem_brand_key, oem_no_3_key;

UPDATE catalog.oem_cross_references x
SET source_row_nos = m.rows_union,
    merged_source_row_count = m.total_merged
FROM (
    SELECT g.keep_id, g.total_merged,
           (SELECT array_agg(DISTINCT e ORDER BY e) FROM unnest(g.all_rows) AS e) AS rows_union
    FROM tmp_xref_grp g
    WHERE g.row_count > 1
) m
WHERE x.id = m.keep_id;

DELETE FROM catalog.oem_cross_references x
USING tmp_xref_grp g
WHERE x.oem_product_id = g.oem_product_id
  AND x.product_name_key = g.product_name_key
  AND x.oem_brand_key = g.oem_brand_key
  AND x.oem_no_3_key = g.oem_no_3_key
  AND g.row_count > 1
  AND x.id <> g.keep_id;

-- ============================================================
-- 5. 唯一索引（去重完成后才可建立）
-- ============================================================
CREATE UNIQUE INDEX IF NOT EXISTS uq_oem_products_oem_key
    ON catalog.oem_products (oem_key);

-- ============================================================
-- 6. 派生分类 product_category
--    WHY: catalog 没有 type 字段，公开层需要分类维度做筛选/导航。
--    来源: product_name_candidates 的候选产品名，按关键词归类。
--    规则顺序: cabin → air → fuel → oil → hydraulic → others（先匹配先赢）。
--    未命中或候选为空 → others（实测 49,391 行中 39,372 行无候选名，占 79.7%）。
-- ============================================================
ALTER TABLE catalog.oem_products
    ADD COLUMN IF NOT EXISTS product_category text NOT NULL DEFAULT 'others';

COMMENT ON COLUMN catalog.oem_products.product_category IS
    '派生分类（cabin/air/fuel/oil/hydraulic/others）。由 product_name_candidates 关键词归类，未知归 others。';

UPDATE catalog.oem_products p
SET product_category = c.category
FROM (
    SELECT id,
           CASE
               WHEN has_cabin THEN 'cabin'
               WHEN has_air   THEN 'air'
               WHEN has_fuel  THEN 'fuel'
               WHEN has_oil   THEN 'oil'
               WHEN has_hyd   THEN 'hydraulic'
               ELSE 'others'
           END AS category
    FROM (
        SELECT id,
               EXISTS (SELECT 1 FROM jsonb_array_elements_text(product_name_candidates) v
                       WHERE lower(v) LIKE '%cabin%') AS has_cabin,
               EXISTS (SELECT 1 FROM jsonb_array_elements_text(product_name_candidates) v
                       WHERE lower(v) LIKE '%air%' OR lower(v) LIKE '%a/c%') AS has_air,
               EXISTS (SELECT 1 FROM jsonb_array_elements_text(product_name_candidates) v
                       WHERE lower(v) LIKE '%fuel%' OR lower(v) LIKE '%petrol%'
                          OR lower(v) LIKE '%urea%' OR lower(v) LIKE '%gas filter%') AS has_fuel,
               EXISTS (SELECT 1 FROM jsonb_array_elements_text(product_name_candidates) v
                       WHERE lower(v) LIKE '%oil%' OR lower(v) LIKE '%lube%') AS has_oil,
               EXISTS (SELECT 1 FROM jsonb_array_elements_text(product_name_candidates) v
                       WHERE lower(v) LIKE '%hydraulic%' OR lower(v) LIKE '%pressure%'
                          OR lower(v) LIKE '%return%' OR lower(v) LIKE '%suction%'
                          OR lower(v) LIKE '%strainer%' OR lower(v) LIKE '%cartridge%') AS has_hyd
        FROM catalog.oem_products
    ) flags
) c
WHERE p.id = c.id
  AND p.product_category IS DISTINCT FROM c.category;

CREATE INDEX IF NOT EXISTS idx_oem_products_category
    ON catalog.oem_products (product_category);

-- ============================================================
-- 7. 索引补充（公开层按 oem_key 检索、按分类筛选）
-- ============================================================
CREATE INDEX IF NOT EXISTS idx_oem_products_display_trgm
    ON catalog.oem_products USING gin (oem_no_1_display gin_trgm_ops);

-- ============================================================
-- 8. 重写发布函数：锚点按"规范化键"收敛
-- WHY: 原函数按 oem_no_1_normalized（保留空格）分组，源数据里 'FS 104' 与 'FS104'
--      会被当成两个锚点分别插入。第 5 步建立 uq_oem_products_oem_key 后，
--      再次发布同一批次会插入第二行并撞唯一索引，导致发布直接失败。
--      修正为按 catalog.normalize_oem_no1() 收敛锚点，并改以 oem_key 作冲突目标。
-- 兼容: 存储的 oem_no_1_normalized 取该键下的一个代表值（MIN），
--      oem_no_1_display 仍取 MIN 展示值；对已存在的行走 ON CONFLICT 更新。
-- ============================================================
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
        SELECT catalog.normalize_oem_no1(s.oem_no_1_normalized) AS oem_key,
               MIN(s.oem_no_1_normalized) AS oem_no_1_normalized,
               MIN(s.oem_no_1_display) AS oem_no_1_display
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
        ) s
        GROUP BY catalog.normalize_oem_no1(s.oem_no_1_normalized)
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
    -- WHY 去重: 归一到 oem_key 后，'FS 104' 与 'FS104' 两个 staging 规格行会同时命中
    --          同一锚点，导致 INSERT 提议两行相同 oem_key（ON CONFLICT 不允许二次命中）。
    --          用 DISTINCT ON 每键只取一行（键内取 oem_no_1_normalized 最小者）。
    LEFT JOIN (
        SELECT DISTINCT ON (catalog.normalize_oem_no1(oem_no_1_normalized))
               catalog.normalize_oem_no1(oem_no_1_normalized) AS oem_key,
               product_name_candidates, spec_payload, conflict_field_count
        FROM staging.product_specs_clean
        WHERE batch_id = p_batch_id
        ORDER BY catalog.normalize_oem_no1(oem_no_1_normalized), oem_no_1_normalized
    ) s ON s.oem_key = a.oem_key
    ON CONFLICT (oem_key) DO UPDATE
    SET oem_no_1_normalized = EXCLUDED.oem_no_1_normalized,
        oem_no_1_display = EXCLUDED.oem_no_1_display,
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
    -- WHY DISTINCT ON: 锚点收敛后，同一 OEM 号的两种写法会挂到同一产品，
    --   其同名交叉号（product/brand/oem3 相同）会撞 uq 约束；每键只保留
    --   merged_source_row_count 最大的一行，避免发布中断。
    SELECT DISTINCT ON (p.id, x.product_name_key, x.oem_brand_key, x.oem_no_3_key)
        p.id, p_batch_id, x.product_name_key, x.product_name_1,
        x.oem_brand_key, x.oem_brand, x.oem_no_3_key, x.oem_no_3,
        x.source_row_nos, x.merged_source_row_count
    FROM staging.oem_numbers_clean x
    JOIN catalog.oem_products p
      ON p.oem_key = catalog.normalize_oem_no1(x.oem_no_1_normalized)
    WHERE x.batch_id = p_batch_id
    ORDER BY p.id, x.product_name_key, x.oem_brand_key, x.oem_no_3_key,
             x.merged_source_row_count DESC, x.oem_no_1_normalized;

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
    JOIN catalog.oem_products p
      ON p.oem_key = catalog.normalize_oem_no1(a.oem_no_1_normalized)
    WHERE a.batch_id = p_batch_id
      AND a.oem_no_1_normalized IS NOT NULL;

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

COMMENT ON FUNCTION catalog.publish_oem_clean_batch(bigint) IS
    'OEM 目录发布：按 catalog.normalize_oem_no1() 收敛锚点，冲突目标为 oem_key。'
    '相较 033 版本修复了同一 OEM 号多种写法导致重复锚点 / 违反 oem_key 唯一索引的问题。';

COMMIT;