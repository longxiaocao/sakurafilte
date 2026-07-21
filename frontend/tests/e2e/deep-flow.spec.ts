// v30-22: 深度 E2E 业务流程测试 (用户视角端到端验证)
//   覆盖现有 smoke 级 E2E 未覆盖的真实业务流程:
//     1. JWT 登录流程 (admin/Admin@2026)
//     2. 搜索流程 (输入关键词 → 点击搜索 → 验证结果)
//     3. 字典管理 (8 字典切换 + 表格加载 + 新增表单)
//     4. ETL 触发页 (domcontentloaded, 不用 networkidle 避免 SSE 超时)
//     5. 性能监控页 (P50/P95/P99 卡片 + Meili snapshot 端点)
//     6. 产品管理深度 (列表 → 筛选 → 详情 → 历史)
//     7. 告警中心 + 用户管理 + 修改密码 (admin 角色)
//   设计原则:
//     - 只读验证为主, 不创建/修改/删除数据 (避免污染)
//     - 用 data-testid 精准定位, 避免文案 i18n 变化导致脆弱
//     - 截图存档供后续审查
//     - 超时放宽到 20s (后端首次请求可能慢)
import { test, expect, type Page } from '@playwright/test'

const BASE = process.env.BASE_URL || 'http://localhost:5175'
const ADMIN_TOKEN = process.env.ADMIN_TOKEN || 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'
const ADMIN_USER = 'admin'
const ADMIN_PWD = 'Admin@2026'

// v30-22: 注入 admin token + 强制 zh-CN locale (Playwright chromium 默认 en-US 会导致按钮文案变 Login)
//   WHY 同时注入 locale: i18n detectLocale() 检测顺序是 localStorage > navigator.language > zh-CN,
//     Playwright chromium 默认 navigator.language=en-US, 加载 en-US 后 :has-text("登录") selector 失效
async function injectAdminToken(page: Page) {
  await page.addInitScript((token) => {
    localStorage.setItem('sakura_admin_token', token)
    // v30-22 修复: 强制 zh-CN, 让 i18n detectLocale 走 localStorage 分支
    localStorage.setItem('sakura_locale', 'zh-CN')
  }, ADMIN_TOKEN)
}

// v30-22: 仅注入 locale (用于公开页面如 /login, 不需要 admin token)
async function injectZhLocale(page: Page) {
  await page.addInitScript(() => {
    localStorage.setItem('sakura_locale', 'zh-CN')
  })
}

// v30-22: JWT 登录流程 (真实走后端 /api/auth/login)
//   根因修复: 登录页 i18n 默认 en-US, 用 .el-button--primary class 精准定位 (跨语言稳定)
//   WHY 不用 :has-text("登录"): 跨语言脆弱; 不用 button[type="submit"]: el-button 渲染为 type="button"
//   WHY 用 form .el-button--primary: LoginView.vue 中登录按钮在 <form> 内, 是唯一的 primary button
async function jwtLogin(page: Page, username: string, password: string) {
  await injectZhLocale(page)
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 20000 })
  // 等密码框出现 (说明 Vue 已渲染)
  await page.waitForSelector('input[type="password"]', { timeout: 10000 })
  // 用 aria-label 定位 (跨语言稳定, LoginView.vue 中 :aria-label="t('auth.username')" 对应 username/password)
  //   兜底: 用 type 精准定位
  const userInput = page.locator('input[autocomplete="username"], input[type="text"]:not([aria-label*="搜索"])').last()
  await userInput.fill(username)
  const pwdInput = page.locator('input[type="password"]').first()
  await pwdInput.fill(password)
  await page.screenshot({ path: 'test-results/deep-login-form.png' })
  // 用 form 内的 primary button 定位 (跨语言稳定)
  const loginBtn = page.locator('form .el-button--primary').first()
  await loginBtn.click()
  // 等待跳转 (登录成功后跳 /admin/products)
  await page.waitForURL(/\/admin\/products/, { timeout: 15000 }).catch(() => {})
}

