-- V24-F73: 修复 migration 历史与实际 schema 不一致 (重写版 v2)
--   根因: __EFMigrationsHistory 标记 20260717125735_AddMr1PrimaryKeyAndV2Fields 为已应用,
--         但实际 DB schema 缺失该 migration 添加的所有列/索引/约束
--   修复策略: 多段独立事务 (单段失败不影响其他), 幂等 SQL (IF NOT EXISTS / IF EXISTS)
--   已知限制: cross_references 有 71,395 组重复 (oem_brand, oem_no_3), 唯一索引 uq_xrefs_brand_oem3 跳过
--   备份: spike-test/_e2e_audit/db_backup_20260718_152920.sql (schema-only)

\echo '===== 1. cross_references 表修复 ====='

-- 1.1 添加缺失列
ALTER TABLE cross_references ADD COLUMN IF NOT EXISTS sort_order integer NOT NULL DEFAULT 0;
ALTER TABLE cross_references ADD COLUMN IF NOT EXISTS machine_type character varying(50) NULL DEFAULT 'others';
ALTER TABLE cross_references ADD COLUMN IF NOT EXISTS is_published boolean NOT NULL DEFAULT true;
ALTER TABLE cross_references ADD COLUMN IF NOT EXISTS oem_2 character varying(100) NULL;

-- 1.2 修改列类型/约束 (DO 块保护)
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name='cross_references' AND column_name='oem_no_3'
                 AND (character_maximum_length IS DISTINCT FROM 200 OR is_nullable='YES')) THEN
        ALTER TABLE cross_references ALTER COLUMN oem_no_3 TYPE character varying(200);
        ALTER TABLE cross_references ALTER COLUMN oem_no_3 SET DEFAULT '';
        ALTER TABLE cross_references ALTER COLUMN oem_no_3 SET NOT NULL;
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name='cross_references' AND column_name='oem_brand' AND is_nullable='YES') THEN
        ALTER TABLE cross_references ALTER COLUMN oem_brand SET DEFAULT '';
        ALTER TABLE cross_references ALTER COLUMN oem_brand SET NOT NULL;
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name='cross_references' AND column_name='is_discontinued' AND is_nullable='YES') THEN
        ALTER TABLE cross_references ALTER COLUMN is_discontinued SET DEFAULT false;
        ALTER TABLE cross_references ALTER COLUMN is_discontinued SET NOT NULL;
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name='cross_references' AND column_name='product_id' AND is_nullable='YES') THEN
        ALTER TABLE cross_references ALTER COLUMN product_id SET NOT NULL;
    END IF;
END $$;

-- 1.3 删除旧索引
DROP INDEX IF EXISTS ix_cross_references_oem_brand_oem_no_3;
DROP INDEX IF EXISTS idx_xref_brand_no;
DROP INDEX IF EXISTS uq_xrefs_product_brand_no;

-- 1.4 创建新索引
CREATE INDEX IF NOT EXISTS idx_xrefs_brand_oem3_sort
    ON cross_references (oem_brand, sort_order, oem_no_3)
    WHERE is_discontinued = false AND is_published = true;

-- 1.5 唯一索引 uq_xrefs_brand_oem3: 检查重复, 有则跳过
DO $$
DECLARE dup_count integer;
BEGIN
    SELECT COUNT(*) INTO dup_count FROM (
        SELECT oem_brand, oem_no_3 FROM cross_references
        WHERE is_discontinued = false
        GROUP BY oem_brand, oem_no_3 HAVING COUNT(*) > 1
    ) t;

    IF dup_count = 0 THEN
        CREATE UNIQUE INDEX IF NOT EXISTS uq_xrefs_brand_oem3
            ON cross_references (oem_brand, oem_no_3)
            WHERE is_discontinued = false;
        RAISE NOTICE 'uq_xrefs_brand_oem3 唯一索引已创建';
    ELSE
        RAISE NOTICE '跳过 uq_xrefs_brand_oem3: 发现 % 组重复 (oem_brand, oem_no_3), 需先清理重复数据', dup_count;
    END IF;
END $$;

-- 1.6 check constraint
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='chk_xref_machine_type') THEN
        ALTER TABLE cross_references
            ADD CONSTRAINT chk_xref_machine_type
            CHECK (machine_type IS NULL OR machine_type IN ('agriculture', 'commercial', 'construction', 'industrial', 'others'));
    END IF;
END $$;

\echo '===== 2. products 表修复 ====='

-- 2.1 添加缺失的 *_raw 列
ALTER TABLE products ADD COLUMN IF NOT EXISTS d1_mm_raw text NULL;
ALTER TABLE products ADD COLUMN IF NOT EXISTS d2_mm_raw text NULL;
ALTER TABLE products ADD COLUMN IF NOT EXISTS d3_mm_raw text NULL;
ALTER TABLE products ADD COLUMN IF NOT EXISTS d4_mm_raw text NULL;
ALTER TABLE products ADD COLUMN IF NOT EXISTS h1_mm_raw text NULL;
ALTER TABLE products ADD COLUMN IF NOT EXISTS h2_mm_raw text NULL;
ALTER TABLE products ADD COLUMN IF NOT EXISTS h3_mm_raw text NULL;
ALTER TABLE products ADD COLUMN IF NOT EXISTS h4_mm_raw text NULL;

