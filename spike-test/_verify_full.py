"""全链路数据验证: DB 数据完整性 + API 端点 + 前端字典 typeahead"""
import json
import urllib.request

BASE = "http://localhost:5148"
TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"

def api_get(path, admin=False):
    headers = {"X-Admin-Token": TOKEN} if admin else {}
    req = urllib.request.Request(f"{BASE}{path}", headers=headers)
    with urllib.request.urlopen(req, timeout=10) as r:
        return json.loads(r.read())

print("===== 1. DB 数据完整性 =====", flush=True)
import psycopg2
c = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = c.cursor()

tables = {
    'products': None, 'cross_references': None, 'machine_applications': None,
    'dict_product_name1': None, 'dict_product_name2': None, 'dict_type': None,
    'dict_oem_no3': None, 'dict_media': None, 'dict_machine': None, 'dict_engine': None,
}
for t in tables:
    cur.execute(f"SELECT count(*) FROM {t}")
    tables[t] = cur.fetchone()[0]
    print(f"  {t}: {tables[t]:,}", flush=True)

# product_name_1/2 验证
cur.execute("SELECT count(*) FROM products WHERE product_name_1 IS NOT NULL")
pn1 = cur.fetchone()[0]
cur.execute("SELECT count(*) FROM products WHERE product_name_2 IS NOT NULL")
pn2 = cur.fetchone()[0]
print(f"\n  product_name_1 NOT NULL: {pn1:,} ({pn1/tables['products']:.1%})", flush=True)
print(f"  product_name_2 NOT NULL: {pn2:,} ({pn2/tables['products']:.1%})", flush=True)

# xrefs 关联验证
cur.execute("SELECT count(DISTINCT product_id) FROM cross_references")
xref_products = cur.fetchone()[0]
print(f"  xrefs 关联产品数: {xref_products:,} / {tables['products']:,}", flush=True)

# apps 关联验证
cur.execute("SELECT count(DISTINCT product_id) FROM machine_applications")
app_products = cur.fetchone()[0]
print(f"  apps 关联产品数:  {app_products:,} / {tables['products']:,}", flush=True)

c.close()

print("\n===== 2. API 搜索端点 =====", flush=True)
try:
    result = api_get("/api/search?q=AIR&page=1&pageSize=5")
    total = result.get('total', 0)
    items = len(result.get('items', []))
    print(f"  /api/search?q=AIR: total={total}, items={items}", flush=True)
except Exception as e:
    print(f"  /api/search 失败: {e}", flush=True)

try:
    result = api_get("/api/search/health")
    print(f"  /api/search/health: {result}", flush=True)
except Exception as e:
    print(f"  /api/search/health 失败: {e}", flush=True)

print("\n===== 3. Admin 字典 typeahead =====", flush=True)
dict_endpoints = [
    "/api/admin/dict/product-name1/typeahead?q=Air",
    "/api/admin/dict/product-name2/typeahead?q=Standard",
    "/api/admin/dict/types/typeahead?q=Air",
    "/api/admin/dict/medias/typeahead?q=Cell",
    "/api/admin/dict/machines/typeahead?q=Cat",
    "/api/admin/dict/engines/typeahead?q=Cum",
    "/api/admin/dict/oem-no3s/typeahead?q=BOS",
    "/api/admin/oem-brands/typeahead?q=Bosch",
]
for ep in dict_endpoints:
    try:
        result = api_get(ep, admin=True)
        n = len(result) if isinstance(result, list) else result.get('total', '?')
        print(f"  {ep.split('/typeahead')[0].split('/')[-1]}: {n} results", flush=True)
    except Exception as e:
        print(f"  {ep}: 失败 - {e}", flush=True)

print("\n===== 4. Admin 产品列表 =====", flush=True)
try:
    result = api_get("/api/admin/products?page=1&pageSize=3", admin=True)
    items = result.get('items', [])
    total = result.get('total', 0)
    print(f"  /api/admin/products: total={total}, sample={len(items)}", flush=True)
    if items:
        p = items[0]
        print(f"    sample: oem={p.get('oemNoDisplay')} type={p.get('type')} pn1={p.get('productName1')} pn2={p.get('productName2')}", flush=True)
except Exception as e:
    print(f"  /api/admin/products 失败: {e}", flush=True)

print("\n[DONE] 全链路验证完成", flush=True)
