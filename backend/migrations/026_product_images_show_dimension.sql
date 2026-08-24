-- 026: 产品图尺寸标注开关 (V2 功能: 详情页主图叠加长宽高标注线)
--   show_dimension=true 时, 前端详情页在该图上叠加 SVG 尺寸标注 (D1 直径 x H1 高度)
--   管理后台可逐图切换; 默认 false (不标注)
--   idempotent 脚本, 可重复执行 (ADD COLUMN IF NOT EXISTS + DEFAULT false)
ALTER TABLE product_images ADD COLUMN IF NOT EXISTS show_dimension boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN product_images.show_dimension IS '是否在主图上叠加尺寸标注线 (D1 x H1); 管理后台逐图配置';