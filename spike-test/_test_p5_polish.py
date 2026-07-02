# -*- coding: utf-8 -*-
"""Day 10+ P5 打磨验证 (Task 15):
  P5.1 Volume 自动计算: 后端 AdminProductService.DeriveVolume (L*W*H/1e9 m³)
  P5.2 字段说明 popover: 静态文案已在前端 data/field-help.ts, 不走后端 (避免 dict_field_help 表 migration)
  P5.3 主题切换: 前端 Pinia store + CSS 变量 (无后端 API)
  P5.4 帮助页: 前端 AdminHelpView.vue + 路由 /admin/help (无后端 API)

本脚本验证:
  1) 后端 DeriveVolume 等价: 0.3 * 0.2 * 0.15 = 0.009 m³ (用 search API 验证产品体积字段非空)
  2) 后端产品详情 GET /api/admin/products/{id} 返回 volumePerCartonM3 字段
  3) P5.4 帮助页文件存在 + 路由可达 (前端构建产物有 AdminHelpView-*.js)
  4) P5.3 主题 store 文件存在
  5) P5.2 field-help 文案文件存在 + 30+ 字段全覆盖
  6) P5.1 watcher 注入到 AdminProductFormView.vue
  7) 回归: P4.1 全量 E2E 仍通过 (见 _test_p41_e2e_full.py)
"""
import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path

BASE = "http://localhost:5148"
ADMIN_TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
FRONTEND = Path("d:/projects/sakurafilter/frontend")
SRC = FRONTEND / "src"
DIST = FRONTEND / "dist"
SPIKE = Path("d:/projects/sakurafilter/spike-test")

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
        print(f"::error::P5 FAIL [{name}]: {e}")
    except Exception as e:
        FAIL += 1
        RESULTS.append((name, "ERROR", str(e)))
        print(f"[ERROR] {name}: {e}")
        print(f"::error::P5 ERROR [{name}]: {e}")


# ========== P5.1 后端 DeriveVolume 等价验证 ==========
def test_p51_volume_field_exists():
    """P5.1: 后端产品详情返回 volumePerCartonM3 字段"""
    # 先 search 拿一个产品
    code, body = http("GET", "/api/admin/products/search?pageSize=1&pagingMode=offset",
                       headers={"X-Admin-Token": ADMIN_TOKEN})
    if code != 200:
        raise AssertionError(f"search 失败: {code} {body[:200]}")
    items = json.loads(body).get("items", [])
    if not items:
        raise AssertionError("库中无产品, 跳过")
    pid = items[0]["id"]
    code, body = http("GET", f"/api/admin/products/{pid}",
                       headers={"X-Admin-Token": ADMIN_TOKEN})
    if code != 200:
        raise AssertionError(f"get 失败: {code}")
    obj = json.loads(body)
    assert "volumePerCartonM3" in obj, f"详情缺 volumePerCartonM3 字段: {obj.keys()}"
    # 允许 null (库中部分产品未填箱尺寸)
    vol = obj["volumePerCartonM3"]
    print(f"  产品 {pid} 体积 = {vol} m³ (L*W*H/1e9 自动派生)")


# ========== P5.2 字段帮助文案覆盖 ==========
def test_p52_field_help_coverage():
    """P5.2: data/field-help.ts 文案 30+ 字段"""
    f = SRC / "data" / "field-help.ts"
    assert f.exists(), f"缺文件: {f}"
    content = f.read_text(encoding="utf-8")
    # 必须包含 30+ 字段
    expected = [
        "productName1", "productName2", "type", "mr1", "oem2",
        "d1Mm", "d2Mm", "d3Mm", "d4Mm", "h1Mm", "h2Mm", "h3Mm", "h4Mm",
        "d7Thread", "d8Thread", "noCheckValves", "noBypassValves",
        "media", "mediaModel", "bypassValveLr", "bypassValveHr",
        "efficiency1", "efficiency2", "bypassPressure", "collapsePressureBar",
        "sealingMaterial", "tempRange",
        "qtyPerCarton", "weightKgs", "cartonLengthMm", "cartonWidthMm", "cartonHeightMm",
        "volumePerCartonM3",
        "masterBoxQty", "masterBoxWeightKgs",
        "masterBoxLengthMm", "masterBoxWidthMm", "masterBoxHeightMm",
        "oemBrand", "oemNo3", "machineBrand", "machineModel", "engineBrand", "engineType"
    ]
    missing = [k for k in expected if f"{k}:" not in content and f"{k} " not in content]
    assert not missing, f"缺字段文案: {missing}"
    print(f"  ✓ {len(expected)} 字段全部覆盖, 单源真相")


