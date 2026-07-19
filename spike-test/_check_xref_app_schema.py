"""检查 cross_references 和 machine_applications 表结构"""
import psycopg2

CONN = "host=localhost port=5432 dbname=spike_test_v3 user=postgres password=784533"

def main():
    conn = psycopg2.connect(CONN)
    cur = conn.cursor()

    for table in ["cross_references", "machine_applications"]:
        print(f"\n=== {table} 列结构 ===")
        cur.execute("""
            SELECT column_name, data_type, is_nullable
            FROM information_schema.columns
            WHERE table_name = %s
            ORDER BY ordinal_position
        """, (table,))
        for row in cur.fetchall():
            print(f"  {row[0]}: {row[1]} (nullable={row[2]})")

    cur.close()
    conn.close()

if __name__ == "__main__":
    main()
