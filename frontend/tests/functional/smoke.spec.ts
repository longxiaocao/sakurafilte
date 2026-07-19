// P0-E2E-2: CI 功能性 smoke 测试 (不依赖数据, 只验证页面加载 + 路由 + 组件渲染)
//   WHY: 视觉回归 (dict-pages.spec.ts) 需要数据, CI 空库会失败
//        功能性 smoke 只验证页面能加载、关键 DOM 元素出现, 不依赖数据状态
//   覆盖:
//     1. 公开搜索页 (/search) 能加载
//     2. 公开产品详情页 (/product/:oem) 路由能匹配
//     3. admin token 注入后后台产品页 (/admin/products) 能加载
//     4. 8 个字典页 (/admin/dict/*) 能加载 (.dict-head 出现)
//     5. ETL 触发页 (/admin/etl) 能加载
//     6. 性能监控页 (/admin/perf) 能加载
//     7. 帮助页 (/admin/help) 能加载
//     8. 对比页 (/admin/compare) 能加载
import { test, expect } from '@playwright/test'

const BASE = process.env.BASE_URL || 'http://localhost:5173'
const ADMIN_TOKEN = process.env.ADMIN_TOKEN || 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'

// 注入 admin token 的辅助函数
async function injectAdminToken(page: import('@playwright/test').Page) {
  await page.addInitScript((token) => {
    localStorage.setItem('sakura_admin_token', token)
  }, ADMIN_TOKEN)
}

test.describe('P0-E2E-2 功能性 smoke (CI 空库友好)', () => {
  test('1. 公开搜索页 /search 能加载', async ({ page }) => {
    await page.goto(`${BASE}/search`, { waitUntil: 'networkidle', timeout: 15000 })
    // 等待 AppHeader 出现 (说明 Vue 应用已挂载)
    await page.waitForSelector('nav, header, .app-header', { timeout: 10000 })
    // 截图存档 (不对比, 仅诊断)
    await page.screenshot({ path: 'test-results/smoke-search.png' })
  })

  test('2. 公开产品详情页路由能匹配 (404 也算 PASS, 只验证不白屏)', async ({ page }) => {
    // 用一个不存在的 OEM, 期望 404 提示而非白屏
    await page.goto(`${BASE}/product/nonexistent-oem-12345`, { waitUntil: 'networkidle', timeout: 15000 })
    await page.waitForTimeout(1000)
    // 页面应有内容 (不是空白)
    const bodyText = await page.locator('body').innerText()
    expect(bodyText.length).toBeGreaterThan(0)
  })

  test('3. admin token 注入后后台产品页能加载', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/products`, { waitUntil: 'networkidle', timeout: 15000 })
    // 等待页面标题或表格容器出现
    await page.waitForSelector('h1, .el-input, .admin-products', { timeout: 10000 })
    await page.screenshot({ path: 'test-results/smoke-admin-products.png' })
  })

  test.describe('4. 8 个字典页能加载', () => {
    const dicts = [
      { name: 'oem-brands', path: '/admin/dict/oem-brands' },
      { name: 'product-name1s', path: '/admin/dict/product-name1s' },
      { name: 'product-name2s', path: '/admin/dict/product-name2s' },
      { name: 'types', path: '/admin/dict/types' },
      { name: 'oem-no3s', path: '/admin/dict/oem-no3s' },
      { name: 'medias', path: '/admin/dict/medias' },
      { name: 'machines', path: '/admin/dict/machines' },
      { name: 'engines', path: '/admin/dict/engines' }
    ]
    for (const dict of dicts) {
      test(`字典页 ${dict.name} 能加载 (.dict-head 出现)`, async ({ page }) => {
        await injectAdminToken(page)
        await page.goto(`${BASE}${dict.path}`, { waitUntil: 'networkidle', timeout: 15000 })
        // 等表头出现 (说明组件已渲染, 不依赖数据)
        await page.waitForSelector('.dict-head', { timeout: 10000 })
      })
    }
  })

  test('5. ETL 触发页能加载', async ({ page }) => {
    await injectAdminToken(page)
    // V24-F78/F79: ETL 页通过 useEtlProgress 建立 fetch + ReadableStream SSE 长连接
    //   持续接收进度 → networkidle 永远达不到 (15s 超时失败)
    //   改用 domcontentloaded: SSE 不影响 DOM 加载完成, 验证页面挂载即可
    await page.goto(`${BASE}/admin/etl`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('h1, .el-card, .el-button', { timeout: 10000 })
  })

  test('6. 性能监控页能加载', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/perf`, { waitUntil: 'networkidle', timeout: 15000 })
    await page.waitForSelector('h1, .el-card, .perf-card', { timeout: 10000 })
  })

  test('7. 帮助页能加载', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/help`, { waitUntil: 'networkidle', timeout: 15000 })
    await page.waitForSelector('h1, .el-card, .help-section', { timeout: 10000 })
  })

  test('8. 对比页能加载 (空状态)', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/compare`, { waitUntil: 'networkidle', timeout: 15000 })
    // 对比页用 .compare-root 容器, 空状态用 .text-muted 提示
    await page.waitForSelector('.compare-root, .compare-toolbar', { timeout: 10000 })
  })
})
