"""检查 1M 合成数据是否已生成"""
import psycopg2, os

PG = dict(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
conn = psycopg2.connect(**PG); cur = conn.cursor()

cur.execute("SELECT relname, n_live_tup FROM pg_stat_user_tables WHERE relname IN ('products', 'cross_references', 'machine_applications')")
print('=== 当前表行数 ===')
for relname, n_live_tup in cur.fetchall():
    print(f'  {relname:25s}  n_live_tup={n_live_tup:,}')

# 找 1M 合成数据文件
candidates = [
    'd:/projects/sakurafilter/spike-test/output/synthetic_products_1000k.jsonl',
    'd:/projects/sakurafilter/spike-test/output/synthetic_xrefs_1000k.jsonl',
    'd:/projects/sakurafilter/spike-test/output/synthetic_apps_1000k.jsonl',
]
print('\n=== 1M 合成数据文件 ===')
for f in candidates:
    if os.path.exists(f):
        size = os.path.getsize(f) / 1024 / 1024
        print(f'  ✓ {f} ({size:.1f} MB)')
    else:
        print(f'  ✗ {f} 不存在')
