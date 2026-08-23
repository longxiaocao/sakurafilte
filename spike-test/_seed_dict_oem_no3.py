# -*- coding: utf-8 -*-
"""Day 10+ P2.2 seed: dict_oem_no3
从 cross_references.oem_no_3 提取 distinct, 插入到 dict_oem_no3
"""
import sys
import psycopg2

PG = dict(host="localhost", port=5432, dbname="spike_test_v3",
          user="postgres", password="784533")


def main():
    conn = psycopg2.connect(**PG)
    cur = conn.cursor()
    cur.execute("""
        SELECT DISTINCT oem_no_3
        FROM cross_references
        WHERE oem_no_3 IS NOT NULL AND oem_no_3 <> ''
        ORDER BY oem_no_3
    """)
    distinct = [r[0] for r in cur.fetchall()]
    total = len(distinct)
    cur.execute("SELECT COUNT(*) FROM dict_oem_no3")
    before = cur.fetchone()[0]
    inserted = 0
    already = 0
    cur.execute("SELECT COALESCE(MAX(sort_order), 0) FROM dict_oem_no3")
    next_so = cur.fetchone()[0] + 10
    for v in distinct:
        cur.execute("""
            INSERT INTO dict_oem_no3 (oem_no_3, sort_order, created_at, updated_at)
            VALUES (%s, %s, now(), now())
            ON CONFLICT (oem_no_3) DO NOTHING
            RETURNING id
        """, (v, next_so))
        if cur.fetchone():
            inserted += 1
        else:
            already += 1
        next_so += 10
    conn.commit()
    cur.execute("SELECT COUNT(*) FROM dict_oem_no3")
    after = cur.fetchone()[0]
    conn.close()
    print(f"=== dict_oem_no3 seed ===")
    print(f"  source distinct    = {total}")
    print(f"  inserted (new)     = {inserted}")
    print(f"  already existed    = {already}")
    print(f"  table before/after = {before} / {after}")
    return 0 if inserted + already == total else 1


if __name__ == "__main__":
    sys.exit(main())
