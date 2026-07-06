"""P0-1 验证: 死信清理进度"""
import psycopg2
import time

conn = psycopg2.connect(host='localhost', port=5432, database='spike_test_v3',
                        user='postgres', password='784533')
cur = conn.cursor()
for i in range(8):
    cur.execute("SELECT COUNT(*) FROM search_index_dead_letter")
    n = cur.fetchone()[0]
    cur.execute("""
        SELECT
            COUNT(*) FILTER (WHERE status='recovered') AS rec,
            COUNT(*) FILTER (WHERE status='active') AS act
        FROM search_index_dead_letter
    """)
    rec, act = cur.fetchone()
    print(f"t={i*5:>2}s  total={n:>10,}  recovered={rec:>10,}  active={act:>10,}")
    time.sleep(5)
cur.close(); conn.close()
