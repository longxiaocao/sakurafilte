"""Phase 4: 端到端用户流程测试

覆盖完整用户旅程:
1. ETL 触发 (统一端点 entityType + cascade)
2. ETL 进度查询 + 历史
3. 公开搜索 (含分页 cursor)
4. 产品详情
5. 对比接口
6. 8 字典 list + typeahead
7. 字典 CRUD (Machine create 含 machineCategory 验证 BUG FIX B)
8. 产品图片字段名验证 (BUG FIX D: imageUrl 而非 url)
"""
import json
import time
import urllib.request
import urllib.error
import psycopg2

BASE = "http://localhost:5148"
TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
ADMIN_HEADERS = {"Content-Type": "application/json", "X-Admin-Token": TOKEN}
PUBLIC_HEADERS = {"Content-Type": "application/json"}

results = []

def record(name, ok, detail=""):
    results.append((name, ok, detail))
    status = "[PASS]" if ok else "[FAIL]"
    print(f"  {status} {name}" + (f" - {detail}" if detail else ""))

def api(method, path, body=None, admin=True):
    headers = ADMIN_HEADERS if admin else PUBLIC_HEADERS
    data = json.dumps(body).encode("utf-8") if body else None
    req = urllib.request.Request(f"{BASE}{path}", data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return r.status, json.loads(r.read()) if r.headers.get('content-type', '').startswith('application/json') else r.read()
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read())
        except:
            return e.code, {}

def wait_etl_idle(timeout=120):
    for _ in range(timeout):
        code, s = api("GET", "/api/etl/status")
        if s.get("status") in ("idle", "completed", "failed", "cancelled"):
            return True
        time.sleep(1)
    return False

# ===== Step 1: ETL 触发 (统一端点) =====
print("=" * 60)
print("Step 1: ETL 触发 (统一端点 entityType + cascade)")
print("=" * 60)
wait_etl_idle()

# 1.1 触发 products full-load cascade=true
code, resp = api("POST", "/api/etl/import", {
    "jsonlPath": r"d:\projects\sakurafilter\spike-test\output\cleaned\products.jsonl",
    "mode": "full-load", "entityType": "products", "cascade": True
})
record("1.1 触发 products full-load cascade=true", code == 202, f"HTTP {code}")
time.sleep(3)
wait_etl_idle()
code, s = api("GET", "/api/etl/status")
record("1.2 products ETL 完成", s.get("status") == "completed" and s.get("inserted", 0) > 0,
       f"inserted={s.get('inserted')}")

# 1.3 触发 xrefs (验证 entityType 路由 - BUG FIX A)
wait_etl_idle()
code, resp = api("POST", "/api/etl/import", {
    "jsonlPath": r"d:\projects\sakurafilter\spike-test\output\cleaned\xrefs.jsonl",
    "mode": "full-load", "entityType": "xrefs"
})
record("1.3 触发 xrefs (entityType 路由)", code == 202, f"HTTP {code}")
time.sleep(3)
wait_etl_idle()
code, s = api("GET", "/api/etl/status")
record("1.4 xrefs ETL 完成", s.get("status") == "completed" and s.get("inserted", 0) > 0,
       f"inserted={s.get('inserted')}")

# 1.5 触发 apps
wait_etl_idle()
code, resp = api("POST", "/api/etl/import", {
    "jsonlPath": r"d:\projects\sakurafilter\spike-test\output\cleaned\apps.jsonl",
    "mode": "full-load", "entityType": "apps"
})
record("1.5 触发 apps (entityType 路由)", code == 202, f"HTTP {code}")
time.sleep(3)
wait_etl_idle()
code, s = api("GET", "/api/etl/status")
record("1.6 apps ETL 完成", s.get("status") == "completed" and s.get("inserted", 0) > 0,
       f"inserted={s.get('inserted')}")

# ===== Step 2: 公开搜索 =====
print("\n" + "=" * 60)
print("Step 2: 公开搜索")
print("=" * 60)
# 用 oemNo3 (cross_references.oem_no_3, 实际值如 DON-33240)
code, r = api("GET", "/api/public/search?oemNo3=DON&pageSize=5", admin=False)
record("2.1 公开搜索 oemNo3=DON", code == 200 and len(r.get("items", [])) > 0,
       f"items={len(r.get('items', []))}")

code, r = api("GET", "/api/public/search?oemNo3=DON&pageSize=3", admin=False)
record("2.2 分页搜索 (page)", code == 200, f"items={len(r.get('items', []))} hasMore={r.get('hasMore')}")

code, r = api("GET", "/api/search/health", admin=False)
record("2.3 搜索健康检查", code == 200, f"status={r.get('status')}")

# ===== Step 3: 产品详情 =====
print("\n" + "=" * 60)
print("Step 3: 产品详情")
print("=" * 60)
code, r = api("GET", "/api/public/search?oemNo3=DON&pageSize=1", admin=False)
if r.get("items"):
    oem = r["items"][0].get("oemNoDisplay") or r["items"][0].get("oem_no_display")
    if oem:
        code, r = api("GET", f"/api/products/{oem}", admin=False)
        record("3.1 产品详情", code == 200, f"oem={oem}")
    else:
        record("3.1 产品详情", False, "无 oemNoDisplay 字段")
else:
    record("3.1 产品详情", False, "搜索无结果")

