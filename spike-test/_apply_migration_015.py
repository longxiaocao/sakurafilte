"""Day 7.10.1 应用 migration 015: dead_letter 加 status 列"""
import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
with open(r'd:\projects\sakurafilter\backend\migrations\015_dead_letter_status.sql', 'r') as f:
    cur.execute(f.read())
conn.commit()
print('migration 015 已应用')

# 验证新列
cur.execute("""
    SELECT column_name, data_type, is_nullable, column_default
    FROM information_schema.columns
    WHERE table_name='search_index_dead_letter'
    AND column_name IN ('status', 'recovered_at', 'recovered_to_pending_id')
    ORDER BY column_name
""")
for r in cur.fetchall(): print(f'  column: {r}')

# 验证 status 分布
cur.execute("SELECT status, count(*) FROM search_index_dead_letter GROUP BY status")
for r in cur.fetchall(): print(f'  status {r[0]}: {r[1]} rows')

# 验证索引
cur.execute("""
    SELECT indexname FROM pg_indexes
    WHERE tablename='search_index_dead_letter'
    AND indexname IN ('idx_dead_letter_active_recovery', 'idx_dead_letter_recovered_at', 'idx_dead_letter_payload_hash')
""")
idx = [r[0] for r in cur.fetchall()]
print(f'  索引: {idx}')

conn.close()
