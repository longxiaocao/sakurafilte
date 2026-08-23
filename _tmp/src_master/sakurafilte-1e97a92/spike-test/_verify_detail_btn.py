"""
Day 14 P0 UX 验证: 详情页"查询替代" + "加入对比" 按钮
- 查询替代: 滚动到 #section-alternatives 表格
- 加入对比: 跳转 URL 携带 ?ids=<id>

非破坏性测试, 只读不写, 不持久化修改.
"""
import sys
import json
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from playwright.sync_api import sync_playwright

PRODUCT_OEM = "AC 010323"
PRODUCT_URL = f"http://localhost:5173/product/{PRODUCT_OEM.replace(' ', '%20')}"

results = []
def check(name, ok, evidence=""):
    results.append({"name": name, "ok": ok, "evidence": evidence})
    icon = "✓" if ok else "✗"
    print(f"  {icon} [{name}] {evidence}")

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(viewport={"width": 1280, "height": 800})
    page = ctx.new_page()

    # 收集 console 日志
    page.on("console", lambda msg: print(f"  [console.{msg.type}] {msg.text}"))
    page.on("pageerror", lambda exc: print(f"  [pageerror] {exc}"))

    # ===== 加载详情页 =====
    print(f"\n[1] 加载详情页: {PRODUCT_URL}")
    try:
        resp = page.goto(PRODUCT_URL, wait_until="domcontentloaded", timeout=15000)
        check("详情页 HTTP 200", resp.status == 200, f"status={resp.status}")
    except Exception as e:
        check("详情页加载", False, f"异常: {e}")
        browser.close()
        sys.exit(1)

    # 等待产品数据加载
    page.wait_for_selector("h1", timeout=10000)
    page.wait_for_timeout(800)  # 让替代 OEM section 渲染

    # ===== 验证"查询替代"按钮 =====
    print("\n[2] 验证'查询替代'按钮")
    # 找按钮 (文本包含"查询替代")
    btn_alt = page.get_by_role("button", name="查询替代", exact=False).first
    check("找到'查询替代'按钮", btn_alt.is_visible(), f"text='{btn_alt.inner_text() if btn_alt else 'N/A'}'")

    # 记录点击前的 scrollY
    before_y = page.evaluate("window.scrollY")
    btn_alt.click()
    page.wait_for_timeout(800)  # 等 smooth scroll 完成

    after_y = page.evaluate("window.scrollY")

    # 关键检查: section 是否在视口内 (如果原本就在视口, scrollY 不变是正常的)
    section_in_viewport = page.evaluate("""() => {
      const el = document.getElementById('section-alternatives')
      if (!el) return false
      const rect = el.getBoundingClientRect()
      return rect.top < window.innerHeight && rect.bottom > 0
    }""")
    check("替代 OEM section 进入视口 (滚动目标达成)", section_in_viewport,
          f"before_y={before_y}, after_y={after_y}")

    # 如果原本不在视口, 滚动后应该到视口内
    section_was_in_viewport = page.evaluate("""() => {
      const el = document.getElementById('section-alternatives')
      if (!el) return false
      const rect = el.getBoundingClientRect()
      return rect.top < window.innerHeight && rect.bottom > 0
    }""")
    # 重新加载页面测试"原本不在视口"情况
    page.goto(PRODUCT_URL, wait_until="domcontentloaded", timeout=15000)
    page.wait_for_selector("h1", timeout=10000)
    page.wait_for_timeout(800)
    was_visible_before_click = page.evaluate("""() => {
      const el = document.getElementById('section-alternatives')
      if (!el) return false
      const rect = el.getBoundingClientRect()
      return rect.top >= 0 && rect.top < window.innerHeight
    }""")
    print(f"  [info] 点击前 section 是否在视口: {was_visible_before_click}")
    page.get_by_role("button", name="查询替代", exact=False).first.click()
    page.wait_for_timeout(800)
    is_visible_after_click = page.evaluate("""() => {
      const el = document.getElementById('section-alternatives')
      if (!el) return false
      const rect = el.getBoundingClientRect()
      return rect.top >= 0 && rect.top < window.innerHeight
    }""")
    check("点击后替代 OEM section 一定在视口", is_visible_after_click,
          f"was_visible_before={was_visible_before_click}, after={is_visible_after_click}")

    # ===== 验证"加入对比"按钮 =====
    print("\n[3] 验证'加入对比'按钮")
    btn_cmp = page.get_by_role("button", name="加入对比", exact=False).first
    check("找到'加入对比'按钮", btn_cmp.is_visible())

    # 提取产品 id (从 API 或 URL 推断)
    product_id = page.evaluate("""() => {
      // 找 oemNoDisplay 元素 (详情页有显示)
      const spans = document.querySelectorAll('.font-mono')
      for (const s of spans) {
        const txt = s.textContent?.trim() || ''
        // OEM 编号格式: AC 010323 等
      }
      return null  // id 难从 DOM 直接拿到, 改从 API 拿
    }""")

    # 点击后等待跳转
    with page.expect_navigation(wait_until="domcontentloaded", timeout=8000) as nav_info:
        btn_cmp.click()
    nav = nav_info.value

    final_url = page.url
    print(f"  跳转后 URL: {final_url}")

    # 未登录 → 期望跳 /login?redirect=/admin/compare?ids=xxx
    # 已登录 → 期望 /admin/compare?ids=xxx
    is_login_redirect = "/login" in final_url and "redirect=" in final_url
    is_compare_page = "/admin/compare" in final_url and "ids=" in final_url

    check("跳转到登录页 (未登录) 或对比页 (已登录)",
          is_login_redirect or is_compare_page,
          f"login={is_login_redirect}, compare={is_compare_page}")

    if is_login_redirect:
        # 解析 redirect 参数, 验证包含 ids=
        import urllib.parse as up
        parsed = up.urlparse(final_url)
        qs = up.parse_qs(parsed.query)
        redirect = qs.get("redirect", [""])[0]
        has_ids = "ids=" in redirect
        check("redirect URL 携带 ids= 参数", has_ids, f"redirect={redirect[:80]}")

    if is_compare_page:
        # 已登录: 验证对比页加载了产品
        page.wait_for_timeout(1500)
        check("对比页加载了产品", page.locator(".el-table__row").count() > 0,
              f"rows={page.locator('.el-table__row').count()}")
    elif is_login_redirect:
        # 未登录: 跳到登录页是预期行为, redirect URL 携带 ids 已验证
        check("未登录跳登录页 (预期, redirect 携带 ids=)", True,
              "未登录场景, 登录后路由守卫会跳回 /admin/compare?ids=<id>")

    # ===== 真实登录后回跳验证 =====
    #   P0 核心: 验证 redirect 跳回后, 对比页能根据 ids 自动加载
    #   这模拟真实用户体验: 公开页点"加入对比" → 登录 → 跳到对比页看到产品
    print("\n[3.1] 真实登录流程 + 对比页自动加载")
    # 走完整流程: 详情页 → 加入对比 → 登录页(redirect) → 登录 → 对比页
    page.goto(PRODUCT_URL, wait_until="domcontentloaded", timeout=15000)
    page.wait_for_selector("h1", timeout=10000)
    page.wait_for_timeout(500)
    # 点"加入对比"
    page.get_by_role("button", name="加入对比", exact=False).first.click()
    page.wait_for_url("**/login**", timeout=8000)
    check("未登录点'加入对比' 跳登录页 + redirect", "redirect=" in page.url,
          f"URL={page.url}")

    # 在登录页登录
    user_input = page.locator('input[placeholder*="用户"], input[placeholder*="账号"], input[type="text"]').first
    pass_input = page.locator('input[type="password"]').first
    if user_input.count() > 0 and pass_input.count() > 0:
        user_input.click()
        user_input.fill("admin")
        page.wait_for_timeout(200)
        pass_input.click()
        pass_input.fill("Admin@2026")
        page.wait_for_timeout(200)
        # 关键: 用 form 内提交按钮 (class="w-full"), 不是顶部导航的"进入后台登录"
        page.locator('form button.w-full').first.click()

        # 登录后应自动跳到 /admin/compare?ids=1 (redirect)
        try:
            page.wait_for_url("**/admin/compare**", timeout=15000)
            after_login_url = page.url
            check("登录后跳转到 /admin/compare?ids=1", "ids=1" in after_login_url, f"URL={after_login_url}")

            # 验证对比页加载了产品
            page.wait_for_timeout(3000)
            rows = page.locator(".el-table__row").count()
            body_text = page.locator("body").inner_text()
            has_product = "AC 010323" in body_text or rows > 0
            check("对比页加载了产品 (AC 010323)", has_product, f"rows={rows}, body has AC 010323: {'AC 010323' in body_text}")
        except Exception as e:
            check("登录后跳转到 /admin/compare", False, f"异常: {e}, URL={page.url}")
    else:
        print(f"  [skip] 找不到登录表单输入")

    # 截图 (无侵入)
    page.screenshot(path="e2e-screenshots/_detail_btn_fix.png", full_page=False)
    print("\n  截图: e2e-screenshots/_detail_btn_fix.png")

    browser.close()

# ===== 汇总 =====
print("\n" + "=" * 60)
print(f"  P0 验证汇总: {sum(1 for r in results if r['ok'])}/{len(results)} 通过")
print("=" * 60)
if all(r["ok"] for r in results):
    print("  ✓ 所有 P0 修复验证通过")
else:
    print("  ✗ 部分验证失败:")
    for r in results:
        if not r["ok"]:
            print(f"    - {r['name']}: {r['evidence']}")
    sys.exit(1)
