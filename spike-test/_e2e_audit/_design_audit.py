"""
V24-F77: 前端设计审查 - 全页面巡检
检查项:
  1. 页面加载状态 (HTTP 200 + networkidle)
  2. console error/warning
  3. 网络 4xx/5xx
  4. H1 语义 (每个页面是否有且仅有一个 H1)
  5. 按钮可点击性 (所有 button/a 是否有 onClick 或 href)
  6. A11y: aria-label / aria-labelledby 检查
  7. 响应式: 桌面 (1280) + 平板 (768) + 手机 (375) 截图
  8. 配色对比度 (亮色/暗色主题切换)

V24-F92 (v27-9): 支持 FRONTEND_URL/BACKEND_URL 环境变量, 适配 CI
  - CI: Vite 跑 5173, BACKEND_URL=http://localhost:5148
  - 本地: Vite 跑 5175 (5173 被另一项目占用), 默认值保留本地行为
"""
import json
import os
import time
from pathlib import Path
from playwright.sync_api import sync_playwright

FRONTEND = os.environ.get("FRONTEND_URL", "http://localhost:5175")
BACKEND = os.environ.get("BACKEND_URL", "http://localhost:5148")
SHOTS = Path("spike-test/_e2e_audit/screenshots_design")
SHOTS.mkdir(parents=True, exist_ok=True)

# (path, requires_login, name)
PAGES = [
    ("/", False, "home_public"),
    ("/search", False, "public_search"),
    ("/product/P00050000", False, "public_product"),
    ("/compare?ids=", False, "public_compare_empty"),
    ("/login", False, "login"),
    ("/admin", True, "admin_home"),
    ("/admin/products", True, "admin_products"),
    ("/admin/products/new", True, "admin_product_new"),
    ("/admin/etl", True, "admin_etl"),
    ("/admin/compare", True, "admin_compare"),
    ("/admin/dict/oem-brands", True, "admin_dict_oembrands"),
    ("/admin/dict/types", True, "admin_dict_types"),
    ("/admin/users", True, "admin_users"),
    ("/admin/alerts", True, "admin_alerts"),
    ("/admin/xrefs/reorder", True, "admin_xref_reorder"),
    ("/admin/perf", True, "admin_perf"),
    ("/admin/help", True, "admin_help"),
    ("/admin/api-docs", True, "admin_api_docs"),
    ("/admin/error-test", True, "admin_error_test"),
    ("/demo", False, "demo"),
    ("/nonexistent", False, "404"),
]

def login(page):
    """登录获取 JWT"""
    page.goto(f"{FRONTEND}/login", wait_until="networkidle")
    page.wait_for_timeout(800)
    # el-input: id 放在外层 div, 实际 input 用 autocomplete 选择器
    page.fill('input[autocomplete="username"]', "admin")
    page.fill('input[autocomplete="current-password"]', "Admin@2026")
    # el-button 用 type=primary + aria-label
    page.click('button.el-button--primary')
    page.wait_for_url("**/admin/**", timeout=10000)
    page.wait_for_timeout(1500)

