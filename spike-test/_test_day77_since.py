"""Day 7.7 验证: GET /api/admin/dead-letter ?since 参数多 ISO8601 格式"""
import json
import urllib.request
import psycopg2
import time

API = "http://localhost:5148"

# 先确保 dead_letter 表有数据
# 先清理已有 day77_test 残留
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("DELETE FROM search_index_dead_letter WHERE operation = 'day77_test'")
print(f"清理残留 day77_test {cur.rowcount} 行")
conn.commit()
cur.execute("SELECT count(*) FROM search_index_dead_letter")
total = cur.fetchone()[0]
print(f"dead_letter 当前总数: {total}")
# 注入 3 条不同时间的数据 (使用 NULL payload 避免与已有约束冲突)
print("注入 3 条测试数据 (moved_at: 今天/昨天/上个月)")
cur.execute("""
    INSERT INTO search_index_dead_letter
        (original_id, operation, payload, retry_count, last_error, created_at, moved_at)
    VALUES
        (999001, 'day77_test', '{}'::jsonb, 5, 'err-today',  now() - interval '1 hour', now() - interval '1 hour'),
        (999002, 'day77_test', '{}'::jsonb, 5, 'err-yest',  now() - interval '1 day',  now() - interval '1 day'),
        (999003, 'day77_test', '{}'::jsonb, 5, 'err-month', now() - interval '30 days',now() - interval '30 days')
""")
conn.commit()
new_total = total + 3
print(f"注入后总数: {new_total}")
conn.close()

def hit(since=None, operation="day77_test"):
    url = API + f"/api/admin/dead-letter?limit=10&operation={operation}"
    if since:
        url += f"&since={urllib.parse.quote(since)}"
    with urllib.request.urlopen(url, timeout=10) as r:
        return json.loads(r.read())

# 测试 1: 不带 since (全部)
print("\n=== T1: 不带 since (全部) ===")
r = hit()
print(f"  total={r['total']} totalInRange={r['totalInRange']} returned={r['returned']} since={r.get('since')}")
# returned 应等于 day77_test 过滤后的行数 (limit=10 时)
assert r['returned'] == 3, f"expected 3 day77_test rows returned, got {r['returned']}"
errs = sorted([it['lastError'] for it in r['items']])
assert errs == ['err-month', 'err-today', 'err-yest'], f"unexpected: {errs}"
print(f"  ✅ 返回 day77_test 3 条: {errs}")

# 测试 2: ISO8601 date-only (YYYY-MM-DD)
print("\n=== T2: since=2026-07-01 (date only) ===")
r = hit("2026-07-01")
print(f"  totalInRange={r['totalInRange']} returned={r['returned']} since={r['since']}")
assert r['returned'] == 1, f"expected 1 (today), got {r['returned']}"
assert r['items'][0]['lastError'] == 'err-today'
print("  ✅ 只返回 today 的 1 条")

# 测试 3: ISO8601 datetime UTC
print("\n=== T3: since=2026-06-30T00:00:00Z ===")
r = hit("2026-06-30T00:00:00Z")
print(f"  totalInRange={r['totalInRange']} returned={r['returned']} since={r['since']}")
assert r['returned'] == 2, f"expected 2 (today+yesterday), got {r['returned']}"
print("  ✅ 返回 today + yesterday 共 2 条")

# 测试 4: ISO8601 + 时区
print("\n=== T4: since=2026-06-15T08:00:00+08:00 (上海时区) ===")
r = hit("2026-06-15T08:00:00+08:00")
# +08:00 = UTC 2026-06-15T00:00:00Z,30 天前记录 moved_at = now() - 30 days (2026-06-01)
# 2026-06-15T00:00:00Z > 2026-06-01T00:00:00Z → 应过滤掉 month 记录
print(f"  totalInRange={r['totalInRange']} returned={r['returned']} since={r['since']}")
assert r['returned'] == 2, f"expected 2, got {r['returned']}"
print("  ✅ 时区换算正确,返回 2 条")

# 测试 5: 非法格式
print("\n=== T5: since=not-a-date (400 BadRequest) ===")
try:
    r = hit("not-a-date")
    print(f"  意外成功: {r}")
    assert False, "应当 400"
except urllib.error.HTTPError as e:
    body = json.loads(e.read())
    print(f"  ✅ 返回 {e.code}: {body['error']}")
    assert e.code == 400

# 测试 6: 未来时间 → 0 条
print("\n=== T6: since=2099-01-01 (未来) ===")
r = hit("2099-01-01")
print(f"  totalInRange={r['totalInRange']} returned={r['returned']}")
assert r['totalInRange'] == 0
print("  ✅ 未来时间返回 0 条")

print("\n=== 全部 6 个 ?since 测试通过 ===")
