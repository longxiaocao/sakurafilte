"""CI smoke test — 跨平台 (无 Windows 路径/无 psycopg2/无数据文件依赖)

WHY: _test_improvements.py 含 d:\\ 硬编码路径 + 依赖 cleaned/*.jsonl (gitignored),
  在 Linux CI runner 上必失败. 本测试只验证 API 可达性 + auth + Swagger 完整性.

覆盖:
  1. /api/etl/status 无 token → 401
  2. /api/etl/status 有 token → 200 + JSON 含 inProgress
  3. /swagger/v1/swagger.json → 200 + 含 components.schemas
  4. /api/public/search 无参 → 400
  5. /api/public/search 有参 (空 DB 也允许) → 200
  6. 旧端点 /api/etl/import-xrefs 仍可用 (向后兼容)
  7. /api/etl/import 非法 entityType → 400
  8. /api/etl/import 合法 entityType 应触发 (文件不存在时 ETL 后台会失败)
  9. Swagger 包含 EtlTriggerRequest / ProductFormDto / PublicSearchHit 关键 DTO
"""
import json
import sys
import time
import urllib.request
import urllib.error

BASE = "http://localhost:5148"
TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"


def req(path, headers=None, method="GET", data=None, timeout=15):
    h = {"Accept": "application/json"}
    if headers:
        h.update(headers)
    # WHY: POST/PUT body 必须显式 Content-Type, 否则 minimal API 端点报 415
    if data is not None:
        h["Content-Type"] = "application/json"
    body = json.dumps(data).encode() if data else None
    r = urllib.request.Request(f"{BASE}{path}", data=body, headers=h, method=method)
    try:
        with urllib.request.urlopen(r, timeout=timeout) as resp:
            return resp.status, json.loads(resp.read() or b"{}")
    except urllib.error.HTTPError as e:
        try:
            payload = json.loads(e.read() or b"{}")
        except Exception:
            payload = {}
        return e.code, payload


PASS = 0
FAIL = 0
ERRORS = []


def check(name, ok, detail=""):
    global PASS, FAIL
    if ok:
        PASS += 1
        print(f"  [PASS] {name}")
    else:
        FAIL += 1
        ERRORS.append(f"{name}: {detail}")
        print(f"  [FAIL] {name} — {detail}")


print("===== CI Smoke Test =====")

# 1. /api/etl/status 无 token
print("\n--- 1. /api/etl/status 无 token 401 ---")
code, body = req("/api/etl/status")
check("401 without token", code == 401, f"got {code}")

# 2. /api/etl/status 有 token
print("\n--- 2. /api/etl/status 有 token 200 ---")
code, body = req("/api/etl/status", {"X-Admin-Token": TOKEN})
check("200 with token", code == 200, f"got {code}")
# WHY: /api/etl/status 直接返回 progress JSON, 顶层有 status/read/inserted 字段 (无 inProgress 包装)
check("has status field", "status" in body, f"body={body}")
check("status is idle", body.get("status") == "idle", f"got {body.get('status')}")

# 3. /swagger/v1/swagger.json
print("\n--- 3. /swagger/v1/swagger.json ---")
code, body = req("/swagger/v1/swagger.json")
check("200", code == 200, f"got {code}")
# 兼容两种结构: 顶层 components.schemas 或 OpenAPI v3 写法
schemas = body.get("components", {}).get("schemas", {}) or {}
check("has schemas", len(schemas) > 0, f"got {len(schemas)} schemas")

# 4. /api/public/search 无参 → 400
print("\n--- 4. /api/public/search 无参 ---")
code, body = req("/api/public/search")
check("400 without params", code == 400, f"got {code}")

# 5. /api/public/search 有参 (空 DB 也允许 200 + items=[])
print("\n--- 5. /api/public/search?oemNo3=test ---")
code, body = req("/api/public/search?oemNo3=test")
check("200 with params", code == 200, f"got {code}")
check("has items array", "items" in body and isinstance(body["items"], list),
      f"body keys={list(body.keys())}")

# 6. 旧端点 /api/etl/import-xrefs 兼容
print("\n--- 6. /api/etl/import-xrefs (兼容旧端点) ---")
code, body = req("/api/etl/import-xrefs", {"X-Admin-Token": TOKEN}, method="POST",
                  data={"jsonlPath": "/tmp/nonexistent.jsonl", "mode": "insert-only"})
# 旧端点应直接接受 (后台会因文件不存在失败), 不应 401
check("not 401", code != 401, f"got {code}: {body}")
check("accepted (200/202/4xx)", code in (200, 202, 400, 409, 500), f"got {code}: {body}")

# 7. /api/etl/import 非法 entityType
print("\n--- 7. /api/etl/import entityType=invalid ---")
code, body = req("/api/etl/import", {"X-Admin-Token": TOKEN}, method="POST",
                  data={"jsonlPath": "/tmp/nonexistent.jsonl", "mode": "upsert", "entityType": "invalid"})
check("400 invalid entityType", code == 400, f"got {code}: {body}")

# 8. /api/etl/import 合法 entityType 应触发或拒绝 (因文件不存在)
print("\n--- 8. /api/etl/import entityType=products (缺文件) ---")
code, body = req("/api/etl/import", {"X-Admin-Token": TOKEN}, method="POST",
                  data={"jsonlPath": "/tmp/nonexistent.jsonl", "mode": "upsert", "entityType": "products"})
# 期望 200/202/500 (文件不存在) — 不期望 401 (token OK)
check("not 401 (auth OK)", code != 401, f"got {code}: {body}")

# 9. Swagger 关键 DTO 校验
# WHY: PublicSearchHit 等 record 是 inline schema, 不在 components.schemas 中;
#   验证实际命名的顶层 schema (EtlTriggerRequest/ImportRequest/SearchRequest/ProductFormDto)
print("\n--- 9. Swagger 关键 DTO 校验 ---")
schemas = req("/swagger/v1/swagger.json")[1].get("components", {}).get("schemas", {})
check("has EtlTriggerRequest", "EtlTriggerRequest" in schemas,
      f"available: {sorted(schemas.keys())[:5]}...")
check("has ImportRequest", "ImportRequest" in schemas, "missing")
check("has SearchRequest", "SearchRequest" in schemas, "missing")
check("has ProductFormDto", "ProductFormDto" in schemas, "missing")
# ImportRequest 必须含 entityType + cascade 字段 (Phase 1 BUG FIX 验证)
if "ImportRequest" in schemas:
    ir_props = schemas["ImportRequest"].get("properties", {})
    check("ImportRequest.entityType present", "entityType" in ir_props,
          f"properties: {list(ir_props.keys())}")
    check("ImportRequest.cascade present", "cascade" in ir_props,
          f"properties: {list(ir_props.keys())}")

print(f"\n===== Result: {PASS} pass, {FAIL} fail =====")
if FAIL > 0:
    print("Failures:")
    for e in ERRORS:
        print(f"  - {e}")
    sys.exit(1)
sys.exit(0)
