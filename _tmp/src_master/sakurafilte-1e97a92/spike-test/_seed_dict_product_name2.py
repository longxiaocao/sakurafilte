# -*- coding: utf-8 -*-
"""Day 10+ P2.2 seed: dict_product_name2
从 products.product_name_2 提取 distinct, 插入到 dict_product_name2
"""
import sys
import psycopg2

PG = dict(host="localhost", port=5432, dbname="spike_test_v3",
          user="postgres", password="784533")


def main():
    conn = psycopg2.connect(**PG)
    cur = conn.cursor()
    cur.execute("""
        SELECT DISTINCT product_name_2
        FROM products
        WHERE product_name_2 IS NOT NULL AND product_name_2 <> ''
        ORDER BY product_name_2
    """)
    distinct = [r[0] for r in cur.fetchall()]
    total = len(distinct)
    cur.execute("SELECT COUNT(*) FROM dict_product_name2")
    before = cur.fetchone()[0]
    inserted = 0
    already = 0
    cur.execute("SELECT COALESCE(MAX(sort_order), 0) FROM dict_product_name2")
    next_so = cur.fetchone()[0] + 10
    for v in distinct:
        cur.execute("""
            INSERT INTO dict_product_name2 (product_name_2, sort_order, created_at, updated_at)
            VALUES (%s, %s, now(), now())
            ON CONFLICT (product_name_2) DO NOTHING
            RETURNING id
        """, (v, next_so))
        if cur.fetchone():
            inserted += 1
        else:
            already += 1
        next_so += 10
    conn.commit()
    cur.execute("SELECT COUNT(*) FROM dict_product_name2")
    after = cur.fetchone()[0]
    conn.close()
    print(f"=== dict_product_name2 seed ===")
    print(f"  source distinct    = {total}")
    print(f"  inserted (new)     = {inserted}")
    print(f"  already existed    = {already}")
    print(f"  table before/after = {before} / {after}")
    return 0 if inserted + already == total else 1


if __name__ == "__main__":
    sys.exit(main())
