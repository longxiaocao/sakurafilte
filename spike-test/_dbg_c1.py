import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("SELECT id, oem_no_display, is_published, is_discontinued, updated_at FROM products WHERE oem_no_display LIKE 'DAY82-TEST-%' ORDER BY id")
rows = cur.fetchall()
print(f'共 {len(rows)} 行')
for r in rows:
    print(r)
print('\n--- product_history 相关 ---')
cur.execute("SELECT product_id, change_type, changed_at FROM product_history WHERE product_id IN (SELECT id FROM products WHERE oem_no_display LIKE 'DAY82-TEST-%') ORDER BY product_id, changed_at")
for r in cur.fetchall():
    print(r)
