import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()

# 1) products 全部列
print('=== products 全部列 ===')
cur.execute("""
    SELECT column_name, data_type, is_nullable, column_default
    FROM information_schema.columns
    WHERE table_name = 'products'
    ORDER BY ordinal_position
""")
for r in cur.fetchall():
    print(f'  {r[0]:30s} {r[1]:30s} nullable={r[2]} default={r[3]}')

# 2) xref_oem_brand 表是否存在
print()
print('=== xref_oem_brand 表 ===')
cur.execute("""
    SELECT EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_name = 'xref_oem_brand'
    )
""")
exists = cur.fetchone()[0]
print(f'  存在: {exists}')

# 3) xref_oem_brand 列 (如果存在)
if exists:
    cur.execute("""
        SELECT column_name, data_type, is_nullable, column_default
        FROM information_schema.columns
        WHERE table_name = 'xref_oem_brand'
        ORDER BY ordinal_position
    """)
    for r in cur.fetchall():
        print(f'  {r[0]:30s} {r[1]:30s} nullable={r[2]} default={r[3]}')

conn.close()
