-- 一次性脚本,不可重跑: 含 TRUNCATE 业务表 (cross_references/products/machine_applications 等),
-- 重复执行会清空已导入的公开层数据; 仅在 catalog → public 单次切换时执行。
-- 036_catalog_to_public_cutover.sql
-- 目的: 把已发布的 OEM 目录（catalog schema，48,733 个锚点）装载为公开层数据，
--      替换旧的 public 业务数据（9,376 产品）。公开站点、搜索、详情、对比、SEO 全部照旧读取 public 表。
--
-- 背景与口径:
--   * OEM NO 1 规范化键（catalog.oem_key，去空白+大写）是目录的唯一身份锚点（ADR #33/#34）。
--   * 旧 public 数据里 products.mr_1 与 products.oem_no_normalized 100% 相同，
--     即 mr_1 事实上一直是"OEM 号"字段。故本次直接令 mr_1 = oem_key，
--     搜索索引主键（Meili 属性名 mr_1）与全部既有代码路径保持不变，零代码改动即可检索。
--   * catalog.oem_mr1_mappings 为 0 条是设计预期（客户 MR.1 编码未定稿），不参与本次装载。
--
-- 三处旧约束已不适用于新数据模型，必须放宽（见第 2/3/4 步）：
--   1) chk_mr_1_format: 新键含 '-' '.' '/' '_'（1,383 个），原规则仅允许字母数字。
--   2) uq_xrefs_brand_oem3: 旧模型要求一个 OEM3 全局只属于一个产品；
--      新模型中同一竞品 OEM3 可交叉引用多个自有产品（实测 3,815 组冲突）。
--   3) uq_apps_product_brand_model: 旧模型要求 (产品,品牌,型号) 唯一；
--      新模型同组合可有多条适配明细（实测 709,843 → 560,398，83,708 组冲突）。
--
-- 规格映射: catalog.spec_payload 的键与 products 列一一对应
--   d1→d1_mm / d2→d2_mm / d3→d3_mm / h1→h1_mm / h2→h2_mm / h3→h3_mm
--   media→media / seal_material→sealing_material / efficiency_1→efficiency_1 / efficiency_2→efficiency_2
--   temperature_range→temp_range / δ_collapse_pressure→collapse_pressure_bar
--   no_bypass_valves→no_bypass_valves / no_check_valves→no_check_valves / bypass_pressure→bypass_pressure
--   thread→d7_thread / d8→d8_thread / remark→remark
--   数值列同时写入 *_raw 保留原文（如 '96.0 mm'），数值列做前缀数字提取。
--
-- 幂等性: 清空后重建，可重复执行（前提是 catalog 层数据已就绪且未变）。
-- 前置: 已按运维要求完成生产库全量 pg_dump 备份。

BEGIN;

-- ============================================================
-- 1. 规格数值解析辅助函数
-- ============================================================
-- 从 '96.0 mm' / '25 bar' 这类文本中取前导数字；无数字则返回 NULL。
CREATE OR REPLACE FUNCTION public.parse_spec_numeric(p_text text)
RETURNS numeric
LANGUAGE sql IMMUTABLE PARALLEL SAFE
AS $$
    SELECT CASE
        WHEN p_text IS NULL THEN NULL
        WHEN p_text ~ '[0-9]' THEN (regexp_match(p_text, '[0-9]+(?:\.[0-9]+)?'))[1]::numeric
        ELSE NULL
    END
$$;

COMMENT ON FUNCTION public.parse_spec_numeric(text) IS
    '规格文本取前导数字（''96.0 mm'' → 96.0）；无数字返回 NULL。用于 catalog.spec_payload 装载。';

-- ============================================================
-- 2. 放宽 mr_1 格式约束（承接含 - . / _ 的 OEM 锚点键）
-- ============================================================
-- WHY 放开字符集: mr_1 现承载 OEM 锚点键，实测键中出现 - / . " + 五类符号
--   （例 'LVO3/4"ALU'、'1S24V-250'、'025.0227.1'），原 '^[A-Za-z0-9]{1,50}$' 会拒绝 1,383 条。
--   正则中 '-' 置于字符组末尾，避免被解析为区间。
ALTER TABLE public.products DROP CONSTRAINT IF EXISTS chk_mr_1_format;
ALTER TABLE public.products ADD CONSTRAINT chk_mr_1_format
    CHECK (mr_1 IS NULL OR mr_1::text ~ '^[A-Za-z0-9/._"+-]{1,50}$');

-- ============================================================
-- 3. 取消交叉号全局唯一（同一 OEM3 可交叉引用多个自有产品）
-- ============================================================
DROP INDEX IF EXISTS public.uq_xrefs_brand_oem3;

-- ============================================================
-- 4. 取消机型适配 (产品,品牌,型号) 唯一（同组合可有多条适配明细）
-- ============================================================
DROP INDEX IF EXISTS public.uq_apps_product_brand_model;