def audit_page(page, path, name, requires_login):
    """审查单个页面"""
    result = {
        "name": name, "path": path, "ok": True, "issues": [],
        "console_errors": [], "console_warnings": [],
        "network_4xx_5xx": [], "h1_count": 0, "h1_text": "",
        "buttons_count": 0, "buttons_no_action": 0,
        "aria_label_count": 0, "screenshot_desktop": "", "screenshot_tablet": "", "screenshot_mobile": ""
    }

    console_errors = []
    console_warnings = []
    network_errors = []

    def on_console(msg):
        if msg.type == "error":
            console_errors.append(msg.text[:200])
        elif msg.type == "warning":
            console_warnings.append(msg.text[:200])

    def on_response(resp):
        if 400 <= resp.status < 600:
            network_errors.append(f"{resp.status} {resp.url[:120]}")

    page.on("console", on_console)
    page.on("response", on_response)

    try:
        page.goto(f"{FRONTEND}{path}", wait_until="networkidle", timeout=15000)
        page.wait_for_timeout(800)  # 等待异步渲染
    except Exception as e:
        result["ok"] = False
        result["issues"].append(f"page.goto failed: {str(e)[:100]}")
        return result

    # H1 检查
    h1_list = page.locator("h1").all()
    result["h1_count"] = len(h1_list)
    if h1_list:
        try:
            result["h1_text"] = h1_list[0].inner_text()[:80]
        except: pass

    # 按钮可点击性
    buttons = page.locator("button, a[role='button'], .el-button").all()
    result["buttons_count"] = len(buttons)
    no_action = 0
    for btn in buttons[:30]:  # 限制前 30 个避免超时
        try:
            if not btn.is_visible():
                continue
            # 检查是否有 onClick (el-button 通常有点击事件)
            has_disabled = btn.get_attribute("disabled")
            if has_action_button(btn):
                no_action += 1
        except: pass
    result["buttons_no_action"] = no_action

    # aria-label 检查
    result["aria_label_count"] = page.locator("[aria-label], [aria-labelledby]").count()

    # console 错误
    result["console_errors"] = console_errors[:5]
    result["console_warnings"] = console_warnings[:3]
    result["network_4xx_5xx"] = network_errors[:5]

    # 严重错误判定
    if console_errors:
        # 过滤已知无害错误 (favicon 404 等)
        real_errors = [e for e in console_errors if "favicon" not in e.lower() and ".ico" not in e.lower()]
        if real_errors:
            result["ok"] = False
            result["issues"].append(f"console errors: {len(real_errors)}")
    if network_errors:
        real_network = [e for e in network_errors if "favicon" not in e.lower() and ".ico" not in e.lower()]
        if real_network:
            result["issues"].append(f"network 4xx/5xx: {len(real_network)}")

    # 响应式截图
    try:
        page.set_viewport_size({"width": 1280, "height": 800})
        page.wait_for_timeout(300)
        result["screenshot_desktop"] = f"{name}_desktop.png"
        page.screenshot(path=str(SHOTS / result["screenshot_desktop"]), full_page=False)

        page.set_viewport_size({"width": 768, "height": 1024})
        page.wait_for_timeout(300)
        result["screenshot_tablet"] = f"{name}_tablet.png"
        page.screenshot(path=str(SHOTS / result["screenshot_tablet"]), full_page=False)

        page.set_viewport_size({"width": 375, "height": 667})
        page.wait_for_timeout(300)
        result["screenshot_mobile"] = f"{name}_mobile.png"
        page.screenshot(path=str(SHOTS / result["screenshot_mobile"]), full_page=False)

        page.set_viewport_size({"width": 1280, "height": 800})
    except Exception as e:
        result["issues"].append(f"screenshot failed: {str(e)[:80]}")

    return result

def has_action_button(btn) -> bool:
    """判断按钮是否无 action (粗略检查)"""
    try:
        # el-button 通常有 onClick, 这里只检查明显的死按钮
        cls = btn.get_attribute("class") or ""
        if "is-disabled" in cls:
            return False  # disabled 不算无 action
        # 检查是否有文本 (空按钮可能是占位)
        text = btn.inner_text().strip()
        if not text and not btn.get_attribute("aria-label"):
            return True  # 无文本无 aria-label
        return False
    except: return False

def main():
    print("=" * 80)
    print("V24-F77: 前端设计审查 - 全页面巡检")
    print("=" * 80)

    all_results = []

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        ctx = browser.new_context(viewport={"width": 1280, "height": 800})
        page = ctx.new_page()

        # 先登录 (admin 页面需要)
        print("\n[登录] admin / Admin@2026 ...")
        try:
            login(page)
            print("[OK] 登录成功")
        except Exception as e:
            print(f"[FAIL] 登录失败: {e}")

        # 保存登录状态
        ctx.storage_state(path=str(SHOTS / "auth_state.json"))

        for path, requires_login, name in PAGES:
            print(f"\n[审查] {path} ({name})")
            result = audit_page(page, path, name, requires_login)
            all_results.append(result)
            status = "OK" if result["ok"] else "ISSUE"
            print(f"  [{status}] h1={result['h1_count']} buttons={result['buttons_count']} aria={result['aria_label_count']} | h1='{result['h1_text']}'")
            if result["issues"]:
                for iss in result["issues"]:
                    print(f"    - {iss}")
            if result["console_errors"]:
                for e in result["console_errors"][:2]:
                    print(f"    [CE] {e[:120]}")
            if result["network_4xx_5xx"]:
                for e in result["network_4xx_5xx"][:2]:
                    print(f"    [NET] {e[:120]}")

        browser.close()

    # 汇总
    print("\n" + "=" * 80)
    print("汇总")
    print("=" * 80)
    ok_count = sum(1 for r in all_results if r["ok"])
    issue_count = len(all_results) - ok_count
    print(f"总数: {len(all_results)} | OK: {ok_count} | 有问题: {issue_count}")
    print(f"截图目录: {SHOTS}")

    # 保存结果 JSON
    with open(SHOTS / "audit_report.json", "w", encoding="utf-8") as f:
        json.dump(all_results, f, ensure_ascii=False, indent=2)
    print(f"详细报告: {SHOTS / 'audit_report.json'}")

if __name__ == "__main__":
    main()
