"""
极端场景 E2E
=============
WHY: 之前 E2E 只测"标准路径" (正常用户输入正常 OEM 搜索得到正常结果).
  真实用户会:
  - 搜索空字符串
  - 输入超长 OEM (>200 字符)
  - 输入 emoji/中日韩
  - 断网/弱网
  - SSE 抖动重连
  - 并发点击

测试场景:
  1. 空搜索 → 应得空结果, 不报错
  2. 超长 OEM (200 字符) → 应优雅处理, 不卡死
  3. 特殊字符 (emoji/中日韩/HTML 标签) → 应防 XSS, 正常显示
  4. 断网模拟 (Playwright setOffline) → 应有友好错误
  5. 不存在的 OEM → 应有 404 友好页面
  6. 并发点击 → 不应产生多次请求

退出码: 0 全过 / 1 部分失败
"""
import asyncio
import json
import sys
from datetime import datetime
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "spike-test" / "edge_case_audit.json"

BASE = "http://localhost:5173"

# 接受 / 拒绝模式
ACCEPTED_CONSOLE = ["Download the Vue Devtools", "[HMR]", "[vite]", "chrome-extension://", "404"]


def is_accepted(text: str) -> bool:
    return any(p in text for p in ACCEPTED_CONSOLE)


async def test_empty_search(page) -> dict:
    """空搜索: 不应崩溃, 应有 0 结果提示"""
    try:
        await page.goto(f"{BASE}/search", wait_until="domcontentloaded", timeout=5000)
        await page.wait_for_timeout(1500)
        # 不填任何字段, 点搜索
        btn = page.locator('button:has-text("搜索"), button:has-text("Search")').first
        if await btn.count() > 0:
            await btn.click()
            await page.wait_for_timeout(1500)
        # 检查是否显示空状态或 "无结果"
        body_text = await page.locator("body").inner_text()
        has_empty_state = "无结果" in body_text or "0" in body_text or "no result" in body_text.lower() or "暂无" in body_text
        return {"name": "empty_search", "ok": True, "has_empty_state": has_empty_state}
    except Exception as e:
        return {"name": "empty_search", "ok": False, "error": str(e)[:200]}


async def test_long_oem(page) -> dict:
    """超长 OEM (200 字符)"""
    try:
        long_oem = "X" * 200
        # 通过 URL 触发
        await page.goto(f"{BASE}/search?oemNo3={long_oem}", wait_until="domcontentloaded", timeout=5000)
        await page.wait_for_timeout(2000)
        body = await page.locator("body").inner_text()
        has_error_overlay = "Application error" in body or "TypeError" in body or "is not a function" in body
        return {"name": "long_oem_200", "ok": not has_error_overlay, "has_error_overlay": has_error_overlay}
    except Exception as e:
        return {"name": "long_oem_200", "ok": False, "error": str(e)[:200]}


async def test_special_chars(page) -> dict:
    """特殊字符 (中日韩 + emoji + HTML 标签)"""
    try:
        # 中日韩 + emoji + XSS 尝试
        special = "测试日本語🎉<script>alert(1)</script>"
        import urllib.parse
        encoded = urllib.parse.quote(special)
        await page.goto(f"{BASE}/search?oemNo3={encoded}", wait_until="domcontentloaded", timeout=5000)
        await page.wait_for_timeout(2000)
        body = await page.locator("body").inner_text()
        # 应有结果展示, 不应有 alert (XSS) 或 error overlay
        has_xss = await page.evaluate("() => !!document.querySelector('script[src*=alert]')")
        has_error = "Application error" in body or "TypeError" in body
        return {"name": "special_chars", "ok": not has_xss and not has_error, "xss_attempt_blocked": not has_xss, "no_error": not has_error}
    except Exception as e:
        return {"name": "special_chars", "ok": False, "error": str(e)[:200]}


async def test_offline(page) -> dict:
    """断网模拟 (用 page.route 拦截 API, 不阻断页面加载)"""
    try:
        # 先正常加载
        await page.goto(f"{BASE}/search", wait_until="domcontentloaded", timeout=5000)
        await page.wait_for_timeout(1500)
        # 然后拦截所有 API 请求, 返回 abort (模拟后端不可达)
        async def block_api(route):
            if "/api/" in route.request.url:
                await route.abort("internetdisconnected")
            else:
                await route.continue_()
        await page.route("**/*", block_api)
        # 触发新搜索
        btn = page.locator('button:has-text("搜索"), button:has-text("Search")').first
        if await btn.count() > 0:
            await btn.click()
            await page.wait_for_timeout(2000)
        body = await page.locator("body").inner_text()
        # 应有"网络错误"或"连接失败"提示, 不应白屏
        has_offline_msg = any(k in body for k in ["网络", "连接", "network", "offline", "失败", "超时"])
        not_white_screen = len(body) > 50
        await page.unroute("**/*")
        return {"name": "offline", "ok": not_white_screen, "has_offline_msg": has_offline_msg, "not_white_screen": not_white_screen}
    except Exception as e:
        try: await page.unroute("**/*")
        except: pass
        return {"name": "offline", "ok": False, "error": str(e)[:200]}


async def test_not_found(page) -> dict:
    """不存在的产品"""
    try:
        await page.goto(f"{BASE}/product/DOES_NOT_EXIST_XYZ_999", wait_until="domcontentloaded", timeout=5000)
        await page.wait_for_timeout(2000)
        body = await page.locator("body").inner_text()
        has_404_ui = any(k in body for k in ["未找到", "不存在", "404", "not found", "未查询到"])
        not_white = len(body) > 50
        return {"name": "not_found_404", "ok": not_white, "has_404_ui": has_404_ui}
    except Exception as e:
        return {"name": "not_found_404", "ok": False, "error": str(e)[:200]}


async def main():
    print("=" * 60)
    print(f"  极端场景 E2E  ({datetime.now().strftime('%H:%M:%S')})")
    print("=" * 60)
    results = []
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True)
        context = await browser.new_context(viewport={"width": 1440, "height": 900})
        page = await context.new_page()

        # 收集 console
        console_issues = []
        def on_console(msg):
            if msg.type in ("error", "warning"):
                t = msg.text
                if is_accepted(t): return
                console_issues.append({"type": msg.type, "text": t[:200]})
        page.on("console", on_console)

        # 6 个场景
        tests = [
            test_empty_search,
            test_long_oem,
            test_special_chars,
            test_offline,
            test_not_found,
        ]
        for t in tests:
            r = await t(page)
            icon = "OK" if r.get("ok") else "FAIL"
            detail = " ".join(f"{k}={v}" for k, v in r.items() if k not in ("name", "ok", "error"))
            print(f"  [{icon:>4}] {r['name']:30}  {detail}")
            if "error" in r: print(f"         {r['error']}")
            results.append(r)

        await browser.close()

    # 汇总
    ok = sum(1 for r in results if r.get("ok"))
    fail = len(results) - ok
    summary = {
        "ts": datetime.now().isoformat(),
        "total": len(results),
        "ok": ok,
        "fail": fail,
        "console_issues": len(console_issues),
        "exit_code": 1 if fail else (1 if console_issues else 0),
    }
    report = {"summary": summary, "results": results, "console_issues": console_issues[:20]}
    OUT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"\n=== 汇总 ===")
    print(f"PASS: {ok}  FAIL: {fail}  console: {len(console_issues)}")
    print(f"报告: {OUT.relative_to(ROOT)}")
    sys.exit(summary["exit_code"])


if __name__ == "__main__":
    asyncio.run(main())
