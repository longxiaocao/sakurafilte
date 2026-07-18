# V24-F73 修复后快速验证脚本 (简化版, 避免 reload 导致导航中断)
from playwright.sync_api import sync_playwright

FRONTEND = "http://localhost:5175"

KEY_PAGES = [
    ("/admin/xrefs/reorder", "V24-F73: AdminXrefReorder (之前 500)"),
    ("/admin/compare", "V24-F72: AdminCompareView H1"),
    ("/admin/etl", "SSE 401 验证"),
    ("/demo", "V24-F72: el-radio 弃用警告"),
    ("/admin/alerts", "V24-F72: i18n common.action"),
    ("/admin/errors", "V24-F72: i18n errorview.aria"),
]


def main():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(viewport={"width": 1440, "height": 900})
        page = context.new_page()

        # 登录
        page.goto(f"{FRONTEND}/login", wait_until="networkidle")
        page.fill("#login-username", "admin")
        page.fill("#login-password", "Admin@2026")
        page.locator("button:has-text('登录')").first.click()
        page.wait_for_url("**/admin/**", timeout=10000)
        page.wait_for_load_state("networkidle")
        print("[OK] 登录成功")

        for path, desc in KEY_PAGES:
            errors = []
            warnings = []
            net_errors = []

            def on_console(msg, errs=errors, warns=warnings):
                if msg.type == "error":
                    errs.append(msg.text)
                elif msg.type == "warning":
                    warns.append(msg.text)

            def on_response(resp, ne=net_errors):
                if resp.status >= 400:
                    ne.append(f"{resp.status} {resp.url}")

            page.on("console", on_console)
            page.on("response", on_response)

            try:
                page.goto(f"{FRONTEND}{path}", wait_until="networkidle", timeout=15000)
                page.wait_for_timeout(2000)  # 等待动态内容加载

                h1_count = page.locator("h1").count()
                buttons = page.locator("button:visible").count()

                status = "OK" if not errors and not net_errors else "ERR"
                print(f"[{status}] {path} - {desc}")
                print(f"        h1={h1_count} btns={buttons} console_errs={len(errors)} warns={len(warnings)} net_errs={len(net_errors)}")
                if errors:
                    for e in errors[:3]:
                        print(f"        ERR: {e[:200]}")
                if net_errors:
                    for e in net_errors[:3]:
                        print(f"        NET: {e[:200]}")
                if warnings:
                    for w in warnings[:2]:
                        print(f"        WARN: {w[:200]}")
            except Exception as e:
                print(f"[FAIL] {path} - {desc} | 异常: {e}")

            page.remove_listener("console", on_console)
            page.remove_listener("response", on_response)

        browser.close()


if __name__ == "__main__":
    main()
