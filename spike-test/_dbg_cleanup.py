import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()

# 模拟测试清理流程
print('清理前:')
cur.execute("SELECT id, oem_no_display, is_discontinued FROM products WHERE oem_no_display LIKE 'DAY82-TEST-%'")
for r in cur.fetchall():
    print(' ', r)

cur.execute("DELETE FROM cross_references WHERE oem_no_3 LIKE 'DAY82-TEST-%'")
print(f'\n  cross_references DELETE: {cur.rowcount} 行')
cur.execute("DELETE FROM machine_applications WHERE machine_model LIKE 'DAY82-TEST-%'")
print(f'  machine_applications DELETE: {cur.rowcount} 行')
cur.execute("DELETE FROM product_images WHERE product_id IN (SELECT id FROM products WHERE oem_no_display LIKE 'DAY82-TEST-%')")
print(f'  product_images DELETE: {cur.rowcount} 行')
cur.execute("DELETE FROM product_history WHERE changed_fields::text LIKE '%DAY82-TEST-%'")
print(f'  product_history DELETE: {cur.rowcount} 行')
try:
    cur.execute("DELETE FROM products WHERE oem_no_display LIKE 'DAY82-TEST-%'")
    print(f'  products DELETE: {cur.rowcount} 行')
except Exception as e:
    print(f'  products DELETE 失败: {e}')
conn.commit()

print('\n清理后:')
cur.execute("SELECT id, oem_no_display, is_discontinued FROM products WHERE oem_no_display LIKE 'DAY82-TEST-%'")
for r in cur.fetchall():
    print(' ', r)
