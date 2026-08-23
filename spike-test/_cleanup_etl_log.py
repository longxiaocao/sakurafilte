"""清理 etl_progress_log 脏数据,准备 Day 7.7 端到端测试"""
import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
# Day 7.6 阶段:xrefs 错记为 apps / full-load 错记为 ETL_ENTITY 都属于 bug
cur.execute("DELETE FROM etl_progress_log WHERE entity_type IN ('ETL_ENTITY', 'apps') AND mode IN ('upsert', 'full-load')")
print(f'清理脏数据 {cur.rowcount} 行')
conn.commit()
cur.execute('SELECT id, entity_type, mode, status, read_count FROM etl_progress_log ORDER BY id')
print('清理后剩余:')
for r in cur.fetchall():
    print(f'  id={r[0]:3} entity={r[1]:10} mode={r[2]:12} status={r[3]:10} read={r[4]}')
conn.close()
