"""Day 7.10 Item 4 应用 migration 014: search_index_dead_letter 加恢复元数据列"""
import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
with open(r'd:\projects\sakurafilter\backend\migrations\014_add_dead_letter_recovery.sql', 'r') as f:
    cur.execute(f.read())
conn.commit()
print('migration 014 已应用')

cur.execute("""
    SELECT column_name, data_type, is_nullable, column_default
    FROM information_schema.columns
    WHERE table_name='search_index_dead_letter'
    AND column_name IN ('recovery_count', 'last_recovery_at', 'last_recovery_error')
    ORDER BY column_name
""")
for r in cur.fetchall(): print(f'  column: {r}')

cur.execute("""
    SELECT indexname FROM pg_indexes
    WHERE tablename='search_index_dead_letter' AND indexname='idx_dead_letter_recovery'
""")
idx = [r[0] for r in cur.fetchall()]
print(f'  index: {idx}')

# 验证老记录默认 recovery_count=0
cur.execute("SELECT count(*) FROM search_index_dead_letter WHERE recovery_count = 0")
print(f'  老记录 (recovery_count=0): {cur.fetchone()[0]} 行')
conn.close()
