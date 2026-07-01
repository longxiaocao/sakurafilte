"""验证 EtlLogCleanupService 注册时插入了 etl_log.* 默认配置"""
import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("SELECT key, value, description FROM system_settings WHERE key LIKE 'etl_log.%' ORDER BY key")
rows = cur.fetchall()
print('system_settings 中 etl_log.* 配置:')
for r in rows:
    print(f'  {r[0]:30} = {r[1]:8} | {r[2]}')
conn.close()
