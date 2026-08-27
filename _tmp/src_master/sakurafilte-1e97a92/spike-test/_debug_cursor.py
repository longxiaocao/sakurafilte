"""Debug: 看 cursor 实际生成内容"""
import urllib.request
import json
import base64
import psycopg2
import os

# Seed 历史
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("SELECT max(id) FROM products")
pid = cur.fetchone()[0]

# 清掉旧 seed
cur.execute("DELETE FROM product_history WHERE product_id = %s AND changed_by = 'debug-cursor'", (pid,))

# 插 20 条 (用 UTC datetime)
from datetime import datetime, timedelta, timezone
base = datetime(2026, 7, 1, 12, 0, 0, tzinfo=timezone.utc)
for i in range(20):
    cur.execute("""
        INSERT INTO product_history (product_id, change_type, changed_by, changed_at, changed_fields)
        VALUES (%s, 'update', 'debug-cursor', %s, %s)
    """, (pid, base + timedelta(minutes=i * 5), json.dumps({"_i": i})))
conn.commit()
print(f"Seeded 20 records for product_id={pid}")

# 拉第一页
HEADERS = {"X-Admin-Token": "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"}
url = f"http://localhost:5148/api/admin/products/{pid}/history?limit=5"
req = urllib.request.Request(url, headers=HEADERS)
with urllib.request.urlopen(req) as r:
    h1 = json.loads(r.read())
print(f"\nPage 1 items: {len(h1['items'])}")
for it in h1['items']:
    print(f"  id={it['id']} changedAt={it['changedAt']} type={it['changeType']}")
print(f"Next cursor: {h1.get('nextCursor')}")

# 解析 cursor
cursor = h1.get('nextCursor')
if cursor:
    s64 = cursor.replace('-', '+').replace('_', '/')
    s64 += '=' * (4 - len(s64) % 4) if len(s64) % 4 else ''
    decoded = base64.b64decode(s64).decode('utf-8')
    print(f"\nDecoded cursor: {decoded}")

# 拉第二页
url2 = f"http://localhost:5148/api/admin/products/{pid}/history?limit=5&cursor={cursor}"
req2 = urllib.request.Request(url2, headers=HEADERS)
with urllib.request.urlopen(req2) as r:
    h2 = json.loads(r.read())
print(f"\nPage 2 items: {len(h2['items'])}")
for it in h2['items']:
    print(f"  id={it['id']} changedAt={it['changedAt']} type={it['changeType']}")

# 检查重叠
p1_ids = {x['id'] for x in h1['items']}
p2_ids = {x['id'] for x in h2['items']}
overlap = p1_ids & p2_ids
print(f"\nOverlap: {overlap}")

# 清理
cur.execute("DELETE FROM product_history WHERE product_id = %s AND changed_by = 'debug-cursor'", (pid,))
conn.commit()
conn.close()
print(f"\n清理 debug-cursor 历史")
