import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("SELECT COUNT(*) FROM products")
print(f'products: {cur.fetchone()[0]:,}')
cur.execute("SELECT COUNT(*) FROM cross_references")
print(f'cross_references: {cur.fetchone()[0]:,}')
cur.execute("SELECT COUNT(*) FROM machine_applications")
print(f'machine_applications: {cur.fetchone()[0]:,}')
cur.execute("SELECT current_query(), state FROM pg_stat_activity WHERE datname='spike_test_v3'")
print('\nActive queries:')
for r in cur.fetchall()[:5]:
    print(f'  {r}')
