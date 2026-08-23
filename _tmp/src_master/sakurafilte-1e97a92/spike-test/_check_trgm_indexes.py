"""检查 spike_test_v3 库的现有 GIN trgm 索引"""
import os
import psycopg2

CONN = os.environ.get(
    "PG_TEST_CONNECTION_STRING",
    "host=localhost port=5432 dbname=spike_test_v3 user=postgres password=784533"
)

conn = psycopg2.connect(CONN)
with conn.cursor() as cur:
    cur.execute("""
        SELECT indexname, tablename, indexdef
        FROM pg_indexes
        WHERE (
            tablename IN ('products', 'cross_references', 'machine_applications')
            AND indexname LIKE '%trgm%'
        )
        OR indexname LIKE '%trgm%'
        ORDER BY tablename, indexname;
    """)
    rows = cur.fetchall()

print(f"现有 GIN trgm 索引 (spike_test_v3): {len(rows)} 个")
for row in rows:
    print(f"  - {row[0]} on {row[1]}")
    print(f"    {row[2]}")

# 检查 pg_trgm 扩展
with conn.cursor() as cur:
    cur.execute("SELECT extname, extversion FROM pg_extension WHERE extname = 'pg_trgm';")
    ext = cur.fetchone()
    print(f"\npg_trgm 扩展: {ext if ext else '未安装'}")

conn.close()
