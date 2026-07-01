import psycopg2
c = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = c.cursor()
cur.execute("SELECT id, oem_no_display, updated_at FROM products WHERE oem_no_display LIKE 'DAY82P2-%' ORDER BY id DESC")
print('DAY82P2- 产品 updated_at:')
for r in cur.fetchall():
    print(f'  id={r[0]} {r[1]} updated_at={r[2]}')
