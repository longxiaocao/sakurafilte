-- **idempotent 脚本, 可重复执行** (ADD COLUMN IF NOT EXISTS + DEFAULT false)
-- 027: cross_references 白名单标记 (V3 2026-08-24)
--   WHY: sort_order 是源数据自带的优先级值(0~100 小整数, 92% 记录 >0),
--        同时承担搜索排序(BrandSortOrderMin/OemListSortOrderMin)。
--        原白名单判定 "sort_order > 0" 导致几乎全部 OEM 都在白名单(用户反馈)。
--   V3 方案: 新增 is_whitelisted 区分"白名单"(管理员手动维护, 少量) 与 "源排序"。
--        白名单判定改 is_whitelisted=true; sort_order 保留用于搜索排序。
--        现有数据默认 false → 白名单清空, 由管理员重新手工添加。

ALTER TABLE cross_references
    ADD COLUMN IF NOT EXISTS is_whitelisted boolean NOT NULL DEFAULT false;

-- 提示: 若需把现有"已维护"的少量白名单迁移过来, 可手工 UPDATE 指定 id;
-- 默认全部置 false (白名单清空, 与旧 sort_order>0 判定解耦)。
