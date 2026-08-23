"""
ETL pipeline v2: 逐步导入 products -> xrefs -> apps
增强: 重试 + 等待空闲 + 每步验证 DB 行数
"""
import json
import time
import urllib.request
import psycopg2

BASE = "http://localhost:5148"
TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
PG = dict(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')

TASKS = [
    ("products", r"d:\projects\sakurafilter\spike-test\output\cleaned\products.jsonl", "full-load"),
    ("xrefs",    r"d:\projects\sakurafilter\spike-test\output\cleaned\xrefs.jsonl", "full-load"),
    ("apps",     r"d:\projects\sakurafilter\spike-test\output\cleaned\apps.jsonl", "full-load"),
]


def get_status():
    req = urllib.request.Request(f"{BASE}/api/etl/status", headers={"X-Admin-Token": TOKEN})
    with urllib.request.urlopen(req, timeout=10) as r:
        return json.loads(r.read())


def trigger_import(jsonl_path, mode):
    body = json.dumps({"jsonlPath": jsonl_path, "mode": mode}).encode("utf-8")
    req = urllib.request.Request(f"{BASE}/api/etl/import", data=body,
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
            print(f"  [{name}] API 连接失败: {e}, 等待恢复...", flush=True)
            time.sleep(5)
            continue

        if s["status"] in ("completed", "failed", "cancelled"):
            return s
        now = time.time()
        if now - last_print > 3:
            elapsed = now - t0
            print(f"  [{name}] {s['status']} stage={s['stage']} read={s['read']} ins={s['inserted']} err={s['errors']} {elapsed:.0f}s", flush=True)
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
    for i, (name, path, mode) in enumerate(TASKS):
        print(f"\n===== [{i+1}/3] ETL {name} ({mode}) =====", flush=True)

        # 等空闲
        if not wait_idle():
            print(f"  等待空闲超时, 跳过 {name}", flush=True)
            continue

        # 触发
        try:
            http_code = trigger_import(path, mode)
            print(f"  触发: HTTP {http_code}", flush=True)
        except Exception as e:
            print(f"  触发失败: {e}", flush=True)
            continue

        # 等完成
        result = wait_complete(name)
        status = result["status"]

        # 等后端稳定 (防 connection refused)
        time.sleep(2)

        # 验证 DB
        table_map = {"products": "products", "xrefs": "cross_references", "apps": "machine_applications"}
        db_n = db_count(table_map[name])
        read = result.get("read", 0)
        inserted = result.get("inserted", 0)
        errors = result.get("errors", 0)
        elapsed = result.get("elapsedSec", 0)
        last_err = result.get("lastError", "")

        if status == "completed" and db_n > 0:
            print(f"  [PASS] {name}: read={read:,} ins={inserted:,} err={errors} db={db_n:,} {elapsed:.1f}s", flush=True)
        elif status == "completed" and db_n == 0:
            print(f"  [WARN] {name}: ETL 报 completed 但 DB 行数=0! 可能事务回滚", flush=True)
            # 重试一次
            print(f"  重试 {name} ...", flush=True)
            if wait_idle():
                try:
                    trigger_import(path, mode)
                    result2 = wait_complete(name)
                    db_n2 = db_count(table_map[name])
                    print(f"  重试结果: status={result2['status']} db={db_n2:,}", flush=True)
                except Exception as e:
                    print(f"  重试失败: {e}", flush=True)
        else:
            print(f"  [FAIL] {name}: status={status} err={last_err}", flush=True)
            break

    # 最终验证
    print("\n===== 最终 DB 验证 =====", flush=True)
    for t in ["products", "cross_references", "machine_applications"]:
        n = db_count(t)
        print(f"  {t}: {n:,} rows", flush=True)


if __name__ == "__main__":
    main()
