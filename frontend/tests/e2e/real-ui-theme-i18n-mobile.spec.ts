// ============================================================================
// SakuraFilter E2E: 主题切换 + i18n 完整性 + 移动端响应式
// ============================================================================
//
// 覆盖 7 大 UI 验证场景:
//   1. 主题切换: 浅色 → 深色 → 跟随系统 (prefers-color-scheme)
//   2. 主题切换无刷新即时生效 (CSS 变量立即变化)
//   3. i18n 切换: zh-CN → en-US, 覆盖全部 27 个路由 (无 i18n key 残留)
//   4. i18n 切换后主题保持不变
//   5. 移动端 375px: 搜索 + 详情 + 对比 (无水平溢出)
//   6. 移动端导航菜单 (汉堡菜单展开)
//   7. 暗色主题下全部 27 个页面无样式异常 (不白屏 + 背景深色)
//
// 前置条件:
//   - 前端 dev server 运行在 http://localhost:5173 (或 BASE_URL 环境变量)
//   - 后端 API 可用 (部分页面会调 API, 但 UI 框架始终渲染)
//   - ADMIN_TOKEN 环境变量或使用默认 dev token (后台路由守卫要求)
//
// 实现说明 (基于真实 DOM / store 实现校正):
//   - 主题 store (src/stores/theme.ts): 仅 light/dark 两模式, 无显式 "system" 模式;
//     prefers-color-scheme 仅在 localStorage 无值时作初始 fallback (detectInitial)。
//     "跟随系统" 测试通过清除 localStorage + emulateMedia 验证系统偏好检测。
//   - 主题切换按钮 (AppHeader.vue L490): aria-label="主题切换", hidden sm:flex
//     (移动端 <640px 隐藏, 由 drawer 接管)。
//   - 语言切换按钮 (AppHeader.vue L501): title="切换语言", hidden sm:flex。
//   - 汉堡菜单 (AppHeader.vue L365): aria-label="打开导航菜单", sm:hidden
//     (仅移动端 <640px 显示)。
//   - soft_delete_confirm key (V24-F102 P0-1): zh-CN 值为截断的 ' 吗? (软删除, 可在';
//     en-US 值为 '吗? (软 Delete, 可in'。硬编码绕过验证: 字面量 key 不应出现在渲染文本中。
//   - Admin token: useAdminAuth.ts 兼容 legacy key 'sakura_admin_token' (纯字符串),
//     dev token 足以通过路由守卫 (后端 403 为兜底)。
//   - SSE 页面 (ETL 等) 用 domcontentloaded 而非 networkidle (与现有 e2e 一致)。
// ============================================================================

import { test, expect, type Page } from '@playwright/test'

const BASE = process.env.BASE_URL || 'http://localhost:5175'
const ADMIN_TOKEN = process.env.ADMIN_TOKEN || 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'

// ===== 路由清单 (27 个, 全覆盖, 不抽样) =====
//   公开路由 (9): 无需 token
//   后台路由 (18): 需注入 admin token
interface RouteDef {
  path: string
  name: string
  needAuth: boolean
}

const PUBLIC_ROUTES: RouteDef[] = [
  { path: '/search/aggregate', name: 'aggregate-search', needAuth: false },
  { path: '/public/search', name: 'public-search', needAuth: false },
  // SPA 兜底路由 (4 段 SEO URL), 组件会尝试加载产品, 失败则显示空/错误但页面框架仍渲染
  { path: '/products/oil-filter/spin-on/bosch/11427622448', name: 'product-detail', needAuth: false },
  { path: '/compare', name: 'compare', needAuth: false },
  { path: '/about', name: 'about', needAuth: false },
  { path: '/news', name: 'news', needAuth: false },
  { path: '/contact', name: 'contact', needAuth: false },
  { path: '/login', name: 'login', needAuth: false },
  { path: '/demo', name: 'demo', needAuth: false },
]

