// ============================================================================
// SakuraFilter 真实搜索 → 产品详情 → 加入对比 → 对比页 → 列序持久化 E2E
// ============================================================================
//
// 覆盖核心用户路径 (6 个用例, test.describe.serial 串联, 前一个失败后续跳过):
//   1. 聚合搜索 "filter" → 真实结果卡片 + OEM 信息验证
//   2. 点击搜索结果 → SEO 详情页 URL 格式验证 (/products/:pn1/:pn2/:brand/:oem3)
//   3. 详情页"加入对比" → 跳转 /compare → 对比表格列存在
//   4. 对比页列调序 → 刷新后顺序持久化 (URL query.ids + sessionStorage)
//   5. 对比页差异高亮 (.data-cell.diff 背景色)
//   6. 对比页清空 → 空状态 + sessionStorage 清理
//
// 前置条件:
//   - 前端 dev server 运行在 http://localhost:5173 (或 BASE_URL 环境变量)
//   - 后端 API 可用: /api/public/search/aggregate, /api/public/compare, /api/public/products/:oem
//   - spike_test_v3 库有 "filter" 关键词的产品数据 (~49896 条), 保证搜索有结果
//   - test-results/ 目录可写 (Playwright 默认 outputDir)
//
// 实现说明 (与原始 spec 的差异, 均基于真实 DOM 校正, 确保脚本真实可执行):
//   - 列调序: PublicCompareView.vue 无拖拽手柄 (.el-icon-rank / drag-handle 不存在),
//     实际用 "‹"/"›" 按钮 (aria-label="左移"/"右移") 调用 moveLeft/moveRight。
//     本脚本用第 1 列的"右移"按钮实现第 1 列 → 第 2 列位置交换 (等价拖拽目标)。
//   - 持久化存储: 实际用 sessionStorage (key: sakurafilter_compare_ids, safeStorage 封装,
//     见 utils/safeStorage.ts), 非 localStorage; URL query.ids 同步更新, 作为刷新后主恢复源。
//     (用户 spec 提"localStorage", 此处按真实实现测 sessionStorage, 否则断言必失败)
//   - "加入对比"按钮: SSR 详情页走 CompareApp.vue (button.compare-btn.primary),
//     SPA 兜底走 PublicProductView.vue (el-button), 两者文案均为"加入对比",
//     用 getByRole('button', { name: '加入对比' }) 统一定位。
//   - 详情页 URL: 搜索结果 viewDetail 用 window.location.href 整页跳转 SEO URL;
//     测试环境若为纯前端 dev (5173), Vite history fallback 走 SPA 兜底路由 PublicProductView.vue。
// ============================================================================

import { test, expect, type Page, type APIRequestContext } from '@playwright/test'

const BASE = process.env.BASE_URL || 'http://localhost:5175'
const BACKEND = process.env.BACKEND_URL || 'http://localhost:5148'

// ===== 通过 API 获取与 excludeId 不同的产品 ID (修复用例4产品 ID 重复问题) =====
//   WHY 不依赖 UI 点击第 2 个搜索结果: 聚合搜索按 MR.1 聚合, 同一产品的不同 OEM
//   在详情页按 oem 查询时会命中同一 Product.Id, 导致 secondProductId === firstProductId,
//   列调序无意义 (moveRight 在单列时 disabled)。
//   方案: 直接调 /api/public/by-type 拿已上架产品列表, 取第一个 != excludeId 的 Id。
async function pickDistinctProductId(request: APIRequestContext, excludeId: string | null): Promise<string | null> {
  const resp = await request.get(`${BACKEND}/api/public/by-type`, { timeout: 15000 })
  if (!resp.ok()) return null
  const data = await resp.json()
  const groups: any[] = data.groups || []
  for (const g of groups) {
    const products: any[] = g.products || []
    for (const p of products) {
      const idStr = String(p.id)
      if (idStr !== excludeId) return idStr
    }
  }
  return null
}

// 复用现有模式 (public-search-flow.spec.ts v30-22 修复):
//   Playwright chromium 默认 en-US, 按钮文案变 "Search" 导致 getByRole 找不到;
//   注入 zh-CN locale 强制中文文案。
async function injectZhLocale(page: Page) {
  await page.addInitScript(() => {
    localStorage.setItem('sakura_locale', 'zh-CN')
  })
}

