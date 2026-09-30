-- 038_supplement_product_category.sql
-- 目的: 修复 035 派生分类覆盖率过低的问题（others 占 82.9%），并把结果传播到公开层。
--
-- WHY: 035 仅以 catalog.oem_products.product_name_candidates 为唯一来源做关键词归类，
--      但实测 48,733 行中仅 9,376 行有候选名（19.2%），其余无处归类 → 全部落 others。
--      而 catalog.oem_cross_references.product_name_1 覆盖 529,461/529,499 = 99.99%，
--      是唯一高杠杆来源，可作为主判据（权重 2），候选名作为辅判据（权重 1）。
--      （另两条候选来源已被数据证伪: spec_payload->>'remark' 与候选名重叠 9,376/9,377，
--        无增量；spec_payload->>'media' 仅 7.4% 且无分类语义。）
--
-- 规则集 v2（经生产数据干跑验证）:
--   1) 主判据: 交叉引用名 product_name_1 关键词计数 × 2
--   2) 辅判据: 候选名 product_name_candidates 关键词计数 × 1
--   3) 按 (分数 DESC, 类别优先序 ASC) 取一；优先序 cabin → air → fuel → oil → hydraulic
--   4) 关键词全未命中 → 按 OEM NO 1 展示值首段前缀兜底（见下表）
--   5) 前缀也未识别 → others
--   WHY 删掉 035 的 '%cartridge%'/'%element%' 归 hydraulic: 二者是通用形态词，
--      会把空气/机油滤芯误判为液压；改为仅保留真正的液压语义词。
--   WHY oil 需排除 '%air/oil%': 该类是空气/机油复合滤，主形态仍是 air。
--
-- 前缀兜底精度实测（P1 决定性证据，合计 26,618 行中仅 26 例冲突 = 0.098%）:
--   SH→hydraulic 12,633/12,635(100.0%)   SA→air 6,752/6,755(100.0%)   OS→air 350/350(100.0%)
--   OA→air 270/270(100.0%)               SO→oil 1,590/1,594(99.7%)     SN→fuel 2,290/2,295(99.8%)
--   SC→cabin 2,611/2,618(99.7%)          SI→air 96/101(95.0%)
--
-- 效果: others 40,378 → 9,908（82.9% → 20.3%）
--       air 2,091→16,428 / hydraulic 5,693→14,218 / fuel 176→3,877 / cabin 310→2,674 / oil 85→1,628
--
-- 分类维度保持 6 类不变: 前端 dict_type、AdminTypesView.FIXED_TYPES、i18n 文案、
--   契约测试均硬编码 6 值；新增类别（liquid/carbon/breather 等）属独立产品决策，不在本迁移范围。
--
-- 幂等性: 函数 CREATE OR REPLACE + UPDATE ... IS DISTINCT FROM，可重复执行。
-- 不更新 updated_at: 与 035 口径一致（派生列重算不代表业务变更）。
-- 前置: 已导出可回滚快照 _backups/category_before_038.csv（48,733 行）。

BEGIN;

-- ============================================================
-- 1. 重算函数（返回实际变更行数，便于审计）
-- ============================================================
CREATE OR REPLACE FUNCTION catalog.refresh_product_categories()
RETURNS bigint
LANGUAGE plpgsql
AS $fn$
DECLARE
    v_changed bigint := 0;
