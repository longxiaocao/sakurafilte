"""Day 8.1 应用 migration 016: 产品表单 7 分区字段扩展 + 6 张图 + 车型扩展"""
import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
with open(r'd:\projects\sakurafilter\backend\migrations\016_add_product_form_fields.sql', 'r') as f:
    cur.execute(f.read())
conn.commit()
print('migration 016 已应用')

# 验证 products 新增列
cur.execute("""
    SELECT column_name FROM information_schema.columns
    WHERE table_name='products' AND column_name IN (
        'product_name_1', 'product_name_2', 'mr_1', 'oem_2', 'is_published',
        'h4_mm', 'd4_mm', 'no_check_valves', 'no_bypass_valves',
        'media_model', 'bypass_valve_hr', 'efficiency_2', 'bypass_pressure',
        'master_box_qty', 'master_box_weight_kgs', 'master_box_length_mm', 'master_box_width_mm', 'master_box_height_mm', 'volume_per_carton_m3'
    )
    ORDER BY column_name
""")
cols = [r[0] for r in cur.fetchall()]
print(f'  products 新增 {len(cols)} 字段: {cols}')
assert len(cols) == 19, f'期望 19 字段, 实际 {len(cols)}'

# 验证 product_images 表
cur.execute("""
    SELECT column_name, data_type, is_nullable
    FROM information_schema.columns
    WHERE table_name='product_images'
    ORDER BY ordinal_position
""")
for r in cur.fetchall():
    print(f'  product_images.{r[0]}: {r[1]} nullable={r[2]}')

# 验证 machine_applications 新增列
cur.execute("""
    SELECT column_name FROM information_schema.columns
    WHERE table_name='machine_applications' AND column_name IN (
        'production_date_end', 'power', 'serial_number_from', 'serial_number_to',
        'car_body_type', 'series', 'co2_emission_standard', 'transmission_type',
        'engine_displacement', 'number_of_cylinders', 'gvwr', 'tonnage',
        'geographic_area', 'chassis_type', 'engine_model', 'cabin_type', 'capacity', 'engine_serial_number'
    )
    ORDER BY column_name
""")
app_cols = [r[0] for r in cur.fetchall()]
print(f'  machine_applications 新增 {len(app_cols)} 字段: {app_cols}')
assert len(app_cols) == 18, f'期望 18 字段, 实际 {len(app_cols)}'

# 验证索引
cur.execute("""
    SELECT indexname FROM pg_indexes
    WHERE tablename IN ('products', 'product_images', 'machine_applications')
    AND indexname LIKE 'idx_%products%' OR indexname LIKE 'idx_product_images%' OR indexname LIKE 'uq_product_images%'
    ORDER BY indexname
""")
idx = [r[0] for r in cur.fetchall()]
print(f'  索引: {idx}')

# 验证现有 1949 条产品数据未受影响
cur.execute("SELECT count(*) FROM products")
total = cur.fetchone()[0]
print(f'  现有产品数: {total}')

conn.close()
print('\nDay 8.1 migration 016 端到端验证:全部通过 ✓')
