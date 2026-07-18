-- 检查 V2 迁移 (20260717125735_AddMr1PrimaryKeyAndV2Fields) 涉及的所有表结构
-- 对比 ModelSnapshot 期望 schema 与实际 DB schema 的差异

\echo '===== cross_references 实际列 ====='
SELECT column_name, data_type, character_maximum_length, is_nullable, column_default
FROM information_schema.columns
WHERE table_name='cross_references' ORDER BY ordinal_position;

\echo ''
\echo '===== products 实际列 (检查 V2 新增的 *_raw 列) ====='
SELECT column_name, data_type, character_maximum_length
FROM information_schema.columns
WHERE table_name='products' AND column_name LIKE '%_raw'
ORDER BY ordinal_position;

\echo ''
\echo '===== product_images 实际列 (检查 image_role/oem_no_3) ====='
SELECT column_name, data_type
FROM information_schema.columns
WHERE table_name='product_images'
ORDER BY ordinal_position;

\echo ''
\echo '===== machine_applications 实际列 (检查 machine_category) ====='
SELECT column_name, data_type
FROM information_schema.columns
WHERE table_name='machine_applications'
ORDER BY ordinal_position;

\echo ''
\echo '===== products.mr_1 类型 (应 varchar(10)) ====='
SELECT column_name, data_type, character_maximum_length
FROM information_schema.columns
WHERE table_name='products' AND column_name='mr_1';

\echo ''
\echo '===== products.oem_no_normalized nullable (应 nullable) ====='
SELECT column_name, is_nullable
FROM information_schema.columns
WHERE table_name='products' AND column_name='oem_no_normalized';

\echo ''
\echo '===== 缺失的索引 ====='
SELECT indexname FROM pg_indexes WHERE tablename='cross_references';
SELECT indexname FROM pg_indexes WHERE tablename='product_images' AND indexname LIKE 'uq_%';
SELECT indexname FROM pg_indexes WHERE tablename='machine_applications' AND indexname='idx_machine_apps_category';

\echo ''
\echo '===== 缺失的 check constraints ====='
SELECT conname FROM pg_constraint WHERE conrelid='cross_references'::regclass AND conname='chk_xref_machine_type';
