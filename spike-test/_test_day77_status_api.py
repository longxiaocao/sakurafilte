"""Day 7.7 验证: status API 包含 skipped_duplicate"""
import json
import time
import urllib.request
import urllib.error

API = "http://localhost:5148"

def post(path, body):
    req = urllib.request.Request(
        API + path, data=json.dumps(body).encode('utf-8'),
        headers={'Content-Type': 'application/json'}, method='POST',
    )
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return json.loads(r.read())
    except urllib.error.HTTPError as e:
        if e.code == 409:
            time.sleep(2)
            with urllib.request.urlopen(req, timeout=60) as r:
                return json.loads(r.read())
        raise

def get_status():
    with urllib.request.urlopen(API + "/api/etl/status", timeout=5) as r:
        return json.loads(r.read())

# 触发 apps upsert (含 2 行 DISTINCT ON 去重)
print("=== 触发 apps upsert (期望 skipped_duplicate=2) ===")
r = post("/api/etl/import-apps", {
    "jsonlPath": r"d:\projects\sakurafilter\spike-test\output\cleaned\apps.jsonl",
    "mode": "upsert"
})

# 等待完成
t0 = time.time()
while time.time() - t0 < 30:
    s = get_status()
    if s.get('status') in ('completed', 'failed'):
        break
    time.sleep(0.5)

# 验证 status API 字段
print(f"\n  status API 字段:")
keys = ['status', 'read', 'inserted', 'updated', 'skipped',
        'skippedMissingOem', 'skippedNullField', 'skippedDuplicate',
        'errors', 'indexed', 'indexPending', 'elapsedSec', 'recentErrors']
for k in keys:
    v = s.get(k, '<MISSING>')
    if k == 'recentErrors':
        v = f"[{len(v)} entries]"
    print(f"    {k:20} = {v}")

# 关键断言
assert 'skippedDuplicate' in s, f"缺少 skippedDuplicate 字段"
assert s['skippedDuplicate'] == 2, f"期望 2, 实际 {s['skippedDuplicate']}"
print(f"\n  ✅ status API 暴露 skipped_duplicate = {s['skippedDuplicate']}")
