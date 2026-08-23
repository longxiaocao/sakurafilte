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
print("Step 7: 产品图片上传 + 字段名验证 (BUG FIX D: imageUrl 而非 url)")
print("=" * 60)
# 先找一个产品 id
pg = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = pg.cursor()
cur.execute("SELECT id FROM products LIMIT 1")
row = cur.fetchone()
pg.close()
if row:
    pid = row[0]

    # 改进 2: 先上传 1 张测试图片, 再验证字段名
    import base64
    # 1x1 像素 PNG (透明)
    png_b64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="
    png_bytes = base64.b64decode(png_b64)

    # multipart/form-data 上传
    boundary = "----TestBoundary1234567890"
    body = b"--" + boundary.encode() + b"\r\n"
    body += b'Content-Disposition: form-data; name="file"; filename="test.png"\r\n'
    body += b"Content-Type: image/png\r\n\r\n"
    body += png_bytes + b"\r\n"
    body += b"--" + boundary.encode() + b"--\r\n"

    req = urllib.request.Request(
        f"{BASE}/api/admin/products/{pid}/images/1",
        data=body,
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}", "X-Admin-Token": TOKEN},
        method="POST"
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            upload_code = resp.status
            upload_resp = json.loads(resp.read())
    except urllib.error.HTTPError as e:
        upload_code = e.code
        try:
            upload_resp = json.loads(e.read())
        except:
            upload_resp = {}

    record("7.1 上传测试图片", upload_code == 200 or upload_code == 201,
           f"HTTP {upload_code}")

    if upload_code in (200, 201):
        # 验证返回的字段名
        has_image_url = "imageUrl" in upload_resp
        has_url = "url" in upload_resp
        has_size_bytes = "sizeBytes" in upload_resp
        has_file_size = "fileSize" in upload_resp
        record("7.2 图片字段名 imageUrl (非 url)", has_image_url and not has_url,
               f"imageUrl={'有' if has_image_url else '无'} url={'有' if has_url else '无'}")
        record("7.3 图片字段名 sizeBytes (非 fileSize)", has_size_bytes and not has_file_size,
               f"sizeBytes={'有' if has_size_bytes else '无'} fileSize={'有' if has_file_size else '无'}")

        # 验证 list 端点也返回正确字段名
        code, r = api("GET", f"/api/admin/products/{pid}/images")
        if code == 200 and isinstance(r, list) and len(r) > 0:
            first_img = r[0]
            list_has_image_url = "imageUrl" in first_img
            list_has_size_bytes = "sizeBytes" in first_img
            record("7.4 list 端点字段名一致", list_has_image_url and list_has_size_bytes,
                   f"imageUrl={'有' if list_has_image_url else '无'} sizeBytes={'有' if list_has_size_bytes else '无'}")
        else:
            record("7.4 list 端点字段名一致", False, f"list 返回空或错误 (HTTP {code})")
    else:
        record("7.2 图片字段名 imageUrl", False, "上传失败, 跳过")
        record("7.3 图片字段名 sizeBytes", False, "上传失败, 跳过")
        record("7.4 list 端点字段名一致", False, "上传失败, 跳过")
else:
    record("7.1 上传测试图片", False, "无产品数据")
    record("7.2 图片字段名 imageUrl", False, "跳过")
    record("7.3 图片字段名 sizeBytes", False, "跳过")
    record("7.4 list 端点字段名一致", False, "跳过")

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