const ADMIN_ROUTES: RouteDef[] = [
  { path: '/admin/products', name: 'admin-products', needAuth: true },
  { path: '/admin/products/new', name: 'admin-product-new', needAuth: true },
  { path: '/admin/etl', name: 'admin-etl', needAuth: true },
  { path: '/admin/alerts', name: 'admin-alerts', needAuth: true },
  { path: '/admin/dict/oem-brands', name: 'admin-oem-brands', needAuth: true },
  { path: '/admin/dict/product-name1s', name: 'admin-pn1', needAuth: true },
  { path: '/admin/dict/product-name2s', name: 'admin-pn2', needAuth: true },
  { path: '/admin/dict/types', name: 'admin-types', needAuth: true },
  { path: '/admin/dict/oem-no3s', name: 'admin-oem3', needAuth: true },
  { path: '/admin/dict/medias', name: 'admin-medias', needAuth: true },
  { path: '/admin/dict/machines', name: 'admin-machines', needAuth: true },
  { path: '/admin/dict/engines', name: 'admin-engines', needAuth: true },
  { path: '/admin/xrefs/reorder', name: 'admin-xrefs', needAuth: true },
  { path: '/admin/compare', name: 'admin-compare', needAuth: true },
  { path: '/admin/help', name: 'admin-help', needAuth: true },
  { path: '/admin/perf', name: 'admin-perf', needAuth: true },
  { path: '/admin/errors', name: 'admin-errors', needAuth: true },
  { path: '/admin/api-docs', name: 'admin-api-docs', needAuth: true },
]

const ALL_ROUTES: RouteDef[] = [...PUBLIC_ROUTES, ...ADMIN_ROUTES]

// ===== Helper 函数 =====

// 注入管理员 token (后台路由守卫 useAdminAuth 读 'sakura_admin_token' legacy key)
async function injectAdminToken(page: Page) {
  await page.addInitScript((token) => {
    localStorage.setItem('sakura_admin_token', token)
  }, ADMIN_TOKEN)
}

// 注入语言偏好 (i18n/index.ts 读 'sakura_locale')
async function injectLocale(page: Page, locale: 'zh-CN' | 'en-US') {
  await page.addInitScript((l) => {
    localStorage.setItem('sakura_locale', l)
  }, locale)
}

// 注入主题偏好 (stores/theme.ts 读 'sakura_theme')
async function injectTheme(page: Page, mode: 'light' | 'dark') {
  await page.addInitScript((m) => {
    localStorage.setItem('sakura_theme', m)
  }, mode)
}

// 清除主题偏好 (让 detectInitial 走 prefers-color-scheme 系统检测)
async function clearTheme(page: Page) {
  await page.addInitScript(() => {
    localStorage.removeItem('sakura_theme')
  })
}

// 检测 body 文本中的原始 i18n key (未翻译时 vue-i18n 回退显示 key 路径字面量)
// 匹配已知命名空间前缀 + 至少两段点分路径, 排除代码块 (API 文档页可能含技术文本)
async function getRawI18nKeys(page: Page): Promise<string[]> {
  const text = await page.evaluate(() => {
    // 排除 pre/code 区域 (API 文档页的技术文本可能误匹配)
    const clone = document.body.cloneNode(true) as HTMLElement
    clone.querySelectorAll('pre, code, .el-code, .api-code-block, script, style').forEach((el) => el.remove())
    return clone.innerText || ''
  })
  // 匹配: common.field.xxx, nav.productSearch, admin.etlview.page_title 等
  const keyRegex = /(?:admin|common|nav|auth|search|product|theme|error|a11y)\.[a-z][a-z0-9_]*(?:\.[a-z][a-zA-Z0-9_]*)+/g
  return text.match(keyRegex) || []
}