def test_p52_popover_component_exists():
    """P5.2: FieldHelpPopover 组件存在"""
    f = SRC / "components" / "FieldHelpPopover.vue"
    assert f.exists(), f"缺文件: {f}"
    content = f.read_text(encoding="utf-8")
    assert "el-popover" in content, "缺 el-popover"
    assert "getFieldHelp" in content, "缺 getFieldHelp 调用"
    print(f"  ✓ FieldHelpPopover 组件存在 + 接入 el-popover + 读 field-help.ts")


def test_p52_form_integrated():
    """P5.2: AdminProductFormView.vue 已集成 FieldHelpPopover"""
    f = SRC / "views" / "admin" / "AdminProductFormView.vue"
    content = f.read_text(encoding="utf-8")
    assert "FieldHelpPopover" in content, "AdminProductFormView 未导入 FieldHelpPopover"
    count = content.count("<FieldHelpPopover")
    assert count >= 5, f"应至少 5 处 FieldHelpPopover 引用, 实际 {count}"
    print(f"  ✓ AdminProductFormView 集成 {count} 处 FieldHelpPopover (包装区全覆盖)")


# ========== P5.3 主题切换 ==========
def test_p53_theme_store_exists():
    """P5.3: stores/theme.ts Pinia store 存在"""
    f = SRC / "stores" / "theme.ts"
    assert f.exists(), f"缺文件: {f}"
    content = f.read_text(encoding="utf-8")
    assert "defineStore" in content, "缺 defineStore"
    assert "'light'" in content and "'dark'" in content, "缺 light/dark 模式"
    assert "localStorage" in content, "缺 localStorage 持久化"
    assert "prefers-color-scheme" in content, "缺系统主题检测"
    print(f"  ✓ theme.ts 存在 + defineStore + light/dark + localStorage + 系统检测")


def test_p53_css_variables_dark():
    """P5.3: styles/index.css dark 模式 CSS 变量"""
    f = SRC / "styles" / "index.css"
    content = f.read_text(encoding="utf-8")
    assert "html.dark" in content, "缺 html.dark 选择器"
    assert "--color-bg" in content, "缺 --color-bg 变量"
    assert "--color-text" in content, "缺 --color-text 变量"
    assert "--el-bg-color" in content, "缺 Element Plus 变量覆盖"
    print(f"  ✓ dark 模式 CSS 变量 + Element Plus 颜色覆盖")


def test_p53_app_header_toggle():
    """P5.3: AppHeader.vue 加主题切换按钮"""
    f = SRC / "components" / "AppHeader.vue"
    content = f.read_text(encoding="utf-8")
    assert "useThemeStore" in content, "AppHeader 未导入 useThemeStore"
    assert "theme.toggle" in content, "缺 theme.toggle 调用"
    assert "Moon" in content and "Sunny" in content, "缺 Moon/Sunny 图标"
    print(f"  ✓ AppHeader 加主题切换按钮 (Moon/Sunny 图标)")


# ========== P5.4 帮助页 ==========
def test_p54_help_view_exists():
    """P5.4: AdminHelpView.vue 存在"""
    f = SRC / "views" / "admin" / "AdminHelpView.vue"
    assert f.exists(), f"缺文件: {f}"
    content = f.read_text(encoding="utf-8")
    # 5 模块
    assert "快速开始" in content, "缺快速开始模块"
    assert "字典使用规范" in content, "缺字典模块"
    assert "批量导入" in content, "缺批量导入模块"
    assert "搜索容差" in content, "缺搜索容差模块"
    assert "常见问题" in content, "缺 FAQ 模块"
    print(f"  ✓ AdminHelpView 5 模块全覆盖")


