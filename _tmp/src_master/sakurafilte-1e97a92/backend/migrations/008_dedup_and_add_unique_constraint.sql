-- 一次性脚本,不可重跑 (P2-7.1 标注)
-- 原用途: products 表 oem_no_normalized 去重 (DELETE 重复行) + 加唯一约束 (Day 5 1M 合成数据修复)
-- 008_dedup_and_add_unique_constraint.sql
-- Day 5 修复: 1M 合成数据有 37,873 个重复 (oem_no_normalized),需要去重
-- WHY: 为 ON CONFLICT (oem_no_normalized) DO UPDATE 准备前提条件
-- 策略: 保留每组中 id 最大的行(最近插入),删除其余

-- 1) 用窗口函数去重,保留 id 最大
DELETE FROM products p
USING (
    SELECT id, ROW_NUMBER() OVER (
        PARTITION BY oem_no_normalized
        ORDER BY id DESC
    ) AS rn
    FROM products
) d
WHERE p.id = d.id AND d.rn > 1;

-- 2) 验证无重复
-- SELECT oem_no_normalized, COUNT(*) FROM products GROUP BY oem_no_normalized HAVING COUNT(*) > 1;
-- 应该返回 0 行

-- 3) 创建 UNIQUE 索引
CREATE UNIQUE INDEX IF NOT EXISTS uq_products_oem_normalized
    ON products (oem_no_normalized);

ANALYZE products;
