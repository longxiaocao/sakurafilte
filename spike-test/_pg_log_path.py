"""查 PG 日志文件位置"""
import psycopg2
PG = dict(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
conn = psycopg2.connect(**PG); cur = conn.cursor()
cur.execute("SHOW data_directory")
print('data_dir:', cur.fetchone()[0])
cur.execute("SHOW log_directory")
print('log_dir:', cur.fetchone()[0])
cur.execute("SHOW log_filename")
print('log_filename:', cur.fetchone()[0])
cur.execute("SHOW logging_collector")
print('logging_collector:', cur.fetchone()[0])
cur.execute("SHOW log_destination")
print('log_destination:', cur.fetchone()[0])