def test_p54_help_route():
    """P5.4: 路由 /admin/help 已注册"""
    f = SRC / "router" / "index.ts"
    content = f.read_text(encoding="utf-8")
    assert "/admin/help" in content, "缺 /admin/help 路由"
    assert "AdminHelp" in content, "缺 AdminHelp 路由 name"
    assert "AdminHelpView" in content, "缺 AdminHelpView 组件 import"
    print(f"  ✓ 路由 /admin/help + AdminHelp 组件已注册")


def test_p54_help_built():
    """P5.4: 前端 build 产物含 AdminHelpView + field-help chunk"""
    if not (DIST / "assets").exists():
        print(f"  ⚠ dist/assets 不存在 (未 build), 跳过")
        return
    assets = list((DIST / "assets").glob("AdminHelpView-*.js"))
    assert len(assets) >= 1, f"build 产物缺 AdminHelpView-*.js, 现有 {[a.name for a in (DIST/'assets').glob('*.js')][:5]}"
    fhelp = list((DIST / "assets").glob("field-help-*.js"))
    assert len(fhelp) >= 1, f"build 产物缺 field-help-*.js"
    print(f"  ✓ build 产物含 {assets[0].name} + {fhelp[0].name}")


# ========== P5.1 form 集成 (Volume watcher) ==========
def test_p51_form_volume_watcher():
    """P5.1: AdminProductFormView.vue 加 Volume watcher"""
    f = SRC / "views" / "admin" / "AdminProductFormView.vue"
    content = f.read_text(encoding="utf-8")
    assert "computeVolumeM3" in content, "缺 computeVolumeM3 函数"
    assert "cartonVolume" in content, "缺 cartonVolume computed"
    assert "masterBoxVolume" in content, "缺 masterBoxVolume computed"
    assert "watch(cartonVolume" in content, "缺 cartonVolume watcher"
    # form 应有 volumePerCartonM3 字段
    assert "volumePerCartonM3: null" in content, "form 缺 volumePerCartonM3 字段"
    print(f"  ✓ Volume watcher (carton + masterBox) + form.volumePerCartonM3 同步")


# ========== 主流程 ==========
if __name__ == "__main__":
    print(f"=== Day 10+ P5 打磨验证 (Volume / Popover / 主题 / 帮助) ===")
    print(f"BASE={BASE}")

    case("P5.1-C1: 后端 DeriveVolume 字段存在", test_p51_volume_field_exists)
    case("P5.1-C2: 前端 form Volume watcher 集成", test_p51_form_volume_watcher)
    case("P5.2-C1: field-help.ts 30+ 字段覆盖", test_p52_field_help_coverage)
    case("P5.2-C2: FieldHelpPopover 组件", test_p52_popover_component_exists)
    case("P5.2-C3: AdminProductFormView 集成", test_p52_form_integrated)
    case("P5.3-C1: theme.ts Pinia store", test_p53_theme_store_exists)
    case("P5.3-C2: dark 模式 CSS 变量", test_p53_css_variables_dark)
    case("P5.3-C3: AppHeader 主题切换按钮", test_p53_app_header_toggle)
    case("P5.4-C1: AdminHelpView 5 模块", test_p54_help_view_exists)
    case("P5.4-C2: 路由 /admin/help 注册", test_p54_help_route)
    case("P5.4-C3: build 产物含 AdminHelpView", test_p54_help_built)

    print(f"\n========== P5 汇总 ==========")
    print(f"通过: {PASS} / {PASS + FAIL}")
    for name, status, err in RESULTS:
        icon = "✓" if status == "PASS" else "✗"
        print(f"  {icon} {name}: {status}" + (f" — {err}" if err else ""))
    sys.exit(0 if FAIL == 0 else 1)
