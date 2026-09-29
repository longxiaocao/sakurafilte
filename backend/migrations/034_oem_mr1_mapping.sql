-- OEM NO 1 到 MR.1 的可追溯映射：复用 uq_catalog_oem_mr1_active_value 保证生效 MR.1 全局唯一。
CREATE OR REPLACE FUNCTION catalog.set_oem_mr1_mapping(
    p_oem_no_1_normalized TEXT,
    p_mr1 TEXT,
    p_assigned_by TEXT,
    p_change_reason TEXT DEFAULT NULL)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_oem_product_id BIGINT;
    v_active_mr1 TEXT;
BEGIN
    SELECT id INTO v_oem_product_id
    FROM catalog.oem_products
    WHERE oem_no_1_normalized = p_oem_no_1_normalized
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION '未找到 OEM NO 1: %', p_oem_no_1_normalized USING ERRCODE = 'P0002';
    END IF;

    IF p_mr1 IS NOT NULL AND EXISTS (
        SELECT 1 FROM catalog.oem_mr1_mappings
        WHERE mr1 = p_mr1 AND ended_at IS NULL AND oem_product_id <> v_oem_product_id
    ) THEN
        RAISE EXCEPTION 'MR.1 % 已关联到其他 OEM NO 1', p_mr1 USING ERRCODE = '23505';
    END IF;

    SELECT mr1 INTO v_active_mr1
    FROM catalog.oem_mr1_mappings
    WHERE oem_product_id = v_oem_product_id AND ended_at IS NULL
    FOR UPDATE;

    IF v_active_mr1 IS NOT DISTINCT FROM p_mr1 THEN
        RETURN;
    END IF;

    UPDATE catalog.oem_mr1_mappings
    SET ended_at = now()
    WHERE oem_product_id = v_oem_product_id AND ended_at IS NULL;

    IF p_mr1 IS NOT NULL THEN
        INSERT INTO catalog.oem_mr1_mappings (oem_product_id, mr1, assigned_by, change_reason)
        VALUES (v_oem_product_id, p_mr1, p_assigned_by, NULLIF(btrim(p_change_reason), ''));
    END IF;
END;
$$;
