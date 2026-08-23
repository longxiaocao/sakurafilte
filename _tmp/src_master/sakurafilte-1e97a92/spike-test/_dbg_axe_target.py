"""精确定位axe-core报告的no-label元素 #el-id-XXXX-XX"""
import sys
from pathlib import Path
from playwright.sync_api import sync_playwright

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    page = browser.new_page()

    # 登录
    page.goto('http://localhost:5173/login', wait_until='networkidle')
    page.fill('input[autocomplete="username"]', 'admin')
    page.fill('input[autocomplete="current-password"]', 'Admin@2026')
    page.locator('button:has-text("登录")').first.click()
    page.wait_for_url('**/admin/**', timeout=10000)

    # 进入产品列表
    page.goto('http://localhost:5173/admin/products', wait_until='networkidle')
    page.wait_for_timeout(2000)

    # 触发分页
    el_pagination = page.locator('.el-pagination').first
    if el_pagination.count() > 0:
        # 滚动到底部触发分页渲染
        el_pagination.scroll_into_view_if_needed()
        page.wait_for_timeout(500)
        print('=== .el-pagination 内部所有 input ===')
        inputs = page.query_selector_all('.el-pagination input')
        for i, inp in enumerate(inputs):
            outer = inp.evaluate('el => el.outerHTML.slice(0, 300)')
            aria = inp.get_attribute('aria-label')
            print(f'  [{i}] aria-label={aria!r} outer={outer[:200]}')

    # 打印所有 input (无 aria-label / 无 aria-labelledby / 无 placeholder)
    print('\n=== 所有可能的 no-label input ===')
    all_inputs = page.query_selector_all('input, textarea, select')
    for i, inp in enumerate(all_inputs):
        el_id = inp.get_attribute('id') or ''
        aria_label = inp.get_attribute('aria-label')
        aria_labelledby = inp.get_attribute('aria-labelledby')
        placeholder = inp.get_attribute('placeholder')
        # 判断是否为 el-pagination 的 input
        in_pagination = inp.evaluate('el => !!el.closest(".el-pagination")')
        # 隐藏 input
        type_attr = inp.get_attribute('type')
        hidden = type_attr == 'hidden'
        if in_pagination or (not aria_label and not aria_labelledby and not hidden):
            outer = inp.evaluate('el => el.outerHTML.slice(0, 250)')
            print(f'  [{i}] id={el_id!r} aria-label={aria_label!r} ph={placeholder!r} type={type_attr!r} in_pagination={in_pagination}')
            print(f'      outer: {outer[:200]}')

    browser.close()
