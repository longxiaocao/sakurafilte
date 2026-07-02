# -*- coding: utf-8 -*-
"""Day 10+ P2.2 seed: dict_machine (3 字段: machine_brand + machine_model + machine_name)
从 machine_applications 提取 distinct (brand, model, name) 组合
"""
import sys
import psycopg2

PG = dict(host="localhost", port=5432, dbname="spike_test_v3",
          user="postgres", password="784533")


def main():
    conn = psycopg2.connect(**PG)
    cur = conn.cursor()
    cur.execute("""
        SELECT brand, model, name
        FROM (
            SELECT DISTINCT
                machine_brand AS brand,
                NULLIF(machine_model, '') AS model,
                NULLIF(model_name, '') AS name
            FROM machine_applications
            WHERE machine_brand IS NOT NULL AND machine_brand <> ''
        ) t
        ORDER BY brand, model NULLS LAST, name NULLS LAST
    """)
    distinct = [(r[0], r[1], r[2]) for r in cur.fetchall()]
    total = len(distinct)
    cur.execute("SELECT COUNT(*) FROM dict_machine")
    before = cur.fetchone()[0]
    inserted = 0
    already = 0
    cur.execute("SELECT COALESCE(MAX(sort_order), 0) FROM dict_machine")
    next_so = cur.fetchone()[0] + 10
    for brand, model, name in distinct:
        cur.execute("""
            INSERT INTO dict_machine (machine_brand, machine_model, machine_name, sort_order, created_at, updated_at)
            VALUES (%s, %s, %s, %s, now(), now())
            ON CONFLICT (machine_brand, machine_model, machine_name) DO NOTHING
            RETURNING id
        """, (brand, model, name, next_so))
        if cur.fetchone():
            inserted += 1
        else:
            already += 1
        next_so += 10
    conn.commit()
    cur.execute("SELECT COUNT(*) FROM dict_machine")
    after = cur.fetchone()[0]
    conn.close()
    print(f"=== dict_machine seed ===")
    print(f"  source (brand,model,name) distinct = {total}")
    print(f"  inserted (new)                     = {inserted}")
    print(f"  already existed                    = {already}")
    print(f"  table before/after                 = {before} / {after}")
    return 0 if inserted + already == total else 1


if __name__ == "__main__":
    sys.exit(main())