BEGIN
    WITH kw AS (
        SELECT x.oem_product_id AS pid,
               count(*) FILTER (WHERE lower(x.product_name_1) LIKE '%cabin%') AS x_cabin,
               count(*) FILTER (WHERE lower(x.product_name_1) LIKE '%air%'
                                  OR lower(x.product_name_1) LIKE '%a/c%') AS x_air,
               count(*) FILTER (WHERE lower(x.product_name_1) LIKE '%fuel%'
                                  OR lower(x.product_name_1) LIKE '%petrol%'
                                  OR lower(x.product_name_1) LIKE '%urea%'
                                  OR lower(x.product_name_1) LIKE '%gas filter%') AS x_fuel,
               count(*) FILTER (WHERE (lower(x.product_name_1) LIKE '%oil%'
                                  OR lower(x.product_name_1) LIKE '%lube%')
                                  AND lower(x.product_name_1) NOT LIKE '%air/oil%') AS x_oil,
               count(*) FILTER (WHERE lower(x.product_name_1) LIKE '%hydraulic%'
                                  OR lower(x.product_name_1) LIKE '%pressure%'
                                  OR lower(x.product_name_1) LIKE '%return%'
                                  OR lower(x.product_name_1) LIKE '%suction%'
                                  OR lower(x.product_name_1) LIKE '%strainer%') AS x_hyd
        FROM catalog.oem_cross_references x
        GROUP BY x.oem_product_id
    ),
    -- 候选名一次展开后按类别计数（避免 5 次 jsonb_array_elements_text 重复展开）
    cand AS (
        SELECT p.id AS pid,
               count(*) FILTER (WHERE lower(v) LIKE '%cabin%') AS n_cabin,
               count(*) FILTER (WHERE lower(v) LIKE '%air%'
                                  OR lower(v) LIKE '%a/c%') AS n_air,
               count(*) FILTER (WHERE lower(v) LIKE '%fuel%'
                                  OR lower(v) LIKE '%petrol%'
                                  OR lower(v) LIKE '%urea%'
                                  OR lower(v) LIKE '%gas filter%') AS n_fuel,
               count(*) FILTER (WHERE (lower(v) LIKE '%oil%'
                                  OR lower(v) LIKE '%lube%')
                                  AND lower(v) NOT LIKE '%air/oil%') AS n_oil,
               count(*) FILTER (WHERE lower(v) LIKE '%hydraulic%'
                                  OR lower(v) LIKE '%pressure%'
                                  OR lower(v) LIKE '%return%'
                                  OR lower(v) LIKE '%suction%'
                                  OR lower(v) LIKE '%strainer%') AS n_hyd
        FROM catalog.oem_products p
        LEFT JOIN LATERAL jsonb_array_elements_text(p.product_name_candidates) AS v ON true
        GROUP BY p.id
    ),
    picked AS (
        SELECT p.id AS pid,
               COALESCE(kw_pick.category, prefix.category) AS new_category
        FROM catalog.oem_products p
        LEFT JOIN kw ON kw.pid = p.id
        LEFT JOIN cand ON cand.pid = p.id
        CROSS JOIN LATERAL (
            SELECT coalesce(kw.x_cabin, 0) * 2 + coalesce(cand.n_cabin, 0) AS s_cabin,
                   coalesce(kw.x_air, 0)   * 2 + coalesce(cand.n_air, 0)   AS s_air,
                   coalesce(kw.x_fuel, 0)  * 2 + coalesce(cand.n_fuel, 0)  AS s_fuel,
                   coalesce(kw.x_oil, 0)   * 2 + coalesce(cand.n_oil, 0)   AS s_oil,
                   coalesce(kw.x_hyd, 0)   * 2 + coalesce(cand.n_hyd, 0)   AS s_hyd
        ) s2
        LEFT JOIN LATERAL (
            SELECT v.category
            FROM (VALUES ('cabin', s2.s_cabin, 1),
                         ('air', s2.s_air, 2),
                         ('fuel', s2.s_fuel, 3),
                         ('oil', s2.s_oil, 4),
                         ('hydraulic', s2.s_hyd, 5)) AS v(category, score, pref)
            WHERE v.score > 0
            ORDER BY v.score DESC, v.pref ASC
            LIMIT 1
        ) kw_pick ON true
        -- 前缀兜底: 仅在关键词全未命中时生效（COALESCE 的第二分支）
        CROSS JOIN LATERAL (
            SELECT CASE split_part(upper(p.oem_no_1_display), ' ', 1)
                       WHEN 'SH' THEN 'hydraulic'
                       WHEN 'SA' THEN 'air'
                       WHEN 'SI' THEN 'air'
                       WHEN 'OS' THEN 'air'
                       WHEN 'OA' THEN 'air'
                       WHEN 'SC' THEN 'cabin'
                       WHEN 'SN' THEN 'fuel'
                       WHEN 'SO' THEN 'oil'
                       ELSE 'others'
                   END AS category
        ) prefix
    ),
    upd AS (
        UPDATE catalog.oem_products p
        SET product_category = pk.new_category
        FROM picked pk
        WHERE p.id = pk.pid
          AND p.product_category IS DISTINCT FROM pk.new_category
        RETURNING 1
    )
    SELECT count(*) INTO v_changed FROM upd;

    RETURN v_changed;
END;
$fn$;

COMMENT ON FUNCTION catalog.refresh_product_categories() IS
    '重算 catalog.oem_products.product_category。规则: 交叉引用名关键词×2 + 候选名关键词×1 打分，'
    '关键词全未命中则按 OEM NO 1 首段前缀兜底，仍未识别归 others。返回变更行数。幂等。';

-- ============================================================
-- 2. 执行一次重算
-- ============================================================
SELECT catalog.refresh_product_categories() AS changed_rows;

-- ============================================================
-- 3. 传播到公开层（Meili type 过滤与前端分类导航的依据）
-- ============================================================
UPDATE public.products p
SET type = c.product_category
FROM catalog.oem_products c
WHERE p.mr_1 = c.oem_key
  AND p.type IS DISTINCT FROM c.product_category;

COMMIT;
