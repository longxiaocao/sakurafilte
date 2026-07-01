-- Day 8.1: 产品表单 7 分区字段扩展
-- 用途: 支撑后台产品录入表单 (规格表 新思路.xlsx - 后台新增产品格式)
-- 设计原则:
--   - 全部字段 nullable 或带默认值, 不影响现有 1949 条产品数据
--   - ProductImages 单独表 (而非 image_key_1-6 字段): 支持元数据 + 软删除 + 未来排序
--   - MachineApplication 一次性补齐 16 个车型扩展字段
--   - 所有变更 IF NOT EXISTS, 幂等可重跑

-- ============================================
-- 1) Product 主表扩展 (规格分区 1-6)
-- ============================================
ALTER TABLE products
    -- 分区 1: Product Name 1, Product Name 2, MR.1, OEM 2, 上架
    -- WHY product_name_1 在 product 而非 xref: 规格把它作为"产品主名", 一个产品只一个
    --      当前 product_name_1 散在 xref 表是 ETL 临时方案, 正式录入需归一化
    ADD COLUMN IF NOT EXISTS product_name_1   VARCHAR(100),
    ADD COLUMN IF NOT EXISTS product_name_2   VARCHAR(100),
    ADD COLUMN IF NOT EXISTS mr_1             VARCHAR(50),
    ADD COLUMN IF NOT EXISTS oem_2            VARCHAR(50),
    ADD COLUMN IF NOT EXISTS is_published     BOOLEAN NOT NULL DEFAULT true,

    -- 分区 3: H4, D4 (已有 H1-H3, D1-D3) + 阀门数量
    ADD COLUMN IF NOT EXISTS h4_mm            DECIMAL(10, 2),
    ADD COLUMN IF NOT EXISTS d4_mm            DECIMAL(10, 2),
    ADD COLUMN IF NOT EXISTS no_check_valves  INT,
    ADD COLUMN IF NOT EXISTS no_bypass_valves INT,

    -- 分区 5: Media Model, Bypass Valve HR, Efficiency 2, Bypass Pressure
    ADD COLUMN IF NOT EXISTS media_model         VARCHAR(200),
    ADD COLUMN IF NOT EXISTS bypass_valve_hr     DECIMAL(10, 2),
    ADD COLUMN IF NOT EXISTS efficiency_2        VARCHAR(50),
    ADD COLUMN IF NOT EXISTS bypass_pressure     VARCHAR(50),

    -- 分区 6: MasterBox 独立于 Carton + 派生体积
    -- WHY 派生体积: 规格要求"根据长宽高自动计算", 但允许 ETL 覆盖修正
    ADD COLUMN IF NOT EXISTS master_box_qty            INT,
    ADD COLUMN IF NOT EXISTS master_box_weight_kgs     DECIMAL(10, 2),
    ADD COLUMN IF NOT EXISTS master_box_length_mm      DECIMAL(10, 2),
    ADD COLUMN IF NOT EXISTS master_box_width_mm       DECIMAL(10, 2),
    ADD COLUMN IF NOT EXISTS master_box_height_mm      DECIMAL(10, 2),
    ADD COLUMN IF NOT EXISTS volume_per_carton_m3      DECIMAL(12, 6);

-- ============================================
-- 2) 索引 (后台搜索统筹: 按 Product Name 1, MR.1, OEM 2, 上架过滤)
-- ============================================
CREATE INDEX IF NOT EXISTS idx_products_product_name_1 ON products (product_name_1);
CREATE INDEX IF NOT EXISTS idx_products_mr_1           ON products (mr_1);
CREATE INDEX IF NOT EXISTS idx_products_oem_2          ON products (oem_2);
-- WHY 部分索引: 前台公开页默认只查上架产品, 索引大小减半
CREATE INDEX IF NOT EXISTS idx_products_is_published_true ON products (id) WHERE is_published = true;

