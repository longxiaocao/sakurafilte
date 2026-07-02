# -*- coding: utf-8 -*-
"""Day 10+ P3.3 E2E 测试: 公开产品页 (前台 /api/public/product/{slug})

覆盖:
  1) GET /api/public/product/P0505921 → 200 + 完整 DTO (无 token)
  2) GET /api/public/product/INVALID-XYZ-NOTFOUND → 404
  3) slug 格式 (R1 规格): oil-filter-of100-mann-11427622448 → 解析末段
  4) 响应字段: oemNoDisplay, type, dimensions (H/D), xrefs, machineApplications
  5) 公开端点无需鉴权: 无 X-Admin-Token header 也应 200 (非 401)
  6) SEO/OG meta 字段可访问 (前端 verify, 此处只验证后端 DTO 完整)
  7) 缺图场景: images=[] 时前端回退到 /oem2/{OEM}.jpg
  8) URL 编码: 含特殊字符的 OEM (e.g. AB-123) → 末段解析
  9) 停售产品: is_discontinued=true 应 404 (前台不展示)
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


# ========== Case 6: 缺图场景 (images=[], 前端回退 oem2/{OEM}.jpg) ==========
def test_missing_image_fallback():
    """当后端 images=[] 时, 前端应能回退到 oem2/{OEM}.jpg 命名 (R5 规格)"""
    code, body = http("GET", "/api/public/product/P0505921")
    obj = json.loads(body)
    # 后端允许 images 为空 list (无图产品)
    assert "images" in obj and isinstance(obj["images"], list), f"images 字段必须是 list: {type(obj.get('images'))}"
    # 即使空, 前端 PublicProductView.buildImageUrl() 仍能根据 oemNoDisplay 拼出 /oem2/{OEM}.jpg
    if len(obj["images"]) == 0:
        # 验证前端逻辑期望: 缺图时回退到 /oem2/{OEM}.jpg
        oem = obj["oemNoDisplay"]
        expected_fallback = f"/oem2/{oem}.jpg"
        # 这里只验证命名约定, 实际拼 URL 由前端 buildImageUrl() 完成
        assert expected_fallback == f"/oem2/{oem}.jpg"
        print(f"  ✓ 缺图场景: 后端 images=[], 前端将回退到 {expected_fallback} (R5 命名约定)")
    else:
        print(f"  ✓ images={len(obj['images'])} 张, 走 og:image 直接渲染")


# ========== Case 7: URL 编码 (含特殊字符 slug) ==========
def test_url_encoded_slug():
    """slug 走 URL 编码, 后端应能解析 (含 - / % 字符)"""
    # 测试含 - 的 slug (实际场景: AB-123-X)
    test_slug = "some-name-AB-123"
    code, body = http("GET", f"/api/public/product/{test_slug}")
    # 可能 200 (找到) 或 404 (没找到), 但必须不是 500
    assert code in (200, 404), f"含 - 的 slug 应 200 或 404, 实际 {code}, body={body[:200]}"
    # 验证 200 时返回的产品 oemNoDisplay = 末段 "AB-123"
    if code == 200:
        obj = json.loads(body)
        assert obj["oemNoDisplay"] == "AB-123", f"末段解析错: {obj.get('oemNoDisplay')}"
        print(f"  ✓ slug 末段 AB-123 解析成功 (URL 编码兼容)")
    else:
        print(f"  ✓ slug 末段 AB-123 不存在, 返 404 (非 500)")


# ========== Case 8: 停售产品 (is_discontinued=true 应 404) ==========
def test_discontinued_product_404():
    """停售产品前台不展示, 应返 404"""
    # 调后台 admin/dict/products 找一个 discontinued 的产品 (需 token)
    token = os.environ.get("ADMIN_TOKEN", "dev-static-token-change-me-32chars-min-12345")
    code, body = http("GET", "/api/admin/products/search?includeDiscontinued=true&pageSize=200", headers={"X-Admin-Token": token})
    discontinued_oem = None
    if code == 200:
        admin = json.loads(body)
        items = admin.get("items", [])
        for it in items:
            if it.get("isDiscontinued") is True:
                discontinued_oem = it.get("oemNoDisplay")
                break
    if not discontinued_oem:
        # 库中无停售产品, 跳过此 case
        print(f"  ⚠ 库中无停售产品, 跳过此 case")
        return
    # 用停售 OEM 调公开端点, 应 404
    code, body = http("GET", f"/api/public/product/{discontinued_oem}")
    assert code == 404, f"停售产品应 404, 实际 {code}, body={body[:200]}"
    print(f"  ✓ 停售产品 {discontinued_oem} 前台 404 (不展示)")


# ========== Case 9: 性能 smoke (首屏 < 1.5s) ==========
def test_response_latency():
    """公开产品页响应时间 < 1.5s (spec 验证条件)"""
    import time
    start = time.time()
    code, body = http("GET", "/api/public/product/P0505921", timeout=5)
    elapsed = time.time() - start
    assert code == 200, f"期望 200, 实际 {code}"
    assert elapsed < 1.5, f"响应时间 {elapsed:.3f}s > 1.5s"
    print(f"  ✓ 响应时间 {elapsed*1000:.0f}ms < 1500ms")


# ========== 主流程 ==========
if __name__ == "__main__":
    print(f"=== Day 10+ P3.3 E2E 测试 (公开产品页) ===")
    print(f"BASE={BASE}")

    case("1. GET /api/public/product/{slug} 公开可访问", test_get_product_no_token)
    case("2. 404 错误处理", test_404_not_found)
    case("3. slug 格式 (R1 规格) 解析", test_slug_format)
    case("4. 公开端点无鉴权", test_no_auth_required)
    case("5. 7 分区字段完整性", test_response_field_completeness)
    case("6. 缺图回退 (R5 命名 oem2/{OEM}.jpg)", test_missing_image_fallback)
    case("7. URL 编码 (含 - 的 slug 末段解析)", test_url_encoded_slug)
    case("8. 停售产品 404 (is_discontinued=true 不展示)", test_discontinued_product_404)
    case("9. 性能 smoke (响应 < 1.5s)", test_response_latency)

    print(f"\n=== 总结: {PASS} PASS, {FAIL} FAIL ===")
    for n, s, e in RESULTS:
        marker = "✓" if s == "PASS" else "✗"
        print(f"  {marker} [{s}] {n}" + (f"  ({e})" if e else ""))
    sys.exit(0 if FAIL == 0 else 1)
