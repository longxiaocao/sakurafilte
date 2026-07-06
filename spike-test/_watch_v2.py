"""P22 验证: 死信清理速率提升"""
import psycopg2
import time
conn = psycopg2.connect(host='localhost', port=5432, database='spike_test_v3',
                        user='postgres', password='784533')
cur = conn.cursor()
for i in range(12):
    cur.execute("SELECT COUNT(*) FROM search_index_dead_letter WHERE status='recovered' AND moved_at < NOW() - INTERVAL '3 days'")
    n = cur.fetchone()[0]
    print(f"t={i*10:>3}s  recoverable: {n:>10,}")
    if n == 0:
        break
    time.sleep(10)
cur.close(); conn.close()