-- ============================================
-- 3) ProductImages 表 (规格分区 4: 6 张图)
-- ============================================
-- WHY 单独表 (而非 6 个 image_key_1-6 字段):
--   - 元数据 (uploaded_at, uploaded_by, file_size, width, height)
--   - 软删除 (清空 slot 即可, 不影响产品)
--   - 独立 INSERT/UPDATE 性能更好 (更新 1 张图不动产品主表)
--   - 未来支持图排序 (display_order 字段预留)
CREATE TABLE IF NOT EXISTS product_images (
    id              BIGSERIAL PRIMARY KEY,
    product_id      BIGINT       NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    slot            SMALLINT     NOT NULL CHECK (slot BETWEEN 1 AND 6),
    image_key       VARCHAR(500) NOT NULL,
    file_size       BIGINT,
    content_type    VARCHAR(50),
    width           INT,
    height          INT,
    is_primary      BOOLEAN      NOT NULL DEFAULT false,
    display_order   INT          NOT NULL DEFAULT 0,
    uploaded_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    uploaded_by     VARCHAR(100),
    -- WHY UNIQUE (product_id, slot): 同一 slot 只允许一张图, 覆盖上传
    UNIQUE (product_id, slot)
);
CREATE INDEX IF NOT EXISTS idx_product_images_product_id ON product_images (product_id);
-- WHY 部分唯一索引: 同一产品只允许一张 is_primary=true
CREATE UNIQUE INDEX IF NOT EXISTS uq_product_images_primary
    ON product_images (product_id) WHERE is_primary = true;

-- ============================================
-- 4) MachineApplication 扩展 (规格分区 7: 车型信息)
-- ============================================
-- WHY 一次性补齐 16 字段: 避免后续分批 ALTER 锁表, 1M+ 产品下 R锁/Metadata锁会卡 ETL
ALTER TABLE machine_applications
    ADD COLUMN IF NOT EXISTS production_date_end      DATE,
    ADD COLUMN IF NOT EXISTS power                    VARCHAR(50),
    ADD COLUMN IF NOT EXISTS serial_number_from       VARCHAR(50),
    ADD COLUMN IF NOT EXISTS serial_number_to         VARCHAR(50),
    ADD COLUMN IF NOT EXISTS car_body_type            VARCHAR(100),
    ADD COLUMN IF NOT EXISTS series                   VARCHAR(100),
    ADD COLUMN IF NOT EXISTS co2_emission_standard    VARCHAR(50),
    ADD COLUMN IF NOT EXISTS transmission_type        VARCHAR(50),
    ADD COLUMN IF NOT EXISTS engine_displacement      VARCHAR(50),
    ADD COLUMN IF NOT EXISTS number_of_cylinders      INT,
    ADD COLUMN IF NOT EXISTS gvwr                     VARCHAR(50),
    ADD COLUMN IF NOT EXISTS tonnage                  VARCHAR(50),
    ADD COLUMN IF NOT EXISTS geographic_area          VARCHAR(100),
    ADD COLUMN IF NOT EXISTS chassis_type             VARCHAR(100),
    ADD COLUMN IF NOT EXISTS engine_model             VARCHAR(100),
    ADD COLUMN IF NOT EXISTS cabin_type               VARCHAR(100),
    ADD COLUMN IF NOT EXISTS capacity                 VARCHAR(50),
    ADD COLUMN IF NOT EXISTS engine_serial_number     VARCHAR(100);

-- 5) 字段扩展完成, 验证
-- 期望输出: product_name_1, product_name_2, mr_1, oem_2, is_published, h4_mm, d4_mm, no_check_valves, no_bypass_valves, media_model, bypass_valve_hr, efficiency_2, bypass_pressure, master_box_qty, master_box_weight_kgs, master_box_length_mm, master_box_width_mm, master_box_height_mm, volume_per_carton_m3
-- DO $$
-- BEGIN
--     RAISE NOTICE 'Day 8.1 migration 016 完成: products 表新增 19 字段 + product_images 表 + machine_applications 扩展 18 字段';
-- END $$;
