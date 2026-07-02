# -*- coding: utf-8 -*-
"""Day 10+ P2.2 seed: dict_media (2 字段: media_name + media_model)
从 products.media + products.media_model 提取 distinct (name, model) 组合
"""
import sys
import psycopg2

PG = dict(host="localhost", port=5432, dbname="spike_test_v3",
          user="postgres", password="784533")


def main():
    conn = psycopg2.connect(**PG)
    cur = conn.cursor()
    # 1) (media, media_model) 联合 distinct, 排除 NULL/空 name
    #    PG 限制: SELECT DISTINCT + ORDER BY 表达式必须完全一致
    #    用 CTE 先 NULLIF 再 DISTINCT 走完整列名
    cur.execute("""
        SELECT media, mm
        FROM (
            SELECT DISTINCT media, NULLIF(media_model, '') AS mm
            FROM products
            WHERE media IS NOT NULL AND media <> ''
        ) t
        ORDER BY media, mm NULLS LAST
    """)
    distinct = [(r[0], r[1]) for r in cur.fetchall()]
    total = len(distinct)
    cur.execute("SELECT COUNT(*) FROM dict_media")
    before = cur.fetchone()[0]
    inserted = 0
    already = 0
    cur.execute("SELECT COALESCE(MAX(sort_order), 0) FROM dict_media")
    next_so = cur.fetchone()[0] + 10
    for name, model in distinct:
        cur.execute("""
            INSERT INTO dict_media (media_name, media_model, sort_order, created_at, updated_at)
            VALUES (%s, %s, %s, now(), now())
            ON CONFLICT (media_name, media_model) DO NOTHING
            RETURNING id
        """, (name, model, next_so))
        if cur.fetchone():
            inserted += 1
        else:
            already += 1
        next_so += 10
    conn.commit()
    cur.execute("SELECT COUNT(*) FROM dict_media")
    after = cur.fetchone()[0]
    conn.close()
    print(f"=== dict_media seed ===")
    print(f"  source (name,model) distinct = {total}")
    print(f"  inserted (new)               = {inserted}")
    print(f"  already existed              = {already}")
    print(f"  table before/after           = {before} / {after}")
    return 0 if inserted + already == total else 1


if __name__ == "__main__":
    sys.exit(main())
