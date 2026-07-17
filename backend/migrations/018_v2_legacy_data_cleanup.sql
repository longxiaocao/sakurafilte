-- 一次性脚本,不可重跑 (Task 0.1.2 v6 修订版)
-- 文件: 018_v2_legacy_data_cleanup.sql
-- 用途: V2 架构迁移 - 业务表清空 + system_settings 配置项插入
-- 执行前提:
--   1. EF Core 迁移 AddMr1PrimaryKeyAndV2Fields 已执行成功
--   2. ETL 服务已停止 (避免迁移期间数据写入)
--   3. 已备份数据库 (pg_dump)
-- WHY 业务表 TRUNCATE:
--   - V2 主键改为 mr_1, 旧 products 表数据无 mr_1 值 (NULL)
--   - 旧 cross_references 缺 oem_2/sort_order/machine_type/is_published 字段值
--   - 旧 product_images 缺 oem_no_3/image_role 字段值
--   - 需通过 ETL 重新导入 XLSX 数据填充 V2 字段
-- WHY 保留字典/用户/系统配置:
--   - xref_oem_brand: OEM 品牌字典 (sort_order 优先级, 不丢)
--   - users: 后台用户账号 (不丢)
--   - system_settings: 系统配置 (不丢)
--   - product_history: 历史变更记录 (审计要求, 不丢)
-- WARNING: 不可重跑! 已执行过的环境再执行会丢失 ETL 重新导入的数据

BEGIN;

-- ===== 阶段 1: 业务表 TRUNCATE (保留字典/用户/系统配置/历史) =====

-- 1.1 清空 product_images (FK CASCADE 依赖 products, 必须先清)
TRUNCATE TABLE product_images RESTART IDENTITY CASCADE;

-- 1.2 清空 machine_applications (FK CASCADE 依赖 products)
TRUNCATE TABLE machine_applications RESTART IDENTITY CASCADE;

-- 1.3 清空 cross_references (FK CASCADE 依赖 products)
TRUNCATE TABLE cross_references RESTART IDENTITY CASCADE;

-- 1.4 清空 products (主表)
TRUNCATE TABLE products RESTART IDENTITY CASCADE;

-- 1.5 清空 search_index_pending (旧索引任务, V2 结构不兼容)
TRUNCATE TABLE search_index_pending RESTART IDENTITY CASCADE;

-- 1.6 清空 search_index_dead_letter (旧死信任务)
TRUNCATE TABLE search_index_dead_letter RESTART IDENTITY CASCADE;

-- 1.7 清空 etl_alert_history (旧告警)
TRUNCATE TABLE etl_alert_history RESTART IDENTITY CASCADE;

-- 1.8 清空 etl_progress (旧进度快照)
TRUNCATE TABLE etl_progress RESTART IDENTITY CASCADE;

-- 1.9 清空 cleanup_failures (旧清理失败记录)
TRUNCATE TABLE cleanup_failures RESTART IDENTITY CASCADE;

-- 1.10 清空 partition6_placeholder (分区 6 占位表)
TRUNCATE TABLE partition6_placeholder RESTART IDENTITY CASCADE;

-- ===== 阶段 2: system_settings 插入 V2 配置项 (ON CONFLICT 幂等) =====
--   WHY ON CONFLICT: 允许重跑不报错 (但阶段 1 TRUNCATE 不可重跑)
--   spec Task 0.1.1: 插入 10 项新配置

INSERT INTO system_settings (key, value, description, updated_at)
VALUES
    -- 需求 1: MR.1 编码长度
    ('mr1.max_length', '10', 'MR.1 编码最大长度 (1-10 位字母数字)', now()),
    ('mr1.regex_pattern', '^[A-Za-z0-9]{1,10}$', 'MR.1 编码正则校验模式', now()),

    -- 需求 2: OEM 3 优先展示
    ('oem3.sort_order_default', '0', 'OEM 3 默认排序值 (越小越靠前)', now()),
    ('oem3.brand_priority_enabled', 'true', '是否启用 Brand 优先级排序', now()),

    -- 需求 4: 图片命名规则
    ('image.primary_naming_field', 'oem_no_3', '主图命名字段 (V2: 按 OEM 3)', now()),
    ('image.detail_naming_field', 'mr_1', '详情图命名字段 (V2: 按 MR.1)', now()),
    ('image.max_count_per_product', '6', '单个产品最大图片数 (主图 1 + 详情图 5)', now()),

    -- 需求 5: 聚合搜索
    ('search.max_page_depth', '100', '搜索最大分页深度 (超出抛 SEARCH_PAGE_TOO_DEEP)', now()),
    ('search.highlight_pre_tag', '<mark>', '搜索高亮起始标签', now()),
    ('search.highlight_post_tag', '</mark>', '搜索高亮结束标签', now())
ON CONFLICT (key) DO UPDATE
SET value = EXCLUDED.value,
    description = EXCLUDED.description,
    updated_at = now();

-- ===== 阶段 3: 验证 (可选, 不阻塞) =====
-- 执行后人工验证:
--   \d products                      -- 确认 mr_1 varchar(10) + idx_products_mr_1_unique + chk_mr_1_format
--   \d cross_references              -- 确认 oem_2/sort_order/machine_type/is_published/xmin + uq_xrefs_brand_oem3
--   \d product_images                -- 确认 oem_no_3/image_role + uq_product_images_primary + chk_image_role_slot
--   \d machine_applications          -- 确认 machine_category + chk_machine_apps_category
--   SELECT COUNT(*) FROM products;   -- 应为 0
--   SELECT key, value FROM system_settings WHERE key LIKE 'mr1.%' OR key LIKE 'oem3.%' OR key LIKE 'image.%' OR key LIKE 'search.%';

COMMIT;

-- ===== 阶段 4: 后续操作 (应用层, 非 SQL) =====
-- 1. 启动 ETL 服务, 拖入 XLSX 文件重新导入 V2 数据
-- 2. ETL 导入完成后, 触发 Meilisearch 索引重建 (新结构 Mr1IndexDoc)
-- 3. 验证公开搜索 /api/public/search/aggregate 返回 V2 结构数据