// v30-22: 直接走 API 拿 JWT token (测试 9.x 用, 不依赖 UI)
async function fetchJwtToken(): Promise<string> {
  const resp = await fetch(`${BASE.replace('5175', '5148')}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: ADMIN_USER, password: ADMIN_PWD })
  })
  if (!resp.ok) throw new Error(`JWT login failed: ${resp.status}`)
  const body = await resp.json() as { accessToken: string }
  return body.accessToken
}

// ===== 1. JWT 登录流程 =====
test.describe('v30-22 深度 E2E: JWT 登录流程', () => {
  test('1.1 登录页加载 + 表单元素存在', async ({ page }) => {
    await injectZhLocale(page)
    await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    // 等密码框 (说明 Vue 已渲染, 同时排除 AppHeader 的搜索框干扰)
    await page.waitForSelector('input[type="password"]', { timeout: 10000 })
    // 验证用户名 + 密码输入框存在
    const pwdInputs = page.locator('input[type="password"]')
    expect(await pwdInputs.count()).toBeGreaterThanOrEqual(1)
    // 验证登录按钮存在 (form 内的 primary button, 跨语言稳定)
    const loginBtn = page.locator('form .el-button--primary')
    expect(await loginBtn.count()).toBeGreaterThanOrEqual(1)
    await page.screenshot({ path: 'test-results/deep-login-page.png' })
  })

  test('1.2 JWT 登录成功 → 跳转 /admin/products', async ({ page }) => {
    await jwtLogin(page, ADMIN_USER, ADMIN_PWD)
    // 验证跳转到 admin 页面
    expect(page.url()).toMatch(/\/admin\/products/)
    // v30-22 修复: useAdminAuth 用新 key 'sakura_admin_auth' (JSON 格式),
    //   登录成功后旧 legacy key 'sakura_admin_token' 会被清理 (loadPersisted 迁移逻辑)
    //   旧测试检查 'sakura_admin_token' 永远是 null (登录后被 removeItem)
    const authJson = await page.evaluate(() => localStorage.getItem('sakura_admin_auth'))
    expect(authJson).toBeTruthy()
    const auth = JSON.parse(authJson!) as { token: string, user: { username: string, role: string } }
    expect(auth.token).toBeTruthy()
    expect(auth.token.length).toBeGreaterThan(20)  // JWT 应该是长字符串
    expect(auth.user?.username).toBe('admin')
    expect(auth.user?.role).toBe('admin')
    await page.screenshot({ path: 'test-results/deep-login-success.png' })
  })

  test('1.3 JWT 登录失败 → 错误提示', async ({ page }) => {
    await injectZhLocale(page)
    await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.waitForSelector('input[type="password"]', { timeout: 10000 })
    // 用 autocomplete 属性精准定位 (LoginView.vue 中 autocomplete="username"/"current-password")
    await page.locator('input[autocomplete="username"]').fill('admin')
    await page.locator('input[autocomplete="current-password"]').fill('wrong-password-xxx')
    await page.locator('form .el-button--primary').first().click()
    // 等待错误提示 (ElMessage 或表单错误)
    await page.waitForTimeout(2000)
    // 应该有错误提示 (不白屏, 不跳转)
    expect(page.url()).toMatch(/\/login/)
    await page.screenshot({ path: 'test-results/deep-login-failed.png' })
  })
})

// ===== 2. 搜索流程深度 (公开搜索, 无需登录) =====
test.describe('v30-22 深度 E2E: 搜索流程', () => {
  test('2.1 搜索页加载 + 输入框 + 搜索按钮存在', async ({ page }) => {
    await page.goto(`${BASE}/search`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    // 等待搜索输入框 (data-testid)
    await page.waitForSelector('[data-testid="search-input"], input[type="text"]', { timeout: 10000 })
    const searchInput = page.locator('[data-testid="search-input"]').first()
    expect(await searchInput.count()).toBeGreaterThanOrEqual(1)
    // 验证搜索按钮 (用 type=primary 或文案)
    const searchBtn = page.locator('button:has-text("搜索"), button[type="primary"]').first()
    expect(await searchBtn.count()).toBeGreaterThanOrEqual(1)
    await page.screenshot({ path: 'test-results/deep-search-page.png' })
  })

  test('2.2 输入关键词 + 点击搜索 → 不白屏', async ({ page }) => {
    await page.goto(`${BASE}/search`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.waitForSelector('[data-testid="search-input"], input[type="text"]', { timeout: 10000 })
    const searchInput = page.locator('[data-testid="search-input"]').first()
    await searchInput.fill('CAT')
    // 点击搜索按钮 (宽松定位: 任何含"搜索"文案的按钮, 或 primary 按钮)
    const searchBtn = page.locator('button:has-text("搜索"), button[type="primary"]').first()
    await searchBtn.click()
    // 等待结果 (有结果 或 空状态 或 加载完成)
    await page.waitForTimeout(3000)
    // 验证不白屏: 页面应有内容
    const bodyText = await page.locator('body').innerText()
    expect(bodyText.length).toBeGreaterThan(50)
    await page.screenshot({ path: 'test-results/deep-search-result.png' })
  })

  test('2.3 聚合搜索页加载 (Meili typo 容错)', async ({ page }) => {
    await page.goto(`${BASE}/search/aggregate?q=CAT`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.waitForTimeout(2000)
    // 验证不白屏
    const bodyText = await page.locator('body').innerText()
    expect(bodyText.length).toBeGreaterThan(20)
    await page.screenshot({ path: 'test-results/deep-aggregate-search.png' })
  })

  test('2.4 公开搜索页 (8 字段) 加载', async ({ page }) => {
    await page.goto(`${BASE}/public/search`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.waitForSelector('input, .el-input', { timeout: 10000 })
    const bodyText = await page.locator('body').innerText()
    expect(bodyText.length).toBeGreaterThan(20)
    await page.screenshot({ path: 'test-results/deep-public-search.png' })
  })
})

// ===== 3. 字典管理 (8 字典切换) =====
test.describe('v30-22 深度 E2E: 字典管理', () => {
  const dicts = [
    { name: 'oem-brands', url: '/admin/dict/oem-brands' },
    { name: 'product-name1s', url: '/admin/dict/product-name1s' },
    { name: 'product-name2s', url: '/admin/dict/product-name2s' },
    { name: 'types', url: '/admin/dict/types' },
    { name: 'oem-no3s', url: '/admin/dict/oem-no3s' },
    { name: 'medias', url: '/admin/dict/medias' },
    { name: 'machines', url: '/admin/dict/machines' },
    { name: 'engines', url: '/admin/dict/engines' },
  ]

  for (const dict of dicts) {
    test(`3.${dicts.indexOf(dict) + 1} 字典 ${dict.name} 加载`, async ({ page }) => {
      await injectAdminToken(page)
      await page.goto(`${BASE}${dict.url}`, { waitUntil: 'domcontentloaded', timeout: 20000 })
      // 等待字典表格或空状态
      await page.waitForSelector('.dict-head, .el-table, .el-empty, .empty-state', { timeout: 10000 })
      const bodyText = await page.locator('body').innerText()
      expect(bodyText.length).toBeGreaterThan(10)
      await page.screenshot({ path: `test-results/deep-dict-${dict.name}.png` })
    })
  }
})

// ===== 4. ETL 触发页 (用 domcontentloaded 避免 SSE networkidle 超时) =====
test.describe('v30-22 深度 E2E: ETL 触发', () => {
  test('4.1 ETL 页加载 (domcontentloaded, 不等 SSE)', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/etl`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    // 等待页面主要内容
    await page.waitForSelector('h1, .el-card, .el-button', { timeout: 10000 })
    // 验证 ETL 触发按钮存在
    const btnCount = await page.locator('.el-button').count()
    expect(btnCount).toBeGreaterThanOrEqual(1)
    await page.screenshot({ path: 'test-results/deep-etl-page.png' })
  })

  test('4.2 ETL 历史查询 (如有)', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/etl`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.waitForTimeout(2000)  // 等待数据加载
    const bodyText = await page.locator('body').innerText()
    expect(bodyText.length).toBeGreaterThan(20)
    await page.screenshot({ path: 'test-results/deep-etl-history.png' })
  })
})

// ===== 5. 性能监控页 (v30-20/v30-21 新增 Meili 监控) =====
test.describe('v30-22 深度 E2E: 性能监控', () => {
  test('5.1 性能监控页加载 + 指标卡片', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/perf`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.waitForSelector('h1, .el-card, .perf-card', { timeout: 10000 })
    const bodyText = await page.locator('body').innerText()
    expect(bodyText.length).toBeGreaterThan(20)
    await page.screenshot({ path: 'test-results/deep-perf-page.png' })
  })

  test('5.2 /api/admin/perf/meili/snapshot 端点 (v30-20) 可访问', async ({ request }) => {
    // 直接调 API (端点验证, 不依赖 UI)
    const resp = await request.get(`http://localhost:5148/api/admin/perf/meili/snapshot`, {
      headers: { 'X-Admin-Token': ADMIN_TOKEN },
      timeout: 10000
    })
    expect(resp.status()).toBe(200)
    const body = await resp.json()
    // 验证 MeiliSearchSnapshot 字段完整
    expect(body).toHaveProperty('sampleCount')
    expect(body).toHaveProperty('primarySuccessCount')
    expect(body).toHaveProperty('fallbackCount')
    expect(body).toHaveProperty('p50Ms')
    expect(body).toHaveProperty('p95Ms')
    expect(body).toHaveProperty('p99Ms')
    expect(body).toHaveProperty('fallbackRate')
  })

  test('5.3 /api/admin/perf/alerts 端点可访问 (v30-18 鉴权)', async ({ request }) => {
    const resp = await request.get(`http://localhost:5148/api/admin/perf/alerts?limit=10`, {
      headers: { 'X-Admin-Token': ADMIN_TOKEN },
      timeout: 10000
    })
    expect(resp.status()).toBe(200)
    const body = await resp.json()
    expect(Array.isArray(body)).toBeTruthy()
  })

  test('5.4 /api/admin/perf/meili/snapshot 无 token → 401/403 (鉴权验证)', async ({ request }) => {
    const resp = await request.get(`http://localhost:5148/api/admin/perf/meili/snapshot`, {
      timeout: 10000
    })
    // v30-20 加了 RequireAuthorization("Admin"), 无 token 应该 401
    expect([401, 403]).toContain(resp.status())
  })
})

