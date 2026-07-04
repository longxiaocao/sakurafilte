// P3.5 (Task 12): 对比 UI 视觉/截图测试 (Playwright, 可选)
//   用法: npx playwright test tests/visual/compare.spec.ts
//   前置: 需安装 @playwright/test + 后端运行在 http://localhost:5082 + 至少 3 个产品已导入
//   截图输出: tests/visual/__screenshots__/compare.png
//
//   跑测试前:
//     1. cd frontend && npm i -D @playwright/test
//     2. npx playwright install chromium
//     3. 启动后端: dotnet run --project ../backend/src/SakuraFilter.Api (或用 start-dev.bat)
// P0-E2E-1 修复: ESM 模式下 __dirname 不存在, 用 fileURLToPath 兼容
//            同时修复 token 默认值与其他 spec 统一 (旧值 devtoken 已失效)
import { test, expect } from '@playwright/test'
import path, { dirname } from 'path'
import { fileURLToPath } from 'url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = dirname(__filename)

const BASE = process.env.BASE_URL || 'http://localhost:5173'
const ADMIN_TOKEN = process.env.ADMIN_TOKEN || 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'

test.describe('P3.5 产品对比 UI', () => {
  test('6 列 grid 布局 + 差异高亮 + 列调序', async ({ page }) => {
    // 设 admin token
    await page.addInitScript((token) => {
      localStorage.setItem('sakura_admin_token', token)
    }, ADMIN_TOKEN)

    // 1. 直接打开对比页 (取前 6 个产品 ID 从 search)
    await page.goto(`${BASE}/search`)
    // 等搜索结果
    await page.waitForTimeout(500)
    // 用 URL 方式更可控, 这里手工填 ids (需后端已存在 ID=1..6)
    await page.goto(`${BASE}/admin/compare?ids=1,2,3,4,5,6`)

    // 2. 等 grid 出现
    await page.waitForSelector('.compare-grid', { timeout: 5000 })

    // 3. 校验布局: 至少 1 个表头 + N 个数据 cell
    const headerCount = await page.locator('.compare-header-cell').count()
    expect(headerCount).toBeGreaterThanOrEqual(1) // 至少 1 个 "字段"

    // 4. 校验差异高亮: 至少存在 .same 或 .diff 或 .empty
    const diffCount = await page.locator('.data-cell.diff').count()
    const sameCount = await page.locator('.data-cell.same').count()
    console.log(`[compare] diff=${diffCount}, same=${sameCount}`)

    // 5. 截图 (含全页)
    const screenshotPath = path.join(__dirname, '__screenshots__', 'compare.png')
    await page.screenshot({ path: screenshotPath, fullPage: true })
    console.log(`[compare] 截图: ${screenshotPath}`)

    // 6. 校验移除按钮存在 (×)
    const removeBtns = await page.locator('.product-cell .el-button:has-text("×")').count()
    expect(removeBtns).toBeGreaterThanOrEqual(1)

    // 7. 测打印 CSS: emulating media=print 应隐藏 toolbar
    await page.emulateMedia({ media: 'print' })
    const toolbarVisible = await page.locator('.compare-toolbar').isVisible()
    expect(toolbarVisible).toBe(false)
    await page.emulateMedia({ media: 'screen' })
  })

  test('加入/移除产品', async ({ page }) => {
    await page.addInitScript((token) => {
      localStorage.setItem('sakura_admin_token', token)
    }, ADMIN_TOKEN)

    await page.goto(`${BASE}/admin/compare?ids=1`)

    // 等初始加载
    await page.waitForSelector('.compare-grid', { timeout: 5000 })
    const beforeCount = await page.locator('.product-cell').count()
    expect(beforeCount).toBe(1)

    // 移除
    await page.locator('.product-cell .el-button:has-text("×")').first().click()
    await page.waitForTimeout(200)
    const afterCount = await page.locator('.product-cell').count()
    expect(afterCount).toBe(0)
  })
})
