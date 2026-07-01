"""Day 9.5 应用 migration 017: etl_progress_log 加 reason_code 列"""
import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()

with open(r'd:\projects\sakurafilter\backend\migrations\017_add_etl_cancel_reason_code.sql', 'r') as f:
    cur.execute(f.read())
conn.commit()
print('migration 017 (reason_code) 已应用')

# 验证新列
cur.execute("""
    SELECT column_name, data_type, character_maximum_length
    FROM information_schema.columns
    WHERE table_name='etl_progress_log' AND column_name = 'reason_code'
""")
r = cur.fetchone()
print(f'  新增列: name={r[0]} type={r[1]} len={r[2]}')
assert r is not None, 'reason_code 列未创建'
assert r[1] == 'character varying', f'类型错, 实际 {r[1]}'

# 验证老记录的 reason_code 留 NULL (符合预期)
cur.execute("""
    SELECT count(*) AS total,
           count(*) FILTER (WHERE reason_code IS NULL) AS null_code
    FROM etl_progress_log
""")
r = cur.fetchone()
print(f'  etl_progress_log 总数={r[0]}, NULL reason_code={r[1]}')
assert r[1] == r[0], '所有历史行 reason_code 应为 NULL'

conn.close()
print('\nDay 9.5 migration 017 (reason_code) 端到端验证:全部通过 ✓')