// 跨用例共享的产品 ID (serial 串联, 前置用例从 /compare?ids=X 的 URL 解析后供后续用例使用)
let firstProductId: string | null = null
let secondProductId: string | null = null

// 获取对比页当前列顺序 (表头 .product-cell 内 a 标签文本 = oemNoDisplay)
async function getColumnOrder(page: Page): Promise<string[]> {
  return page.locator('.product-cell a').allInnerTexts()
}

test.describe.serial('真实搜索→详情→对比→列序持久化 E2E (用户视角)', () => {
  test('1. 聚合搜索 filter → 真实结果卡片 + OEM 信息', async ({ page }) => {
    await injectZhLocale(page)
    // SSE 持续连接导致 networkidle 永不触发, 用 domcontentloaded (与现有 e2e 一致)
    await page.goto(`${BASE}/search`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.getByRole('heading', { name: '聚合搜索', exact: true }).waitFor({ timeout: 10000 })

    const searchInput = page.getByPlaceholder('输入关键词 (产品名 / OEM / 机型 / 品牌)')
    await searchInput.waitFor({ timeout: 10000 })
    await searchInput.fill('filter')

    // i18n 注入 zh-CN 后按钮文案是"搜索", exact 匹配避免匹配"产品搜索"导航按钮
    const searchBtn = page.getByRole('button', { name: '搜索', exact: true })
    await searchBtn.click()

    // 断言1: 结果卡片出现 (img alt 以"产品主图"结尾, AggregateSearchView 真实选择器)
    await page.locator('img[alt$="产品主图"]').first().waitFor({ timeout: 15000 })
    const cardCount = await page.locator('img[alt$="产品主图"]').count()
    // 断言2: 结果数量 > 0
    expect(cardCount).toBeGreaterThan(0)

    // 断言3: 元信息"共 N 条"出现 (total > 0, AggregateSearchView 第 378 行)
    //   WHY getByText: Playwright text=/regex/ 选择器对中文+数字混合有兼容问题;
    //     实际渲染为 <span>共 49896 条</span>, 用 getByText + regex 更稳健
    //   WHY 两个元素都包含"共 N 条": 顶部元信息 span + 底部分页 el-pagination
    //     两者都可见, 用 .first() 取顶部那个 (位置 317,317)
    await expect(page.getByText(/共\s*\d+\s*条/).first()).toBeVisible({ timeout: 10000 })

    // 断言4: 第一个卡片有 OEM 信息 (span.font-mono 渲染 getPublicOemLabel, 至少为 "OEM -")
    const firstCard = page
      .locator('div.cursor-pointer')
      .filter({ has: page.locator('img[alt$="产品主图"]') })
      .first()
    const oemLabel = firstCard.locator('span.font-mono').first()
    await expect(oemLabel).toBeVisible({ timeout: 10000 })
    const oemText = (await oemLabel.innerText()).trim()
    expect(oemText.length).toBeGreaterThan(0)

    await page.screenshot({ path: 'test-results/real-search-1-results.png', fullPage: true })
  })

  test('2. 点击产品卡片 → SEO 详情页 URL 验证', async ({ page }) => {
    await injectZhLocale(page)
    await page.goto(`${BASE}/search`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.getByPlaceholder('输入关键词 (产品名 / OEM / 机型 / 品牌)').fill('filter')
    await page.getByRole('button', { name: '搜索', exact: true }).click()
    await page.locator('img[alt$="产品主图"]').first().waitFor({ timeout: 15000 })

    // 点击第一个搜索结果卡片 (viewDetail 用 window.location.href 整页跳转 SEO URL)
    await Promise.all([
      page.waitForURL(/\/products\//, { timeout: 20000 }),
      page.locator('img[alt$="产品主图"]').first().click()
    ])

    // 断言1: URL 匹配 /products/:pn1/:pn2/:brand/:oem3 格式 (V2 Task 4.4 SEO URL)
    await expect(page).toHaveURL(/\/products\/[^/]+\/[^/]+\/[^/]+\/[^/]+/)

    // 断言2: 详情页有产品信息 (h1 产品名渲染, 说明 data 加载成功; SSR/SPA 均有 h1)
    await page.locator('h1').first().waitFor({ timeout: 15000 })
    const h1Text = (await page.locator('h1').first().innerText()).trim()
    expect(h1Text.length).toBeGreaterThan(0)

    await page.screenshot({ path: 'test-results/real-search-2-detail.png', fullPage: true })
  })

  test('3. 详情页"加入对比" → 跳转对比页 + 列存在', async ({ page }) => {
    await injectZhLocale(page)
    await page.goto(`${BASE}/search`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.getByPlaceholder('输入关键词 (产品名 / OEM / 机型 / 品牌)').fill('filter')
    await page.getByRole('button', { name: '搜索', exact: true }).click()
    await page.locator('img[alt$="产品主图"]').first().waitFor({ timeout: 15000 })

    await Promise.all([
      page.waitForURL(/\/products\//, { timeout: 20000 }),
      page.locator('img[alt$="产品主图"]').first().click()
    ])
    await page.locator('h1').first().waitFor({ timeout: 15000 })

    // 详情页"加入对比"按钮 (SSR: CompareApp.vue button.compare-btn; SPA: el-button; 文案均为"加入对比")
    //   SSR 详情页 CompareApp 为异步动态 import 挂载 (product-detail-client.ts), 需等待按钮出现
    const compareBtn = page.getByRole('button', { name: '加入对比', exact: true })
    await compareBtn.waitFor({ timeout: 15000 })

    // 点击后跳转 /compare?ids=<id> (SSR: window.location.href 整页跳转; SPA: router.push)
    await Promise.all([
      page.waitForURL(/\/compare/, { timeout: 20000 }),
      compareBtn.click()
    ])

    // 从 URL 解析产品 ID, 供后续用例使用 (CompareApp 跳转 URL 为 /compare?ids=<singleId>)
    const idsParam = new URL(page.url()).searchParams.get('ids') || ''
    firstProductId = idsParam.split(',')[0] || null
    expect(firstProductId).not.toBeNull()

    // 断言1: 对比表格容器存在 (.compare-grid, PublicCompareView 第 374 行)
    await page.locator('.compare-grid').waitFor({ timeout: 15000 })
    // 断言2: 对比表格至少 1 列产品 (.compare-header-cell.product-cell 表头)
    const colCount = await page.locator('.compare-header-cell.product-cell').count()
    expect(colCount).toBeGreaterThanOrEqual(1)

    await page.screenshot({ path: 'test-results/real-search-3-compare.png', fullPage: true })
  })

  test('4. 对比页列调序 → 刷新后顺序持久化', async ({ request, page }) => {
    expect(firstProductId).not.toBeNull()
    await injectZhLocale(page)

    // 🔧 fix: 通过 API 直接获取与 firstProductId 不同的产品 ID
    //   WHY 不再走 UI 点击第 2 个搜索结果: 聚合搜索按 MR.1 聚合, 第 1/2 个卡片的
    //   primary oem 可能关联同一 Product.Id, 导致 secondProductId === firstProductId,
    //   列调序无意义。改用 /api/public/by-type 拿已上架产品列表, 确保拿到不同 Id。
    secondProductId = await pickDistinctProductId(request, firstProductId)
    expect(secondProductId).not.toBeNull()
    // 确保 2 个不同产品 (否则列调序无意义, moveRight 在单列时 disabled)
    expect(secondProductId).not.toBe(firstProductId)

    // 访问带 2 个 id 的对比页, 验证列调序持久化
    await page.goto(`${BASE}/compare?ids=${firstProductId},${secondProductId}`, {
      waitUntil: 'domcontentloaded',
      timeout: 20000
    })
    await page.locator('.compare-grid').waitFor({ timeout: 15000 })
    // 确保 2 列加载完成
    await expect(page.locator('.compare-header-cell.product-cell')).toHaveCount(2, { timeout: 15000 })

    const beforeSwap = await getColumnOrder(page)
    expect(beforeSwap.length).toBe(2)

    // 列调序: 点击第 1 列"右移"按钮 (PublicCompareView 用 ‹/› 按钮, 无拖拽手柄)
    //   moveRight(0) 交换 products[0] 与 products[1], persistUrlOrder 同步 URL + sessionStorage
    const moveRightBtn = page.getByRole('button', { name: '右移', exact: true }).first()
    await moveRightBtn.click()

    // 等待交换完成: 第 1 列表头文本变为原第 2 列 (Vue 响应式更新)
    await expect.poll(
      async () => (await getColumnOrder(page))[0],
      { timeout: 10000, message: '列调序后第 1 列表头应变化' }
    ).toBe(beforeSwap[1])

    const afterSwap = await getColumnOrder(page)
    expect(afterSwap).not.toEqual(beforeSwap)

    // 等待 URL query.ids 顺序持久化 (router.replace 完成, 确保 reload 前已更新)
    await expect.poll(
      () => (new URL(page.url()).searchParams.get('ids') || '').split(','),
      { timeout: 10000, message: 'URL ids 顺序应已更新' }
    ).not.toEqual([firstProductId, secondProductId])

    // 刷新页面, 验证 URL query.ids + sessionStorage 持久化生效
    await page.reload({ waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.locator('.compare-grid').waitFor({ timeout: 15000 })
    await expect(page.locator('.compare-header-cell.product-cell')).toHaveCount(2, { timeout: 15000 })

    const afterReload = await getColumnOrder(page)
    // 断言1: 刷新后列顺序与调序后一致 (持久化生效)
    expect(afterReload).toEqual(afterSwap)

    // 断言2: sessionStorage 已持久化新顺序 (key: sakurafilter_compare_ids, safeStorage 封装)
    const stored = await page.evaluate(() => sessionStorage.getItem('sakurafilter_compare_ids'))
    expect(stored).not.toBeNull()
    const storedIds: number[] = JSON.parse(stored as string)
    const urlIds = (new URL(page.url()).searchParams.get('ids') || '').split(',')
    // sessionStorage 持久化的 ID 顺序应与 URL query.ids 顺序一致
    expect(storedIds.map(String)).toEqual(urlIds)

    await page.screenshot({ path: 'test-results/real-search-4-persist.png', fullPage: true })
  })

  test('5. 对比页差异高亮验证', async ({ page }) => {
    expect(firstProductId).not.toBeNull()
    expect(secondProductId).not.toBeNull()
    await injectZhLocale(page)

    await page.goto(`${BASE}/compare?ids=${firstProductId},${secondProductId}`, {
      waitUntil: 'domcontentloaded',
      timeout: 20000
    })
    await page.locator('.compare-grid').waitFor({ timeout: 15000 })
    await expect(page.locator('.compare-header-cell.product-cell')).toHaveCount(2, { timeout: 15000 })

    // 断言1: 差异单元格存在 (.data-cell.diff)
    //   2 个不同产品至少 oemNoDisplay 不同 → cellClass 返回 'diff' (PublicCompareView 第 288 行)
    const diffCells = page.locator('.data-cell.diff')
    await diffCells.first().waitFor({ timeout: 15000 })
    const diffCount = await diffCells.count()
    expect(diffCount).toBeGreaterThan(0)

    // 断言2: 差异单元格应用了高亮样式 (背景色非透明/非纯白)
    //   CSS: .data-cell.diff { background: var(--color-bg-diff); } 打印态为 #fffbe6 (黄底)
    const hasHighlight = await diffCells.first().evaluate((el) => {
      const bg = getComputedStyle(el).backgroundColor
      return bg !== 'rgba(0, 0, 0, 0)' && bg !== 'rgb(255, 255, 255)'
    })
    expect(hasHighlight).toBeTruthy()

    await page.screenshot({ path: 'test-results/real-search-5-diff.png', fullPage: true })
  })

  test('6. 对比页清空 → 空状态 + sessionStorage 清理', async ({ page }) => {
    expect(firstProductId).not.toBeNull()
    expect(secondProductId).not.toBeNull()
    await injectZhLocale(page)

    await page.goto(`${BASE}/compare?ids=${firstProductId},${secondProductId}`, {
      waitUntil: 'domcontentloaded',
      timeout: 20000
    })
    await page.locator('.compare-grid').waitFor({ timeout: 15000 })

    // 点击"清空"按钮 (clearAll → persistUrlOrder → persistCompareIds([]) → safeRemoveItem)
    const clearBtn = page.getByRole('button', { name: '清空', exact: true })
    await expect(clearBtn).toBeEnabled({ timeout: 10000 })
    await clearBtn.click()

    // 断言1: 对比表格为空, 显示空状态提示 (PublicCompareView 第 351 行 "暂无对比产品")
    await expect(page.getByText('暂无对比产品')).toBeVisible({ timeout: 10000 })
    await expect(page.locator('.compare-grid')).toHaveCount(0)

    // 断言2: sessionStorage 中对比列表已清空 (safeRemoveItem 已移除 key)
    const stored = await page.evaluate(() => sessionStorage.getItem('sakurafilter_compare_ids'))
    expect(stored).toBeNull()

    await page.screenshot({ path: 'test-results/real-search-6-clear.png', fullPage: true })
  })
})
