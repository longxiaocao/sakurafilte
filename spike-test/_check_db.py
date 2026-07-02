import psycopg2
c = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3',
                     user='postgres', password='784533')
cur = c.cursor()
cur.execute("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename")
print('tables:', [r[0] for r in cur.fetchall()])
for t in ['products', 'cross_references', 'machine_applications']:
    cur.execute(f"SELECT count(*) FROM {t}")
    print(f"{t}: {cur.fetchone()[0]:,} rows")
c.close()