# ===== Step 4: 对比接口 =====
print("\n" + "=" * 60)
print("Step 4: 对比接口")
print("=" * 60)
code, r = api("GET", "/api/public/search?oemNo3=DON&pageSize=3", admin=False)
if r.get("items") and len(r["items"]) >= 2:
    ids = [it.get("id") for it in r["items"][:2] if it.get("id")]
    if len(ids) >= 2:
        code, r = api("POST", "/api/admin/products/compare", {"ids": ids})
        record("4.1 对比 2 个产品", code == 200, f"HTTP {code}")
    else:
        record("4.1 对比 2 个产品", False, "无 id 字段")
else:
    record("4.1 对比 2 个产品", False, "搜索结果不足 2 个")

# ===== Step 5: 8 字典 list + typeahead =====
print("\n" + "=" * 60)
print("Step 5: 8 字典 list + typeahead")
print("=" * 60)
dicts = [
    ("oem-brands", "oemBrand"), ("product-name1s", "productName1"), ("product-name2s", "productName2"),
    ("types", "type"), ("oem-no3s", "oemNo3"), ("medias", "mediaName"),
    ("machines", "machineBrand"), ("engines", "engineBrand")
]
for dict_name, _ in dicts:
    code, r = api("GET", f"/api/admin/dict/{dict_name}?pageSize=5")
    items = r.get("items", []) if isinstance(r, dict) else []
    record(f"5.{dict_name} list", code == 200 and len(items) > 0, f"items={len(items)}")

    code, r = api("GET", f"/api/admin/dict/{dict_name}/typeahead?q=a&limit=5")
    items = r if isinstance(r, list) else r.get("items", [])
    record(f"5.{dict_name} typeahead", code == 200, f"suggestions={len(items) if isinstance(items, list) else 0}")

# ===== Step 6: Machine create 含 machineCategory (BUG FIX B 验证) =====
print("\n" + "=" * 60)
print("Step 6: Machine create 含 machineCategory (BUG FIX B)")
print("=" * 60)
test_brand = f"TestMachine_{int(time.time())}"
code, r = api("POST", "/api/admin/dict/machines", {
    "machineBrand": test_brand,
    "machineModel": "TM-100",
    "machineName": "测试机型",
    "sortOrder": 999,
    "machineCategory": "engineering"  # BUG FIX B: 之前 create 漏传, 后端默认 "others"
})
record("6.1 Machine create (含 machineCategory=engineering)", code == 201, f"HTTP {code}")
if code == 201 and isinstance(r, dict):
    machine_id = r.get("id")
    # 验证 machineCategory 是否正确写入
    pg = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
    cur = pg.cursor()
    cur.execute("SELECT machine_category FROM dict_machine WHERE id = %s", (machine_id,))
    actual_cat = cur.fetchone()[0]
    pg.close()
    record("6.2 machineCategory 写入正确", actual_cat == "engineering", f"actual={actual_cat}")

    # 清理测试数据
    api("DELETE", f"/api/admin/dict/machines/{machine_id}")

# ===== Step 7: 产品图片字段名验证 (BUG FIX D) =====
print("\n" + "=" * 60)
print("Step 7: 产品图片字段名验证 (BUG FIX D: imageUrl 而非 url)")
print("=" * 60)
# 先找一个产品 id
pg = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = pg.cursor()
cur.execute("SELECT id FROM products LIMIT 1")
row = cur.fetchone()
pg.close()
if row:
    pid = row[0]
    code, r = api("GET", f"/api/admin/products/{pid}/images")
    if code == 200 and isinstance(r, list) and len(r) > 0:
        # 验证字段名是 imageUrl 而非 url
        first_img = r[0]
        has_image_url = "imageUrl" in first_img
        has_url = "url" in first_img
        record("7.1 图片字段名 imageUrl (非 url)", has_image_url and not has_url,
               f"imageUrl={'有' if has_image_url else '无'} url={'有' if has_url else '无'}")
        record("7.2 图片字段名 sizeBytes (非 fileSize)", "sizeBytes" in first_img,
               f"sizeBytes={'有' if 'sizeBytes' in first_img else '无'}")
    else:
        record("7.1 图片字段名验证", False, f"无图片数据 (HTTP {code})")
        record("7.2 图片字段名验证", False, "跳过")
else:
    record("7.1 图片字段名验证", False, "无产品数据")
    record("7.2 图片字段名验证", False, "跳过")

# ===== Step 8: ETL 历史 + 聚合 =====
print("\n" + "=" * 60)
print("Step 8: ETL 历史 + 聚合")
print("=" * 60)
code, r = api("GET", "/api/admin/etl/history?pageSize=5")
items = r.get("items", []) if isinstance(r, dict) else []
record("8.1 ETL 历史列表", code == 200 and len(items) > 0, f"items={len(items)}")

code, r = api("GET", "/api/admin/etl/history/aggregate")
record("8.2 ETL 聚合", code == 200, f"HTTP {code}")

# ===== 汇总 =====
print("\n" + "=" * 60)
print("Phase 4 端到端测试汇总")
print("=" * 60)
total = len(results)
passed = sum(1 for _, ok, _ in results if ok)
failed = total - passed
for name, ok, detail in results:
    status = "✓" if ok else "✗"
    print(f"  {status} {name}" + (f" - {detail}" if detail else ""))
print(f"\n总计: {passed}/{total} 通过, {failed} 失败")
print("=" * 60)
