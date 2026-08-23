"""验证 products 表 oem_no_normalized 与 xrefs product_oem 是否匹配"""
import psycopg2
import json

c = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3',
                     user='postgres', password='784533')
cur = c.cursor()

# 查 products 表样本
cur.execute("SELECT count(*), count(DISTINCT oem_no_normalized) FROM products")
row = cur.fetchone()
print(f"products: {row[0]:,} rows, {row[1]:,} distinct oem_no_normalized")

cur.execute("SELECT oem_no_normalized, oem_no_display FROM products LIMIT 5")
print("\nproducts sample:")
for r in cur.fetchall():
    print(f"  oem_norm={r[0]} | oem_display={r[1]}")

# 读 xrefs.jsonl 前 5 行的 product_oem
with open(r"d:\projects\sakurafilter\spike-test\output\cleaned\xrefs.jsonl", encoding='utf-8') as f:
    xref_oems = set()
    for i, line in enumerate(f):
        if i >= 5: break
        d = json.loads(line)
        xref_oems.add(d['product_oem'])
        print(f"  xref product_oem: {d['product_oem']}")

# 检查交集
cur.execute("SELECT oem_no_normalized FROM products WHERE oem_no_normalized = ANY(%s)", (list(xref_oems),))
matches = [r[0] for r in cur.fetchall()]
print(f"\nxref 前 5 OEM 在 products 中匹配: {len(matches)}/{len(xref_oems)}")

# 直接查 P00000001 是否在 products
cur.execute("SELECT id, oem_no_normalized FROM products WHERE oem_no_normalized = 'P00000001'")
r = cur.fetchone()
print(f"\nP00000001 在 products: {r}")

c.close()
