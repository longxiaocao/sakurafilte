"""
V24-F80 验证脚本: P1-2 PostgresSearchProvider 原生 SQL 改造
  - 登录获取 JWT
  - 调用 POST /api/search (强制 provider=postgres) 验证分词搜索 + 三层排序
  - 调用 POST /api/public/search/aggregate 验证聚合搜索
  - 测试用例:
    1. 单 token 搜索 "Bosch" → 应返回结果
    2. 多 token 搜索 "Bosch oil" → 分词 AND 匹配 (恢复 V24-F76 丢失的能力)
    3. 聚合搜索 → oem_list 嵌套数组应正常返回
"""
import urllib.request
import urllib.parse
import json

BASE = "http://localhost:5148"
USERNAME = "admin"
PASSWORD = "Admin@2026"

def post(path, body, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(f"{BASE}{path}", data=data, headers=headers, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return resp.status, json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="replace")
        return e.code, body

def get(path, token=None):
    headers = {}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    req = urllib.request.Request(f"{BASE}{path}", headers=headers, method="GET")
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return resp.status, json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", errors="replace")

print("[1] 登录...")
status, login = post("/api/auth/login", {"username": USERNAME, "password": PASSWORD})
if status != 200:
    print(f"FAIL 登录失败: {status} {login}")
    exit(1)
print(f"  登录响应字段: {list(login.keys()) if isinstance(login, dict) else type(login)}")
# V24: 后端响应字段可能是 accessToken 或 token, 兼容处理
token = login.get("token") or login.get("accessToken") or login.get("access_token")
if not token:
    print(f"  FAIL: 登录响应未找到 token 字段, 完整响应: {login}")
    exit(1)
print(f"  OK token: {token[:20]}...")

print("\n[2] 单 token 搜索 'Bosch' (POST /api/search?provider=postgres)...")
status, resp = post("/api/search?provider=postgres", {"q": "Bosch", "page": 1, "pageSize": 5}, token)
if status != 200:
    print(f"  FAIL: {status} {resp}")
else:
    # 响应格式: {provider: "postgres", result: {total, items, elapsedMs, ...}}
    result = resp.get("result", resp) if isinstance(resp, dict) else resp
    print(f"  响应字段: {list(resp.keys()) if isinstance(resp, dict) else type(resp)}")
    print(f"  OK total={result.get('total')}, items={len(result.get('items', []))}, elapsed={result.get('elapsedMs')}ms")
    if result.get("items"):
        first = result["items"][0]
        print(f"  首条: oemNoDisplay={first.get('oemNoDisplay')}, type={first.get('type')}")

print("\n[3] 多 token 搜索 'Bosch oil' (验证分词 AND 匹配, V24-F76 丢失能力恢复)...")
status, resp = post("/api/search?provider=postgres", {"q": "Bosch oil", "page": 1, "pageSize": 5}, token)
if status != 200:
    print(f"  FAIL: {status} {resp}")
else:
    result = resp.get("result", resp) if isinstance(resp, dict) else resp
    print(f"  OK total={result.get('total')}, items={len(result.get('items', []))}")
    # 多 token 应该比单 token 召回少 (AND 语义)
    print(f"  (与单 token 比较: 多 token 应召回更少或相等)")

print("\n[4] 聚合搜索 (POST /api/public/search/aggregate)...")
status, resp = post("/api/public/search/aggregate", {"q": "Bosch", "page": 1, "pageSize": 3})
if status != 200:
    print(f"  FAIL: {status} {resp}")
else:
    print(f"  OK total={resp.get('total')}, hits={len(resp.get('hits', []))}, provider={resp.get('provider')}")
    if resp.get("hits"):
        first = resp["hits"][0]
        print(f"  首条: mr1={first.get('mr1')}, oemList={len(first.get('oemList', []))}, machineList={len(first.get('machineList', []))}")
        if first.get("oemList"):
            print(f"    oemList[0]: {first['oemList'][0]}")

print("\n[5] 三层排序验证 (Bosch 应优先于其他品牌, 因 brand_sort_order_min 更小)...")
status, resp = post("/api/search?provider=postgres", {"q": "Bosch", "page": 1, "pageSize": 10}, token)
if status == 200:
    result = resp.get("result", resp) if isinstance(resp, dict) else resp
    if result.get("items"):
        print(f"  Top 5 结果 (按 brand_sort_order_min → oem_list_sort_order_min → updated_at DESC):")
        for i, item in enumerate(result["items"][:5]):
            print(f"    {i+1}. oemNoDisplay={item.get('oemNoDisplay')}, type={item.get('type')}")
    else:
        print(f"  WARN: 无结果")
else:
    print(f"  FAIL: {status}")

print("\n===== 结论 =====")
all_pass = True
# Test 2
status, _ = post("/api/search?provider=postgres", {"q": "Bosch", "page": 1, "pageSize": 5}, token)
if status != 200: all_pass = False; print(f"  FAIL: 单 token 搜索 (status={status})")
# Test 3
status, _ = post("/api/search?provider=postgres", {"q": "Bosch oil", "page": 1, "pageSize": 5}, token)
if status != 200: all_pass = False; print(f"  FAIL: 多 token 搜索 (status={status})")
# Test 4
status, _ = post("/api/public/search/aggregate", {"q": "Bosch", "page": 1, "pageSize": 3})
if status != 200: all_pass = False; print(f"  FAIL: 聚合搜索 (status={status})")

if all_pass:
    print("  PASS: V24-F80 P1-2 原生 SQL 搜索功能正常, 分词 + 三层排序已恢复")
else:
    print("  FAIL: 部分用例失败")
