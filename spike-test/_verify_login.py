"""
直接验证登录 API 在 Playwright 中的完整流程, 不走其他逻辑
"""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent))
from playwright.sync_api import sync_playwright

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(viewport={"width": 1280, "height": 800})
    page = ctx.new_page()

    # 收集所有网络请求
    api_calls = []
    def on_response(r):
        if "/api/" in r.url:
            api_calls.append((r.status, r.url, r.request.method))
    page.on("response", on_response)
    page.on("console", lambda m: print(f"  [console.{m.type}] {m.text}") if m.type != "debug" else None)
    page.on("pageerror", lambda e: print(f"  [pageerror] {e}"))

    # 1. 跳到登录页
    print("[1] 跳到登录页")
    page.goto("http://localhost:5173/login", wait_until="domcontentloaded")
    page.wait_for_timeout(500)

    # 2. 填表
    print("[2] 填表 + 提交")
    user_inp = page.locator('input[placeholder*="用户"], input[placeholder*="账号"]').first
    pass_inp = page.locator('input[type="password"]').first
    user_inp.click()  # 先聚焦
    user_inp.fill("admin")
    page.wait_for_timeout(200)
    pass_inp.click()
    pass_inp.fill("Admin@2026")
    page.wait_for_timeout(200)

    # 验证 fill 生效
    user_val = user_inp.input_value()
    pass_val = pass_inp.input_value()
    print(f"  [debug] user value: '{user_val}', pass value: '{pass_val}'")

    # 3. 监听登录 API 响应
    # 关键: 用 form 内的按钮 (class="w-full" 是 form 提交按钮)
    # 顶部导航的"进入后台登录"按钮 class="hidden sm:flex ..." 不可点
    btn = page.locator('form button.w-full').first
    print(f"  [debug] form submit 按钮 count={btn.count()}, visible={btn.is_visible()}, enabled={btn.is_enabled()}")
    with page.expect_response(lambda r: "/api/auth/login" in r.url, timeout=20000) as resp_info:
        btn.click()
    resp = resp_info.value
    print(f"  [login] status={resp.status}")
    body = resp.text()
    print(f"  [login] body: {body[:200]}")

    # 4. 等跳转
    page.wait_for_timeout(2000)
    print(f"[3] 登录后 URL: {page.url}")

    # 5. 看 localStorage
    ls = page.evaluate("""() => {
      const out = {}
      for (let i = 0; i < localStorage.length; i++) {
        const k = localStorage.key(i)
        out[k] = localStorage.getItem(k)?.substring(0, 100)
      }
      return out
    }""")
    print(f"[4] localStorage: {ls}")

    # 6. 看路由 query
    rquery = page.evaluate("""() => {
      return JSON.stringify(window.location)
    }""")
    print(f"[5] window.location: {rquery}")

    print("\n[6] 所有 API 调用:")
    for s, u, m in api_calls:
        print(f"  {m} {s} {u}")

    browser.close()
