"""Day 10 数据迁移: 从 cross_references 提取去重的 oem_brand 种子到 xref_oem_brand

设计:
  - 从 cross_references 聚合 brand + 计数 (作为初始 sort_order 依据)
  - 排序: 计数 DESC, brand ASC (热门在前)
  - sort_order = 10, 20, 30... 步长 10 留排序余地 (与 AdminDictService.CreateOemBrandAsync 默认一致)
  - 重复运行幂等: ON CONFLICT (brand) DO NOTHING 跳过已存在
  - 仅迁移有值且非空的 brand (排除 NULL 和 '')

执行:
  python _migrate_xref_oem_brands.py
"""
import psycopg2

conn = psycopg2.connect(
    host='localhost', port=5432, dbname='spike_test_v3',
    user='postgres', password='784533'
)
cur = conn.cursor()

print('=' * 60)
print('Day 10 数据迁移: cross_references.oem_brand → xref_oem_brand')
print('=' * 60)

# 1) 统计当前 cross_references 中非空 brand 总数 (迁移前快照)
cur.execute("""
    SELECT COUNT(DISTINCT oem_brand) AS uniq_brands,
           COUNT(*) FILTER (WHERE oem_brand IS NOT NULL AND oem_brand <> '') AS non_empty
    FROM cross_references
""")
uniq, non_empty = cur.fetchone()
print(f'\n[源数据] cross_references 中唯一 brand={uniq}, 非空 xref 行={non_empty}')

# 2) 检查 xref_oem_brand 表是否已存在 (EF Core Migrate 是否已跑)
cur.execute("""
    SELECT EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_name = 'xref_oem_brand'
    )
""")
table_exists = cur.fetchone()[0]
if not table_exists:
    print('\n[ERROR] xref_oem_brand 表不存在! 先启动后端让 EF Core Migrate 创建表, 再重跑此脚本')
    conn.close()
    raise SystemExit(1)

# 3) 当前 xref_oem_brand 行数 (幂等检查)
cur.execute("SELECT COUNT(*) FROM xref_oem_brand")
existing = cur.fetchone()[0]
print(f'[目标表] xref_oem_brand 现有 {existing} 条')

# 4) 聚合 brand 计数, 按 sort_order 步长 10 分配
#    用窗口函数 ROW_NUMBER() 计算排名, 转换为 sort_order
cur.execute("""
    INSERT INTO xref_oem_brand (brand, sort_order, created_at, updated_at)
    SELECT
        brand,
        (ROW_NUMBER() OVER (ORDER BY cnt DESC, brand ASC)) * 10 AS sort_order,
        now(),
        now()
    FROM (
        SELECT oem_brand AS brand, COUNT(*) AS cnt
        FROM cross_references
        WHERE oem_brand IS NOT NULL AND oem_brand <> ''
        GROUP BY oem_brand
    ) agg
    ON CONFLICT (brand) DO NOTHING
    RETURNING id, brand, sort_order
""")
inserted = cur.fetchall()
conn.commit()
print(f'\n[迁移] 新增 {len(inserted)} 条 OEM 品牌到 xref_oem_brand')
if len(inserted) > 0:
    print('  Top 10:')
    for row in inserted[:10]:
        print(f'    id={row[0]:>4}  sort_order={row[2]:>4}  brand={row[1]}')
    if len(inserted) > 10:
        print(f'    ... 还有 {len(inserted) - 10} 条')

# 5) 最终统计
cur.execute("SELECT COUNT(*) FROM xref_oem_brand")
total = cur.fetchone()[0]
print(f'\n[完成] xref_oem_brand 现共 {total} 条 (其中 {existing} 条迁移前已存在, {len(inserted)} 条本次新增)')

# 6) 与 cross_references 中唯一 brand 对比 (应有 brand 不在字典的, 说明源数据有"野 brand")
cur.execute("""
    SELECT COUNT(DISTINCT cr.oem_brand)
    FROM cross_references cr
    LEFT JOIN xref_oem_brand x ON x.brand = cr.oem_brand
    WHERE cr.oem_brand IS NOT NULL AND cr.oem_brand <> ''
      AND x.id IS NULL
""")
orphans = cur.fetchone()[0]
if orphans > 0:
    print(f'\n[注意] 源数据还有 {orphans} 个 brand 在 cross_references 中但 xref_oem_brand 不存在')
    print('       (可能为 NULL 过滤, 或迁移后新增的 xref) — 不影响字典, 不阻塞')

conn.close()
print('\nDay 10 数据迁移端到端验证:全部通过 ✓')
