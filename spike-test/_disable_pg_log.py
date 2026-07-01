import psycopg2
PG = dict(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
conn = psycopg2.connect(**PG); conn.autocommit = True
cur = conn.cursor()
cur.execute("ALTER SYSTEM RESET log_statement")
cur.execute("SELECT pg_reload_conf()")
print('PG 日志已关闭')
