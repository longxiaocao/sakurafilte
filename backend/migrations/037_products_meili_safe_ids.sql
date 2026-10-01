-- idempotent 可重跑: 约束操作用 DROP ... IF EXISTS 后重建 + UPDATE 按值净化, 重复执行结果一致。
-- 037: 公开层 Meili 文档 ID 安全化 + oem_2 回填
-- WHY:
--   1) Meilisearch 文档主键仅允许 [A-Za-z0-9_-]（≤511 字节），而 036 令 mr_1 = OEM 锚点键，
--      实测 325 行含 / . " + 等符号（如 CR800/3、LVO3/4"ALU）。全量重建时含这类键的
--      1000 条批次被整批拒绝（invalid_document_id），索引无法建成。
--      故将 mr_1 净化为 Meili 安全形态（非法字符 → '-'）。实测净化后 48,733 键零冲突。
--   2) 旧公开层 products.oem_2 始终有值，前端结果卡片标签（oemNo3 || oem2）与
--      /api/public/search/batch-oem 第 3 段兜底依赖该字段；036 切库后为 NULL，回填为主显示编号。

BEGIN;

-- 1. mr_1 净化为 Meili 文档 ID 安全形态
UPDATE public.products
SET mr_1 = regexp_replace(upper(mr_1), '[^A-Za-z0-9_-]', '-', 'g')
WHERE mr_1 IS NOT NULL AND mr_1 ~ '[^A-Za-z0-9_-]';

-- 2. 约束收紧为 Meili 可索引字符集（不变量固化，避免后续导入再次产出不可索引主键）
ALTER TABLE public.products DROP CONSTRAINT IF EXISTS chk_mr_1_format;
ALTER TABLE public.products ADD CONSTRAINT chk_mr_1_format
    CHECK (mr_1 IS NULL OR mr_1::text ~ '^[A-Za-z0-9_-]{1,50}$');

-- 3. oem_2 回填（旧契约要求有值）
UPDATE public.products
SET oem_2 = oem_no_display
WHERE oem_2 IS NULL AND oem_no_display IS NOT NULL;

COMMIT;
