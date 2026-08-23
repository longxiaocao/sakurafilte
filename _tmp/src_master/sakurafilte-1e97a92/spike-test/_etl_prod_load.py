# 生产 ETL 真实数据导入演练 (50K products / 623K xrefs / 776K apps)
# 用法: python _etl_prod_load.py  (读 .env.prod 的 ADMIN_DEV_TOKEN, 走 https://localhost 自签名)
import json, os, re, ssl, sys, time, urllib.request

BASE = "https://localhost"
ENV_FILE = r"d:\projects\sakurafilter\.env.prod"

def load_token():
    with open(ENV_FILE, encoding="utf-8") as f:
        for line in f:
            if line.startswith("ADMIN_DEV_TOKEN="):
                return line.split("=", 1)[1].strip()
    raise SystemExit("未找到 ADMIN_DEV_TOKEN")

TOKEN = load_token()
CTX = ssl._create_unverified_context()  # 自签名演练证书

def call(method, path, body=None):
    data = json.dumps(body).encode() if body else None
    req = urllib.request.Request(f"{BASE}{path}", data=data, method=method,
                                 headers={"Content-Type": "application/json", "X-Admin-Token": TOKEN})
    with urllib.request.urlopen(req, timeout=30, context=CTX) as r:
        return json.loads(r.read() or "null")

def wait_complete(entity, timeout_min=90):
    t0 = time.time()
    last_pct = -1
    while time.time() - t0 < timeout_min * 60:
        p = call("GET", "/api/etl/status")
        if p.get("entityType") != entity and p.get("status") not in ("idle", "completed"):
            print(f"  [警告] 状态属于其他实体: {p.get('entityType')} {p.get('status')}")
        pct = p.get("progressPct", p.get("percent", 0))
        cur, total = p.get("current", 0), p.get("total", 0)
        if pct != last_pct or int(time.time()) % 30 == 0:
            print(f"  [{entity}] {p.get('status')} {cur}/{total} {pct}% elapsed={p.get('elapsedSec','?')}s eta={p.get('etaSec','?')}s", flush=True)
            last_pct = pct
        if p.get("status") == "completed":
            print(f"  [{entity}] [OK] 完成 (rows={p.get('processedRows', p.get('rows', '?'))}, 耗时 {time.time()-t0:.0f}s)", flush=True)
            return True
        if p.get("status") == "failed":
            print(f"  [{entity}] [FAIL] 失败: {json.dumps(p, ensure_ascii=False)[:300]}", flush=True)
            return False
        time.sleep(10)
    print(f"  [{entity}] [TIMEOUT] 超时 {timeout_min}min", flush=True)
    return False

STEPS = [
    ("products", "/api/etl/import", {"jsonlPath": "/tmp/etl/products.jsonl", "entityType": "products", "mode": "full-load", "cascade": True}),
    ("xrefs",    "/api/etl/import", {"jsonlPath": "/tmp/etl/xrefs.jsonl",    "entityType": "xrefs",    "mode": "full-load"}),
    ("apps",     "/api/etl/import", {"jsonlPath": "/tmp/etl/apps.jsonl",     "entityType": "apps",     "mode": "full-load"}),
]

if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "all"
    # 断点续跑: 检查当前 ETL 状态, 若某实体已完成则跳过触发
    try:
        st = call("GET", "/api/etl/status")
        print(f"当前 ETL 状态: {st.get('status')} entity={st.get('entityType')} rows={st.get('rowsTotal')}", flush=True)
    except Exception as e:
        print(f"状态查询失败(继续): {e}", flush=True)
    for entity, path, body in STEPS:
        if mode != "all" and entity != mode:
            continue
        if entity == "products":
            try:
                st = call("GET", "/api/etl/status")
                if st.get("entityType") == entity and st.get("status") in ("completed", "running"):
                    print(f"=== {entity} 已在运行/完成 (status={st.get('status')}), 直接轮询 ===", flush=True)
                    wait_complete(entity)
                    continue
            except Exception:
                pass
        print(f"=== 触发 {entity} 导入 ===", flush=True)
        try:
            resp = call("POST", path, body)
            print(f"  202 accepted: {json.dumps(resp, ensure_ascii=False)[:150]}", flush=True)
        except urllib.error.HTTPError as e:
            print(f"  触发失败 {e.code}: {e.read().decode()[:200]}", flush=True)
            sys.exit(1)
        if not wait_complete(entity):
            sys.exit(1)
    print("\n=== 全部导入完成 ===")
