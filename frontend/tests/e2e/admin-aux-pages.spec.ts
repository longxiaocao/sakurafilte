// E2E-辅助管理页深度交互 (2026-09-13 升级: 原仅加载+元素存在 → 真实点击/输入/过滤/表单增删/失败路径)
//   WHY: 原 6 用例只断言"页面加载 + 核心元素存在 + 截图", 未验证交互; 本版覆盖:
//     1. ops 六个 tab 逐个点击切换 (ETL/性能/错误/API 文档/存储配置/孤立图片)
//     2. errors 触发测试错误 + 搜索过滤 (监控链路真实工作)
//     3. api-docs 搜索过滤 + 端点展开
//     4. site-content 新增新闻 → 删除 (不点保存, 无持久化污染)
//     5. alerts 过滤下拉 + 搜索 + 重置 (不触发测试告警, 避免写生产告警数据)
//     6. change-password 旧密码错误 → 失败提示 (不改真实密码)
//   鉴权: 真实 JWT (helpers/auth.ts) — 与后端 DevStaticToken 解耦, 避免旧 token 假通过
import { test, expect, request } from '@playwright/test'
import { loginAsAdmin, injectAdminAuth, type AdminAuth } from './helpers/auth'

const BASE = process.env.BASE_URL || 'http://localhost:5175'

let adminAuth: AdminAuth
test.beforeAll(async () => {
  const ctx = await request.newContext()
  adminAuth = await loginAsAdmin(ctx)
  await ctx.dispose()
})

async function gotoPage(page: import('@playwright/test').Page, path: string) {
  await injectAdminAuth(page, adminAuth)
  await page.goto(`${BASE}${path}`, { waitUntil: 'domcontentloaded', timeout: 45000 })
}

