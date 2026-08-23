"""Apply migration 012 + verify"""
import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
with open(r'd:\projects\sakurafilter\backend\migrations\012_add_etl_progress_log.sql', encoding='utf-8') as f:
    cur.execute(f.read())
conn.commit()
print('迁移 012 应用成功')
cur.execute("""
SELECT column_name, data_type FROM information_schema.columns
WHERE table_name = 'etl_progress_log' ORDER BY ordinal_position
""")
print('表结构:')
for col, typ in cur.fetchall():
    print(f'  {col}: {typ}')
cur.execute("SELECT count(*) FROM etl_progress_log")
print(f'初始行数: {cur.fetchone()[0]}')
conn.close()
