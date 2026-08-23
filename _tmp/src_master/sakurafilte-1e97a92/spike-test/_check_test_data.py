import psycopg2
conn = psycopg2.connect('host=localhost port=5432 dbname=spike_test_v3 user=postgres password=784533')
cur = conn.cursor()
cur.execute("SELECT id, mr_1 FROM products WHERE mr_1 IS NOT NULL AND mr_1 LIKE 'DAY%' ORDER BY id LIMIT 5")
for r in cur.fetchall():
    print(r)
cur.execute("SELECT id, mr_1 FROM products ORDER BY id LIMIT 5")
print('--- 任何产品 ---')
for r in cur.fetchall():
    print(r)
cur.execute("SELECT count(*) FROM products WHERE is_discontinued = false")
print('--- 总数 ---', cur.fetchone())
cur.execute("SELECT count(*) FROM product_history")
print('--- history 总数 ---', cur.fetchone())
