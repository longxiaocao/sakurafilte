import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()

# 1) products NOT NULL 列的 default 值状态
print('=== products NOT NULL cols defaults ===')
cur.execute("""
    SELECT column_name, data_type, is_nullable, column_default
    FROM information_schema.columns
    WHERE table_name = 'products' AND is_nullable = 'NO'
    ORDER BY ordinal_position
""")
for r in cur.fetchall():
    print(f'  {r[0]:30s} {r[1]:30s} nullable={r[2]} default={r[3]}')

# 2) 关键 UNIQUE 索引
print()
print('=== products unique indexes ===')
cur.execute("""
    SELECT indexname, indexdef FROM pg_indexes
    WHERE tablename = 'products' AND indexdef LIKE '%UNIQUE%'
    ORDER BY indexname
""")
for r in cur.fetchall():
    print(f'  {r[0]}: {r[1]}')

# 3) 所有表存在性
print()
print('=== All public tables ===')
cur.execute("""
    SELECT tablename FROM pg_tables
    WHERE schemaname='public' ORDER BY tablename
""")
tables = [r[0] for r in cur.fetchall()]
print(f'  共 {len(tables)} 张表')
for t in tables:
    print(f'  - {t}')

# 4) EF Core 期望的索引名 vs 实际名
print()
print('=== EF Core expected vs actual index names ===')
expected = {
    'pk_products': 'PK',
    'ix_products_oem_no_normalized': 'products.oem_no_normalized (非 UNIQUE)',
    'ix_products_oem_no_display': 'products.oem_no_display',
    'ix_products_type': 'products.type',
    'ix_products_d1_mm': 'products.d1_mm',
    'ix_products_d2_mm': 'products.d2_mm',
    'ix_products_h1_mm': 'products.h1_mm',
    'idx_products_type_d1': 'products(type, d1_mm)',
    'idx_products_type_d2': 'products(type, d2_mm)',
    'idx_products_type_h1': 'products(type, h1_mm)',
}
cur.execute("SELECT indexname FROM pg_indexes WHERE tablename='products'")
actual = {r[0] for r in cur.fetchall()}
for en in expected:
    mark = '✓' if en in actual else '✗'
    print(f'  {mark} {en}')

conn.close()
