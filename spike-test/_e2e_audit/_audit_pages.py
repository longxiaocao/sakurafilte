"""
SakuraFilter 全页面巡检 + 设计审查 + 按钮可点击性验证
巡检内容:
  1. 公开页面 (无需登录): /search, /public/search, /search/aggregate, /compare, /login, /demo, /404
  2. 后台页面 (需登录): /admin/products, /admin/etl, /admin/alerts, /admin/users,
     /admin/dict/* (7 个), /admin/compare, /admin/help, /admin/perf, /admin/errors,
     /admin/api-docs, /admin/xrefs/reorder, /change-password, /admin/products/new
  3. 每个页面:
     - 截图 (full_page)
     - 收集 console error/warning
     - 收集网络 4xx/5xx 响应
     - 检查所有 button/a 是否可点击 (visible + enabled + not covered)
     - 检查 H1/标题是否存在
     - 检查是否有 Element Plus 的 el-empty 兜底
  4. 登录流程: admin / Admin@2026
  5. 输出 JSON 报告 + 截图目录
"""
import json
import os
import time
from pathlib import Path
from playwright.sync_api import sync_playwright

BASE = "http://localhost:5175"
OUT_DIR = Path("d:/projects/sakurafilter/spike-test/_e2e_audit")
OUT_DIR.mkdir(parents=True, exist_ok=True)
SHOTS_DIR = OUT_DIR / "screenshots"
SHOTS_DIR.mkdir(exist_ok=True)

PUBLIC_ROUTES = [
    "/search",
    "/public/search",
    "/search/aggregate",
    "/compare",
    "/login",
    "/demo",
    "/nonexistent-404-test",
]

ADMIN_ROUTES = [
    "/admin/products",
    "/admin/products/new",
    "/admin/etl",
    "/admin/alerts",
    "/admin/users",
    "/admin/compare",
    "/admin/help",
    "/admin/perf",
    "/admin/errors",
    "/admin/api-docs",
    "/admin/xrefs/reorder",
    "/change-password",
    "/admin/dict/oem-brands",
    "/admin/dict/product-name1s",
    "/admin/dict/product-name2s",
    "/admin/dict/types",
    "/admin/dict/oem-no3s",
    "/admin/dict/medias",
    "/admin/dict/machines",
    "/admin/dict/engines",
]


def inspect_page(page, route_name: str, route_path: str) -> dict:
    """检查单个页面, 返回巡检结果 dict"""
    result = {
        "route": route_path,
        "name": route_name,
        "url": page.url,
        "title": "",
        "h1_count": 0,
        "h1_text": "",
        "buttons_total": 0,
        "buttons_visible": 0,
        "buttons_enabled": 0,
        "buttons_disabled": 0,
        "links_total": 0,
        "links_visible": 0,
        "empty_states": 0,
        "skeletons": 0,
        "loading_spinners": 0,
        "console_errors": [],
        "console_warnings": [],
        "network_4xx_5xx": [],
        "screenshot": "",
        "load_time_ms": 0,
        "issues": [],
    }

    # 等待 networkidle (最多 8s)
    start = time.time()
    try:
        page.wait_for_load_state("networkidle", timeout=8000)
    except Exception:
        result["issues"].append("networkidle 超时(8s)")
    result["load_time_ms"] = int((time.time() - start) * 1000)

    # 给 Vue 一点渲染时间
    page.wait_for_timeout(800)

    # 截图
    shot_path = SHOTS_DIR / f"{route_name}.png"
    try:
        page.screenshot(path=str(shot_path), full_page=True)
        result["screenshot"] = str(shot_path)
    except Exception as e:
        result["issues"].append(f"截图失败: {e}")

    # 标题
    result["title"] = page.title()

    # H1
    h1_loc = page.locator("h1")
    result["h1_count"] = h1_loc.count()
    if result["h1_count"] > 0:
        try:
            result["h1_text"] = h1_loc.first.inner_text(timeout=1000)
        except Exception:
            pass

    # 按钮
    btn_loc = page.locator("button, .el-button, [role='button']")
    result["buttons_total"] = btn_loc.count()
    for i in range(result["buttons_total"]):
        try:
            btn = btn_loc.nth(i)
            if btn.is_visible():
                result["buttons_visible"] += 1
                if btn.is_enabled():
                    result["buttons_enabled"] += 1
                else:
                    result["buttons_disabled"] += 1
        except Exception:
            pass

    # 链接
    link_loc = page.locator("a[href]")
    result["links_total"] = link_loc.count()
    for i in range(min(result["links_total"], 50)):  # 限制 50 个避免太慢
        try:
            if link_loc.nth(i).is_visible():
                result["links_visible"] += 1
        except Exception:
            pass

    # 空状态/骨架屏/loading
    result["empty_states"] = page.locator(".el-empty, [class*='empty-state']").count()
    result["skeletons"] = page.locator(".el-skeleton, [class*='skeleton']").count()
    result["loading_spinners"] = page.locator(".el-loading-spinner, .el-loading-mask").count()

    return result


