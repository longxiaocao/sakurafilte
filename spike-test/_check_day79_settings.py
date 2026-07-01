"""Day 7.9 验证: DeadLetterCleanupService + EtlAlertService 注册,默认配置已插入"""
import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()

for prefix in ['etl_log.', 'dead_letter.', 'alert.']:
    cur.execute("SELECT key, value, description FROM system_settings WHERE key LIKE %s ORDER BY key", (prefix + '%',))
    rows = cur.fetchall()
    print(f"\n{prefix}* 配置 ({len(rows)} 条):")
    for r in rows:
        print(f"  {r[0]:32} = {r[1]:8}  | {r[2]}")

# etl_log.retention_enabled 应已被前面脚本开启
cur.execute("SELECT value FROM system_settings WHERE key = 'etl_log.retention_enabled'")
print(f"\netl_log.retention_enabled = {cur.fetchone()[0]}")
conn.close()
