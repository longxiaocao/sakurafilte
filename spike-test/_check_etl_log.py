import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("""
    SELECT id, status, cancel_reason, cancelled_at, last_error, started_at, finished_at
    FROM etl_progress_log
    WHERE status IN ('cancelled', 'failed')
    ORDER BY id DESC LIMIT 10
""")
for r in cur.fetchall():
    print(r)
conn.close()
