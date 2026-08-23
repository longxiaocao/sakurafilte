"""验证三项改进
Day 11+ v2: 路径改用 SCRIPT_DIR 自动算 repo root, 跨平台兼容
"""
import json
import time
import urllib.request
import urllib.error
from pathlib import Path

BASE = "http://localhost:5148"
TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
HEADERS = {"Content-Type": "application/json", "X-Admin-Token": TOKEN}

# 跨平台路径 (CI 是 Linux, 本地是 Windows)
SCRIPT_DIR = Path(__file__).resolve().parent
CLEANED = SCRIPT_DIR / "output" / "cleaned"


def get_status():
    req = urllib.request.Request(f"{BASE}/api/etl/status", headers={"X-Admin-Token": TOKEN})
    with urllib.request.urlopen(req, timeout=10) as r:
        return json.loads(r.read())


def trigger(endpoint, body):
    req = urllib.request.Request(f"{BASE}{endpoint}", data=json.dumps(body).encode("utf-8"),
                                headers=HEADERS, method="POST")
    with urllib.request.urlopen(req, timeout=10) as r:
        return r.status, json.loads(r.read())


def wait_idle():
    for _ in range(30):
        s = get_status()
        if s["status"] in ("idle", "completed", "failed", "cancelled"):
            return True
        time.sleep(1)
    return False


# 测试 1: 改进 1 - 统一端点 entityType 参数 (用 100 行小文件)
print("===== 测试 1: 统一端点 entityType=xrefs =====")
wait_idle()
test_xrefs = str(CLEANED / "xrefs_100.jsonl")
try:
    code, resp = trigger("/api/etl/import", {"jsonlPath": test_xrefs, "mode": "insert-only", "entityType": "xrefs"})
    print(f"  触发统一端点: HTTP {code}")
    time.sleep(5)
    s = get_status()
    print(f"  结果: status={s['status']} read={s['read']} inserted={s['inserted']} errors={s['errors']}")
    if s['read'] == 100 and s['status'] == 'completed':
        print("  [PASS] entityType=xrefs 正确路由")
    else:
        print(f"  [WARN] 期望 read=100 completed, 实际 read={s['read']} {s['status']}")
except Exception as e:
    print(f"  [FAIL] {e}")

# 测试 2: 改进 1 - 非法 entityType
print("\n===== 测试 2: 非法 entityType 拒绝 =====")
wait_idle()
try:
    code, resp = trigger("/api/etl/import", {"jsonlPath": test_xrefs, "mode": "upsert", "entityType": "invalid"})
    print(f"  [FAIL] 期望 400, 实际 HTTP {code}")
except urllib.error.HTTPError as e:
    if e.code == 400:
        print(f"  [PASS] HTTP 400 拒绝非法 entityType")
    else:
        print(f"  [FAIL] 期望 400, 实际 {e.code}")
except Exception as e:
    print(f"  [FAIL] {e}")

# 测试 3: 改进 2 - cascade=false 不清空 xrefs/apps
print("\n===== 测试 3: cascade=false 保护 xrefs/apps =====")
import psycopg2
c = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = c.cursor()
cur.execute("SELECT count(*) FROM cross_references")
xrefs_before = cur.fetchone()[0]
cur.execute("SELECT count(*) FROM machine_applications")
apps_before = cur.fetchone()[0]
print(f"  导入前: xrefs={xrefs_before:,} apps={apps_before:,}")

wait_idle()
test_products = str(CLEANED / "products.jsonl")
try:
    code, resp = trigger("/api/etl/import", {
        "jsonlPath": test_products, "mode": "full-load", "entityType": "products", "cascade": False
    })
    print(f"  触发 cascade=false: HTTP {code}")
    # 等完成
    for _ in range(120):
        s = get_status()
        if s["status"] in ("completed", "failed", "cancelled"):
            break
        time.sleep(2)
    print(f"  结果: status={s['status']} inserted={s['inserted']}")

    cur.execute("SELECT count(*) FROM cross_references")
    xrefs_after = cur.fetchone()[0]
    cur.execute("SELECT count(*) FROM machine_applications")
    apps_after = cur.fetchone()[0]
    cur.execute("SELECT count(*) FROM products")
    products_after = cur.fetchone()[0]
    print(f"  导入后: products={products_after:,} xrefs={xrefs_after:,} apps={apps_after:,}")

    if xrefs_after == xrefs_before and apps_after == apps_before and products_after > 0:
        print(f"  [PASS] cascade=false 保留 xrefs/apps, products 已刷新")
    else:
        print(f"  [FAIL] xrefs 变化={xrefs_after - xrefs_before} apps 变化={apps_after - apps_before}")
except Exception as e:
    print(f"  [FAIL] {e}")
finally:
    c.close()

# 测试 4: 改进 1 - 旧端点兼容
print("\n===== 测试 4: 旧端点 /api/etl/import-xrefs 兼容 =====")
wait_idle()
try:
    code, resp = trigger("/api/etl/import-xrefs", {"jsonlPath": test_xrefs, "mode": "insert-only"})
    print(f"  [PASS] 旧端点兼容: HTTP {code}")
except Exception as e:
    print(f"  [FAIL] {e}")

print("\n===== 验证完成 =====")
