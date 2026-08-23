"""检查 spike_test_v3 现有数据量 + schema 信息"""
import os
import psycopg2

CONN = os.environ.get(
    "PG_TEST_CONNECTION_STRING",
    "host=localhost port=5432 dbname=spike_test_v3 user=postgres password=784533"
)

def main():
    conn = psycopg2.connect(CONN)
    cur = conn.cursor()

    print("=== spike_test_v3 数据量 ===")
    for table in ["products", "cross_references", "machine_applications", "xref_oem_brand"]:
        cur.execute(f"SELECT COUNT(*) FROM {table}")
        count = cur.fetchone()[0]
        print(f"  {table}: {count:,}")

    print("\n=== products 表结构 (前 20 列) ===")
    cur.execute("""
        SELECT column_name, data_type, is_nullable, column_default
        FROM information_schema.columns
        WHERE table_name = 'products'
        ORDER BY ordinal_position
        LIMIT 20
    """)
    for row in cur.fetchall():
        print(f"  {row[0]}: {row[1]} (nullable={row[2]}, default={row[3]})")

    print("\n=== 现有 GIN trgm 索引 ===")
    cur.execute("""
        SELECT indexname, tablename, indexdef
        FROM pg_indexes
        WHERE indexdef LIKE '%gin_trgm%' OR indexdef LIKE '%gin (%'
        ORDER BY tablename, indexname
    """)
    for row in cur.fetchall():
        print(f"  {row[1]}.{row[0]}: {row[2][:120]}...")

    print("\n=== 数据库列表 ===")
    cur.execute("SELECT datname FROM pg_database WHERE datistemplate = false ORDER BY datname")
    for row in cur.fetchall():
        print(f"  {row[0]}")

    cur.close()
    conn.close()

if __name__ == "__main__":
    main()
