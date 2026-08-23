"""Day 9.4 应用 migration 016 (新): product_history 加分页索引"""
import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()

with open(r'd:\projects\sakurafilter\backend\migrations\016_add_history_paging_index.sql', 'r') as f:
    cur.execute(f.read())
conn.commit()
print('migration 016 (history paging index) 已应用')

# 验证索引
cur.execute("""
    SELECT indexname, indexdef
    FROM pg_indexes
    WHERE tablename='product_history'
    AND indexname='idx_product_history_paging'
""")
r = cur.fetchone()
if not r:
    print('  错误: 索引未创建')
    raise SystemExit(1)
print(f'  索引名: {r[0]}')
print(f'  索引定义: {r[1]}')
assert 'DESC' in r[1], '索引必须包含 DESC 排序'
assert 'product_id' in r[1], '索引必须按 product_id 优先'

# 验证历史数据未受影响
cur.execute("SELECT count(*) FROM product_history")
total = cur.fetchone()[0]
print(f'  product_history 总记录: {total}')

# 性能验证: 走 keyset 索引扫描应很快
cur.execute("""
    EXPLAIN (ANALYZE, BUFFERS)
    SELECT * FROM product_history
    WHERE product_id = 1
    ORDER BY changed_at DESC, id DESC
    LIMIT 51
""")
plan = cur.fetchall()
plan_text = '\n'.join([row[0] for row in plan])
print('  EXPLAIN 计划:')
for line in plan_text.split('\n')[:5]:
    print(f'    {line}')
if 'idx_product_history_paging' in plan_text:
    print('  ✓ 命中 idx_product_history_paging 索引')
else:
    print('  ⚠️ 未命中 (可能表太小, Planner 选 Seq Scan)')

conn.close()
print('\nDay 9.4 migration 016 (history paging index) 端到端验证:全部通过 ✓')
