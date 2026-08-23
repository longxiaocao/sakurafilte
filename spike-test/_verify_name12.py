"""验证 products 表 product_name_1/2 数据"""
import psycopg2

c = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3',
                     user='postgres', password='784533')
cur = c.cursor()

cur.execute('SELECT count(*) FROM products')
print('products total:', cur.fetchone()[0])

cur.execute("SELECT count(*) FROM products WHERE product_name_1 IS NOT NULL")
print('product_name_1 NOT NULL:', cur.fetchone()[0])

cur.execute("SELECT count(*) FROM products WHERE product_name_2 IS NOT NULL")
print('product_name_2 NOT NULL:', cur.fetchone()[0])

cur.execute("SELECT count(*) FROM products WHERE product_name_3 IS NOT NULL")
print('product_name_3 NOT NULL:', cur.fetchone()[0])

print('\nSample 5 rows:')
cur.execute('SELECT product_name_1, product_name_2, product_name_3, type FROM products LIMIT 5')
for r in cur.fetchall():
    print(' ', r)

cur.execute("SELECT count(DISTINCT product_name_1) FROM products WHERE product_name_1 IS NOT NULL")
print('\nDISTINCT product_name_1:', cur.fetchone()[0])

cur.execute("SELECT count(DISTINCT product_name_2) FROM products WHERE product_name_2 IS NOT NULL")
print('DISTINCT product_name_2:', cur.fetchone()[0])

print('\nTop 5 product_name_1:')
cur.execute("SELECT product_name_1, count(*) FROM products WHERE product_name_1 IS NOT NULL GROUP BY product_name_1 ORDER BY count(*) DESC LIMIT 5")
for r in cur.fetchall():
    print(' ', r)

c.close()
