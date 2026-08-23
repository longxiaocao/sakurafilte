// 真实鉴权安全 E2E (SakuraFilter)
//   覆盖 8 个安全场景: 未登录跳转 / 登录回跳 / 角色守卫 / 越权 API /
//                       ID 遍历 / cursor 篡改 / JWT 过期 refresh / refresh 失败跳转
//   依赖: 后端 5148 运行 + 前端 5175 运行 + 数据库有 admin 用户 (admin/Admin@2026)
//   凭据来源: .github/workflows/e2e.yml (INITIAL_ADMIN_PASSWORD=Admin@2026)
//   设计原则:
//     - 真实走后端 API (不 mock), 验证端到端鉴权链路
//     - viewer 用户动态创建 (无默认 seed, 见 UserService.SeedDefaultUsersAsync 仅 admin/operator)
//         用户名加时间戳后缀避免软删除后用户名冲突
//         afterAll 软删除 (CleanupViewerUser)
//     - 截图存档供后续审查 (test-results/real-auth-*.png)
//     - test.describe.serial 串行执行 (共享 viewer 用户状态)
//     - 每个用例有真实断言 (不用 waitForTimeout 代替)
import { test, expect, type Page, type APIRequestContext } from '@playwright/test'

const BASE = process.env.BASE_URL || 'http://localhost:5175'
const BACKEND = process.env.BACKEND_URL || 'http://localhost:5148'

// 测试凭据 (与 .github/workflows/e2e.yml 一致, 已 grep 验证)
const ADMIN_USER = 'admin'
const ADMIN_PWD = 'Admin@2026'
// viewer 用户无默认 seed (UserService.SeedDefaultUsersAsync 仅创建 admin/operator),
//   动态创建, 用户名加时间戳后缀避免软删除后用户名占用冲突
const VIEWER_PWD = 'E2eViewer@2026'
const NONEXIST_PRODUCT_ID = 999999999

// localStorage keys (与 useAdminAuth.ts 一致)
const STORAGE_KEY = 'sakura_admin_auth'

// ===== Helpers =====

// 强制 zh-CN locale (Playwright chromium 默认 en-US, 会导致 i18n 走 en-US)
//   WHY: i18n detectLocale 检测顺序 localStorage > navigator.language > zh-CN
async function injectZhLocale(page: Page) {
  await page.addInitScript(() => {
    localStorage.setItem('sakura_locale', 'zh-CN')
  })
}

// 清除所有鉴权 localStorage (确保未登录状态)
async function clearAuthStorage(page: Page) {
  await page.addInitScript(() => {
    localStorage.removeItem('sakura_admin_auth')
    localStorage.removeItem('sakura_admin_token')
  })
}

// 后端 API 登录, 返回 LoginResponse (与 authApi.login 一致)
async function loginViaApi(
  request: APIRequestContext,
  username: string,
  password: string
): Promise<{ accessToken: string; refreshToken: string; user: { username: string; role: string }; expiresIn: number }> {
  const resp = await request.post(`${BACKEND}/api/auth/login`, {
    data: { username, password },
    headers: { 'Content-Type': 'application/json' },
    timeout: 15000
  })
  if (!resp.ok()) {
    throw new Error(`登录失败 ${username}: ${resp.status()} ${await resp.text()}`)
  }
  return await resp.json()
}

// 构造 sakura_admin_auth localStorage JSON (与 useAdminAuth.AuthPersistShape 一致)
function buildAuthPersistJson(login: {
  accessToken: string
  refreshToken: string
  user: { username: string; role: string }
  expiresIn: number
}): string {
  return JSON.stringify({
    token: login.accessToken,
    refreshToken: login.refreshToken,
    user: login.user,
    // expiresIn 单位秒, 转毫秒时间戳 (与 useAdminAuth.setAuth 一致)
    expiresAt: Date.now() + (login.expiresIn || 1800) * 1000
  })
}

// 注入完整鉴权状态到 localStorage (含 zh-CN locale)
async function injectAuthState(page: Page, login: {
  accessToken: string
  refreshToken: string
  user: { username: string; role: string }
  expiresIn: number
}) {
  const json = buildAuthPersistJson(login)
  await page.addInitScript((payload: string) => {
    localStorage.setItem('sakura_locale', 'zh-CN')
    localStorage.setItem('sakura_admin_auth', payload)
  }, json)
}

