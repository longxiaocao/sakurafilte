from playwright.sync_api import sync_playwright
import time
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    page = browser.new_page()
    page.goto('http://localhost:5173/login')
    page.fill('input[autocomplete="username"]', 'admin')
    page.fill('input[autocomplete="current-password"]', 'Admin@2026')
    page.locator('button:has-text("登录")').first.click()
    page.wait_for_url('**/admin/**', timeout=10000)
    print('current url:', page.url)
    time.sleep(1)
    page.goto('http://localhost:5173/admin/products', wait_until='networkidle')
    print('after navigate url:', page.url)
    time.sleep(2)
    # 列所有 el-input/select
    inputs = page.query_selector_all('.el-input, .el-select, .el-input-number')
    print('found inputs:', len(inputs))
    # 找没 aria-label 的
    no_label_count = 0
    for inp in inputs:
        inner = inp.query_selector('input')
        if not inner:
            continue
        aria_label = inner.get_attribute('aria-label')
        aria_labelledby = inner.get_attribute('aria-labelledby')
        inner_id = inner.get_attribute('id')
        if aria_label or aria_labelledby:
            continue
        # 找最近的 label
        form_item = inp.evaluate('el => { const f = el.closest(".el-form-item"); return f ? f.querySelector(".el-form-item__label")?.innerText : null; }')
        if form_item:
            print(f'  form-item-label: {form_item[:30]} (id={inner_id})')
            continue
        no_label_count += 1
        # 打印完整父结构
        outer = inp.evaluate('el => el.outerHTML.slice(0, 300)')
        print(f'  NO LABEL #{no_label_count}: id={inner_id}')
        print(f'    self: {outer}')
        # 打印父级结构
        parent_html = inp.evaluate('el => { let p = el.parentElement; return p ? p.outerHTML.slice(0, 400) : null; }')
        print(f'    parent: {parent_html}')
    print(f'\nTotal no-label: {no_label_count}')
    browser.close()

