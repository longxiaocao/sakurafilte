# V24-F71/V24-F72 修复验证脚本
#   验证项:
#     1. AdminXrefReorder 500 错误已消除 (V24-F71 MemoryCache Size 修复)
#     2. AdminCompareView H1 已添加 (V24-F72)
#     3. i18n 缺失 key 已补充 (V24-F72 common.action.search/reset/refresh)
#     4. DemoView el-radio 弃用警告已消除 (V24-F72 label→value)
#     5. 全页面巡检 — 重新扫描之前 25 页无错误页面是否仍稳定

from playwright.sync_api import sync_playwright
import json

FRONTEND = "http://localhost:5175"
BACKEND = "http://localhost:5148"

# 之前有问题的页面 + 关键代表性页面
TARGET_PAGES = [
    # (路径, 是否需要登录, 描述)
    ("/admin/xrefs/reorder", True, "V24-F71 验证: 之前 500 错误"),
    ("/admin/compare", True, "V24-F72 验证: H1 补充"),
    ("/admin/alerts", True, "V24-F72 验证: common.action.search/reset/refresh i18n"),
    ("/admin/etl", True, "V24-F72 验证: common.action i18n"),
    ("/admin/errors", True, "V24-F72 验证: errorview.aria.trigger_test_error i18n"),
    ("/demo", False, "V24-F72 验证: el-radio label→value 弃用警告"),
    # 代表性公开页面
    ("/search", False, "公开搜索页"),
    ("/public/search", False, "公开搜索页 v2"),
    ("/compare", False, "公开对比页"),
    ("/login", False, "登录页"),
    # 代表性后台页面
    ("/admin/products", True, "产品列表"),
    ("/admin/dict/oem-brands", True, "字典-OEM 品牌"),
    ("/admin/users", True, "用户管理"),
    ("/admin/perf", True, "性能监控"),
    ("/admin/api-docs", True, "API 文档"),
    ("/admin/help", True, "帮助中心"),
    ("/change-password", True, "修改密码"),
]


def login(page):
    """登录后台"""
    page.goto(f"{FRONTEND}/login", wait_until="networkidle")
    page.fill("#login-username", "admin")
    page.fill("#login-password", "Admin@2026")
    page.locator("button:has-text('登录')").first.click()
    page.wait_for_url("**/admin/**", timeout=10000)
    page.wait_for_load_state("networkidle")


def audit_page(page, path, need_login, desc, console_errors, console_warnings, network_errors, h1_counts):
    """巡检单个页面"""
    url = f"{FRONTEND}{path}"
    page.goto(url, wait_until="networkidle", timeout=15000)

    # 收集 console errors / warnings
    page_errors = []
    page_warnings = []

    def on_console(msg):
        if msg.type == "error":
            page_errors.append(msg.text)
        elif msg.type == "warning":
            page_warnings.append(msg.text)

    page.on("console", on_console)

    # 重新加载触发 console 收集
    page.reload(wait_until="networkidle", timeout=15000)
    page.wait_for_timeout(1000)

    # 收集网络错误
    page_network_errors = []

    def on_response(resp):
        if resp.status >= 400:
            page_network_errors.append(f"{resp.status} {resp.url}")

    page.on("response", on_response)
    page.reload(wait_until="networkidle", timeout=15000)
    page.wait_for_timeout(1500)

    # 统计 h1
    h1_count = page.locator("h1").count()

    # 统计可见按钮
    buttons = page.locator("button:visible").count()

    # 截图 (针对之前有问题的页面)
    safe_path = path.replace("/", "_").strip("_")
    screenshot_path = f"_verify_{safe_path}.png"
    page.screenshot(path=screenshot_path, full_page=True)

    result = {
        "path": path,
        "desc": desc,
        "h1_count": h1_count,
        "buttons_visible": buttons,
        "console_errors": page_errors[:5],
        "console_warnings_count": len(page_warnings),
        "console_warnings_sample": page_warnings[:3],
        "network_errors": page_network_errors[:5],
        "screenshot": screenshot_path,
    }

    console_errors.extend(page_errors)
    console_warnings.extend(page_warnings)
    network_errors.extend(page_network_errors)
    h1_counts[path] = h1_count

    return result


