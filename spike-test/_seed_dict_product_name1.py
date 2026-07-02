# -*- coding: utf-8 -*-
"""Day 10+ P2.2 seed: dict_product_name1
从 products.product_name_1 提取 distinct, 插入到 dict_product_name1
幂等: ON CONFLICT DO NOTHING
输出统计: total / inserted / already_exists
"""
import os
import sys
import psycopg2

PG = dict(host="localhost", port=5432, dbname="spike_test_v3",
          user="postgres", password="784533")


def main():
    conn = psycopg2.connect(**PG)
    cur = conn.cursor()
    # 1) 源表 distinct (排除 NULL 和空字符串)
    cur.execute("""
        SELECT DISTINCT product_name_1
        FROM products
        WHERE product_name_1 IS NOT NULL AND product_name_1 <> ''
        ORDER BY product_name_1
    """)
    distinct = [r[0] for r in cur.fetchall()]
    total = len(distinct)
    # 2) 目标表当前数量
    cur.execute("SELECT COUNT(*) FROM dict_product_name1")
    before = cur.fetchone()[0]
    # 3) 批量 INSERT ON CONFLICT (product_name_1) DO NOTHING
    inserted = 0
    already = 0
    # 用 sort_order 步长 10, 起始 max+10
    cur.execute("SELECT COALESCE(MAX(sort_order), 0) FROM dict_product_name1")
    next_so = cur.fetchone()[0] + 10
    for v in distinct:
        cur.execute("""
            INSERT INTO dict_product_name1 (product_name_1, sort_order, created_at, updated_at)
            VALUES (%s, %s, now(), now())
            ON CONFLICT (product_name_1) DO NOTHING
            RETURNING id
        """, (v, next_so))
        if cur.fetchone():
            inserted += 1
        else:
            already += 1
        next_so += 10
    conn.commit()
    cur.execute("SELECT COUNT(*) FROM dict_product_name1")
    after = cur.fetchone()[0]
    conn.close()
    print(f"=== dict_product_name1 seed ===")
    print(f"  source distinct    = {total}")
    print(f"  inserted (new)     = {inserted}")
    print(f"  already existed    = {already}")
    print(f"  table before/after = {before} / {after}")
    return 0 if inserted + already == total else 1


if __name__ == "__main__":
    sys.exit(main())
