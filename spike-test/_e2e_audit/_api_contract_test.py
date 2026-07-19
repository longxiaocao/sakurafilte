# V24-F74: 前后端联动验证 - API 契约测试 v2 (修正路径)
# V24-F92 (v27-9): 支持 BACKEND_URL 环境变量, 适配 CI (CI 用默认 5148, 本地可覆盖)
import urllib.request
import urllib.error
import json
import urllib.parse
import os

BASE = os.environ.get("BACKEND_URL", "http://localhost:5148")

def test_api(method, path, headers=None, body=None, desc=""):
    url = f"{BASE}{path}"
    h = headers or {}
    try:
        data = body.encode('utf-8') if body else None
        req = urllib.request.Request(url, data=data, method=method, headers=h)
        with urllib.request.urlopen(req, timeout=10) as resp:
            status = resp.status
            content = resp.read()
            print(f"[OK]   {method} {path} -> {status} ({len(content)} bytes) - {desc}")
            return {"status": status, "ok": True, "body": content.decode('utf-8', errors='replace')}
    except urllib.error.HTTPError as e:
        body = ""
        try:
            body = e.read().decode('utf-8', errors='replace')[:150]
        except: pass
        print(f"[ERR]  {method} {path} -> {e.code} - {desc} | {e.reason} | body={body}")
        return {"status": e.code, "ok": False, "error": e.reason, "body": body}
    except Exception as e:
        msg = str(e)[:80]
        print(f"[FAIL] {method} {path} -> ERR - {desc} | {msg}")
        return {"status": 0, "ok": False, "error": msg}

# 先登录获取 JWT
# V24-F92 (v27-9): 支持 USE_DEV_TOKEN 模式, CI 用 X-Admin-Token 绕过 JWT 登录
#   - 本地默认: JWT 登录 (admin / Admin@2026)
#   - CI 模式 (USE_DEV_TOKEN=1): 用 ADMIN_TOKEN 环境变量作 X-Admin-Token, 适配空库 CI
USE_DEV_TOKEN = os.environ.get("USE_DEV_TOKEN", "0") == "1"
ADMIN_TOKEN = os.environ.get("ADMIN_TOKEN", "")
jwt = None
jwt_h = {}

if USE_DEV_TOKEN:
    print("=" * 70)
    print("0. CI 模式: 使用 X-Admin-Token (跳过 JWT 登录, 适配空库)")
    print("=" * 70)
    if not ADMIN_TOKEN:
        print("[FAIL] USE_DEV_TOKEN=1 但 ADMIN_TOKEN 环境变量未设置")
    else:
        jwt_h = {"X-Admin-Token": ADMIN_TOKEN}
        print(f"[OK] X-Admin-Token 已设置 ({len(ADMIN_TOKEN)} 字符)")
else:
    print("=" * 70)
    print("0. JWT 登录 (获取 accessToken)")
    print("=" * 70)
    login_body = json.dumps({"username": "admin", "password": "Admin@2026"})
    login_r = test_api("POST", "/api/auth/login", {"Content-Type": "application/json"}, login_body, "JWT 登录")
    if login_r.get("ok"):
        try:
            data = json.loads(login_r["body"])
            jwt = data.get("accessToken") or data.get("access_token")
            if jwt:
                print(f"  [OK] JWT 获取成功 ({len(jwt)} 字符)")
            else:
                print(f"  [WARN] 登录响应无 accessToken: keys={list(data.keys())}")
        except Exception as e:
            print(f"  [FAIL] JWT 解析失败: {e}")

    jwt_h = {"Authorization": f"Bearer {jwt}"} if jwt else {}

print()
print("=" * 70)
print("1. 公开 API (无需认证) - 修正路径")
print("=" * 70)
# POST /api/search (搜索)
search_body = json.dumps({"q": "oil", "page": 1, "pageSize": 5})
r = test_api("POST", "/api/search", {"Content-Type": "application/json"}, search_body, "公开搜索 POST")

# GET /api/public/search (公开搜索 v2)
test_api("GET", "/api/public/search?oemBrand=Bosch&page=1&pageSize=5", desc="公开搜索 GET (oemBrand=Bosch)")

