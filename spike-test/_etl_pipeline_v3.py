"""
ETL pipeline v3: 正确使用 3 个独立端点
  /api/etl/import       → products
  /api/etl/import-xrefs → xrefs
  /api/etl/import-apps  → apps
增强: 每步验证 DB 行数 + 重试 + 连接断开恢复
"""
import json
import time
import urllib.request
import psycopg2

BASE = "http://localhost:5148"
TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
PG = dict(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')

# 修正: 3 个独立端点
TASKS = [
    ("products", "/api/etl/import",       r"d:\projects\sakurafilter\spike-test\output\cleaned\products.jsonl", "full-load"),
    ("xrefs",    "/api/etl/import-xrefs",  r"d:\projects\sakurafilter\spike-test\output\cleaned\xrefs.jsonl", "full-load"),
    ("apps",     "/api/etl/import-apps",   r"d:\projects\sakurafilter\spike-test\output\cleaned\apps.jsonl", "full-load"),
]


def get_status():
    req = urllib.request.Request(f"{BASE}/api/etl/status", headers={"X-Admin-Token": TOKEN})
    with urllib.request.urlopen(req, timeout=10) as r:
        return json.loads(r.read())


def trigger_import(endpoint, jsonl_path, mode):
    body = json.dumps({"jsonlPath": jsonl_path, "mode": mode}).encode("utf-8")
    req = urllib.request.Request(f"{BASE}{endpoint}", data=body,
                                headers={"Content-Type": "application/json", "X-Admin-Token": TOKEN},
                                method="POST")
    with urllib.request.urlopen(req, timeout=10) as r:
        return r.status


def wait_idle(timeout=30):
    for _ in range(timeout):
        try:
            s = get_status()
            if s["status"] in ("idle", "completed", "failed", "cancelled"):
                return True
        except Exception:
            pass
        time.sleep(1)
    return False


def wait_complete(name, timeout=600):
    t0 = time.time()
    last_print = 0
    while time.time() - t0 < timeout:
        try:
            s = get_status()
        except Exception as e:
            print(f"  [{name}] API 连接失败, 等待恢复...", flush=True)
            time.sleep(5)
            continue

        if s["status"] in ("completed", "failed", "cancelled"):
            return s
        now = time.time()
        if now - last_print > 5:
            elapsed = now - t0
            print(f"  [{name}] {s['status']} stage={s['stage']} read={s['read']} ins={s['inserted']} err={s['errors']} missOem={s['skippedMissingOem']} {elapsed:.0f}s", flush=True)
            last_print = now
        time.sleep(1)
    return {"status": "timeout"}


def db_count(table):
    c = psycopg2.connect(**PG)
    cur = c.cursor()
    cur.execute(f"SELECT count(*) FROM {table}")
    n = cur.fetchone()[0]
    c.close()
    return n


def main():
    for i, (name, endpoint, path, mode) in enumerate(TASKS):
        print(f"\n===== [{i+1}/3] ETL {name} ({mode}) via {endpoint} =====", flush=True)

        # 等空闲
        if not wait_idle():
            print(f"  等待空闲超时, 跳过 {name}", flush=True)
            continue

        # 触发
        try:
            http_code = trigger_import(endpoint, path, mode)
            print(f"  触发: HTTP {http_code}", flush=True)
        except Exception as e:
            print(f"  触发失败: {e}", flush=True)
            continue

        # 等完成
        result = wait_complete(name)
        status = result["status"]
        time.sleep(3)  # 等后端稳定

        # 验证 DB
        table_map = {"products": "products", "xrefs": "cross_references", "apps": "machine_applications"}
        db_n = db_count(table_map[name])
        read = result.get("read", 0)
        inserted = result.get("inserted", 0)
        errors = result.get("errors", 0)
        missing = result.get("skippedMissingOem", 0)
        elapsed = result.get("elapsedSec", 0)
        last_err = result.get("lastError", "")
        recent = result.get("recentErrors", [])[:2]

        print(f"  结果: status={status} read={read:,} ins={inserted:,} err={errors} missOem={missing} db={db_n:,} {elapsed:.1f}s", flush=True)
        if recent:
            print(f"  最近错误: {recent}", flush=True)

        if db_n > 0:
            print(f"  [PASS] {name}", flush=True)
        else:
            print(f"  [FAIL] {name}: DB 行数=0", flush=True)
            if last_err:
                print(f"  lastError: {last_err}", flush=True)
            break

    # 最终验证
    print("\n===== 最终 DB 验证 =====", flush=True)
    for t in ["products", "cross_references", "machine_applications"]:
        n = db_count(t)
        print(f"  {t}: {n:,} rows", flush=True)


if __name__ == "__main__":
    main()