-- 2.2 修改列类型
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name='products' AND column_name='mr_1'
                 AND character_maximum_length IS DISTINCT FROM 10) THEN
        ALTER TABLE products ALTER COLUMN mr_1 TYPE character varying(10);
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name='products' AND column_name='oem_no_normalized' AND is_nullable='NO') THEN
        ALTER TABLE products ALTER COLUMN oem_no_normalized DROP NOT NULL;
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name='products' AND column_name='d1_mm'
                 AND numeric_precision IS DISTINCT FROM 10) THEN
        ALTER TABLE products ALTER COLUMN d1_mm TYPE numeric(10,2);
        ALTER TABLE products ALTER COLUMN d2_mm TYPE numeric(10,2);
        ALTER TABLE products ALTER COLUMN d3_mm TYPE numeric(10,2);
        ALTER TABLE products ALTER COLUMN d4_mm TYPE numeric(10,2);
        ALTER TABLE products ALTER COLUMN h1_mm TYPE numeric(10,2);
        ALTER TABLE products ALTER COLUMN h2_mm TYPE numeric(10,2);
        ALTER TABLE products ALTER COLUMN h3_mm TYPE numeric(10,2);
        ALTER TABLE products ALTER COLUMN h4_mm TYPE numeric(10,2);
    END IF;
END $$;

-- 2.3 删除旧索引
DROP INDEX IF EXISTS ix_products_mr_1;
DROP INDEX IF EXISTS ix_products_oem_no_normalized;

-- 2.4 创建新索引
CREATE UNIQUE INDEX IF NOT EXISTS idx_products_mr_1_unique
    ON products (mr_1) WHERE mr_1 IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_products_oem_no_normalized
    ON products (oem_no_normalized) WHERE oem_no_normalized IS NOT NULL;

-- 2.5 check constraint
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='chk_mr_1_format') THEN
        ALTER TABLE products
            ADD CONSTRAINT chk_mr_1_format
            CHECK (mr_1 IS NULL OR mr_1 ~ '^[A-Za-z0-9]{1,10}$');
    END IF;
END $$;

\echo '===== 3. product_images 表修复 ====='

ALTER TABLE product_images ADD COLUMN IF NOT EXISTS image_role character varying(20) NOT NULL DEFAULT 'detail';
ALTER TABLE product_images ADD COLUMN IF NOT EXISTS oem_no_3 character varying(200) NULL;

DROP INDEX IF EXISTS ix_product_images_product_id_slot;

CREATE UNIQUE INDEX IF NOT EXISTS uq_product_images_detail_slot
    ON product_images (product_id, slot) WHERE image_role = 'detail';

-- uq_product_images_primary: 检查重复
DO $$
DECLARE dup_count integer;
BEGIN
    SELECT COUNT(*) INTO dup_count FROM (
        SELECT oem_no_3 FROM product_images
        WHERE image_role = 'primary' AND oem_no_3 IS NOT NULL
        GROUP BY oem_no_3 HAVING COUNT(*) > 1
    ) t;

    IF dup_count = 0 THEN
        CREATE UNIQUE INDEX IF NOT EXISTS uq_product_images_primary
            ON product_images (oem_no_3)
            WHERE image_role = 'primary' AND oem_no_3 IS NOT NULL;
        RAISE NOTICE 'uq_product_images_primary 唯一索引已创建';
    ELSE
        RAISE NOTICE '跳过 uq_product_images_primary: 发现 % 组重复 oem_no_3', dup_count;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='chk_image_role') THEN
        ALTER TABLE product_images
            ADD CONSTRAINT chk_image_role
            CHECK (image_role IN ('primary', 'detail'));
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='chk_image_role_slot') THEN
        ALTER TABLE product_images
            ADD CONSTRAINT chk_image_role_slot
            CHECK ((image_role = 'primary' AND slot = 1) OR (image_role = 'detail' AND slot BETWEEN 2 AND 6));
    END IF;
END $$;

\echo '===== 4. machine_applications 表修复 ====='

ALTER TABLE machine_applications ADD COLUMN IF NOT EXISTS machine_category character varying(50) NULL DEFAULT 'others';

CREATE INDEX IF NOT EXISTS idx_machine_apps_category
    ON machine_applications (machine_category, machine_brand, machine_model);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='chk_machine_apps_category') THEN
        ALTER TABLE machine_applications
            ADD CONSTRAINT chk_machine_apps_category
            CHECK (machine_category IS NULL OR machine_category IN ('agriculture', 'commercial', 'construction', 'industrial', 'others'));
    END IF;
END $$;

\echo ''
\echo '===== 修复完成, 验证 ====='
SELECT 'cross_references' AS table_name, COUNT(*) AS col_count
FROM information_schema.columns WHERE table_name='cross_references'
UNION ALL
SELECT 'products_raw_cols', COUNT(*) FROM information_schema.columns
WHERE table_name='products' AND column_name LIKE '%_raw'
UNION ALL
SELECT 'product_images', COUNT(*) FROM information_schema.columns
WHERE table_name='product_images' AND column_name IN ('image_role','oem_no_3')
UNION ALL
SELECT 'machine_applications', COUNT(*) FROM information_schema.columns
WHERE table_name='machine_applications' AND column_name='machine_category';
