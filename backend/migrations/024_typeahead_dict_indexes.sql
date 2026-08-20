-- idempotent 可重跑: 024 移除全局 GIN, 改每字段 partial GIN (DROP/CREATE INDEX IF NOT EXISTS, 可重复执行)
-- 修复 typeahead 性能: 移除全局 GIN, 改每字段局部(partial) GIN
--
-- 根因 (实测): 023 建的全局 GIN `ix_typeahead_dict_value_trgm` 在 `value` 单列上,
--   查询 `WHERE field=@f AND value ILIKE @p` 时规划器优先用全局 GIN 扫描全部 1465 万行,
--   再按 field 过滤 —— 即使 oem-brand 仅 59 行 distinct, cold 查询也要 12.8s。
--   字典表实际 1465 万行 (oem-no3 占 1249 万), 与 023 注释假设的"万行级"不符。
--
-- 修复:
--   1) DROP 全局 GIN (性能杀手)
--   2) 对每个「低/中基数」字段建 partial GIN (WHERE field='X'), 查询 field=X 时只扫该字段自身行
--      近唯一高基数字段 oem-no3(1249万)/engine-type(199万) 由服务层基数守卫短路返回空, 不建索引
--      (建了也用不到, 且 1249 万行 GIN 构建慢、本机易冻结)
--   3) 剩余字段 (oem-brand 59 / oem-no2 59 / machine-brand 40 / model-name 20 /
--      engine-brand 6 / machine-model 158400) 经 PK(field,value) 或 partial GIN 均毫秒级
--
-- 幂等: DROP IF EXISTS + CREATE INDEX IF NOT EXISTS

DROP INDEX IF EXISTS ix_typeahead_dict_value_trgm;

CREATE INDEX IF NOT EXISTS ix_td_oem_brand_trgm
    ON typeahead_dict USING gin (value gin_trgm_ops) WHERE field = 'oem-brand';
CREATE INDEX IF NOT EXISTS ix_td_oem_no2_trgm
    ON typeahead_dict USING gin (value gin_trgm_ops) WHERE field = 'oem-no2';
CREATE INDEX IF NOT EXISTS ix_td_machine_brand_trgm
    ON typeahead_dict USING gin (value gin_trgm_ops) WHERE field = 'machine-brand';
CREATE INDEX IF NOT EXISTS ix_td_machine_model_trgm
    ON typeahead_dict USING gin (value gin_trgm_ops) WHERE field = 'machine-model';
CREATE INDEX IF NOT EXISTS ix_td_model_name_trgm
    ON typeahead_dict USING gin (value gin_trgm_ops) WHERE field = 'model-name';
CREATE INDEX IF NOT EXISTS ix_td_engine_brand_trgm
    ON typeahead_dict USING gin (value gin_trgm_ops) WHERE field = 'engine-brand';

-- 提示规划器: 字段等值过滤后各字段行数差异巨大, 让规划器偏向用 field 等值 + 局部 GIN
ANALYZE typeahead_dict;
