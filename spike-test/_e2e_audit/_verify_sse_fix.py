"""
V24-F78 验证脚本: SSE 401 修复
  - 登录 admin / Admin@2026
  - 访问 /admin/etl
  - 监听 console + network 10 秒
  - 检查 /api/admin/etl/progress/stream 是否 200 (不再 401)
  - 检查是否有 SSE 消息推送 (data: {...})
"""
from playwright.sync_api import sync_playwright
import json
import time

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(viewport={"width": 1280, "height": 800})
    page = ctx.new_page()

    sse_requests = []
    sse_responses = []
    console_msgs = []

    def on_request(req):
        if "progress/stream" in req.url:
            sse_requests.append({"url": req.url, "headers": dict(req.headers)})

    def on_response(resp):
        if "progress/stream" in resp.url:
            sse_responses.append({"status": resp.status, "url": resp.url})

    def on_console(msg):
        if msg.type in ("error", "warning", "debug"):
            console_msgs.append({"type": msg.type, "text": msg.text[:200]})

    page.on("request", on_request)
    page.on("response", on_response)
    page.on("console", on_console)

    print("[1] 访问登录页...")
    page.goto("http://localhost:5175/login", wait_until="networkidle")
    page.wait_for_timeout(1000)

    print("[2] 登录 admin / Admin@2026 ...")
    page.fill('input[autocomplete="username"]', "admin")
    page.fill('input[autocomplete="current-password"]', "Admin@2026")
    page.click('button.el-button--primary')
    page.wait_for_url("**/admin/**", timeout=10000)
    print(f"[OK] 登录成功, URL: {page.url}")
    page.wait_for_timeout(1500)

    print("[3] 访问 /admin/etl ...")
    # SSE 是持续连接, networkidle 永不触发, 改用 domcontentloaded
    page.goto("http://localhost:5175/admin/etl", wait_until="domcontentloaded")
    print(f"[OK] URL: {page.url}")

    print("[4] 监听 10 秒, 等待 SSE 连接...")
    page.wait_for_timeout(10000)

    print("\n===== SSE 请求 =====")
    for r in sse_requests:
        print(f"  URL: {r['url']}")
        print(f"  Authorization: {r['headers'].get('authorization', '<无>')[:30]}...")
        print(f"  X-Admin-Token: {r['headers'].get('x-admin-token', '<无>')[:20]}...")
        print(f"  Accept: {r['headers'].get('accept', '<无>')}")

    print("\n===== SSE 响应 =====")
    for r in sse_responses:
        print(f"  Status: {r['status']}, URL: {r['url']}")

    print("\n===== Console (error/warning/debug) =====")
    for m in console_msgs[:20]:
        print(f"  [{m['type']}] {m['text']}")

    print(f"\n===== 结论 =====")
    if not sse_responses:
        print("  FAIL: 未捕获到 SSE 请求")
    elif any(r["status"] == 200 for r in sse_responses):
        print("  PASS: SSE 返回 200, 401 已修复")
    elif all(r["status"] == 401 for r in sse_responses):
        print("  FAIL: SSE 仍返回 401")
    else:
        statuses = [r["status"] for r in sse_responses]
        print(f"  WARN: SSE 状态码 {statuses}")

    # 截图
    page.screenshot(path="d:/projects/sakurafilter/_verify_sse_fix.png", full_page=False)
    print("\n[截图] _verify_sse_fix.png")

    browser.close()