# POST /api/public/search/aggregate (聚合搜索)
agg_body = json.dumps({"q": "oil", "page": 1, "pageSize": 5})
test_api("POST", "/api/public/search/aggregate", {"Content-Type": "application/json"}, agg_body, "聚合搜索 POST")

# GET /api/public/typeahead/{field} (typeahead)
#   field ∈ oem-brand | oem-no2 | oem-no3 | machine-brand | machine-model | model-name | engine-brand | engine-type
#   修正: 前端 PublicSearchView.vue 使用 kebab-case (oem-brand), 之前测试脚本误用下划线
test_api("GET", "/api/public/typeahead/oem-brand?q=B&limit=10", desc="typeahead oem-brand (kebab-case)")
test_api("GET", "/api/public/typeahead/machine-brand?q=C&limit=10", desc="typeahead machine-brand (kebab-case)")

# GET /api/search/health
test_api("GET", "/api/search/health", desc="搜索健康检查")

# GET /sitemap.xml
test_api("GET", "/sitemap.xml", desc="sitemap")

print()
print("=" * 70)
print("2. 后台 API (JWT Bearer 认证)")
print("=" * 70)
test_api("GET", "/api/admin/products?page=1&pageSize=5", jwt_h, desc="产品列表")
test_api("GET", "/api/admin/dict/oem-brands?page=1&pageSize=5", jwt_h, desc="字典 OEM 品牌")
test_api("GET", "/api/admin/dict/types?page=1&pageSize=5", jwt_h, desc="字典类型")
test_api("GET", "/api/admin/etl/history?limit=5", jwt_h, desc="ETL 历史")
test_api("GET", "/api/admin/alerts/rules", jwt_h, desc="告警规则")
test_api("GET", "/api/admin/users", jwt_h, desc="用户列表")
test_api("GET", "/api/admin/xrefs/reorder/brands", jwt_h, desc="XrefReorder Brands (V24-F73)")
test_api("GET", "/api/admin/xrefs/reorder?oemBrand=Bosch", jwt_h, desc="XrefReorder by brand (V24-F73)")
test_api("GET", "/api/admin/auth/status", jwt_h, desc="auth/status")

print()
print("=" * 70)
print("3. 错误处理验证")
print("=" * 70)
test_api("GET", "/api/admin/products", desc="401 无 token")
test_api("GET", "/api/admin/products", {"Authorization": "Bearer invalid-jwt"}, desc="401 错误 JWT")
test_api("GET", "/api/admin/xrefs/reorder", jwt_h, desc="400 缺少 oemBrand")
test_api("GET", "/api/public/product/NONEXISTENT-OEM-12345", desc="404 不存在的产品")

print()
print("=" * 70)
print("4. 前后端联动: 搜索 -> 产品详情")
print("=" * 70)
# 先搜索拿一个产品 oem
try:
    search_body2 = json.dumps({"q": "oil", "page": 1, "pageSize": 1})
    req = urllib.request.Request(f"{BASE}/api/search",
        data=search_body2.encode('utf-8'), method="POST",
        headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=10) as resp:
        data = json.loads(resp.read().decode('utf-8'))
        # 兼容多种响应结构
        items = data.get("items") or data.get("result", {}).get("items") or data.get("data", {}).get("items") or []
        if not items and isinstance(data, dict):
            # 看下顶层 keys
            print(f"  [INFO] 搜索响应 keys: {list(data.keys())}")
            # 可能是 {provider, result: {items}}
            if "result" in data:
                items = data["result"].get("items", [])
        if items:
            item = items[0]
            oem = item.get("oem2") or item.get("oemNoDisplay") or item.get("mr1") or item.get("oem_no_2")
            print(f"  [INFO] 搜索返回首个产品: oem={oem}, keys={list(item.keys())[:8]}")
            if oem:
                test_api("GET", f"/api/public/product/{urllib.parse.quote(str(oem))}", desc=f"产品详情联动 (oem={oem})")
        else:
            print(f"  [WARN] 搜索返回 0 条, 响应: {json.dumps(data, ensure_ascii=False)[:200]}")
except Exception as e:
    print(f"  [FAIL] 搜索解析失败: {e}")

print()
print("验证完成")
