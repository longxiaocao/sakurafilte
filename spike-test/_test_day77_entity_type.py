"""Day 7.7 端到端验证: etl_progress_log 三个 entity 都正确记录"""
import json
import time
import urllib.request
import urllib.error
import psycopg2

API = "http://localhost:5148"

def post(path, body, retries=3):
    last = None
    for i in range(retries):
        try:
            req = urllib.request.Request(
                API + path,
                data=json.dumps(body).encode('utf-8'),
                headers={'Content-Type': 'application/json'},
                method='POST',
            )
            with urllib.request.urlopen(req, timeout=120) as r:
                return json.loads(r.read())
        except urllib.error.HTTPError as e:
            if e.code == 409:
                # 等待 in-flight 完成
                time.sleep(2)
                last = e
                continue
            raise
    raise last

def get_status():
    with urllib.request.urlopen(API + "/api/etl/status", timeout=5) as r:
        return json.loads(r.read())

def wait_idle(timeout=60):
    t0 = time.time()
    while time.time() - t0 < timeout:
        s = get_status()
        if s.get('status') in ('completed', 'failed', 'idle'):
            return s
        time.sleep(1)
    raise TimeoutError(f"ETL did not finish in {timeout}s, last status: {s}")

# 1) products upsert (期望 entity=products)
print("=== 1) products upsert ===")
r = post("/api/etl/import", {"jsonlPath": r"d:\projects\sakurafilter\spike-test\output\cleaned\products.jsonl", "mode": "upsert"})
s = wait_idle()
print(f"  status={s.get('status')} read={s.get('read')} inserted={s.get('inserted')} duration={s.get('elapsedSec'):.3f}s")

# 2) xrefs upsert (期望 entity=xrefs)
print("\n=== 2) xrefs upsert ===")
r = post("/api/etl/import-xrefs", {"jsonlPath": r"d:\projects\sakurafilter\spike-test\output\cleaned\xrefs.jsonl", "mode": "upsert"})
s = wait_idle()
print(f"  status={s.get('status')} read={s.get('read')} inserted={s.get('inserted')} duration={s.get('elapsedSec'):.3f}s")

# 3) apps upsert (期望 entity=apps)
print("\n=== 3) apps upsert ===")
r = post("/api/etl/import-apps", {"jsonlPath": r"d:\projects\sakurafilter\spike-test\output\cleaned\apps.jsonl", "mode": "upsert"})
s = wait_idle()
print(f"  status={s.get('status')} read={s.get('read')} inserted={s.get('inserted')} duration={s.get('elapsedSec'):.3f}s")

# 4) 验证 etl_progress_log
print("\n=== 4) 验证 etl_progress_log ===")
time.sleep(2)  # 等异步落库完成
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("""
    SELECT id, entity_type, mode, status, read_count, inserted_count,
           skipped_count, skipped_duplicate, duration_sec
    FROM etl_progress_log
    ORDER BY id
""")
rows = cur.fetchall()
print(f"  etl_progress_log 共 {len(rows)} 行:")
for r in rows:
    print(f"    id={r[0]:3} entity={r[1]:10} mode={r[2]:12} status={r[3]:10} read={r[4]:5} ins={r[5]:5} skip={r[6]:3} dup={r[7]:3} dur={r[8]:.3f}s")

# 关键断言
entities = {r[1] for r in rows}
print(f"\n  唯一 entity_type: {entities}")
assert 'products' in entities, f"缺少 products: {entities}"
assert 'xrefs' in entities, f"缺少 xrefs (被错记为 apps 是 bug): {entities}"
assert 'apps' in entities, f"缺少 apps: {entities}"
assert 'ETL_ENTITY' not in entities, f"残留脏数据 ETL_ENTITY: {entities}"
print("\n  ✅ 三个 entity_type 都正确")
conn.close()
