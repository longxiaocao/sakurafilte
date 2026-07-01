"""检查 DAY82P2- 产品的实际存储时间戳"""
import psycopg2

PG = dict(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
conn = psycopg2.connect(**PG); cur = conn.cursor()
cur.execute("""
  SELECT id, oem_no_display,
         created_at,
         updated_at,
         updated_at AT TIME ZONE 'UTC' as updated_utc,
         EXTRACT(EPOCH FROM updated_at) as updated_epoch
  FROM products
  WHERE oem_no_display LIKE 'DAY82P2-%'
  ORDER BY id
""")
for row in cur.fetchall():
    print(row)
