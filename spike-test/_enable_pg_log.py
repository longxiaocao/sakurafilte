"""启用 PG 端 log_statement=all, 看到底 Npgsql 传了什么 SQL"""
import psycopg2

PG = dict(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
conn = psycopg2.connect(**PG); conn.autocommit = True
cur = conn.cursor()

# 临时开启 log_statement=all (会记录所有 SQL 到 PG log)
try:
    cur.execute("ALTER SYSTEM SET log_statement = 'all'")
    cur.execute("SELECT pg_reload_conf()")
    print('✓ log_statement=all 已启用')
except Exception as e:
    print(f'启用失败: {e}')
conn.close()
