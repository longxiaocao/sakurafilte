# -*- coding: utf-8 -*-
"""Day 10+ P3.3 E2E 测试: 公开产品页 (前台 /api/public/product/{slug})

覆盖:
  1) GET /api/public/product/P0505921 → 200 + 完整 DTO (无 token)
  2) GET /api/public/product/INVALID-XYZ-NOTFOUND → 404
  3) slug 格式 (R1 规格): oil-filter-of100-mann-11427622448 → 解析末段
  4) 响应字段: oemNoDisplay, type, dimensions (H/D), xrefs, machineApplications
  5) 公开端点无需鉴权: 无 X-Admin-Token header 也应 200 (非 401)
  6) SEO/OG meta 字段可访问 (前端 verify, 此处只验证后端 DTO 完整)
"""
import json
import os
import sys
import urllib.error
import urllib.request

BASE = "http://localhost:5148"
PASS = 0
FAIL = 0
RESULTS = []


def http(method, path, body=None, headers=None, timeout=15):
    url = BASE + path
    h = {"Content-Type": "application/json"}
    if headers:
        h.update(headers)
    data = None
    if body is not None:
        data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(url, data=data, headers=h, method=method)
    try:
        r = urllib.request.urlopen(req, timeout=timeout)
        return r.status, r.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8")
    except urllib.error.URLError as e:
        return 0, f"[URL unreachable: {e.reason}]"


def case(name, fn):
    global PASS, FAIL
    print(f"\n--- {name} ---")
    try:
        fn()
        PASS += 1
        RESULTS.append((name, "PASS", None))
        print(f"[PASS] {name}")
    except AssertionError as e:
        FAIL += 1
        RESULTS.append((name, "FAIL", str(e)))
        print(f"[FAIL] {name}: {e}")
        print(f"::error::P3.3 FAIL [{name}]: {e}")
    except Exception as e:
        FAIL += 1
        RESULTS.append((name, "ERROR", str(e)))
        print(f"[ERROR] {name}: {e}")
        print(f"::error::P3.3 ERROR [{name}]: {e}")


# ========== Case 1: 公开产品页 (无 token, 200 + 完整 DTO) ==========
def test_get_product_no_token():
    """无 X-Admin-Token, 调 /api/public/product/{slug} 应返 200 + 完整 DTO"""
    code, body = http("GET", "/api/public/product/P0505921")
    if code == 0:
        raise AssertionError(f"后端未启动: {body[:200]}")
    assert code == 200, f"GET /public/product/P0505921 期望 200, 实际 {code}, body={body[:300]}"
    obj = json.loads(body)
    # 验证关键字段
    assert "id" in obj, f"响应缺 id: {obj.keys()}"
    assert "oemNoDisplay" in obj, f"响应缺 oemNoDisplay"
    assert obj["oemNoDisplay"] == "P0505921", f"oemNoDisplay 不对: {obj['oemNoDisplay']}"
    assert "type" in obj, f"响应缺 type"
    assert obj["type"] == "Air", f"type 不对: {obj['type']}"
    # 7 分区字段 (任选部分验证)
    for f in ["h1Mm", "h2Mm", "d1Mm", "d2Mm", "media"]:
        assert f in obj, f"响应缺 {f}"
    # 交叉引用 + 车型
    assert "crossReferences" in obj and isinstance(obj["crossReferences"], list)
    assert "machineApplications" in obj and isinstance(obj["machineApplications"], list)
    assert len(obj["crossReferences"]) > 0, f"应有 crossReferences, 实际空"
    assert len(obj["machineApplications"]) > 0, f"应有 machineApplications, 实际空"
    # images 字段 (可能空 list, 但必须存在)
    assert "images" in obj, f"响应缺 images 字段"
    print(f"  ✓ P0505921 公开可访问, 7 分区字段齐全, xrefs={len(obj['crossReferences'])}, apps={len(obj['machineApplications'])}")


