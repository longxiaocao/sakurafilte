"""Day 7.8 验证: 死信表 keyset cursor 分页"""
import json
import urllib.parse
import urllib.request
import psycopg2

API = "http://localhost:5148"

# 确保有 day78_test 数据 (插入 8 条,moved_at 递增)
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("DELETE FROM search_index_dead_letter WHERE operation = 'day78_cursor'")
deleted = cur.rowcount
print(f"清理 day78_cursor 残留 {deleted} 行")
conn.commit()

# 插入 8 条,moved_at 严格递增 (1min 间隔)
cur.execute("""
    INSERT INTO search_index_dead_letter
        (original_id, operation, payload, retry_count, last_error, created_at, moved_at)
    VALUES
        (770001, 'day78_cursor', '{}'::jsonb, 5, 'err-1', now() - interval '7 minutes', now() - interval '7 minutes'),
        (770002, 'day78_cursor', '{}'::jsonb, 5, 'err-2', now() - interval '6 minutes', now() - interval '6 minutes'),
        (770003, 'day78_cursor', '{}'::jsonb, 5, 'err-3', now() - interval '5 minutes', now() - interval '5 minutes'),
        (770004, 'day78_cursor', '{}'::jsonb, 5, 'err-4', now() - interval '4 minutes', now() - interval '4 minutes'),
        (770005, 'day78_cursor', '{}'::jsonb, 5, 'err-5', now() - interval '3 minutes', now() - interval '3 minutes'),
        (770006, 'day78_cursor', '{}'::jsonb, 5, 'err-6', now() - interval '2 minutes', now() - interval '2 minutes'),
        (770007, 'day78_cursor', '{}'::jsonb, 5, 'err-7', now() - interval '1 minutes', now() - interval '1 minutes'),
        (770008, 'day78_cursor', '{}'::jsonb, 5, 'err-8', now(),                            now())
""")
conn.commit()
cur.execute("SELECT count(*) FROM search_index_dead_letter WHERE operation = 'day78_cursor'")
total = cur.fetchone()[0]
print(f"插入 day78_cursor {total} 行")
conn.close()

def hit(**params):
    qs = '&'.join(f"{k}={urllib.parse.quote(str(v))}" for k, v in params.items())
    url = API + "/api/admin/dead-letter?" + qs
    with urllib.request.urlopen(url, timeout=10) as r:
        return json.loads(r.read())

# T1: limit=3,无 cursor → 返回前 3 条, hasMore=true, nextCursor 不为空
print("\n=== T1: limit=3, 第 1 页 ===")
r = hit(operation="day78_cursor", limit=3)
errs = [it['lastError'] for it in r['items']]
print(f"  returned={r['returned']} hasMore={r['hasMore']} nextCursor={r['nextCursor']}")
print(f"  items (按 movedAt 降序): {errs}")
assert r['returned'] == 3
assert r['hasMore'] == True
assert errs == ['err-8', 'err-7', 'err-6'], f"unexpected order: {errs}"
next_cursor = r['nextCursor']
assert next_cursor, "nextCursor 不应为空"
print(f"  ✅ 3 条 + hasMore + nextCursor")

# T2: 用 T1 的 nextCursor 翻第 2 页
print(f"\n=== T2: 第 2 页 (cursor={next_cursor}) ===")
r = hit(operation="day78_cursor", limit=3, cursor=next_cursor)
errs = [it['lastError'] for it in r['items']]
print(f"  returned={r['returned']} hasMore={r['hasMore']} nextCursor={r['nextCursor']}")
print(f"  items: {errs}")
assert r['returned'] == 3
assert r['hasMore'] == True
assert errs == ['err-5', 'err-4', 'err-3'], f"unexpected: {errs}"
next_cursor = r['nextCursor']
print(f"  ✅ 第 2 页正确")

# T3: 第 3 页 (末页,只剩 2 条)
print(f"\n=== T3: 第 3 页 (cursor={next_cursor}) ===")
r = hit(operation="day78_cursor", limit=3, cursor=next_cursor)
errs = [it['lastError'] for it in r['items']]
print(f"  returned={r['returned']} hasMore={r['hasMore']} nextCursor={r['nextCursor']}")
print(f"  items: {errs}")
assert r['returned'] == 2, f"expected 2, got {r['returned']}"
assert r['hasMore'] == False
assert r['nextCursor'] is None
assert errs == ['err-2', 'err-1'], f"unexpected: {errs}"
print(f"  ✅ 末页 hasMore=false, nextCursor=null")

# T4: cursor 格式错 → 400
print(f"\n=== T4: cursor=not-a-cursor (400) ===")
try:
    r = hit(operation="day78_cursor", limit=3, cursor="garbage")
    print(f"  意外成功: {r}")
    assert False
except urllib.error.HTTPError as e:
    body = json.loads(e.read())
    print(f"  ✅ 返回 {e.code}: {body['error']}")
    assert e.code == 400

# T5: limit=10 一次性取完 (无 cursor)
print(f"\n=== T5: limit=10, 一次性取完 ===")
r = hit(operation="day78_cursor", limit=10)
errs = [it['lastError'] for it in r['items']]
print(f"  returned={r['returned']} hasMore={r['hasMore']} nextCursor={r['nextCursor']}")
print(f"  items: {errs}")
assert r['returned'] == 8
assert r['hasMore'] == False
assert r['nextCursor'] is None
assert errs == ['err-8', 'err-7', 'err-6', 'err-5', 'err-4', 'err-3', 'err-2', 'err-1']
print(f"  ✅ 8 条全取, hasMore=false")

# 清理
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("DELETE FROM search_index_dead_letter WHERE operation = 'day78_cursor'")
conn.commit()
print(f"\n清理 day78_cursor {cur.rowcount} 行")

print("\n=== 全部 5 个 cursor 分页测试通过 ===")
