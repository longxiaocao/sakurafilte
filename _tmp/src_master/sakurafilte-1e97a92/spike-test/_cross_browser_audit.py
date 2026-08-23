"""
跨浏览器 + 视口审计
=====================
WHY: 之前所有 E2E 在 Chromium 1440x900 跑, 50%+ 移动用户 + 30% Firefox/Safari 用户
  没有任何覆盖. 本脚本:
  1. 3 视口: 375x812 (iPhone X) / 768x1024 (iPad) / 1440x900 (桌面)
  2. 2 浏览器: Chromium / Firefox
  3. 6 个公开路由 + console 监听
  4. 截图保存到 spike-test/screenshots/
  5. 任何 console 错误或水平滚动条 → WARN/FAIL

退出码: 0 OK / 1 WARN / 2 FAIL
"""
import asyncio
import json
import sys
from datetime import datetime
from pathlib import Path
from playwright.async_api import async_playwright, ConsoleMessage

ROOT = Path(__file__).resolve().parent.parent
OUT_JSON = ROOT / "spike-test" / "cross_browser_audit.json"
SHOTS_DIR = ROOT / "spike-test" / "screenshots"

VIEWPORTS = [
    ("mobile-375", 375, 812),
    ("tablet-768", 768, 1024),
    ("desktop-1440", 1440, 900),
]

BROWSERS = ["chromium", "firefox", "webkit"]

# 代表性路由 (公开页, 无需登录)
ROUTES = [
    ("/", "home"),
    ("/search", "search"),
    ("/product/AC 010323", "product-detail"),
    ("/compare?ids=1,2,3", "compare"),
    ("/login", "login"),
]

# 已知可接受警告
ACCEPTED_PATTERNS = [
    "Download the Vue Devtools",
    "[HMR]", "[vite]",
    "chrome-extension://",
    # 404 错误 (负面用例)
    "404",
]


def is_accepted(text: str) -> bool:
    return any(pat in text for pat in ACCEPTED_PATTERNS)


async def audit_one(browser, browser_name: str, vp_name: str, w: int, h: int, log: list):
    """一个浏览器 × 一个视口, 访问所有路由"""
    print(f"\n=== {browser_name} @ {vp_name} ({w}x{h}) ===")
    context = await browser.new_context(viewport={"width": w, "height": h})
    page = await context.new_page()

    # console 监听
    issues_at_start = len(log)

    def on_console(msg: ConsoleMessage):
        if msg.type in ("error", "warning"):
            text = msg.text
            if is_accepted(text):
                return
            log.append({
                "browser": browser_name, "viewport": vp_name,
                "type": msg.type, "text": text[:200],
                "route": page.url, "ts": datetime.now().isoformat(),
            })

    page.on("console", on_console)

    for route, name in ROUTES:
        try:
            await page.goto(f"http://localhost:5173{route}", wait_until="domcontentloaded", timeout=5000)
            await page.wait_for_timeout(1500)
            # 检查横向滚动
            scroll_w = await page.evaluate("document.documentElement.scrollWidth")
            client_w = await page.evaluate("document.documentElement.clientWidth")
            has_hscroll = scroll_w > client_w + 1  # 容差 1px
            # 截图
            SHOTS_DIR.mkdir(exist_ok=True)
            shot_path = SHOTS_DIR / f"{browser_name}_{vp_name}_{name}.png"
            await page.screenshot(path=str(shot_path), full_page=False)
            print(f"  [{'OK' if not has_hscroll else 'HSCROLL'}] {route}  → {shot_path.name}")
        except Exception as e:
            print(f"  [ERR] {route}  {e}")
            log.append({
                "browser": browser_name, "viewport": vp_name,
                "type": "load_error", "text": str(e)[:200],
                "route": route,
            })

    await context.close()


async def main():
    SHOTS_DIR.mkdir(exist_ok=True)
    log = []
    print("=" * 70)
    print(f"  跨浏览器 + 视口审计  ({datetime.now().strftime('%Y-%m-%d %H:%M:%S')})")
    print(f"  浏览器: {BROWSERS}  视口: {[v[0] for v in VIEWPORTS]}  路由: {len(ROUTES)}")
    print("=" * 70)

    async with async_playwright() as p:
        for browser_name in BROWSERS:
            browser_type = getattr(p, browser_name)
            try:
                browser = await browser_type.launch(headless=True)
            except Exception as e:
                print(f"[SKIP] {browser_name} 启动失败: {e}")
                continue
            for vp_name, w, h in VIEWPORTS:
                await audit_one(browser, browser_name, vp_name, w, h, log)
            await browser.close()

    # 汇总
    err_count = sum(1 for x in log if x["type"] in ("error", "load_error"))
    warn_count = sum(1 for x in log if x["type"] == "warning")

    report = {
        "ts": datetime.now().isoformat(),
        "summary": {
            "total_issues": len(log),
            "errors": err_count,
            "warnings": warn_count,
            "exit_code": 2 if err_count else (1 if warn_count else 0),
            "shot_count": len(list(SHOTS_DIR.glob("*.png"))),
        },
        "all_issues": log,
    }
    OUT_JSON.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"\n=== 汇总 ===")
    print(f"ERROR: {err_count}  WARN: {warn_count}  截图: {report['summary']['shot_count']}")
    print(f"报告: {OUT_JSON.relative_to(ROOT)}")
    print(f"截图: {SHOTS_DIR.relative_to(ROOT)}/")

    if log:
        print(f"\n问题分组:")
        from collections import Counter
        c = Counter((x['browser'], x['viewport'], x['type']) for x in log)
        for (b, v, t), n in c.most_common(10):
            print(f"  [{n:>3}] {b} @ {v}  {t}")

    sys.exit(report["summary"]["exit_code"])


if __name__ == "__main__":
    asyncio.run(main())
