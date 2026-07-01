import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("SELECT max(id), min(id), count(*) FROM products")
print(f"products: min={cur.fetchone()}")

cur.execute("SELECT id, oem_no_display FROM products ORDER BY id DESC LIMIT 5")
for r in cur.fetchall():
    print(f"  id={r[0]} oem={r[1]}")

# 删掉无效的 product_history 记录
cur.execute("DELETE FROM product_history WHERE product_id NOT IN (SELECT id FROM products)")
print(f"删了 {cur.rowcount} 条 orphan history 记录")
conn.commit()

# 重新统计
cur.execute("""
    SELECT ph.product_id, count(*), p.oem_no_display
    FROM product_history ph
    JOIN products p ON p.id = ph.product_id
    GROUP BY ph.product_id, p.oem_no_display
    ORDER BY count(*) DESC
    LIMIT 5
""")
print("\n有产品对应 + 历史最多的:")
for r in cur.fetchall():
    print(f"  product_id={r[0]} history_count={r[1]} oem={r[2]}")
conn.close()