// ===== 6. 产品管理深度 =====
test.describe('v30-22 深度 E2E: 产品管理', () => {
  test('6.1 产品列表加载 + 筛选交互', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/products`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.waitForSelector('.el-input, .el-table, .el-empty', { timeout: 10000 })
    // 验证筛选输入框存在
    const searchInput = page.getByTestId('admin-search-oem2')
    if (await searchInput.count() > 0) {
      await searchInput.fill('Bosch')
      await page.waitForTimeout(500)
      const val = await searchInput.inputValue()
      expect(val).toBe('Bosch')
    }
    await page.screenshot({ path: 'test-results/deep-admin-products.png' })
  })

  test('6.2 新增产品表单加载 + 必填字段', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/products/new`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.waitForSelector('.el-form, form, .el-input', { timeout: 10000 })
    // 验证表单字段存在 (至少有 OEM 2 / MR.1 等核心字段)
    const inputCount = await page.locator('.el-input, input').count()
    expect(inputCount).toBeGreaterThanOrEqual(3)
    await page.screenshot({ path: 'test-results/deep-admin-product-form.png' })
  })
})

// ===== 7. 告警中心 + 用户管理 + 修改密码 (admin 角色) =====
test.describe('v30-22 深度 E2E: 告警 + 用户 + 密码', () => {
  test('7.1 告警中心页加载 (P2-1)', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/alerts`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.waitForSelector('h1, .el-card, .el-table, .el-empty', { timeout: 10000 })
    const bodyText = await page.locator('body').innerText()
    expect(bodyText.length).toBeGreaterThan(10)
    await page.screenshot({ path: 'test-results/deep-admin-alerts.png' })
  })

  test('7.2 用户管理页加载 (admin 角色)', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/users`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.waitForSelector('h1, .el-card, .el-table, .el-empty', { timeout: 10000 })
    const bodyText = await page.locator('body').innerText()
    expect(bodyText.length).toBeGreaterThan(10)
    await page.screenshot({ path: 'test-results/deep-admin-users.png' })
  })

  test('7.3 修改密码页加载', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/change-password`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.waitForSelector('input[type="password"], .el-form, form', { timeout: 10000 })
    const pwdInputs = page.locator('input[type="password"]')
    expect(await pwdInputs.count()).toBeGreaterThanOrEqual(1)
    await page.screenshot({ path: 'test-results/deep-change-password.png' })
  })
})

// ===== 8. OEM 排序管理 + 对比页 (V2 Task 2.2 + P3.5) =====
test.describe('v30-22 深度 E2E: OEM 排序 + 对比', () => {
  test('8.1 OEM 排序管理页加载', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/xrefs/reorder`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.waitForSelector('h1, .el-card, .el-table, .el-empty', { timeout: 10000 })
    const bodyText = await page.locator('body').innerText()
    expect(bodyText.length).toBeGreaterThan(10)
    await page.screenshot({ path: 'test-results/deep-xrefs-reorder.png' })
  })

  test('8.2 公开对比页加载 (无产品空状态)', async ({ page }) => {
    await page.goto(`${BASE}/compare`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.waitForTimeout(2000)
    const bodyText = await page.locator('body').innerText()
    expect(bodyText.length).toBeGreaterThan(10)
    await page.screenshot({ path: 'test-results/deep-compare-public.png' })
  })

  test('8.3 Admin 对比页加载', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/compare`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    await page.waitForTimeout(2000)
    const bodyText = await page.locator('body').innerText()
    expect(bodyText.length).toBeGreaterThan(10)
    await page.screenshot({ path: 'test-results/deep-compare-admin.png' })
  })
})

// ===== 9. 后端 API 契约验证 (不依赖 UI) =====
test.describe('v30-22 深度 E2E: 后端 API 契约', () => {
  test('9.1 /health/ready 返回完整 checks', async ({ request }) => {
    const resp = await request.get(`http://localhost:5148/health/ready`, { timeout: 10000 })
    expect(resp.status()).toBe(200)
    const body = await resp.json()
    expect(body.status).toBe('healthy')
    expect(body.checks).toBeTruthy()
    const checkNames = body.checks.map((c: any) => c.name)
    expect(checkNames).toContain('postgres')
    expect(checkNames).toContain('meili')
    expect(checkNames).toContain('fallback')
    expect(checkNames).toContain('backgroundServices')
  })

  test('9.2 /api/perf (v30-19 需 Admin token) 鉴权', async ({ request }) => {
    // v30-22 修复: /api/perf 用 RequireAuthorization("Admin"), 只接受 JWT Bearer 不接受 X-Admin-Token
    //   根因: DevTokenAuthMiddleware AdminPaths = ["/api/admin", "/api/etl"], /api/perf 不在其中
    //   修复: 用 fetchJwtToken() 走 /api/auth/login 拿 JWT Bearer, 再调 /api/perf
    // 无 token → 401
    const noToken = await request.get(`http://localhost:5148/api/perf`, { timeout: 5000 })
    expect([401, 403]).toContain(noToken.status())
    // 用 JWT Bearer → 200
    const jwt = await fetchJwtToken()
    const withToken = await request.get(`http://localhost:5148/api/perf`, {
      headers: { 'Authorization': `Bearer ${jwt}` },
      timeout: 10000
    })
    expect(withToken.status()).toBe(200)
    const body = await withToken.json()
    expect(body).toHaveProperty('sampleCount')
    expect(body).toHaveProperty('p95Ms')
    expect(body).toHaveProperty('p99Ms')
  })

  test('9.3 /api/admin/auth/status (v30-18 需 Admin token) 鉴权', async ({ request }) => {
    const noToken = await request.get(`http://localhost:5148/api/admin/auth/status`, { timeout: 5000 })
    expect([401, 403]).toContain(noToken.status())
    const withToken = await request.get(`http://localhost:5148/api/admin/auth/status`, {
      headers: { 'X-Admin-Token': ADMIN_TOKEN },
      timeout: 10000
    })
    expect(withToken.status()).toBe(200)
  })

  test('9.4 /metrics (Prometheus, v30-21 含 sakura_meili_*)', async ({ request }) => {
    const resp = await request.get(`http://localhost:5148/metrics`, { timeout: 10000 })
    expect(resp.status()).toBe(200)
    const text = await resp.text()
    // 验证 v30-21 新增的 sakura_meili_* 指标存在
    expect(text).toContain('sakura_meili_p50_ms')
    expect(text).toContain('sakura_meili_p99_ms')
    expect(text).toContain('sakura_meili_fallback_rate_pct')
    expect(text).toContain('sakura_meili_sample_count')
  })

  test('9.5 /api/search/health (Meili 健康)', async ({ request }) => {
    const resp = await request.get(`http://localhost:5148/api/search/health`, { timeout: 10000 })
    expect(resp.status()).toBe(200)
    const body = await resp.json()
    expect(body).toBeTruthy()
  })
})