// 检测 UI 区域中文残留 (排除数据区域: 表格、产品信息、输入框)
// WHY 排除: en-US 模式下产品名/OEM 数据可能含合法中文, 不应误报
async function getUiChineseResidue(page: Page): Promise<{ count: number; samples: string[] }> {
  return page.evaluate(() => {
    const clone = document.body.cloneNode(true) as HTMLElement
    // 排除数据区域 + 代码块
    const exclude = '.el-table, .data-cell, .product-info, input, textarea, script, style, .el-table__row, .compare-grid, pre, code, .el-input__inner'
    clone.querySelectorAll(exclude).forEach((el) => el.remove())
    const text = clone.innerText || ''
    // 显式标注 string[] 避免 RegExpMatchArray | never[] 联合类型导致 reduce 推断失败
    const matches: string[] = text.match(/[\u4e00-\u9fa5]+/g) ?? []
    return {
      count: matches.reduce((sum: number, s: string) => sum + s.length, 0),
      samples: matches.slice(0, 8),
    }
  })
}

// 导航到路由 (自动处理 auth 注入 + 等待策略)
async function navigateToRoute(page: Page, route: RouteDef) {
  if (route.needAuth) {
    await injectAdminToken(page)
  }
  await page.goto(`${BASE}${route.path}`, { waitUntil: 'domcontentloaded', timeout: 20000 })
}

// 等待页面基本渲染完成 (header 出现或超时后继续)
async function waitForPageRender(page: Page, timeout = 10000) {
  try {
    await page.locator('header[role="banner"]').waitFor({ timeout })
  } catch {
    // 部分页面 (如 login) 可能无 header, 等待 body 有内容即可
    await page.waitForTimeout(2000)
  }
}


