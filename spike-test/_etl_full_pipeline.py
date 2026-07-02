"""
依次 ETL full-load 导入 products → xrefs → apps
轮询 /api/etl/status 等待每个完成后再触发下一个
"""
import json
import time
import urllib.request

BASE = "http://localhost:5148"
TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
HEADERS = {"Content-Type": "application/json", "X-Admin-Token": TOKEN}

TASKS = [
    ("products", r"d:\projects\sakurafilter\spike-test\output\cleaned\products.jsonl", "full-load"),
    ("xrefs",    r"d:\projects\sakurafilter\spike-test\output\cleaned\xrefs.jsonl", "full-load"),
    ("apps",     r"d:\projects\sakurafilter\spike-test\output\cleaned\apps.jsonl", "full-load"),
]


def get_status():
    req = urllib.request.Request(f"{BASE}/api/etl/status", headers={"X-Admin-Token": TOKEN})
    with urllib.request.urlopen(req) as r:
        return json.loads(r.read())


def trigger_import(jsonl_path, mode):
    body = json.dumps({"jsonlPath": jsonl_path, "mode": mode}).encode("utf-8")
    req = urllib.request.Request(f"{BASE}/api/etl/import", data=body, headers=HEADERS, method="POST")
    with urllib.request.urlopen(req) as r:
        return r.status


def wait_complete(name, timeout=300):
    t0 = time.time()
    while time.time() - t0 < timeout:
        s = get_status()
        if s["status"] in ("completed", "failed", "cancelled"):
            return s
        elapsed = time.time() - t0
        print(f"  [{name}] {s['status']} stage={s['stage']} read={s['read']} inserted={s['inserted']} errors={s['errors']} {elapsed:.0f}s", flush=True)
        time.sleep(2)
    return {"status": "timeout"}


def main():
    for name, path, mode in TASKS:
        print(f"\n===== ETL {name} ({mode}) =====", flush=True)
        print(f"  文件: {path}", flush=True)

        # 等待空闲
        for _ in range(30):
            s = get_status()
            if s["status"] in ("idle", "completed", "failed", "cancelled"):
                break
            time.sleep(1)

        http_code = trigger_import(path, mode)
        print(f"  触发: HTTP {http_code}", flush=True)

        result = wait_complete(name)
        status = result["status"]
        read = result.get("read", 0)
        inserted = result.get("inserted", 0)
        errors = result.get("errors", 0)
        elapsed = result.get("elapsedSec", 0)
        last_err = result.get("lastError", "")

        if status == "completed":
            print(f"  [PASS] {name}: read={read:,} inserted={inserted:,} errors={errors} elapsed={elapsed:.1f}s", flush=True)
        else:
            print(f"  [FAIL] {name}: status={status} error={last_err}", flush=True)
            break

    print("\n===== 全部完成 =====", flush=True)
    s = get_status()
    print(f"最终状态: {s['status']} | read={s['read']:,} | inserted={s['inserted']:,} | errors={s['errors']}", flush=True)


if __name__ == "__main__":
    main()
