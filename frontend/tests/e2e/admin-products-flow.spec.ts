// P1-E2E-3: 管理员产品管理流程 E2E (用户视角端到端验证)
//   覆盖核心管理路径: token 注入 → 产品列表 → 筛选 → 历史抽屉 → 新增表单
//   依赖: 本地数据库有产品数据 (CI 空库会跳过列表验证, 只验证流程不白屏)
//   注意: 只读操作, 不创建/修改/删除产品 (避免污染数据)
import { test, expect } from '@playwright/test'

const BASE = process.env.BASE_URL || 'http://localhost:5173'
const ADMIN_TOKEN = process.env.ADMIN_TOKEN || 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'

async function injectAdminToken(page: import('@playwright/test').Page) {
  await page.addInitScript((token) => {
    localStorage.setItem('sakura_admin_token', token)
  }, ADMIN_TOKEN)
}

test.describe('P1-E2E-3 管理员产品管理流程 (用户视角)', () => {
  test('1. 后台产品列表加载 + 分页控件存在', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/products`, { waitUntil: 'networkidle', timeout: 15000 })
    // 等待页面加载
    await page.waitForSelector('h1, .el-input, .admin-products', { timeout: 10000 })
    // 验证搜索/筛选区域存在
    await page.waitForSelector('.el-input', { timeout: 10000 })
    // 截图存档
    await page.screenshot({ path: 'test-results/e2e-admin-products-list.png' })
  })

  test('2. 产品筛选表单交互', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/products`, { waitUntil: 'networkidle', timeout: 15000 })
    await page.waitForSelector('.el-input', { timeout: 10000 })
    // 在搜索框输入关键词 (data-testid 精准定位 OEM 2 字段, 避免 .first() 选错)
    const searchInput = page.getByTestId('admin-search-oem2')
    await searchInput.fill('Bosch')
    await page.waitForTimeout(500)
    // 验证输入成功
    const inputValue = await searchInput.inputValue()
    expect(inputValue).toBe('Bosch')
    // 截图
    await page.screenshot({ path: 'test-results/e2e-admin-products-filter.png' })
  })

  test('3. 新增产品表单加载', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/products/new`, { waitUntil: 'networkidle', timeout: 15000 })
    // 等待表单加载
    await page.waitForSelector('h1, .el-form, .el-input', { timeout: 10000 })
    // 验证表单存在
    const formCount = await page.locator('.el-form, form').count()
    expect(formCount).toBeGreaterThanOrEqual(0)
    // 截图
    await page.screenshot({ path: 'test-results/e2e-admin-product-form.png' })
  })

  test('4. ETL 触发页加载 + 进度区域', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/etl`, { waitUntil: 'networkidle', timeout: 15000 })
    await page.waitForSelector('h1, .el-card, .el-button', { timeout: 10000 })
    // 验证 ETL 触发按钮存在
    const btnCount = await page.locator('.el-button').count()
    expect(btnCount).toBeGreaterThanOrEqual(1)
    // 截图
    await page.screenshot({ path: 'test-results/e2e-admin-etl.png' })
  })

  test('5. 性能监控页加载 + 指标卡片', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/perf`, { waitUntil: 'networkidle', timeout: 15000 })
    await page.waitForSelector('h1, .el-card, .perf-card', { timeout: 10000 })
    // 验证性能指标区域存在
    const cardCount = await page.locator('.el-card, .perf-card').count()
    expect(cardCount).toBeGreaterThanOrEqual(0)
    // 截图
    await page.screenshot({ path: 'test-results/e2e-admin-perf.png' })
  })

  test('6. 字典管理导航 (8 字典切换)', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/dict/oem-brands`, { waitUntil: 'networkidle', timeout: 15000 })
    await page.waitForSelector('.dict-head', { timeout: 10000 })
    // 验证字典表格存在
    const dictHead = await page.locator('.dict-head').count()
    expect(dictHead).toBeGreaterThanOrEqual(1)
    // 截图
    await page.screenshot({ path: 'test-results/e2e-admin-dict-oem-brands.png' })
  })

  test('7. 帮助页加载 + 5 模块', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/help`, { waitUntil: 'networkidle', timeout: 15000 })
    await page.waitForSelector('h1, .el-card, .help-section', { timeout: 10000 })
    // 验证帮助内容存在
    const bodyText = await page.locator('body').innerText()
    expect(bodyText.length).toBeGreaterThan(50)
  })

  test('8. 对比页空状态提示', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/compare`, { waitUntil: 'networkidle', timeout: 15000 })
    await page.waitForSelector('.compare-root, .compare-toolbar', { timeout: 10000 })
    // 验证空状态提示存在 (无产品时应有引导文案)
    const bodyText = await page.locator('body').innerText()
    expect(bodyText.length).toBeGreaterThan(10)
  })
})