test.describe('E2E-辅助管理页 (后台, 深度交互)', () => {
  test('1. /admin/ops 六个 tab 逐个点击切换', async ({ page }) => {
    await gotoPage(page, '/admin/ops')
    await page.waitForSelector('.el-tabs, h1', { timeout: 10000 })
    const tabs = ['ETL 触发与监控', '性能', '错误', 'API 文档', '存储配置', '孤立图片']
    for (let i = 0; i < tabs.length; i++) {
      const name = tabs[i]
      await page.getByRole('tab', { name }).click()
      // tab 被激活 = 切换真实生效
      await expect(page.getByRole('tab', { name })).toHaveAttribute('aria-selected', 'true', { timeout: 10000 })
      // lazy pane 挂载后内容非空 (空态也有"暂无"类文本)
      await expect
        .poll(() => page.locator('.el-tabs__content').innerText().then((t) => t.trim().length), { timeout: 10000 })
        .toBeGreaterThan(0)
      await page.screenshot({ path: `test-results/e2e-ops-tab-${i + 1}.png` })
    }
  })

  test('2. /admin/errors 触发测试错误 + 搜索过滤', async ({ page }) => {
    await gotoPage(page, '/admin/errors')
    await page.waitForSelector('.el-input, h1', { timeout: 10000 })
    // 触发测试错误 → errorMonitor 本地事件 +1 (页面设计功能, 无后端副作用)
    await page.getByRole('button', { name: '触发测试错误' }).first().click()
    // WHY 用 RegExp 字面量: 字符串 'text=/显示 \d+/' 中 \d 会被 JS 转义为字面 d
    await expect(page.getByText(/显示 \d+ \/ \d+/)).toBeVisible({ timeout: 5000 })
    // 搜索无匹配关键词 → 列表过滤为空但不崩溃
    await page.getByPlaceholder('搜索 message / type / tags…').fill('e2e-nomatch-xyz')
    await expect(page.getByText(/显示 0 \/ \d+/)).toBeVisible({ timeout: 5000 })
    await page.screenshot({ path: 'test-results/e2e-errors-filtered.png' })
  })

  test('3. /admin/api-docs 搜索过滤 + 端点展开', async ({ page }) => {
    await gotoPage(page, '/admin/api-docs')
    await page.waitForSelector('section.hairline', { timeout: 15000 })
    const before = await page.locator('section.hairline').count()
    // 搜索 auth → 模块数减少 (Auth 模块过滤后少于全量)
    //   WHY getByRole('textbox', { name: '搜索端点' }): 页内 .el-input__inner 首个是 header 全局搜索框,
    //     会触发联想而非本页过滤; aria-label="搜索端点" (admin.apidocs.search_aria) 精准唯一
    await page.getByRole('textbox', { name: '搜索端点' }).fill('auth')
    await expect.poll(async () => page.locator('section.hairline').count(), { timeout: 10000 }).toBeLessThan(before)
    // 展开第一个端点 → 折叠箭头变 ▼
    await page.locator('section.hairline').first().locator('button').first().click()
    await expect(page.locator('section.hairline').first().getByText('▼').first()).toBeVisible({ timeout: 5000 })
    await page.screenshot({ path: 'test-results/e2e-api-docs-expanded.png' })
  })

  test('4. /admin/site-content 新增新闻 → 删除 (不保存无污染)', async ({ page }) => {
    await gotoPage(page, '/admin/site-content')
    await page.waitForSelector('.el-input, h1', { timeout: 10000 })
    const beforeNews = await page.getByPlaceholder('新闻标题').count()
    await page.getByRole('button', { name: '新增新闻' }).click()
    await expect(page.getByPlaceholder('新闻标题')).toHaveCount(beforeNews + 1, { timeout: 5000 })
    await page.getByPlaceholder('新闻标题').last().fill('E2E 临时新闻')
    // 删除该行 → 列表回到原状 (未保存, 无持久化污染)
    await page.getByRole('button', { name: '删除' }).last().click()
    await expect(page.getByPlaceholder('新闻标题')).toHaveCount(beforeNews, { timeout: 5000 })
    await page.screenshot({ path: 'test-results/e2e-site-content-news.png' })
  })

  test('5. /admin/alerts 过滤下拉 + 搜索 + 重置 (不触发测试告警)', async ({ page }) => {
    await gotoPage(page, '/admin/alerts')
    await page.waitForSelector('.el-select, h1', { timeout: 10000 })
    // severity 下拉 (第 2 个 select: type/severity/status) 选择一项
    await page.locator('.el-select').nth(1).click()
    // WHY :visible: 页面可能存在已关闭 dropdown 的残留 DOM (v-show=false), 不限定可见会点中隐藏项
    await page.locator('.el-select-dropdown__item:visible').first().click()
    // 搜索 → 等待列表请求完成
    const respPromise = page.waitForResponse(
      (r) => r.url().includes('/api/admin/alerts/history') && r.request().method() === 'GET',
      { timeout: 15000 }
    )
    await page.getByRole('button', { name: '搜索' }).first().click()
    const resp = await respPromise
    expect([200, 400]).toContain(resp.status())
    // 重置 → 列表刷新, 页面保持可用
    await page.getByRole('button', { name: '重置' }).first().click()
    await expect(page.locator('.el-table').first()).toBeVisible({ timeout: 5000 })
    await page.screenshot({ path: 'test-results/e2e-alerts-filtered.png' })
  })

  test('6. /change-password 旧密码错误 → 失败提示 (不改真实密码)', async ({ page }) => {
    await gotoPage(page, '/change-password')
    await page.waitForSelector('#old-password', { timeout: 10000 })
    await page.locator('#old-password').fill('WrongOld@2026')
    await page.locator('#new-password').fill('NewPass@2026')
    await page.locator('#confirm-password').fill('NewPass@2026')
    await page.getByRole('button', { name: '确认修改' }).click()
    // 400 旧密码错误 → axios 拦截器弹 error toast, 页面不跳转
    await expect(page.locator('.el-message--error')).toBeVisible({ timeout: 10000 })
    expect(page.url()).toContain('/change-password')
    await page.screenshot({ path: 'test-results/e2e-change-password-fail.png' })
  })
})
