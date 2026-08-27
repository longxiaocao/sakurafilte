"""调试 size filter"""
import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
# 直接查 D1 in [75, 105]
cur.execute("SELECT count(*) FROM products WHERE d1_mm >= 75 AND d1_mm <= 105")
print('D1 in [75,105]:', cur.fetchone())
cur.execute("SELECT count(*) FROM products WHERE d1_mm IS NOT NULL")
print('D1 not null:', cur.fetchone())
cur.execute("SELECT min(d1_mm), max(d1_mm), avg(d1_mm) FROM products")
print('D1 stats:', cur.fetchone())
conn.close()
