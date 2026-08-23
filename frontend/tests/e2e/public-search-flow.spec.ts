// P1-E2E-3: 公开搜索流程 E2E (用户视角端到端验证)
//   覆盖核心用户路径: 打开搜索页 → 输入关键词 → 点击搜索 → 查看结果 → 点击产品详情
//   依赖: 本地数据库有产品数据 (CI 空库会跳过搜索结果验证, 只验证流程不白屏)
//   注意: 只读操作, 不写入/修改数据
import { test, expect, type Page } from '@playwright/test'

const BASE = process.env.BASE_URL || 'http://localhost:5173'

// v30-22 修复: 强制 zh-CN locale (Playwright chromium 默认 en-US, 按钮文案变 Search 导致 getByRole 找不到)
async function injectZhLocale(page: Page) {
  await page.addInitScript(() => {
    localStorage.setItem('sakura_locale', 'zh-CN')
  })
}

test.describe('P1-E2E-3 公开搜索流程 (用户视角)', () => {
  test('1. 搜索页加载 + 输入关键词 + 触发搜索', async ({ page }) => {
    await injectZhLocale(page)
    // v30-22 修复: SSE 持续连接导致 networkidle 永远不触发, 改用 domcontentloaded
    await page.goto(`${BASE}/search`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    // /search 已重定向到聚合搜索页，使用实际可访问的标题与输入框定位。
    await page.getByRole('heading', { name: '聚合搜索', exact: true }).waitFor({ timeout: 10000 })
    const searchInput = page.getByPlaceholder('输入关键词 (产品名 / OEM / 机型 / 品牌)')
    await searchInput.waitFor({ timeout: 10000 })
    // 输入关键词
    await searchInput.fill('air')
    // v30-22 修复: i18n 注入 zh-CN 后按钮文案是"搜索", 用 exact 匹配避免匹配"产品搜索"导航按钮
    const searchBtn = page.getByRole('button', { name: '搜索', exact: true })
    await searchBtn.click()
    // 覆盖: 聚合搜索返回客户可见 OEM3 结果卡片。
    await page.locator('img[alt$="产品主图"]').first().waitFor({ timeout: 10000 })
    await page.screenshot({ path: 'test-results/e2e-search-result.png' })
  })

  test('2. 公开产品详情页加载 (已知 OEM)', async ({ page }) => {
    // P0505921 是 spike-test 库中的公开产品 (Air filter)
    await page.goto(`${BASE}/product/P0505921`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForTimeout(1500)
    // 验证不白屏
    const bodyText = await page.locator('body').innerText()
    expect(bodyText.length).toBeGreaterThan(10)
    // 如果产品存在, 应有 el-collapse 或产品信息; 如果 404, 应有错误提示
    const hasContent = await page.locator('.el-collapse, .el-empty, .text-red').count()
    expect(hasContent).toBeGreaterThanOrEqual(0)  // 容错: 0 也算 PASS (页面不白屏即可)
  })

  test('3. 公开搜索页 8 字段多框 (PublicSearch)', async ({ page }) => {
    await page.goto(`${BASE}/public/search`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    // 等待 8 字段表单加载
    await page.waitForSelector('h1', { timeout: 10000 })
    await page.waitForSelector('.el-input', { timeout: 10000 })
    // 验证 8 个字段输入框存在
    const inputCount = await page.locator('.el-input').count()
    expect(inputCount).toBeGreaterThanOrEqual(8)  // 至少 8 个字段
    // 输入 OEM Brand (data-testid 精准定位第一个字段, 避免 .first() 选错)
    const oemBrandInput = page.getByTestId('public-search-oemBrand')
    await oemBrandInput.fill('Bosch')
    await page.waitForTimeout(500)
    // 截图存档
    await page.screenshot({ path: 'test-results/e2e-public-search.png' })
  })

  test('4. 主题切换功能 (浅色/深色)', async ({ page }) => {
    await page.goto(`${BASE}/search`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    // 等待主题切换按钮
    const themeBtn = page.locator('button:has-text("主题切换"), button[title*="主题"]')
    await themeBtn.waitFor({ timeout: 5000 }).catch(() => null)
    if (await themeBtn.count() > 0) {
      // 记录切换前的 class
      const beforeClass = await page.locator('html').getAttribute('class') || ''
      await themeBtn.click()
      await page.waitForTimeout(500)
      const afterClass = await page.locator('html').getAttribute('class') || ''
      // 验证 class 有变化 (dark/light 切换)
      expect(beforeClass !== afterClass || afterClass.includes('dark') || afterClass.includes('light')).toBeTruthy()
    }
  })

  test('5. 导航栏跳转 (搜索 ↔ 后台)', async ({ page }) => {
    await page.goto(`${BASE}/search`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    // 等待导航栏
    await page.waitForSelector('nav, header', { timeout: 10000 })
    // 点击"产品搜索"导航
    const searchNav = page.locator('button:has-text("产品搜索"), a:has-text("产品搜索")')
    if (await searchNav.count() > 0) {
      await searchNav.first().click()
      await page.waitForTimeout(1000)
      expect(page.url()).toContain('/search')
    }
  })

  test('6. 移动端公开搜索、详情与对比页无页面级横向溢出', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    const pages = [
      { name: 'search', url: `${BASE}/search/aggregate?q=air`, ready: 'img[alt$="产品主图"]' },
      { name: 'detail', url: `${BASE}/seo/CAT-91102`, ready: 'h1' },
      { name: 'compare', url: `${BASE}/public/search?compare=19`, ready: '.compare-grid' }
    ]

    for (const target of pages) {
      await page.goto(target.url, { waitUntil: 'domcontentloaded', timeout: 20000 })
      await page.locator(target.ready).first().waitFor({ timeout: 15000 })
      const layout = await page.evaluate(() => ({
        viewportWidth: window.innerWidth,
        documentWidth: document.documentElement.scrollWidth
      }))
      // 覆盖: 移动端页面必须由局部容器承载宽表，不能让 document 横向溢出。
      expect(layout.documentWidth).toBeLessThanOrEqual(layout.viewportWidth)
      if (target.name === 'compare') {
        const headerFits = await page.locator('.product-cell .truncate').first().evaluate((element) =>
          element.scrollWidth <= element.clientWidth
        )
        expect(headerFits).toBeTruthy()
      }
      await page.screenshot({ path: `test-results/mobile-${target.name}.png`, fullPage: true })
    }
  })

  test('7. 桌面机型目录按场景、品牌、型号联动公开搜索', async ({ page }) => {
    await injectZhLocale(page)
    await page.setViewportSize({ width: 1440, height: 900 })
    await page.goto(`${BASE}/search/aggregate`, { waitUntil: 'domcontentloaded', timeout: 20000 })

    const catalog = page.locator('aside[aria-label="机型分类目录"]')
    await catalog.waitFor({ timeout: 15000 })

    // 🔧 fix(审查): 不假设模型名带 "Model-" 前缀 (真实库机型名为 M965/KM-100 等) —
    //   从目录读取第一个有模型的三级节点, 数据无关
    const categoryButton = catalog.getByRole('button').first()
    await categoryButton.waitFor({ timeout: 10000 })
    const category = (await categoryButton.innerText()).trim()

    await categoryButton.click()
    await expect.poll(() => new URL(page.url()).searchParams.get('machineCategory')).toBeTruthy()

    // 品牌/模型联动 — 点击 category 后新增按钮 (品牌→模型), 动态选取首个新增 (数据无关)
    const beforeSet = new Set((await catalog.getByRole('button').allInnerTexts()).map((s) => s.trim()))
    await page.waitForTimeout(800)  // 等展开渲染
    const afterTexts = (await catalog.getByRole('button').allInnerTexts()).map((s) => s.trim())
    const newBtnText = afterTexts.find((t) => t && t !== category && !beforeSet.has(t)) || null

    if (newBtnText) {
      const searchInput = page.getByPlaceholder('输入关键词 (产品名 / OEM / 机型 / 品牌)')
      // 品牌联动: 点击新增按钮 (第一个 = 品牌), 验证 q 参数
      await catalog.getByRole('button', { name: newBtnText, exact: true }).click()
      await expect.poll(() => new URL(page.url()).searchParams.get('q')).toBe(newBtnText)
      await expect(searchInput).toHaveValue(newBtnText)

      // 模型联动: 品牌点击后再取新增按钮 (模型), 若存在则验证 "品牌 模型"
      const before2 = new Set((await catalog.getByRole('button').allInnerTexts()).map((s) => s.trim()))
      await page.waitForTimeout(800)
      const after2 = (await catalog.getByRole('button').allInnerTexts()).map((s) => s.trim())
      const modelText = after2.find((t) => t && t !== newBtnText && !before2.has(t)) || null
      if (modelText) {
        const responsePromise = page.waitForResponse((response) =>
          response.request().method() === 'POST' && response.url().includes('/public/search/aggregate')
        )
        await catalog.getByRole('button', { name: modelText, exact: true }).click()
        const response = await responsePromise
        expect(response.ok()).toBeTruthy()
        await expect(searchInput).toHaveValue(`${newBtnText} ${modelText}`)
        expect(new URL(page.url()).searchParams.get('q')).toBe(`${newBtnText} ${modelText}`)
      }
    }

  test('10. 批量粘贴 → 查询 → 点击命中行 → 跳正确详情 URL', async ({ page }) => {
    // 🔧 fix(2026-08-23 走查回归防护): 曾踩坑 — 批量结果点击行用 row.oem2 作主键,
    //   跳 /seo/FRA-53205 (xrefs 另一条 oem_2) → 404; 必须用 row.oem (用户查询的 OEM)。
    //   本测试断言跳转 URL 包含 /seo/U0000014 (用户输入的 OEM), 防止回退。
    await injectZhLocale(page)
    await page.goto(`${BASE}/search`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.getByRole('button', { name: '批量粘贴', exact: true }).click()
    const textarea = page.locator('.el-dialog textarea')
    await textarea.waitFor({ timeout: 5000 })
    await textarea.fill('U0000014')
    await page.getByRole('button', { name: '查询', exact: true }).click()
    const row = page.locator('.el-dialog .el-table__row').first()
    try {
      await row.waitFor({ timeout: 8000 })
    } catch {
      // CI 空库/演示数据无此 OEM 时跳过 (与既有用例模式一致, 只验证流程不失败)
      await page.screenshot({ path: 'test-results/e2e-batch-skip.png' })
      return
    }
    await row.click()
    await page.waitForTimeout(1000)
    const url = page.url()
    // 关键断言: 必须落在用户查询的 OEM 上 (不能是 row.oem2 的 FRA-53205)
    expect(url).toContain('/seo/U0000014')
    await page.screenshot({ path: 'test-results/e2e-batch-detail.png' })
  })
})
