import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("SELECT tablename FROM pg_tables WHERE tablename LIKE '%efmigration%' OR tablename LIKE '%Migration%'")
print('Migration tables:')
for r in cur.fetchall():
    print(' ', r[0])
rows = cur.fetchall()
print('EF Migrations applied ({}):'.format(len(rows)))
for r in rows:
    print(' ', r[0])
print()
# 同时查 products 表的实际索引
cur.execute("""
    SELECT indexname, indexdef
    FROM pg_indexes
    WHERE tablename = 'products' AND indexname LIKE '%oem%'
    ORDER BY indexname
""")
print('products oem_* indexes:')
for r in cur.fetchall():
    print(' ', r[0], '|', r[1])
conn.close()
