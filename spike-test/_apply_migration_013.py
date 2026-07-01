"""Day 7.9 应用 migration 013: etl_progress_log 加 alert_sent 列"""
import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
with open(r'd:\projects\sakurafilter\backend\migrations\013_add_etl_alert_sent.sql', 'r') as f:
    cur.execute(f.read())
conn.commit()
print('migration 013 已应用')

cur.execute("""
    SELECT column_name, data_type, is_nullable, column_default
    FROM information_schema.columns
    WHERE table_name='etl_progress_log' AND column_name='alert_sent'
""")
for r in cur.fetchall(): print(f'  column: {r}')

cur.execute("""
    SELECT indexname FROM pg_indexes
    WHERE tablename='etl_progress_log' AND indexname='idx_etl_log_failed_unalerted'
""")
idx = [r[0] for r in cur.fetchall()]
print(f'  index: {idx}')

# 验证老记录默认 alert_sent=false
cur.execute("SELECT count(*) FROM etl_progress_log WHERE alert_sent = false")
print(f'  老记录 (alert_sent=false): {cur.fetchone()[0]} 行')
conn.close()
