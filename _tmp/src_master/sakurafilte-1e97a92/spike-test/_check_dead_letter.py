"""验证 dead_letter 转移逻辑"""
import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()

# 1) dead_letter 中条目的 retry_count 分布
cur.execute("SELECT retry_count, count(*) FROM search_index_dead_letter GROUP BY retry_count")
print('=== dead_letter retry_count 分布 ===')
for rc, n in cur.fetchall():
    print(f'  retry_count={rc}: {n}')

# 2) pending 中条目分布
cur.execute("SELECT retry_count, count(*) FROM search_index_pending GROUP BY retry_count ORDER BY retry_count")
print('\n=== pending retry_count 分布 ===')
for rc, n in cur.fetchall():
    print(f'  retry_count={rc}: {n}')

# 3) dead_letter 示例
cur.execute("SELECT id, original_id, operation, retry_count, last_error, moved_at FROM search_index_dead_letter ORDER BY id DESC LIMIT 3")
print('\n=== dead_letter 最新 3 条 ===')
for r in cur.fetchall():
    print(f'  {r}')

# 4) total before/after 统计
cur.execute("SELECT (SELECT count(*) FROM search_index_dead_letter) + (SELECT count(*) FROM search_index_pending)")
print(f'\n总条目 (pending+dead): {cur.fetchone()[0]}')

conn.close()
