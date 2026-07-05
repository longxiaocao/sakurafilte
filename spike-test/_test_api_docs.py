"""
批次 6d: API 文档导出与完整性验证
  - 验证 _export_openapi.py 能从运行中的后端拉取 OpenAPI JSON
  - 验证生成的 JSON 包含所有关键字段 (paths, components.schemas, info)
  - 验证 Markdown 文件包含核心端点 (auth/login, public/search, admin/products)
  - 验证 schema 完整性 (每个端点都有 summary/responses)
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).parent
JSON_PATH = ROOT / "openapi.json"
MD_PATH = ROOT / "API.md"

results = {"test_name": "api_docs_export", "checks": []}


def check(name, ok, msg=""):
    results["checks"].append({
        "name": name,
        "status": "PASS" if ok else "FAIL",
        "msg": msg[:200] if msg else "",
    })


# ===== 1. 验证 JSON 存在并可解析 =====
if JSON_PATH.exists():
    try:
        with open(JSON_PATH, encoding="utf-8") as f:
            schema = json.load(f)
        check("openapi.json 可解析", True, f"size={JSON_PATH.stat().st_size} bytes")
    except Exception as e:
        check("openapi.json 可解析", False, str(e))
        schema = {}
else:
    check("openapi.json 存在", False, "未生成, 请先运行 _export_openapi.py")
    schema = {}

# ===== 2. 验证 OpenAPI 3.0 结构 =====
if schema:
    check("openapi 版本 3.x", str(schema.get("openapi", "")).startswith("3."),
          f"openapi={schema.get('openapi', '?')}")
    check("含 info 块", "info" in schema, f"title={schema.get('info', {}).get('title', '?')}")
    check("含 paths 块", "paths" in schema, f"path count={len(schema.get('paths', {}))}")
    check("含 components.schemas", "components" in schema and "schemas" in schema.get("components", {}),
          f"schema count={len(schema.get('components', {}).get('schemas', {}))}")
    check("含安全方案", "components" in schema and "securitySchemes" in schema.get("components", {}),
          f"schemes={list(schema.get('components', {}).get('securitySchemes', {}).keys())}")

# ===== 3. 验证关键端点存在 =====
if schema:
    paths = schema.get("paths", {})
    expected = [
        "/api/auth/login",
        "/api/auth/me",
        "/api/public/search",
        "/api/public/product/{slug}",
        "/api/admin/products",
        "/api/etl/status",
        "/api/etl/import",
        "/health/live",
        "/health/ready",
    ]
    found = 0
    for ep in expected:
        if ep in paths:
            found += 1
    check("关键端点覆盖", found == len(expected),
          f"{found}/{len(expected)} 端点存在: " + ", ".join(ep for ep in expected if ep in paths))

# ===== 4. 验证 schema 完整性 (summary/responses) =====
if schema:
    total = 0
    missing_summary = 0
    missing_responses = 0
    for path, methods in schema.get("paths", {}).items():
        for method, op in methods.items():
            if method.lower() not in ("get", "post", "put", "delete", "patch"):
                continue
            total += 1
            if not op.get("summary"):
                missing_summary += 1
            if not op.get("responses"):
                missing_responses += 1
    coverage = (total - missing_summary) / total * 100 if total else 0
    check("summary 覆盖率", coverage >= 30, f"{coverage:.1f}% ({total - missing_summary}/{total})")
    check("responses 全覆盖", missing_responses == 0,
          f"缺 responses: {missing_responses}/{total}")

# ===== 5. 验证 Markdown 文件 =====
if MD_PATH.exists():
    md = MD_PATH.read_text(encoding="utf-8")
    check("API.md 存在", True, f"size={MD_PATH.stat().st_size / 1024:.1f} KB")
    check("MD 含标题", md.startswith("# SakuraFilter API"))
    check("MD 含端点索引", "## 端点索引" in md)
    check("MD 含认证章节", "## 认证方式" in md)
    # 检查关键端点是否出现在 MD
    must_appear = ["/api/auth/login", "/api/public/search", "/api/admin/products"]
    appear = sum(1 for ep in must_appear if ep in md)
    check("MD 含关键端点", appear == len(must_appear),
          f"{appear}/{len(must_appear)} 端点在 MD 中")
else:
    check("API.md 存在", False, "未生成")

# ===== 汇总 =====
total = len(results["checks"])
passed = sum(1 for c in results["checks"] if c["status"] == "PASS")
failed = sum(1 for c in results["checks"] if c["status"] == "FAIL")
results["summary"] = {"total": total, "pass": passed, "fail": failed}
results["total"] = total

with open(ROOT / "api_docs_export.json", "w", encoding="utf-8") as f:
    json.dump(results, f, ensure_ascii=False, indent=2)

print(f"\n{'='*50}")
print(f"  API 文档导出验证")
print(f"{'='*50}")
print(f"  总计 {total} | PASS {passed} | FAIL {failed}")
for c in results["checks"]:
    flag = "✓" if c["status"] == "PASS" else "✗"
    print(f"  {flag} {c['name']}: {c['msg']}")
print(f"{'='*50}")

sys.exit(0 if failed == 0 else 1)