def main():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(viewport={"width": 1440, "height": 900})
        page = context.new_page()

        results = []
        all_console_errors = []
        all_console_warnings = []
        all_network_errors = []
        h1_counts = {}

        # 登录
        try:
            login(page)
            print(f"[OK] 登录成功")
        except Exception as e:
            print(f"[FAIL] 登录失败: {e}")
            # 仍继续验证公开页面

        for path, need_login, desc in TARGET_PAGES:
            try:
                r = audit_page(page, path, need_login, desc, all_console_errors, all_console_warnings, all_network_errors, h1_counts)
                results.append(r)
                status = "OK" if (not r["console_errors"] and not r["network_errors"]) else "ERR"
                print(f"[{status}] {path} - {desc} | h1={r['h1_count']} btns={r['buttons_visible']} errs={len(r['console_errors'])} net_errs={len(r['network_errors'])}")
                if r["console_errors"]:
                    for e in r["console_errors"][:2]:
                        print(f"        ERR: {e[:200]}")
                if r["network_errors"]:
                    for e in r["network_errors"][:3]:
                        print(f"        NET: {e[:200]}")
            except Exception as e:
                print(f"[FAIL] {path} - {desc} | 异常: {e}")
                results.append({"path": path, "desc": desc, "error": str(e)})

        # 汇总
        print("\n" + "=" * 80)
        print("汇总")
        print("=" * 80)
        print(f"巡检页面数: {len(results)}")
        print(f"console errors 总数: {len(all_console_errors)}")
        print(f"console warnings 总数: {len(all_console_warnings)}")
        print(f"network 4xx/5xx 总数: {len(all_network_errors)}")

        # V24-F71 验证: AdminXrefReorder 不应再有 500
        xref_net_errors = [e for e in all_network_errors if "/api/admin/xrefs/reorder" in e or "xrefs/reorder" in e]
        print(f"\n[V24-F71] AdminXrefReorder 网络错误: {len(xref_net_errors)} (期望 0)")
        for e in xref_net_errors:
            print(f"  - {e}")

        # V24-F72 验证: i18n 缺失 key 不应再出现
        i18n_missing = [w for w in all_console_warnings if "Not found" in w and "key" in w]
        print(f"\n[V24-F72] i18n Not found warnings: {len(i18n_missing)} (期望 0)")
        for w in i18n_missing[:5]:
            print(f"  - {w}")

        # V24-F72 验证: el-radio label 弃用警告不应再出现
        elradio_warns = [w for w in all_console_warnings if "el-radio" in w and "label" in w and "deprecated" in w]
        print(f"\n[V24-F72] el-radio label deprecated warnings: {len(elradio_warns)} (期望 0)")
        for w in elradio_warns:
            print(f"  - {w}")

        # V24-F72 验证: AdminCompareView H1
        compare_h1 = h1_counts.get("/admin/compare", 0)
        print(f"\n[V24-F72] AdminCompareView h1 count: {compare_h1} (期望 ≥1)")

        # 输出 JSON 完整结果
        with open("_verify_results.json", "w", encoding="utf-8") as f:
            json.dump({
                "results": results,
                "summary": {
                    "total_pages": len(results),
                    "console_errors_total": len(all_console_errors),
                    "console_warnings_total": len(all_console_warnings),
                    "network_errors_total": len(all_network_errors),
                    "h1_counts": h1_counts,
                    "v24_f71_xref_errors": len(xref_net_errors),
                    "v24_f72_i18n_missing": len(i18n_missing),
                    "v24_f72_elradio_deprecated": len(elradio_warns),
                    "v24_f72_compare_h1": compare_h1,
                }
            }, f, ensure_ascii=False, indent=2)

        browser.close()


if __name__ == "__main__":
    main()
