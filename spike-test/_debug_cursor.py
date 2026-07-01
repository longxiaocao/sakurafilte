"""查 cursor 解析后 SQL 应该命中的行数"""
import psycopg2

PG = dict(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
conn = psycopg2.connect(**PG); cur = conn.cursor()

# 查 id 11795~11799 的实际 stored updated_at
cur.execute("""
  SELECT id, oem_no_display, updated_at, updated_at AT TIME ZONE 'UTC' AS updated_utc
  FROM products WHERE oem_no_display LIKE 'DAY82P2-%' ORDER BY id
""")
print('=== id=11795-11799 实际 stored_value ===')
for row in cur.fetchall():
    print(f'  id={row[0]} oem={row[1]} stored_local={row[2]} stored_utc={row[3]}')

# 模拟 cursor 翻第 2 页: cursor 指向 id=11798 (cursor 字符串中的 id)
# 期望: 找到 updated_at < (11798 的 stored_value) 的所有 DAY82P2- 产品
#       或者 (updated_at == 11798 的 stored_value AND id < 11798)
# 实际看第 2 页应该返回 id=11797, 11796, 11795
print('\n=== 模拟 SQL: page 2 应该返回的行 ===')
cur.execute("""
  SELECT id, updated_at AT TIME ZONE 'UTC' AS utc, EXTRACT(EPOCH FROM updated_at) AS epoch
  FROM products
  WHERE oem_no_display LIKE 'DAY82P2-%'
    AND (updated_at < (SELECT updated_at FROM products WHERE id = 11798)
         OR (updated_at = (SELECT updated_at FROM products WHERE id = 11798)
             AND id < 11798))
  ORDER BY updated_at DESC, id DESC
""")
print(f'  命中 {cur.rowcount} 行:')
for row in cur.fetchall():
    print(f'    id={row[0]} utc={row[1]} epoch={row[2]}')

# 模拟 EF 实际传的字符串 '2026-06-30T21:19:49.004594Z'
print('\n=== 模拟 EF 传 UTC 字符串比较 ===')
cur.execute("""
  SELECT id, updated_at AT TIME ZONE 'UTC' AS utc
  FROM products
  WHERE oem_no_display LIKE 'DAY82P2-%'
    AND (updated_at < '2026-06-30T21:19:49.004594Z'::timestamptz
         OR (updated_at = '2026-06-30T21:19:49.004594Z'::timestamptz
             AND id < 11798))
  ORDER BY updated_at DESC, id DESC
""")
print(f'  命中 {cur.rowcount} 行:')
for row in cur.fetchall():
    print(f'    id={row[0]} utc={row[1]}')