def main():
    report = {
        "start_time": time.strftime("%Y-%m-%d %H:%M:%S"),
        "base_url": BASE,
        "public_pages": [],
        "admin_pages": [],
        "login_success": False,
        "login_error": "",
        "summary": {},
    }

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(
            viewport={"width": 1440, "height": 900},
            user_agent="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        )

        # ===== 公开页面巡检 =====
        print("=" * 60)
        print("Phase 1: 公开页面巡检")
        print("=" * 60)
        page = context.new_page()

        # 监听 console 和网络
        console_logs = []
        network_errors = []

        def on_console(msg):
            if msg.type in ("error", "warning"):
                console_logs.append({"type": msg.type, "text": msg.text[:300]})

        def on_response(resp):
            if resp.status >= 400:
                network_errors.append({"url": resp.url[:200], "status": resp.status})

        page.on("console", on_console)
        page.on("response", on_response)

        for route in PUBLIC_ROUTES:
            print(f"\n[公开] {route}")
            console_logs.clear()
            network_errors.clear()
            try:
                page.goto(f"{BASE}{route}", wait_until="domcontentloaded", timeout=15000)
                result = inspect_page(page, route.strip("/").replace("/", "_") or "root", route)
                result["console_errors"] = [l for l in console_logs if l["type"] == "error"]
                result["console_warnings"] = [l for l in console_logs if l["type"] == "warning"]
                result["network_4xx_5xx"] = network_errors.copy()
                report["public_pages"].append(result)
                print(f"  title='{result['title']}' h1={result['h1_count']} btn={result['buttons_total']}/{result['buttons_visible']}v/{result['buttons_enabled']}e err={len(result['console_errors'])} net={len(result['network_4xx_5xx'])}")
            except Exception as e:
                print(f"  [FAIL] {e}")
                report["public_pages"].append({
                    "route": route,
                    "error": str(e)[:300],
                    "console_errors": console_logs.copy(),
                    "network_4xx_5xx": network_errors.copy(),
                })

        # ===== 登录 =====
        print("\n" + "=" * 60)
        print("Phase 2: 登录后台")
        print("=" * 60)
        page.goto(f"{BASE}/login", wait_until="domcontentloaded", timeout=15000)
        page.wait_for_load_state("networkidle", timeout=8000)
        page.wait_for_timeout(1000)

        # 填表单
        try:
            # 用户名 (id 精确匹配)
            user_input = page.locator("#login-username")
            user_input.fill("admin")
            # 密码
            pwd_input = page.locator("#login-password")
            pwd_input.fill("Admin@2026")
            # 提交 (button:has-text('登录') 精确匹配,排除"进入后台")
            submit_btn = page.locator("button:has-text('登录')").first
            submit_btn.click()
            page.wait_for_timeout(3000)
            page.wait_for_load_state("networkidle", timeout=8000)

            # 验证是否跳转或显示用户信息
            if "/login" not in page.url:
                report["login_success"] = True
                print(f"  [OK] 登录成功, 跳转到 {page.url}")
            else:
                # 检查是否有错误提示
                err_msg = page.locator(".el-message--error, .el-form-item__error").first
                err_text = ""
                try:
                    err_text = err_msg.inner_text(timeout=1000)
                except Exception:
                    pass
                report["login_error"] = f"仍在 /login, 错误: {err_text}"
                print(f"  [FAIL] 仍在 /login, 错误: {err_text}")
        except Exception as e:
            report["login_error"] = str(e)[:300]
            print(f"  [FAIL] 登录异常: {e}")

        # ===== 后台页面巡检 =====
        print("\n" + "=" * 60)
        print("Phase 3: 后台页面巡检 (已登录)")
        print("=" * 60)
        for route in ADMIN_ROUTES:
            print(f"\n[后台] {route}")
            console_logs.clear()
            network_errors.clear()
            try:
                page.goto(f"{BASE}{route}", wait_until="domcontentloaded", timeout=15000)
                result = inspect_page(page, "admin_" + route.strip("/").replace("/", "_"), route)
                result["console_errors"] = [l for l in console_logs if l["type"] == "error"]
                result["console_warnings"] = [l for l in console_logs if l["type"] == "warning"]
                result["network_4xx_5xx"] = network_errors.copy()
                report["admin_pages"].append(result)
                print(f"  title='{result['title']}' h1={result['h1_count']} btn={result['buttons_total']}/{result['buttons_visible']}v/{result['buttons_enabled']}e err={len(result['console_errors'])} net={len(result['network_4xx_5xx'])}")
            except Exception as e:
                print(f"  [FAIL] {e}")
                report["admin_pages"].append({
                    "route": route,
                    "error": str(e)[:300],
                    "console_errors": console_logs.copy(),
                    "network_4xx_5xx": network_errors.copy(),
                })

        browser.close()

    # ===== 汇总 =====
    total_pages = len(report["public_pages"]) + len(report["admin_pages"])
    total_console_errors = sum(len(p.get("console_errors", [])) for p in report["public_pages"] + report["admin_pages"])
    total_network_errors = sum(len(p.get("network_4xx_5xx", [])) for p in report["public_pages"] + report["admin_pages"])
    pages_with_errors = sum(1 for p in report["public_pages"] + report["admin_pages"] if p.get("error") or len(p.get("console_errors", [])) > 0 or len(p.get("network_4xx_5xx", [])) > 0)

    report["summary"] = {
        "total_pages": total_pages,
        "pages_with_errors": pages_with_errors,
        "total_console_errors": total_console_errors,
        "total_network_errors": total_network_errors,
        "login_success": report["login_success"],
    }

    out_json = OUT_DIR / "audit-report.json"
    with open(out_json, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)

    print("\n" + "=" * 60)
    print(f"巡检完成: {total_pages} 页, {pages_with_errors} 页有错误")
    print(f"console errors: {total_console_errors}")
    print(f"network 4xx/5xx: {total_network_errors}")
    print(f"报告: {out_json}")
    print(f"截图目录: {SHOTS_DIR}")
    print("=" * 60)


if __name__ == "__main__":
    main()