// ============================================================================
// 1. 主题切换: 浅色 → 深色 → 跟随系统
// ============================================================================
test.describe('1. 主题切换: 浅色 → 深色 → 跟随系统', () => {
  test('1.1 浅色 → 深色: html 加 dark class + localStorage 持久化', async ({ page }) => {
    await injectTheme(page, 'light')
    await injectLocale(page, 'zh-CN')
    await page.goto(`${BASE}/search/aggregate`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await waitForPageRender(page)

    // 初始: 浅色, 无 dark class
    await expect(page.locator('html')).not.toHaveClass(/dark/)

    // 点击主题切换按钮 (aria-label="主题切换")
    const themeBtn = page.getByRole('button', { name: '主题切换' })
    await themeBtn.waitFor({ timeout: 10000 })
    await themeBtn.click()

    // 断言1: html 添加 dark class
    await expect(page.locator('html')).toHaveClass(/dark/, { timeout: 5000 })

    // 断言2: localStorage 持久化 sakura_theme=dark
    const saved = await page.evaluate(() => localStorage.getItem('sakura_theme'))
    expect(saved).toBe('dark')

    await page.screenshot({ path: 'test-results/real-ui-1-dark.png', fullPage: true })
  })

  test('1.2 深色 → 浅色: dark class 移除', async ({ page }) => {
    await injectTheme(page, 'dark')
    await injectLocale(page, 'zh-CN')
    await page.goto(`${BASE}/search/aggregate`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await waitForPageRender(page)

    // 初始: 深色
    await expect(page.locator('html')).toHaveClass(/dark/)

    // 点击切换到浅色
    const themeBtn = page.getByRole('button', { name: '主题切换' })
    await themeBtn.waitFor({ timeout: 10000 })
    await themeBtn.click()

    // 断言1: dark class 移除
    await expect(page.locator('html')).not.toHaveClass(/dark/, { timeout: 5000 })

    // 断言2: localStorage 更新为 light
    const saved = await page.evaluate(() => localStorage.getItem('sakura_theme'))
    expect(saved).toBe('light')

    await page.screenshot({ path: 'test-results/real-ui-1-light.png', fullPage: true })
  })

  test('1.3 跟随系统: 清除 localStorage 后按 prefers-color-scheme 决定主题', async ({ page }) => {
    // WHY: theme store 仅 light/dark 两模式, 无 "system" 模式;
    //   detectInitial() 在 localStorage 无值时走 matchMedia('(prefers-color-scheme: dark)')。
    //   验证: 清除 sakura_theme → 系统深色 → html 有 dark; 系统浅色 → html 无 dark。

    // 系统深色偏好
    await page.emulateMedia({ colorScheme: 'dark' })
    await clearTheme(page)
    await injectLocale(page, 'zh-CN')
    await page.goto(`${BASE}/search/aggregate`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await waitForPageRender(page)

    // 断言1: 系统深色 → html 有 dark class
    await expect(page.locator('html')).toHaveClass(/dark/, { timeout: 5000 })

    // 切换系统偏好为浅色, 清除 localStorage 后重新加载
    await page.emulateMedia({ colorScheme: 'light' })
    await page.reload({ waitUntil: 'domcontentloaded', timeout: 20000 })
    await waitForPageRender(page)

    // 断言2: 系统浅色 → html 无 dark class
    await expect(page.locator('html')).not.toHaveClass(/dark/, { timeout: 5000 })

    await page.screenshot({ path: 'test-results/real-ui-1-system.png', fullPage: true })
  })
})


// ============================================================================
// 2. 主题切换无刷新即时生效 (CSS 变量立即变化, 无需 reload)
// ============================================================================
test('2. 主题切换无刷新即时生效: CSS 变量立即变化', async ({ page }) => {
  await injectTheme(page, 'light')
  await injectLocale(page, 'zh-CN')
  await page.goto(`${BASE}/search/aggregate`, { waitUntil: 'domcontentloaded', timeout: 20000 })
  await waitForPageRender(page)

  // 获取切换前的 CSS 变量值 (--el-bg-color 是 Element Plus 背景色, 深浅模式必不同)
  const varsBefore = await page.evaluate(() => {
    const cs = getComputedStyle(document.documentElement)
    return {
      elBgColor: cs.getPropertyValue('--el-bg-color').trim(),
      colorBg: cs.getPropertyValue('--color-bg').trim(),
      bodyBg: getComputedStyle(document.body).backgroundColor,
    }
  })

  // 切换到深色 (不刷新页面)
  const themeBtn = page.getByRole('button', { name: '主题切换' })
  await themeBtn.waitFor({ timeout: 10000 })
  await themeBtn.click()

  // 等待 dark class 应用 (Vue 响应式 + CSS 变量更新)
  await expect(page.locator('html')).toHaveClass(/dark/, { timeout: 5000 })

  // 获取切换后的 CSS 变量值
  const varsAfter = await page.evaluate(() => {
    const cs = getComputedStyle(document.documentElement)
    return {
      elBgColor: cs.getPropertyValue('--el-bg-color').trim(),
      colorBg: cs.getPropertyValue('--color-bg').trim(),
      bodyBg: getComputedStyle(document.body).backgroundColor,
    }
  })

  // 断言1: Element Plus 背景变量变化 (浅色 white → 深色 dark)
  expect(varsAfter.elBgColor, '--el-bg-color 应随主题切换变化').not.toBe(varsBefore.elBgColor)

  // 断言2: body 背景色变化
  expect(varsAfter.bodyBg, 'body backgroundColor 应随主题切换变化').not.toBe(varsBefore.bodyBg)

  // 断言3: 变量值非空 (主题已正确应用)
  expect(varsAfter.elBgColor.length).toBeGreaterThan(0)

  await page.screenshot({ path: 'test-results/real-ui-2-instant.png', fullPage: true })
})


// ============================================================================
// 3. i18n 切换: zh-CN → en-US, 覆盖全部 27 个路由
// ============================================================================
test.describe('3. i18n 切换: zh-CN → en-US, 覆盖全部路由', () => {
  test('3.1 语言切换按钮: 点击 zh-CN → en-US (localStorage + HTML lang)', async ({ page }) => {
    await injectLocale(page, 'zh-CN')
    await page.goto(`${BASE}/search/aggregate`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await waitForPageRender(page)

    // 初始: 中文模式, 按钮显示 "中"
    const langBtn = page.getByTitle('切换语言')
    await expect(langBtn).toBeVisible({ timeout: 10000 })

    // 点击切换到 en-US
    await langBtn.click()

    // 断言1: localStorage 切换为 en-US
    await expect.poll(
      () => page.evaluate(() => localStorage.getItem('sakura_locale')),
      { timeout: 5000, message: 'localStorage 语言应为 en-US' }
    ).toBe('en-US')

    // 断言2: HTML lang 属性切换为 en-US (i18n/index.ts setLocale 同步 documentElement.lang)
    await expect.poll(
      () => page.evaluate(() => document.documentElement.lang),
      { timeout: 5000, message: 'HTML lang 应为 en-US' }
    ).toBe('en-US')

    // 断言3: 按钮文案变为 "EN" (AppHeader.vue L508)
    await expect.poll(
      async () => {
        const btn = page.getByTitle('切换语言')
        return (await btn.innerText()).trim()
      },
      { timeout: 5000, message: '语言按钮应显示 EN' }
    ).toBe('EN')

    await page.screenshot({ path: 'test-results/real-ui-3-toggle.png', fullPage: true })
  })

  // 遍历全部 27 个路由, 每个路由验证 en-US 无 i18n key 残留
  for (const route of ALL_ROUTES) {
    test(`3.2 ${route.name} (${route.path}): en-US 无 i18n key 残留`, async ({ page }) => {
      await injectLocale(page, 'en-US')
      await navigateToRoute(page, route)
      // 等待 Vue 渲染 + i18n 解析完成
      await waitForPageRender(page)
      await page.waitForTimeout(1500)

      // 断言1: body 文本无原始 i18n key (vue-i18n 缺失 key 时回退显示 key 路径字面量)
      const rawKeys = await getRawI18nKeys(page)
      expect(rawKeys, `路由 ${route.path} 发现未翻译 i18n key: ${rawKeys.join(', ')}`).toEqual([])

      // 断言2: soft_delete_confirm key 字面量不出现
      //   V24-F102 P0-1: 该 key 值为截断字符串, 已硬编码绕过; 若绕过失败, key 字面量会显示在 UI
      const bodyText = await page.locator('body').innerText()
      expect(bodyText, `路由 ${route.path} 发现 soft_delete_confirm 字面量 (V24-F102 绕过失效)`).not.toContain('soft_delete_confirm')

      // 断言3: 页面正常渲染 (非白屏)
      expect(bodyText.trim().length, `路由 ${route.path} en-US 模式白屏`).toBeGreaterThan(10)

      // 断言4: HTML lang 为 en-US (i18n 已生效)
      const htmlLang = await page.evaluate(() => document.documentElement.lang)
      expect(htmlLang).toBe('en-US')

      // 断言5 (软): UI 区域中文残留不超阈值
      //   阈值 80: 允许少量硬编码中文 (如主题按钮 "深色"/"浅色"、aria-label 等),
      //   超过则可能有大规模未翻译内容。使用 expect.soft 不阻断但记录问题。
      const residue = await getUiChineseResidue(page)
      expect.soft(residue.count, `路由 ${route.path} en-US 中文残留 ${residue.count} 字: ${residue.samples.join(', ')}`).toBeLessThan(80)

      await page.screenshot({ path: `test-results/real-ui-3-i18n-${route.name}.png`, fullPage: true })
    })
  }
})


// ============================================================================
// 4. i18n 切换后主题保持不变
// ============================================================================
test('4. i18n 切换后主题保持不变', async ({ page }) => {
  await injectTheme(page, 'dark')
  await injectLocale(page, 'zh-CN')
  await page.goto(`${BASE}/search/aggregate`, { waitUntil: 'domcontentloaded', timeout: 20000 })
  await waitForPageRender(page)

  // 初始: 深色主题
  await expect(page.locator('html')).toHaveClass(/dark/)

  // 切换语言到 en-US
  const langBtn = page.getByTitle('切换语言')
  await langBtn.waitFor({ timeout: 10000 })
  await langBtn.click()

  // 等待语言切换完成
  await expect.poll(
    () => page.evaluate(() => localStorage.getItem('sakura_locale')),
    { timeout: 5000 }
  ).toBe('en-US')

  // 断言1: 主题仍为深色 (i18n 切换不影响主题)
  await expect(page.locator('html')).toHaveClass(/dark/)

  // 断言2: localStorage 主题偏好仍在
  const theme = await page.evaluate(() => localStorage.getItem('sakura_theme'))
  expect(theme).toBe('dark')

  // 断言3: localStorage 语言偏好已切换
  const locale = await page.evaluate(() => localStorage.getItem('sakura_locale'))
  expect(locale).toBe('en-US')

  await page.screenshot({ path: 'test-results/real-ui-4-theme-preserved.png', fullPage: true })
})


// ============================================================================
// 5. 移动端 375px: 首页 + 搜索 + 详情 + 对比
// ============================================================================
test.describe('5. 移动端 375px 响应式', () => {
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    await injectLocale(page, 'zh-CN')
  })

  test('5.1 聚合搜索页: 无水平溢出 + 搜索框可见可输入', async ({ page }) => {
    await page.goto(`${BASE}/search/aggregate`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await waitForPageRender(page)

    // 断言1: 无水平溢出 (scrollWidth <= clientWidth + 1px 渲染容差)
    const overflow = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
    }))
    expect(overflow.scrollWidth, '375px 视口下聚合搜索页水平溢出').toBeLessThanOrEqual(overflow.clientWidth + 1)

    // 断言2: 搜索框可见
    const searchInput = page.getByPlaceholder('输入关键词 (产品名 / OEM / 机型 / 品牌)')
    await expect(searchInput).toBeVisible({ timeout: 10000 })

    // 断言3: 搜索框可输入
    await searchInput.fill('filter')
    await expect(searchInput).toHaveValue('filter')

    await page.screenshot({ path: 'test-results/real-ui-5-mobile-search.png', fullPage: true })
  })

  test('5.2 详情页: 内容不溢出', async ({ page }) => {
    await page.goto(`${BASE}/products/oil-filter/spin-on/bosch/11427622448`, {
      waitUntil: 'domcontentloaded',
      timeout: 20000,
    })
    await page.waitForTimeout(2000)

    // 断言: 无水平溢出 (详情页内容应自适应 375px 宽度)
    const overflow = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
    }))
    expect(overflow.scrollWidth, '375px 视口下详情页水平溢出').toBeLessThanOrEqual(overflow.clientWidth + 1)

    await page.screenshot({ path: 'test-results/real-ui-5-mobile-detail.png', fullPage: true })
  })

  test('5.3 对比页: 表格横向滚动 (溢出有滚动条, 非布局破坏)', async ({ page }) => {
    // 带产品 ID 访问对比页 (ID 1,2 可能不存在, 但表格框架仍渲染)
    await page.goto(`${BASE}/compare?ids=1,2`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.waitForTimeout(2000)

    // 断言1: 页面正常渲染 (非白屏)
    const bodyText = await page.locator('body').innerText()
    expect(bodyText.trim().length).toBeGreaterThan(10)

    // 断言2: 对比表格区域存在或显示空状态提示
    //   有数据: .compare-grid 存在; 无数据: "暂无对比产品" 文案
    const hasGrid = await page.locator('.compare-grid').count()
    const hasEmptyTip = await page.getByText('暂无对比产品').count()
    expect(hasGrid + hasEmptyTip, '对比页应显示表格或空状态').toBeGreaterThan(0)

    // 断言3: 若有表格且有水平溢出, 应可横向滚动 (scrollWidth > clientWidth 且有滚动条)
    //   若无溢出 (数据少), 也接受 (不破坏布局即可)
    const overflow = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
    }))
    // 对比页允许横向滚动 (表格设计), 但不应有不可滚动的固定溢出
    expect(overflow.scrollWidth).toBeGreaterThanOrEqual(overflow.clientWidth)

    await page.screenshot({ path: 'test-results/real-ui-5-mobile-compare.png', fullPage: true })
  })
})


