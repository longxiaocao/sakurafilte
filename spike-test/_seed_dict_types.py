# -*- coding: utf-8 -*-
"""Day 10+ P2.2 seed: dict_type (固定 5 值: oil/fuel/air/cabin/others)
从 products.type 提取 distinct, 与固定 5 值去重, 插入到 dict_type
sort_order 固定映射 (与 P2.3 排序保持一致):
  oil=10, fuel=20, air=30, cabin=40, others=50
"""
import sys
import psycopg2

PG = dict(host="localhost", port=5432, dbname="spike_test_v3",
          user="postgres", password="784533")

# 固定 5 值 + sort_order (与 P2.3 计划一致)
TYPES = [
    ("oil", 10),
    ("fuel", 20),
    ("air", 30),
    ("cabin", 40),
    ("others", 50),
]


def main():
    conn = psycopg2.connect(**PG)
    cur = conn.cursor()
    # 1) 源表实际 distinct type (用于发现漂移值)
    cur.execute("""
        SELECT DISTINCT type FROM products
        WHERE type IS NOT NULL AND type <> ''
        ORDER BY type
    """)
    source_types = {r[0] for r in cur.fetchall()}
    # 2) 目标表当前数量
    cur.execute("SELECT COUNT(*) FROM dict_type")
    before = cur.fetchone()[0]
    inserted = 0
    already = 0
    for v, so in TYPES:
        cur.execute("""
            INSERT INTO dict_type (type, sort_order, created_at, updated_at)
            VALUES (%s, %s, now(), now())
            ON CONFLICT (type) DO NOTHING
            RETURNING id
        """, (v, so))
        if cur.fetchone():
            inserted += 1
        else:
            already += 1
    conn.commit()
    cur.execute("SELECT COUNT(*) FROM dict_type")
    after = cur.fetchone()[0]
    conn.close()
    # 报告源表漂移 (产品表出现但字典未定义)
    defined = {v for v, _ in TYPES}
    drift = source_types - defined
    print(f"=== dict_type seed ===")
    print(f"  fixed types defined = {len(TYPES)}: {sorted(defined)}")
    print(f"  source distinct     = {len(source_types)}")
    print(f"  drift (未定义)      = {len(drift)}: {sorted(drift)[:10]}{'...' if len(drift) > 10 else ''}")
    print(f"  inserted (new)      = {inserted}")
    print(f"  already existed     = {already}")
    print(f"  table before/after  = {before} / {after}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
