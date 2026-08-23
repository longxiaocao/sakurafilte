import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("""
    SELECT column_name FROM information_schema.columns
    WHERE table_name = 'products'
    AND column_name LIKE 'oem%'
    ORDER BY column_name
""")
for r in cur.fetchall():
    print(r)