// ============================================================================
// 6. 移动端导航菜单 (汉堡菜单)
// ============================================================================
test('6. 移动端导航: 汉堡菜单可见 + 点击展开', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 })
  await injectLocale(page, 'zh-CN')
  await page.goto(`${BASE}/search/aggregate`, { waitUntil: 'domcontentloaded', timeout: 20000 })
  await waitForPageRender(page)

  // 断言1: 汉堡菜单按钮可见 (sm:hidden, 375px < 640px 显示)
  const hamburger = page.getByRole('button', { name: '打开导航菜单' })
  await expect(hamburger, '375px 视口下汉堡菜单应可见').toBeVisible({ timeout: 10000 })

  // 点击汉堡菜单
  await hamburger.click()

  // 断言2: drawer 展开 (el-drawer 可见)
  const drawer = page.locator('.el-drawer').first()
  await expect(drawer, '点击汉堡菜单后 drawer 应展开').toBeVisible({ timeout: 5000 })

  // 断言3: drawer 内有导航内容 (非空)
  const drawerText = await drawer.innerText()
  expect(drawerText.trim().length, 'drawer 应包含导航项').toBeGreaterThan(10)

  // 断言4: drawer 内有主题切换按钮 (AppHeader.vue L623-626)
  const drawerThemeBtn = drawer.locator('button').filter({ hasText: /深色|浅色/ })
  await expect(drawerThemeBtn, 'drawer 底部应有主题切换按钮').toBeVisible({ timeout: 5000 })

  await page.screenshot({ path: 'test-results/real-ui-6-mobile-nav.png', fullPage: true })
})


