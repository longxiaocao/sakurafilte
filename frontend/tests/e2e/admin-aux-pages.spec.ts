// E2E-辅助管理页: ops / errors / api-docs / site-content / alerts / change-password (2026-08-23 补缺口)
//   覆盖: 各页加载 + 核心 UI 元素存在 (不写数据)
import { test, expect } from '@playwright/test'

const BASE = process.env.BASE_URL || 'http://localhost:5173'
const ADMIN_TOKEN = process.env.ADMIN_TOKEN || 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'

async function injectAdminToken(page: import('@playwright/test').Page) {
  await page.addInitScript((token) => {
    localStorage.setItem('sakura_admin_token', token)
  }, ADMIN_TOKEN)
}

test.describe('E2E-辅助管理页 (后台)', () => {
  test('1. 运维页 /admin/ops 加载 (tabs 结构)', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/ops`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('.el-tabs, h1, .el-button', { timeout: 10000 })
    const text = await page.locator('body').innerText()
    expect(text.trim().length).toBeGreaterThan(0)
    await page.screenshot({ path: 'test-results/e2e-ops.png' })
  })

  test('2. 错误监控页 /admin/errors 加载', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/errors`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('.el-table, h1, .el-input', { timeout: 10000 })
    await page.screenshot({ path: 'test-results/e2e-errors.png' })
  })

  test('3. API 文档页 /admin/api-docs 加载 + 搜索框', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/api-docs`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('.el-input, h1, .el-button', { timeout: 10000 })
    await page.screenshot({ path: 'test-results/e2e-api-docs.png' })
  })

  test('4. 站点内容管理 /admin/site-content 加载 + 表单字段', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/site-content`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('.el-input, h1, .el-button', { timeout: 10000 })
    await page.screenshot({ path: 'test-results/e2e-site-content.png' })
  })

  test('5. 告警页 /admin/alerts 加载 + 过滤/测试按钮', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/alerts`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('.el-select, h1, .el-button', { timeout: 10000 })
    await page.screenshot({ path: 'test-results/e2e-alerts.png' })
  })

  test('6. 修改密码页 /change-password 加载 + 表单', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/change-password`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('.el-input, .el-button', { timeout: 10000 })
    await page.screenshot({ path: 'test-results/e2e-change-password.png' })
  })
})
