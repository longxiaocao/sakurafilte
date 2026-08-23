"""Day 9.4 应用 migration 015 (新): etl_progress_log 加取消审计字段"""
import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()

# 注意: 此 migration 与旧 015_dead_letter_status.sql 编号冲突
#   用 IF NOT EXISTS 保护, 重复执行安全
with open(r'd:\projects\sakurafilter\backend\migrations\015_add_etl_cancel_audit.sql', 'r') as f:
    cur.execute(f.read())
conn.commit()
print('migration 015 (cancel audit) 已应用')

# 验证新列
cur.execute("""
    SELECT column_name, data_type, is_nullable
    FROM information_schema.columns
    WHERE table_name='etl_progress_log'
    AND column_name IN ('cancel_reason', 'cancelled_at')
    ORDER BY column_name
""")
rows = cur.fetchall()
print(f'  新增列: {rows}')
assert len(rows) == 2, f'期望 2 列, 实际 {len(rows)}'

# 验证: 老 etl_progress_log 行的 cancel_reason 应该是 NULL
cur.execute("""
    SELECT count(*) AS total,
           count(*) FILTER (WHERE cancel_reason IS NULL) AS null_reason,
           count(*) FILTER (WHERE cancelled_at IS NULL) AS null_at
    FROM etl_progress_log
""")
r = cur.fetchone()
print(f'  etl_progress_log 总数={r[0]}, NULL cancel_reason={r[1]}, NULL cancelled_at={r[2]}')
assert r[1] == r[0], '所有历史行 cancel_reason 应为 NULL'
assert r[2] == r[0], '所有历史行 cancelled_at 应为 NULL'

conn.close()
print('\nDay 9.4 migration 015 (cancel audit) 端到端验证:全部通过 ✓')