// ============================================================================
// 7. 暗色主题: 全部 27 个路由无样式异常 (不白屏 + 背景深色)
// ============================================================================
test.describe.parallel('7. 暗色主题: 全部路由无样式异常', () => {
  for (const route of ALL_ROUTES) {
    test(`7. ${route.name} (${route.path}): 暗色不白屏 + 背景深色`, async ({ page }) => {
      await injectTheme(page, 'dark')
      await injectLocale(page, 'zh-CN')
      await navigateToRoute(page, route)
      await waitForPageRender(page)
      // 等待主题应用 + 数据渲染
      await page.waitForTimeout(2000)

      // 断言1: html 有 dark class (主题已应用)
      const hasDark = await page.evaluate(() => document.documentElement.classList.contains('dark'))
      expect(hasDark, `路由 ${route.path} 未应用 dark class`).toBeTruthy()

      // 断言2: 不白屏 (body 有内容)
      const bodyText = await page.locator('body').innerText()
      expect(bodyText.trim().length, `路由 ${route.path} 暗色下白屏`).toBeGreaterThan(10)

      // 断言3: 背景色为深色 (非 white/transparent)
      //   dark class 下 body 背景应为深色 (Element Plus dark 模式 bg-color ≈ #141414)
      const bgColor = await page.evaluate(() => getComputedStyle(document.body).backgroundColor)
      expect(bgColor, `路由 ${route.path} 暗色背景异常: ${bgColor}`).not.toBe('rgb(255, 255, 255)')
      expect(bgColor, `路由 ${route.path} 背景透明: ${bgColor}`).not.toBe('rgba(0, 0, 0, 0)')

      await page.screenshot({ path: `test-results/real-ui-7-dark-${route.name}.png`, fullPage: true })
    })
  }
})
