"""Day 7.7 验证: Etl:RecentErrorBuffer 配置生效 (期望容量=10)"""
import json
import time
import urllib.request
import urllib.error

API = "http://localhost:5148"

def post(path, body, retries=3):
    last = None
    for i in range(retries):
        try:
            req = urllib.request.Request(
                API + path, data=json.dumps(body).encode('utf-8'),
                headers={'Content-Type': 'application/json'}, method='POST',
            )
            with urllib.request.urlopen(req, timeout=60) as r:
                return json.loads(r.read())
        except urllib.error.HTTPError as e:
            if e.code == 409:
                time.sleep(2); last = e; continue
            raise
    raise last

def get_status():
    with urllib.request.urlopen(API + "/api/etl/status", timeout=5) as r:
        return json.loads(r.read())

def wait_idle(timeout=30):
    t0 = time.time()
    while time.time() - t0 < timeout:
        s = get_status()
        if s.get('status') in ('completed', 'failed', 'idle'):
            return s
        time.sleep(0.5)
    raise TimeoutError(f"ETL did not finish in {timeout}s")

# 触发 apps 导入,15 行坏数据
print("=== 触发 apps 导入 (15 行坏 JSON) ===")
r = post("/api/etl/import-apps", {
    "jsonlPath": r"d:\projects\sakurafilter\spike-test\output\test_recent_errors\apps_bad15.jsonl",
    "mode": "insert-only"
})
s = wait_idle()
print(f"  status={s.get('status')} read={s.get('read')} errors={s.get('errors')}")

# 检查 recentErrors 容量
recent = s.get('recentErrors', [])
print(f"  recentErrors 长度: {len(recent)}")
print(f"  期望: 10 (因配置 buffer=10,15 个错误只保留最近 10 条)")
assert len(recent) == 10, f"expected 10, got {len(recent)}"
for i, e in enumerate(recent):
    msg = e.get('message', '')
    print(f"    [{i+1:2}] {e.get('at')} | {msg[:80]}")
print("\n  ✅ 容量配置生效,保留最近 10 条")