# ========== Case 2: 404 错误处理 ==========
def test_404_not_found():
    """不存在的 OEM 应返 404 + 错误信息"""
    code, body = http("GET", "/api/public/product/INVALID-XYZ-NOTFOUND")
    if code == 0:
        raise AssertionError(f"后端未启动: {body[:200]}")
    assert code == 404, f"不存在产品期望 404, 实际 {code}, body={body[:200]}"
    obj = json.loads(body)
    assert "error" in obj, f"404 响应缺 error 字段"
    print(f"  ✓ 404 错误处理正确: {obj['error']}")


# ========== Case 3: slug 格式 (R1 规格: name1-name2-oemBrand-oemNo) ==========
def test_slug_format():
    """slug = oil-filter-of100-mann-P0505921, 后端解析末段为 oem"""
    code, body = http("GET", "/api/public/product/oil-filter-of100-mann-P0505921")
    if code == 0:
        raise AssertionError(f"后端未启动: {body[:200]}")
    assert code == 200, f"slug 格式期望 200, 实际 {code}, body={body[:300]}"
    obj = json.loads(body)
    assert obj["oemNoDisplay"] == "P0505921", f"slug 末段解析错: {obj['oemNoDisplay']}"
    print(f"  ✓ slug 格式正确解析末段为 oem: P0505921")


# ========== Case 4: 公开端点无鉴权 (无 X-Admin-Token 也应 200) ==========
def test_no_auth_required():
    """显式不传 X-Admin-Token, 应 200 (非 401)"""
    # http() 函数不注入任何 token, 这里仅验证 status
    code, body = http("GET", "/api/public/product/P0505921")
    if code == 0:
        raise AssertionError(f"后端未启动: {body[:200]}")
    assert code != 401, f"公开端点不应要求鉴权, 实际 401: {body[:200]}"
    assert code == 200, f"期望 200, 实际 {code}"
    print(f"  ✓ 公开端点无鉴权 (无 token 仍 200)")


# ========== Case 5: 响应字段完整性 (对照"后台新增产品格式" 7 分区) ==========
def test_response_field_completeness():
    """验证 7 分区所有关键字段都存在"""
    code, body = http("GET", "/api/public/product/P0505921")
    obj = json.loads(body)
    expected_fields = [
        # 分区 1: 基础
        "oemNoDisplay", "oem2", "mr1", "productName1", "productName2", "type", "isPublished",
        # 分区 3: 尺寸
        "d1Mm", "d2Mm", "d3Mm", "d4Mm", "h1Mm", "h2Mm", "h3Mm", "h4Mm",
        "d7Thread", "d8Thread", "noCheckValves", "noBypassValves",
        # 分区 5: 性能
        "media", "mediaModel", "bypassValveLr", "bypassValveHr",
        "efficiency1", "efficiency2", "bypassPressure", "collapsePressureBar",
        "sealingMaterial", "tempRange",
        # 分区 6: 包装
        "qtyPerCarton", "weightKgs", "cartonLengthMm", "cartonWidthMm", "cartonHeightMm",
        "volumePerCartonM3",
        # 派生
        "crossReferences", "machineApplications", "images",
    ]
    missing = [f for f in expected_fields if f not in obj]
    assert not missing, f"响应缺字段: {missing}"
    print(f"  ✓ 7 分区 {len(expected_fields)} 字段全部存在")


# ========== 主流程 ==========
if __name__ == "__main__":
    print(f"=== Day 10+ P3.3 E2E 测试 (公开产品页) ===")
    print(f"BASE={BASE}")

    case("1. GET /api/public/product/{slug} 公开可访问", test_get_product_no_token)
    case("2. 404 错误处理", test_404_not_found)
    case("3. slug 格式 (R1 规格) 解析", test_slug_format)
    case("4. 公开端点无鉴权", test_no_auth_required)
    case("5. 7 分区字段完整性", test_response_field_completeness)

    print(f"\n=== 总结: {PASS} PASS, {FAIL} FAIL ===")
    for n, s, e in RESULTS:
        marker = "✓" if s == "PASS" else "✗"
        print(f"  {marker} [{s}] {n}" + (f"  ({e})" if e else ""))
    sys.exit(0 if FAIL == 0 else 1)