-- ============================================================
-- 5. 分类字典补充 hydraulic（新目录派生分类之一）
-- ============================================================
INSERT INTO public.dict_type (type, sort_order, created_at, updated_at)
VALUES ('hydraulic', 7, now(), now())
ON CONFLICT (type) DO NOTHING;

-- ============================================================
-- 6. 清空旧业务数据与检索补偿队列
-- ============================================================
TRUNCATE TABLE public.cross_references,
               public.machine_applications,
               public.product_images,
               public.products
    RESTART IDENTITY;

TRUNCATE TABLE public.search_index_pending,
               public.search_index_dead_letter
    RESTART IDENTITY;

-- ============================================================
-- 7. 装载产品锚点
-- ============================================================
INSERT INTO public.products (
    oem_no_normalized, oem_no_display, mr_1, type,
    d1_mm, d2_mm, d3_mm, h1_mm, h2_mm, h3_mm,
    d1_mm_raw, d2_mm_raw, d3_mm_raw, h1_mm_raw, h2_mm_raw, h3_mm_raw,
    media, sealing_material, efficiency_1, efficiency_2, temp_range,
    collapse_pressure_bar, collapse_pressure_bar_raw,
    no_bypass_valves, no_bypass_valves_raw,
    no_check_valves, no_check_valves_raw,
    d7_thread, d8_thread, remark,
    is_published, is_discontinued, image_status,
    created_at, updated_at)
SELECT
    c.oem_key,
    c.oem_no_1_display,
    c.oem_key,
    c.product_category,
    public.parse_spec_numeric(c.spec_payload ->> 'd1'),
    public.parse_spec_numeric(c.spec_payload ->> 'd2'),
    public.parse_spec_numeric(c.spec_payload ->> 'd3'),
    public.parse_spec_numeric(c.spec_payload ->> 'h1'),
    public.parse_spec_numeric(c.spec_payload ->> 'h2'),
    public.parse_spec_numeric(c.spec_payload ->> 'h3'),
    c.spec_payload ->> 'd1',
    c.spec_payload ->> 'd2',
    c.spec_payload ->> 'd3',
    c.spec_payload ->> 'h1',
    c.spec_payload ->> 'h2',
    c.spec_payload ->> 'h3',
    c.spec_payload ->> 'media',
    c.spec_payload ->> 'seal_material',
    c.spec_payload ->> 'efficiency_1',
    c.spec_payload ->> 'efficiency_2',
    c.spec_payload ->> 'temperature_range',
    public.parse_spec_numeric(c.spec_payload ->> 'δ_collapse_pressure'),
    c.spec_payload ->> 'δ_collapse_pressure',
    public.parse_spec_numeric(c.spec_payload ->> 'no_bypass_valves')::int,
    c.spec_payload ->> 'no_bypass_valves',
    public.parse_spec_numeric(c.spec_payload ->> 'no_check_valves')::int,
    c.spec_payload ->> 'no_check_valves',
    COALESCE(c.spec_payload ->> 'thread', c.spec_payload ->> 'd7'),
    c.spec_payload ->> 'd8',
    c.spec_payload ->> 'remark',
    true,
    false,
    'pending',
    c.created_at,
    c.updated_at
FROM catalog.oem_products c;

-- ============================================================
-- 8. 装载交叉号
--    新模型无 sort_order / is_published 维护位，取默认值 0 / true；
--    machine_type 无来源，按 CHECK 允许值取 'others'。
-- ============================================================
INSERT INTO public.cross_references (
    product_id, product_name_1, oem_brand, oem_no_3, oem_2,
    sort_order, is_published, is_discontinued, machine_type, created_at)
SELECT
    p.id,
    x.product_name_1,
    COALESCE(NULLIF(btrim(x.oem_brand), ''), x.oem_brand_key),
    COALESCE(NULLIF(btrim(x.oem_no_3), ''), x.oem_no_3_key),
    NULL,
    0, true, false, 'others', x.created_at
FROM catalog.oem_cross_references x
JOIN catalog.oem_products c ON c.id = x.oem_product_id
JOIN public.products p ON p.oem_no_normalized = c.oem_key;

-- ============================================================
-- 9. 装载机型适配
--    is_ongoing 无来源，取 false（非在产，避免前台误标"在产"）。
-- ============================================================
INSERT INTO public.machine_applications (
    product_id, machine_brand, machine_model, model_name,
    engine_brand, engine_type, engine_energy, power, engine_model,
    is_ongoing, is_discontinued, machine_category, created_at)
SELECT
    p.id,
    a.machine_brand, a.machine_model, a.model_name,
    a.engine_brand, a.engine_type, a.engine_energy, a.power, a.engine_model,
    false, false, 'others', a.created_at
FROM catalog.oem_machine_applications a
JOIN catalog.oem_products c ON c.id = a.oem_product_id
JOIN public.products p ON p.oem_no_normalized = c.oem_key;

COMMIT;