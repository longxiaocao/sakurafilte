-- 一次性脚本,不可重跑 (P-Demo 标注)
-- 原用途: 为公开搜索 typeahead 8 字段添加 pg_trgm GIN 索引
-- WHY: PublicTypeaheadService 走 ILIKE '%xxx%' 模糊匹配, 默认走全表扫描
--      百万级 products/cross_references/machine_applications 表查询 >500ms
--      加 pg_trgm GIN 索引后, ILIKE 模糊匹配可走索引, 查询降至 <50ms
--      (PostgreSQL 14+ 支持 ILIKE 直接走 trgm GIN 索引)
-- 依赖: CREATE EXTENSION pg_trgm (已由 EF InitialCreate 迁移创建, 此处 IF NOT EXISTS 兜底)
-- IF NOT EXISTS: 重复执行安全

-- 1. 启用 pg_trgm 扩展 (trigram 模糊匹配)
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- 2. products.oem_2 (OEM 2 编号, 百万级)
CREATE INDEX IF NOT EXISTS ix_products_oem_2_trgm
    ON products USING gin (oem_2 gin_trgm_ops);

-- 3. cross_references.oem_brand (替代品牌厂家名, 百万级)
CREATE INDEX IF NOT EXISTS ix_xrefs_oem_brand_trgm
    ON cross_references USING gin (oem_brand gin_trgm_ops);

-- 4. cross_references.oem_no_3 (替代 OEM 编号, 五百万级)
CREATE INDEX IF NOT EXISTS ix_xrefs_oem_no_3_trgm
    ON cross_references USING gin (oem_no_3 gin_trgm_ops);

-- 5. machine_applications.machine_brand (机型品牌, 百万级)
CREATE INDEX IF NOT EXISTS ix_apps_machine_brand_trgm
    ON machine_applications USING gin (machine_brand gin_trgm_ops);

-- 6. machine_applications.machine_model (机型型号)
CREATE INDEX IF NOT EXISTS ix_apps_machine_model_trgm
    ON machine_applications USING gin (machine_model gin_trgm_ops);

-- 7. machine_applications.model_name (型号名)
CREATE INDEX IF NOT EXISTS ix_apps_model_name_trgm
    ON machine_applications USING gin (model_name gin_trgm_ops);

-- 8. machine_applications.engine_brand (发动机品牌)
CREATE INDEX IF NOT EXISTS ix_apps_engine_brand_trgm
    ON machine_applications USING gin (engine_brand gin_trgm_ops);

-- 9. machine_applications.engine_type (发动机型号)
CREATE INDEX IF NOT EXISTS ix_apps_engine_type_trgm
    ON machine_applications USING gin (engine_type gin_trgm_ops);

-- 验证: 索引创建后, ILIKE '%xxx%' 查询应走 Bitmap Index Scan 而非 Seq Scan
-- EXPLAIN ANALYZE SELECT DISTINCT oem_2 FROM products WHERE oem_2 ILIKE '%P00%' LIMIT 20;