// 用 admin JWT 创建 viewer 用户 (已存在则忽略)
//   传入 adminToken 避免重复登录触发 RateLimit.AuthPermitsPerMinute=5
async function ensureViewerUser(request: APIRequestContext, username: string, adminToken: string): Promise<void> {
  // 尝试创建 viewer 用户
  const createResp = await request.post(`${BACKEND}/api/admin/users`, {
    data: {
      username,
      password: VIEWER_PWD,
      role: 'viewer',
      email: `${username}@test.local`,
      fullName: 'E2E Test Viewer'
    },
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${adminToken}`
    },
    timeout: 10000
  })
  // 409 = 用户已存在 (active), 忽略; 其他非 2xx 抛错
  if (!createResp.ok() && createResp.status() !== 409) {
    throw new Error(`创建 viewer 用户失败: ${createResp.status()} ${await createResp.text()}`)
  }
}

// 软删除 viewer 用户 (afterAll 清理, 失败不阻塞测试)
//   接受可选 adminToken 避免重新登录触发 RateLimit.AuthPermitsPerMinute=5
async function cleanupViewerUser(request: APIRequestContext, username: string, adminToken?: string): Promise<void> {
  try {
    // 优先用传入的 token, 没有则重新登录 (可能触发限流, 但 afterAll 失败不阻塞测试)
    const token = adminToken || (await loginViaApi(request, ADMIN_USER, ADMIN_PWD)).accessToken

    // 查询 viewer 用户 ID (GET /api/admin/users 列表, 只返回未软删除)
    const listResp = await request.get(`${BACKEND}/api/admin/users?page=1&pageSize=200`, {
      headers: { Authorization: `Bearer ${token}` },
      timeout: 10000
    })
    if (!listResp.ok()) return
    const body = await listResp.json()
    const viewer = (body.items || []).find((u: { username: string }) => u.username === username)
    if (!viewer) return

    // 软删除 (DELETE /api/admin/users/{id})
    await request.delete(`${BACKEND}/api/admin/users/${viewer.id}`, {
      headers: { Authorization: `Bearer ${token}` },
      timeout: 10000
    })
  } catch (err) {
    // 清理失败不阻塞测试结果, 仅打印
    console.warn('[cleanup] viewer 用户清理失败:', err)
  }
}

// ===== 测试套件 (serial: 共享 viewer 用户状态, 串行执行) =====

test.describe.serial('SakuraFilter 真实鉴权安全 E2E', () => {
  // 模块级共享状态: beforeAll 创建 viewer 用户, 用例 3/4 用 viewerLogin.accessToken
  let viewerLogin: { accessToken: string; refreshToken: string; user: { username: string; role: string }; expiresIn: number } | null = null
  // 模块级共享 admin token (用例 6 用, 避免 5/min 限流)
  //   WHY 缓存: RateLimit.AuthPermitsPerMinute=5, 每个用例重新登录会触发 429
  let adminLogin: { accessToken: string; refreshToken: string; user: { username: string; role: string }; expiresIn: number } | null = null
  // 用时间戳后缀避免软删除后用户名冲突 (UserService.CreateAsync 检查所有用户含软删除)
  const viewerUsername = `e2e_viewer_${Date.now()}`

  test.beforeAll(async ({ request }) => {
    // 先登录 admin (后续 createViewerUser + 用例 6 复用)
    adminLogin = await loginViaApi(request, ADMIN_USER, ADMIN_PWD)
    // 确保 viewer 用户存在 (已存在则忽略)
    await ensureViewerUser(request, viewerUsername, adminLogin.accessToken)
    // 用 viewer 凭据登录获取 JWT token
    viewerLogin = await loginViaApi(request, viewerUsername, VIEWER_PWD)
  })

  test.afterAll(async ({ request }) => {
    // 软删除 viewer 用户 (失败不阻塞, 复用 adminLogin 避免重新登录触发限流)
    //   注: 如果 8a 已 refresh, adminLogin.accessToken 仍有效 (access 不随 refresh 失效)
    await cleanupViewerUser(request, viewerUsername, adminLogin?.accessToken)
  })

  // ===== 用例 1: 未登录访问受保护页面 → 跳转登录 + redirect 参数保留 =====
  test('1. 未登录访问 /admin/products → 跳转 /login?redirect=...', async ({ page }) => {
    // 不注入任何 token, 清除鉴权 localStorage
    await clearAuthStorage(page)
    await page.goto(`${BASE}/admin/products`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    // 等待跳转到登录页 (路由守卫 next({ path: '/login', query: { redirect: to.fullPath } }))
    await page.waitForURL(/\/login/, { timeout: 10000 })
    const url = page.url()
    // 断言: URL 跳转到 /login?redirect=...
    expect(url).toMatch(/\/login\?redirect=/)
    // 断言: redirect 参数包含 /admin/products (URL encoded: %2Fadmin%2Fproducts)
    const redirectParam = new URL(url).searchParams.get('redirect')
    expect(redirectParam).toBeTruthy()
    expect(redirectParam).toContain('/admin/products')
    await page.screenshot({ path: 'test-results/real-auth-1-redirect.png' })
  })

  // ===== 用例 2: 登录页输入凭据 → 登录成功 → 回跳原页面 =====
  test('2. 登录页输入 admin/Admin@2026 → 登录成功 → 回跳 /admin/products', async ({ page }) => {
    await injectZhLocale(page)
    // 先访问带 redirect 参数的登录页 (模拟从 /admin/products 跳转过来)
    await page.goto(`${BASE}/login?redirect=${encodeURIComponent('/admin/products')}`, {
      waitUntil: 'domcontentloaded',
      timeout: 20000
    })
    // 等密码框出现 (Vue 已渲染)
    await page.waitForSelector('input[type="password"]', { timeout: 10000 })
    // 用 autocomplete 属性精准定位 (LoginView.vue 中 autocomplete="username"/"current-password")
    await page.locator('input[autocomplete="username"]').fill(ADMIN_USER)
    await page.locator('input[autocomplete="current-password"]').fill(ADMIN_PWD)
    // 点击登录按钮 (form 内 primary button, 跨语言稳定, 见 deep-flow.spec.ts)
    await page.locator('form .el-button--primary').first().click()
    // 等待跳转到 /admin/products (登录成功后回跳 redirect 参数指向的页面)
    await page.waitForURL(/\/admin\/products/, { timeout: 15000 })
    // 断言: URL 回跳到 /admin/products
    expect(page.url()).toMatch(/\/admin\/products/)
    // 断言: localStorage 有 sakura_admin_auth (JSON 格式, useAdminAuth.setAuth 写入)
    const authJson = await page.evaluate(() => localStorage.getItem('sakura_admin_auth'))
    expect(authJson).toBeTruthy()
    const auth = JSON.parse(authJson!) as { token: string; user: { username: string; role: string } }
    expect(auth.token).toBeTruthy()
    // JWT 应该是三段式长字符串 (eyJxxx.eyXXX.xxx)
    expect(auth.token.length).toBeGreaterThan(20)
    expect(auth.user?.username).toBe('admin')
    expect(auth.user?.role).toBe('admin')
    await page.screenshot({ path: 'test-results/real-auth-2-login.png' })
  })

  // ===== 用例 3: viewer 角色访问 /admin/users → 跳转 + warning =====
  test('3. viewer 角色访问 /admin/users → 跳转 /admin/products + ElMessage warning', async ({ page }) => {
    expect(viewerLogin).toBeTruthy()
    // 注入 viewer JWT token (role=viewer, 不是 admin)
    await injectAuthState(page, viewerLogin!)
    // 设置跨导航等待: ElMessage.warning 在路由守卫触发 (requireRole='admin' && !isAdmin())
    //   WHY 在 goto 之前设置: waitForSelector 跨导航持续等待, 直到 DOM 出现 .el-message--warning
    const warningPromise = page.waitForSelector('.el-message--warning', { timeout: 8000 }).catch(() => null)
    // 访问 /admin/users (requireRole='admin', viewer 应被拦截)
    await page.goto(`${BASE}/admin/users`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    // 等待跳转 (viewer 被拦截 → 跳 /admin/products)
    await page.waitForURL(/\/admin\/products/, { timeout: 10000 }).catch(() => {})
    const warning = await warningPromise
    // 断言: URL 跳转到 /admin/products (角色守卫生效)
    expect(page.url()).toMatch(/\/admin\/products/)
    // 断言: 出现 ElMessage warning (.el-message--warning)
    //   路由守卫: ElMessage.warning(i18n.global.t('common.feedback.info_005')) + next({ path: '/admin/products' })
    expect(warning).not.toBeNull()
    await page.screenshot({ path: 'test-results/real-auth-3-viewer-block.png' })
  })

  // ===== 用例 4: viewer token 调用 admin API → 后端 403 =====
  test('4. viewer token 调用 /api/admin/users → 403 Forbidden', async ({ request }) => {
    expect(viewerLogin).toBeTruthy()
    const resp = await request.get(`${BACKEND}/api/admin/users?page=1&pageSize=5`, {
      headers: { Authorization: `Bearer ${viewerLogin!.accessToken}` },
      timeout: 10000
    })
    // 断言: 响应 403 Forbidden (viewer 无 admin 权限, 后端 [Authorize(Policy="Admin")] 拦截)
    //   WHY 403 即可: ASP.NET Core [Authorize(Policy)] 失败默认走 ForbidResult 返回 403 空 body
    //   (不进 ProblemDetailsFactory, 故无 errorCode; 业务要求 errorCode 需自定义 AuthenticationMiddleware, 不在本次范围)
    expect(resp.status()).toBe(403)
    // 软断言: 如果有 body 且是 JSON, errorCode 应在预期白名单 (FORBIDDEN / ERR_FORBIDDEN / ROLE_INSUFFICIENT)
    //   无 body 也接受 (ASP.NET Core 默认 Forbid 行为)
    const text = await resp.text().catch(() => '')
    if (text) {
      try {
        const body = JSON.parse(text) as { errorCode?: string }
        if (body.errorCode) {
          expect(['FORBIDDEN', 'ERR_FORBIDDEN', 'ROLE_INSUFFICIENT', 'INSUFFICIENT_ROLE']).toContain(body.errorCode)
        }
      } catch {
        // 非 JSON body (如空字符串或纯文本), 不校验 errorCode
      }
    }
  })

  // ===== 用例 5: 无 token 调用 admin API → 后端 401 =====
  test('5. 无 token 调用 /api/admin/products → 401 Unauthorized', async ({ request }) => {
    // 不带任何 Authorization header
    const resp = await request.get(`${BACKEND}/api/admin/products/search`, {
      timeout: 10000
    })
    // 断言: 响应 401 Unauthorized (JwtBearer 中间件拦截无 token 请求)
    expect(resp.status()).toBe(401)
  })

  // ===== 用例 6: 产品 ID 遍历 → 应返回 404 非 500 =====
  test('6. 不存在的产品 ID 999999999 → 404 非 500, 不泄露堆栈', async ({ request }) => {
    // 复用 beforeAll 缓存的 adminLogin (避免重新登录触发 AuthPermitsPerMinute=5 限流)
    expect(adminLogin).toBeTruthy()
    const resp = await request.get(`${BACKEND}/api/admin/products/${NONEXIST_PRODUCT_ID}`, {
      headers: { Authorization: `Bearer ${adminLogin!.accessToken}` },
      timeout: 10000
    })
    // 断言: 响应 404 Not Found (不是 500, 后端应返回 ProblemDetails 404)
    expect(resp.status()).toBe(404)
    // 断言: 响应体不包含 ex.Message / stack trace / System.Exception (防信息泄露)
    const body = await resp.text()
    expect(body).not.toMatch(/ex\.Message|stack trace|at SakuraFilter\./i)
    expect(body).not.toContain('System.Exception')
    expect(body).not.toMatch(/at line \d+:/)
  })

  // ===== 用例 7: cursor 篡改 → HMAC 签名校验失败 =====
  test('7. cursor 篡改 → 400 + errorCode (CURSOR_INVALID)', async ({ request }) => {
    // 真实端点: GET /api/admin/products/search?cursor=... (AdminProductEndpoints.cs L63-95)
    //   内部调用 AdminProductService.SearchAsync → VerifyAndExtractV2 (L783)
    //   验签失败抛 ArgumentException → catch 返回 ProblemDetailsFactory.FromException → 400 + errorCode
    //   注: /history 端点用 DecodeCursor (旧版), 验签失败静默返回 null 降级到第一页, 不抛异常

    // 步骤 1: 构造 V2 格式假 cursor (sig 段全错, HMAC 验签必失败)
    //   格式: v2:<expUnixTs>|<tsB64>|<mr1B64>|<pageNum>|<sig16> (见 CursorHmac.SignV2 L171)
    //   expTs 设未来 24h (TTL 通过), 但 sig 段错误 → HMAC FixedTimeEquals 比较不等
    const expTs = Math.floor(Date.now() / 1000) + 86400
    // dGVzdA = "test" 的 Base64Url
    const tamperedCursor = `v2:${expTs}|dGVzdA|dGVzdA|1|aaaaaaaaaaaaaaaa`

    // 步骤 2: 用篡改的 cursor 调用 admin 产品搜索 (复用 adminLogin 避免限流)
    //   WHY 必须带 pagingMode=cursor: AdminProductService.SearchAsync L754 仅在 pagingMode=="cursor" 时
    //     才调用 VerifyAndExtractV2, 默认 pagingMode="offset" 会直接忽略 cursor 参数返回 200
    expect(adminLogin).toBeTruthy()
    const resp = await request.get(
      `${BACKEND}/api/admin/products/search?pagingMode=cursor&cursor=${encodeURIComponent(tamperedCursor)}&pageSize=5`,
      { headers: { Authorization: `Bearer ${adminLogin!.accessToken}` }, timeout: 10000 }
    )
    // 断言: 响应 400 Bad Request (VerifyAndExtractV2 抛 ArgumentException, Endpoint catch 转 400)
    expect(resp.status()).toBe(400)
    // 断言: 响应体包含 errorCode (CURSOR_INVALID / SIGNATURE_MISMATCH)
    const body = await resp.json().catch(() => ({}))
    const errorCode = (body as { errorCode?: string })?.errorCode
    expect(errorCode).toBeTruthy()
    expect(['CURSOR_INVALID', 'SIGNATURE_MISMATCH', 'ERR_INVALID_CURSOR', 'INVALID_CURSOR']).toContain(errorCode)
  })

  // ===== 用例 8a: JWT 过期 → 自动 refresh → 继续操作 =====
  test('8a. JWT 过期 (401) → axios 自动 refresh → 请求重试成功', async ({ page, request }) => {
    // 步骤 1: 复用 beforeAll 缓存的 adminLogin (避免重新登录触发 AuthPermitsPerMinute=5 限流)
    //   WHY 复用: refresh 端点用 adminLogin.refreshToken, refresh 后旧 token 失效但 access 仍可用
    expect(adminLogin).toBeTruthy()
    const login = adminLogin!
    const initialToken = login.accessToken

    // 步骤 2: 注入鉴权状态到 localStorage (sakura_admin_auth JSON)
    await injectAuthState(page, login)

    // 步骤 3: 监听 network 请求, 记录是否调用 /api/auth/refresh
    const refreshRequests: string[] = []
    page.on('request', (req) => {
      if (req.url().includes('/api/auth/refresh')) {
        refreshRequests.push(req.url())
      }
    })

    // 步骤 4: 用 page.route 拦截 /api/admin/products/search
    //   第一次返回 401 (模拟 token 过期), 后续放行 (refresh 后重试走真实后端)
    let adminProductsCallCount = 0
    await page.route('**/api/admin/products/search**', async (route) => {
      adminProductsCallCount++
      if (adminProductsCallCount === 1) {
        // 第一次: 返回 401 (触发 axios 拦截器 doRefresh)
        //   WHY 不设 errorCode=ERR_AUTH_FAILED: 那会走登录页专用文案, 不影响 refresh 逻辑
        await route.fulfill({
          status: 401,
          contentType: 'application/json',
          body: JSON.stringify({ title: 'Unauthorized', status: 401 })
        })
      } else {
        // 后续: 放行真实后端 (refresh 后 axios 用新 token 重试)
        await route.continue()
      }
    })

    // 步骤 5: 访问 /admin/products (触发列表请求 → 401 → refresh → 重试)
    await page.goto(`${BASE}/admin/products`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    // 等待 refresh + 重试完成 (axios 拦截器异步, 给足时间)
    await page.waitForTimeout(3000)

    // 断言 1: axios 拦截器自动触发 /api/auth/refresh (http.ts 401 refresh 逻辑)
    expect(refreshRequests.length).toBeGreaterThanOrEqual(1)
    // 断言 2: refresh 成功后请求重试成功 (页面未跳转登录页, 仍在 /admin/products)
    expect(page.url()).toMatch(/\/admin\/products/)
    // 断言 3: localStorage 中 token 已更新 (refresh 后 useAdminAuth.setAuth 写入新 token)
    const authJson = await page.evaluate(() => localStorage.getItem('sakura_admin_auth'))
    expect(authJson).toBeTruthy()
    const newAuth = JSON.parse(authJson!) as { token: string }
    expect(newAuth.token).toBeTruthy()
    // token 应已更新 (refresh 后新 token 与初始 token 不同)
    expect(newAuth.token).not.toBe(initialToken)

    await page.screenshot({ path: 'test-results/real-auth-8a-refresh.png' })
  })

  // ===== 用例 8b: refresh 也过期 (无效 refreshToken) → 跳转登录页 =====
  test('8b. refresh 也过期 (无效 refreshToken) → 跳转 /login', async ({ page, request }) => {
    // 步骤 1: 复用 beforeAll 缓存的 adminLogin (避免重新登录触发 AuthPermitsPerMinute=5 限流)
    //   WHY 复用: 8a 已 refresh 消费 refreshToken, 8b 用 invalid refreshToken 期望 refresh 失败
    expect(adminLogin).toBeTruthy()
    const login = adminLogin!

    // 步骤 2: 注入真实 token + 无效 refreshToken (refresh 必失败)
    //   WHY 不直接构造过期 JWT: 需要 SigningKey 手动签发, 复杂且后端验签可能拒绝
    //   用 page.route 注入 401 更稳定, 真实走 refresh 端点验证 refreshToken 有效性
    //   注意字段名: buildAuthPersistJson 读 login.accessToken (与 LoginResponse 类型一致),
    //     不能用 token 字段名 (会被 JSON.stringify 忽略 undefined → localStorage 缺 token)
    const invalidRefreshAuth = {
      accessToken: login.accessToken,
      refreshToken: 'invalid-refresh-token-xxx-not-in-db',
      user: login.user,
      expiresIn: login.expiresIn
    }
    await injectAuthState(page, invalidRefreshAuth)

    // 步骤 3: 监听 network 请求, 记录 refresh 调用
    let refreshCalled = false
    page.on('request', (req) => {
      if (req.url().includes('/api/auth/refresh')) {
        refreshCalled = true
      }
    })

    // 步骤 4: page.route 拦截 /api/admin/products/search 返回 401 (触发 refresh)
    await page.route('**/api/admin/products/search**', async (route) => {
      await route.fulfill({
        status: 401,
        contentType: 'application/json',
        body: JSON.stringify({ title: 'Unauthorized', status: 401 })
      })
    })

    // 步骤 5: 访问 /admin/products (触发 401 → refresh → refresh 失败 → redirectToLogin)
    await page.goto(`${BASE}/admin/products`, { waitUntil: 'domcontentloaded', timeout: 20000 })
    // 等待跳转到登录页 (http.ts handle401Redirect → /login?return=...)
    //   WHY 先 waitForURL 再 waitForTimeout: 8b 期望 refresh 被调用 (即使失败),
    //     需给 axios 异步流程足够时间; waitForURL 跳转后还要等 refresh 完成
    await page.waitForURL(/\/login/, { timeout: 8000 }).catch(() => {})
    await page.waitForTimeout(2000)  // 额外等待 axios refresh 完成

    // 断言 1: axios 触发了 /api/auth/refresh (但 refresh 失败, refreshToken 无效)
    expect(refreshCalled).toBe(true)
    // 断言 2: URL 跳转到 /login (refresh 失败 → redirectToLogin → handle401 → router.replace)
    expect(page.url()).toMatch(/\/login/)

    await page.screenshot({ path: 'test-results/real-auth-8b-refresh-failed.png' })
  })
})
