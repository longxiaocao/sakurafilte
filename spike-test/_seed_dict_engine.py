# -*- coding: utf-8 -*-
"""Day 10+ P2.2 seed: dict_engine (2 字段: engine_brand + engine_type)
从 machine_applications 提取 distinct (engine_brand, engine_type) 组合
"""
import sys
import psycopg2

PG = dict(host="localhost", port=5432, dbname="spike_test_v3",
          user="postgres", password="784533")


def main():
    conn = psycopg2.connect(**PG)
    cur = conn.cursor()
    cur.execute("""
        SELECT brand, et
        FROM (
            SELECT DISTINCT
                engine_brand AS brand,
                NULLIF(engine_type, '') AS et
            FROM machine_applications
            WHERE engine_brand IS NOT NULL AND engine_brand <> ''
        ) t
        ORDER BY brand, et NULLS LAST
    """)
    distinct = [(r[0], r[1]) for r in cur.fetchall()]
    total = len(distinct)
    cur.execute("SELECT COUNT(*) FROM dict_engine")
    before = cur.fetchone()[0]
    inserted = 0
    already = 0
    cur.execute("SELECT COALESCE(MAX(sort_order), 0) FROM dict_engine")
    next_so = cur.fetchone()[0] + 10
    for brand, type_ in distinct:
        cur.execute("""
            INSERT INTO dict_engine (engine_brand, engine_type, sort_order, created_at, updated_at)
            VALUES (%s, %s, %s, now(), now())
            ON CONFLICT (engine_brand, engine_type) DO NOTHING
            RETURNING id
        """, (brand, type_, next_so))
        if cur.fetchone():
            inserted += 1
        else:
            already += 1
        next_so += 10
    conn.commit()
    cur.execute("SELECT COUNT(*) FROM dict_engine")
    after = cur.fetchone()[0]
    conn.close()
    print(f"=== dict_engine seed ===")
    print(f"  source (brand,type) distinct = {total}")
    print(f"  inserted (new)               = {inserted}")
    print(f"  already existed              = {already}")
    print(f"  table before/after           = {before} / {after}")
    return 0 if inserted + already == total else 1


if __name__ == "__main__":
    sys.exit(main())
