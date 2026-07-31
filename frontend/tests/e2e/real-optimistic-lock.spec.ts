// 产品乐观锁并发冲突 E2E (SakuraFilter)
//   覆盖 4 个场景: 并发编辑触发 409 / 冲突后自动 reload+弹窗 / 还原数据 / 并发上传图片触发 23505
//
// 依赖:
//   - 后端 5148 运行 + 前端 5175 运行 + 数据库有 admin 用户 (admin/Admin@2026)
//   - 数据库至少有一个带 crossReference (oemNo3) 的产品 (供主图上传用例使用)
//   - 凭据来源: .github/workflows/e2e.yml (INITIAL_ADMIN_PASSWORD=Admin@2026)
//
// 409 处理逻辑 (Grep 验证 AdminProductFormView.vue L299-343 + http.ts):
//   - axios 拦截器 (http.ts): ElMessage.error 显示 "数据冲突 (可能被其他用户修改),请刷新重试" (ERR_DB_CONFLICT)
//   - AdminProductFormView.save() catch 块:
//     * 检测 title/detail 包含 "已被修改"/"by_user_modify"/"lost update"
//     * ElMessage.error 显示 "数据已被其他管理员修改, 请刷新后重试"
//     * 有草稿 (useFormDraft): ElMessageBox.confirm 弹窗 ("恢复草稿"/"放弃草稿")
//     * 无草稿: 1.5s 后 window.location.reload() (触发 GET /api/admin/products/:id)
//   - 注意: V24-F78 实际是 SSE 重连修复 (useEtlProgress.ts), 非产品乐观锁自动重试
//     spec 中"V24-F78 修复"描述与代码实际行为不符, 实际行为是"自动 reload + 草稿恢复弹窗"
//     本测试按代码实际行为断言 (reload 或弹窗)
//
// 设计:
//   - 真实走后端 API (不 mock), 验证端到端乐观锁链路
//   - 两个 browser context 模拟两个用户同时编辑 (用同一 admin token, rowVersion 按 xmin 检测与用户无关)
//   - test.describe.serial 串行执行 (共享 testProductId/originalOem2 状态)
//   - 每个用例有真实断言 (不用 waitForTimeout 代替)
//   - 还原步骤必须执行 (避免污染数据)
//   - 截图存档: test-results/real-lock-*.png
//
// 使用:
//   cd frontend
//   npx playwright test tests/e2e/real-optimistic-lock.spec.ts
//   npx playwright test tests/e2e/real-optimistic-lock.spec.ts --headed  # 调试时观察 UI
//   TEST_PRODUCT_ID=123 npx playwright test tests/e2e/real-optimistic-lock.spec.ts  # 指定产品 ID
import { test, expect, type Page, type APIRequestContext } from '@playwright/test'

const BASE = process.env.BASE_URL || 'http://localhost:5175'
const BACKEND = process.env.BACKEND_URL || 'http://localhost:5148'

// 测试凭据 (与 real-auth-security.spec.ts 一致, 已 grep 验证)
const ADMIN_USER = process.env.ADMIN_USER || 'admin'
const ADMIN_PWD = process.env.ADMIN_PWD || 'Admin@2026'

// 可指定测试产品 ID (不指定则动态查找带 oemNo3 的产品)
const TEST_PRODUCT_ID = Number(process.env.TEST_PRODUCT_ID || 0)

// 1x1 透明 PNG (复用 admin-product-image-upload.spec.ts 模式, 用于 setInputFiles 上传)
//   WHY 用 buffer 而非 product-placeholder.svg: SVG (image/svg+xml) 可能被后端拒绝,
//   PNG 是通用图片格式, 更稳定; buffer 不依赖文件路径, 跨平台兼容
//   如需用 SVG 文件, 可改为: await page.locator('input[type="file"]').setInputFiles('public/images/product-placeholder.svg')
const PIXEL_PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64'
)

// ===== Helpers (复用 real-auth-security.spec.ts 模式) =====

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
//   复用 real-auth-security.spec.ts 模式, 同时注入 sakura_admin_auth + sakura_locale
async function injectAuthState(page: Page, login: {
  accessToken: string
  refreshToken: string
  user: { username: string; role: string }
  expiresIn: number
}) {
  const json = buildAuthPersistJson(login)
  await page.addInitScript((payload: string) => {
    // 强制中文 (Playwright Chromium 默认 en-US, 会让 i18n 切到英文, 导致断言文案不匹配)
    localStorage.setItem('sakura_locale', 'zh-CN')
    localStorage.setItem('sakura_admin_auth', payload)
  }, json)
}

// 查找一个带 crossReference (oemNo3) 的产品 (主图上传需要 OEM 3)
//   如指定 TEST_PRODUCT_ID 则直接用, 否则从搜索结果中查找
async function findProductWithOem3(
  request: APIRequestContext,
  token: string,
  preferredId: number
): Promise<{ id: number; mr1: string; oem2: string }> {
  // 如指定了产品 ID, 直接获取并验证
  if (preferredId > 0) {
    const resp = await request.get(`${BACKEND}/api/admin/products/${preferredId}`, {
      headers: { Authorization: `Bearer ${token}` },
      timeout: 10000
    })
    if (resp.ok()) {
      const p = await resp.json()
      // 验证有 crossReference (oemNo3), 否则主图上传用例无法执行
      if (p.crossReferences && p.crossReferences.some((x: any) => x.oemNo3)) {
        return { id: p.id, mr1: p.mr1, oem2: p.oem2 || '' }
      }
    }
    // 指定 ID 无效或无 oemNo3, 继续动态查找
    console.warn(`[findProduct] 指定的 TEST_PRODUCT_ID=${preferredId} 无效或无 oemNo3, 改为动态查找`)
  }

  // 动态查找: GET /api/admin/products/search, 遍历找带 oemNo3 的产品
  const searchResp = await request.get(`${BACKEND}/api/admin/products/search?page=1&pageSize=50`, {
    headers: { Authorization: `Bearer ${token}` },
    timeout: 15000
  })
  if (!searchResp.ok()) {
    throw new Error(`搜索产品失败: ${searchResp.status()} ${await searchResp.text()}`)
  }
  const searchBody = await searchResp.json()
  const items = searchBody.items || []
  if (items.length === 0) {
    throw new Error('数据库无产品, 无法执行乐观锁 E2E 测试')
  }

  // 遍历产品, 找带 crossReference (oemNo3) 的
  for (const item of items) {
    const detailResp = await request.get(`${BACKEND}/api/admin/products/${item.id}`, {
      headers: { Authorization: `Bearer ${token}` },
      timeout: 10000
    })
    if (!detailResp.ok()) continue
    const p = await detailResp.json()
    if (p.crossReferences && p.crossReferences.some((x: any) => x.oemNo3)) {
      return { id: p.id, mr1: p.mr1, oem2: p.oem2 || '' }
    }
  }
  throw new Error('未找到带 crossReference (oemNo3) 的产品, 主图上传用例无法执行')
}

// 展开 el-collapse-item name="8" (图片区, 默认折叠)
//   复用 admin-product-image-upload.spec.ts 模式
async function expandImageSection(page: Page) {
  const header = page.locator('.el-collapse-item__header').filter({ hasText: '图片' }).first()
  await header.click()
  // 等待折叠区内容可见 (input[type="file"] 出现)
  await page.waitForSelector('input[type="file"]', { timeout: 5000 })
}

// 确保产品没有主图 (如有则删除), 为并发上传主图用例准备干净状态
async function ensureNoPrimaryImage(request: APIRequestContext, token: string, mr1: string) {
  // GET 产品图片列表 (imageApi.list)
  const listResp = await request.get(`${BACKEND}/api/admin/products/${encodeURIComponent(mr1)}/images`, {
    headers: { Authorization: `Bearer ${token}` },
    timeout: 10000
  })
  if (!listResp.ok()) return
  const images = await listResp.json()
  // 找主图 (slot=1 或 imageRole=primary)
  const hasPrimary = (images as any[]).some((img) => img.slot === 1 || img.imageRole === 'primary')
  if (hasPrimary) {
    // DELETE 主图 (imageApi.remove)
    await request.delete(`${BACKEND}/api/admin/products/${encodeURIComponent(mr1)}/images/primary/1`, {
      headers: { Authorization: `Bearer ${token}` },
      timeout: 10000
    })
  }
}

// 清理主图 (删除 slot=1 主图, 失败不阻塞)
async function cleanupPrimaryImage(request: APIRequestContext, token: string, mr1: string) {
  try {
    await request.delete(`${BACKEND}/api/admin/products/${encodeURIComponent(mr1)}/images/primary/1`, {
      headers: { Authorization: `Bearer ${token}` },
      timeout: 10000
    })
  } catch (err) {
    console.warn('[cleanup] 主图清理失败:', err)
  }
}

// 定位 oem2 输入框 (label 文案 "OEM 2 (必填)", zh-CN locale)
//   AdminProductFormView.vue: <el-form-item :label="t('admin.productformview.label.oem_required')"><el-input v-model="form.oem2" /></el-form-item>
function locateOem2Input(page: Page) {
  return page.locator('.el-form-item')
    .filter({ has: page.locator('.el-form-item__label', { hasText: 'OEM 2' }) })
    .locator('input')
    .first()
}

// 定位保存按钮 (el-button type="primary", 文案 "保存", zh-CN locale)
function locateSaveButton(page: Page) {
  return page.locator('.el-button--primary').filter({ hasText: '保存' }).first()
}

// 等待编辑页表单加载完成
//   🔧 fix (联调发现真实 bug): 原 toBeVisible(locateOem2Input) 在 reactive form 初次渲染时就满足
//     但此时 load() 异步 GET /api/admin/products/:id 尚未返回, mr1 字段仍是 form 初始值 ''
//     → 表单校验 (mr1Rules required) 失败 → save() 在 validate 处 return → PUT 请求不发出 → 测试超时
//   修复: 等待 GET 响应完成且 mr1 输入框有值 (load() 已将后端 mr1 赋给 form.mr1)
async function waitEditFormLoaded(page: Page, productId: number) {
  const editUrl = `${BASE}/admin/products/${productId}/edit`
  // 等待 GET /api/admin/products/:id 响应 (load() 调用 adminProductApi.get)
  const getPromise = page.waitForResponse(
    (resp) => resp.url().includes(`/api/admin/products/${productId}`) &&
                !resp.url().includes('/images') &&
                !resp.url().includes('/history') &&
                !resp.url().includes('/search') &&
                resp.request().method() === 'GET',
    { timeout: 15000 }
  )
  await page.goto(editUrl, { waitUntil: 'domcontentloaded', timeout: 20000 })
  await getPromise
  // 等 oem2 输入框可见 (form 已渲染)
  await expect(locateOem2Input(page)).toBeVisible({ timeout: 10000 })
  // 等 MR.1 输入框有值 (load() 已完成, form.mr1 已从后端赋值)
  //   WHY 必须等有值: 表单校验 mr1Rules.required, 空 mr1 会让 save() 在 validate 处 return
  await expect(page.locator('.el-form-item')
    .filter({ has: page.locator('.el-form-item__label', { hasText: 'MR.1' }) })
    .locator('input'))
    .not.toHaveValue('', { timeout: 10000 })
}

// ===== 测试套件 (serial: 共享 testProductId/originalOem2, 串行执行) =====

test.describe.serial('产品乐观锁并发冲突 E2E (SakuraFilter)', () => {
  // 模块级共享状态: beforeAll 登录 + 找产品, 各用例复用
  let adminLogin: { accessToken: string; refreshToken: string; user: { username: string; role: string }; expiresIn: number } | null = null
  let testProductId: number = 0
  let testMr1: string = ''
  let originalOem2: string = ''

  test.beforeAll(async ({ request }) => {
    // 登录获取 admin JWT token
    adminLogin = await loginViaApi(request, ADMIN_USER, ADMIN_PWD)
    // 找一个带 crossReference (oemNo3) 的产品 (主图上传用例需要)
    const product = await findProductWithOem3(request, adminLogin!.accessToken, TEST_PRODUCT_ID)
    testProductId = product.id
    testMr1 = product.mr1
    originalOem2 = product.oem2
    // 确保产品没有主图 (为用例 4 准备干净状态)
    await ensureNoPrimaryImage(request, adminLogin!.accessToken, testMr1)
  })

  test.afterAll(async ({ request }) => {
    // 最终清理: 确保主图已删除 (用例 4 可能残留)
    if (adminLogin && testMr1) {
      await cleanupPrimaryImage(request, adminLogin.accessToken, testMr1)
    }
  })

  // ===== 用例 1: 并发编辑同一产品 → 第二个保存收到 409 =====
  test('1. 并发编辑同一产品 → Context B 保存收到 409', async ({ browser }) => {
    expect(adminLogin).toBeTruthy()
    const ctxA = await browser.newContext()
    const ctxB = await browser.newContext()
    const pageA = await ctxA.newPage()
    const pageB = await ctxB.newPage()

    try {
      await injectAuthState(pageA, adminLogin!)
      await injectAuthState(pageB, adminLogin!)

      // 两个 context 都访问编辑页, 等待表单加载
      await Promise.all([
        waitEditFormLoaded(pageA, testProductId),
        waitEditFormLoaded(pageB, testProductId)
      ])

      // Context A: 修改 oem2 并保存
      //   监听 PUT 请求响应 (验证保存成功)
      await locateOem2Input(pageA).fill('TestA-EDIT-001')
      const putAPromise = pageA.waitForResponse(
        (resp) => resp.url().includes(`/api/admin/products/${testProductId}`) && resp.request().method() === 'PUT',
        { timeout: 15000 }
      )
      await locateSaveButton(pageA).click()
      const putRespA = await putAPromise

      // 断言: Context A 保存成功 (200)
      expect(putRespA.status()).toBe(200)
      // 断言: 保存成功后前端 router.push('/admin/products') 跳转到列表页
      //   🔧 fix: 原 expect ElMessage '已保存' 在 router.push 跳转后立即消失, 容易误判
      //     改为等待 URL 变为 /admin/products (save() 成功路径必然触发)
      await expect(pageA).toHaveURL(/\/admin\/products(\?|$)/, { timeout: 10000 })

      // Context B: 修改 oem2 并保存 (用旧 rowVersion, 应触发 409 乐观锁冲突)
      await locateOem2Input(pageB).fill('TestB-EDIT-002')
      const putBPromise = pageB.waitForResponse(
        (resp) => resp.url().includes(`/api/admin/products/${testProductId}`) && resp.request().method() === 'PUT',
        { timeout: 15000 }
      )
      await locateSaveButton(pageB).click()
      const putRespB = await putBPromise

      // 断言: Context B 收到 409 Conflict (乐观锁冲突)
      expect(putRespB.status()).toBe(409)

      // 断言: 出现冲突提示 (.el-message--error, 文案包含 "已被"/"修改"/"冲突"/"409")
      //   axios 拦截器 + save() catch 块都会弹 ElMessage.error (2 个消息同时弹出, 用 .first())
      //   文案: "数据已被其他管理员修改, 请刷新后重试" 或 "数据冲突 (可能被其他用户修改),请刷新重试"
      await expect(pageB.locator('.el-message--error').filter({ hasText: /已被|修改|冲突|409/ }).first())
        .toBeVisible({ timeout: 5000 })

      await pageB.screenshot({ path: 'test-results/real-lock-1-conflict.png' })
    } finally {
      await ctxA.close()
      await ctxB.close()
    }
  })

  // ===== 用例 2: 冲突后前端自动刷新 rowVersion + 重试机制 =====
  //   spec 提"V24-F78 修复", 但 Grep 验证 V24-F78 实际是 SSE 重连修复 (useEtlProgress.ts)
  //   产品乐观锁 409 处理 (AdminProductFormView.vue L299-343):
  //     - 有草稿 (useFormDraft): 弹 ElMessageBox.confirm ("恢复草稿"/"放弃草稿")
  //     - 无草稿: 1.5s 后 window.location.reload() (触发 GET /api/admin/products/:id)
  //   不会自动重试 PUT 请求, 本测试按代码实际行为断言
  test('2. 冲突后前端自动 reload + GET 最新数据 (或弹草稿恢复弹窗)', async ({ browser }) => {
    expect(adminLogin).toBeTruthy()
    const ctxA = await browser.newContext()
    const ctxB = await browser.newContext()
    const pageA = await ctxA.newPage()
    const pageB = await ctxB.newPage()

    try {
      await injectAuthState(pageA, adminLogin!)
      await injectAuthState(pageB, adminLogin!)

      // 两个 context 都访问编辑页
      await Promise.all([
        waitEditFormLoaded(pageA, testProductId),
        waitEditFormLoaded(pageB, testProductId)
      ])

      // 设置 request 监听 (监听 409 后的 reload GET 请求)
      //   reload 会触发 GET /api/admin/products/:id (adminProductApi.get)
      //   排除 /images, /history, /search 子路径
      const getAfterConflictUrls: string[] = []
      pageB.on('request', (req) => {
        const url = req.url()
        if (
          url.includes(`/api/admin/products/${testProductId}`) &&
          req.method() === 'GET' &&
          !url.includes('/images') &&
          !url.includes('/history') &&
          !url.includes('/search')
        ) {
          getAfterConflictUrls.push(url)
        }
      })

      // Context A: 修改 oem2 并保存成功 (刷新 rowVersion)
      await locateOem2Input(pageA).fill('TestA2-EDIT-003')
      const putAPromise = pageA.waitForResponse(
        (resp) => resp.url().includes(`/api/admin/products/${testProductId}`) && resp.request().method() === 'PUT',
        { timeout: 15000 }
      )
      await locateSaveButton(pageA).click()
      const putRespA = await putAPromise
      expect(putRespA.status()).toBe(200)
      //   🔧 fix: 改为等待 URL 跳转 (router.push 后 ElMessage 立即卸载)
      await expect(pageA).toHaveURL(/\/admin\/products(\?|$)/, { timeout: 10000 })

      // Context B: 修改 oem2 并保存, 收到 409
      await locateOem2Input(pageB).fill('TestB2-EDIT-004')
      const putBPromise = pageB.waitForResponse(
        (resp) => resp.url().includes(`/api/admin/products/${testProductId}`) && resp.request().method() === 'PUT',
        { timeout: 15000 }
      )
      await locateSaveButton(pageB).click()
      const putRespB = await putBPromise
      expect(putRespB.status()).toBe(409)

      // 断言: 出现冲突提示 (2 个 ElMessage 同时弹, 用 .first())
      await expect(pageB.locator('.el-message--error').filter({ hasText: /已被|修改|冲突|409/ }).first())
        .toBeVisible({ timeout: 5000 })

      // 等待 reload (1.5s timer + 网络) 或弹窗 (useFormDraft 草稿恢复)
      //   - 有草稿: ElMessageBox.confirm 出现 (文案 "检测到本地草稿")
      //   - 无草稿: 1.5s 后 window.location.reload() → GET /api/admin/products/:id
      await pageB.waitForTimeout(5000)

      // 断言: 冲突后前端自动 reload (GET 最新数据) 或弹草稿恢复弹窗
      //   两种行为互斥, 有草稿弹窗 (不 reload), 无草稿 reload (不弹窗)
      const messageBoxVisible = await pageB.locator('.el-message-box')
        .filter({ hasText: /草稿|冲突|恢复/ })
        .isVisible()
        .catch(() => false)
      const reloaded = getAfterConflictUrls.length > 0

      // 至少一种行为发生 (兼容有/无草稿两种情况)
      expect(messageBoxVisible || reloaded).toBeTruthy()

      await pageB.screenshot({ path: 'test-results/real-lock-2-retry.png' })
    } finally {
      await ctxA.close()
      await ctxB.close()
    }
  })

  // ===== 用例 3: 还原原始数据 (避免污染) =====
  test('3. 还原 oem2 为原值', async ({ browser, request }) => {
    expect(adminLogin).toBeTruthy()
    const ctx = await browser.newContext()
    const page = await ctx.newPage()

    try {
      await injectAuthState(page, adminLogin!)

      // 访问编辑页, 等待表单加载
      await waitEditFormLoaded(page, testProductId)

      // 修改 oem2 为原值 (还原)
      await locateOem2Input(page).fill(originalOem2)

      // 监听 PUT 请求响应
      const putPromise = page.waitForResponse(
        (resp) => resp.url().includes(`/api/admin/products/${testProductId}`) && resp.request().method() === 'PUT',
        { timeout: 15000 }
      )
      await locateSaveButton(page).click()
      const putResp = await putPromise

      // 断言: 还原成功 (200)
      expect(putResp.status()).toBe(200)
      //   🔧 fix: 改为等待 URL 跳转 (router.push 后 ElMessage 立即卸载)
      await expect(page).toHaveURL(/\/admin\/products(\?|$)/, { timeout: 10000 })

      await page.screenshot({ path: 'test-results/real-lock-3-restore.png' })

      // 验证: 用 API GET 产品, 确认 oem2 已还原 (避免仅靠 UI 断言)
      const verifyResp = await request.get(`${BACKEND}/api/admin/products/${testProductId}`, {
        headers: { Authorization: `Bearer ${adminLogin!.accessToken}` },
        timeout: 10000
      })
      expect(verifyResp.ok()).toBeTruthy()
      const product = await verifyResp.json()
      // 🔧 fix: 后端 oem2 可能为 null (DB NULL), originalOem2 是 '' (前端 form 初始值)
      //   统一为字符串比较, null/undefined 都视为空字符串
      expect(product.oem2 ?? '').toBe(originalOem2 || '')
    } finally {
      await ctx.close()
    }
  })

  // ===== 用例 4: 同时上传同一 slot 图片触发 23505 =====
  //   注意: 真实并发触发 23505 不稳定 (admin-product-image-upload.spec.ts 注释明确说"并发场景不稳定")
  //   策略: 用 Promise.all 触发并发上传, 断言一个 200 一个 409
  //   后端行为:
  //     - 23505 唯一约束冲突 → 409 ERR_DB_CONFLICT (并发 INSERT 撞唯一约束)
  //     - IMAGE_PRIMARY_DUPLICATE → 409 (检查发现主图已存在, 非并发场景)
  //   两种 errorCode 都接受 (取决于并发时序)
  test('4. 同时上传主图 slot 1 → 触发 23505 → 409 ERR_DB_CONFLICT', async ({ browser, request }) => {
    expect(adminLogin).toBeTruthy()
    const token = adminLogin!.accessToken

    // 确保产品没有主图 (干净状态, 避免已有的主图干扰并发上传)
    await ensureNoPrimaryImage(request, token, testMr1)

    const ctxA = await browser.newContext()
    const ctxB = await browser.newContext()
    const pageA = await ctxA.newPage()
    const pageB = await ctxB.newPage()

    try {
      await injectAuthState(pageA, adminLogin!)
      await injectAuthState(pageB, adminLogin!)

      // 两个 context 都访问编辑页
      const editUrl = `${BASE}/admin/products/${testProductId}/edit`
      await Promise.all([
        pageA.goto(editUrl, { waitUntil: 'domcontentloaded', timeout: 20000 }),
        pageB.goto(editUrl, { waitUntil: 'domcontentloaded', timeout: 20000 })
      ])
      // 等待表单加载 (折叠区 header 出现)
      await pageA.waitForSelector('.el-collapse-item__header', { timeout: 10000 })
      await pageB.waitForSelector('.el-collapse-item__header', { timeout: 10000 })

      // 展开图片折叠区 (默认折叠, activeNames = ['1', '3', '5', '6'], 不含 '8')
      await expandImageSection(pageA)
      await expandImageSection(pageB)

      // 监听两个 context 的主图上传响应 (POST /api/admin/products/{mr1}/images/primary)
      //   两个 context 的 network 独立, 分别监听各自的请求
      const respAPromise = pageA.waitForResponse(
        (resp) => resp.url().includes('/images/primary') && resp.request().method() === 'POST',
        { timeout: 20000 }
      )
      const respBPromise = pageB.waitForResponse(
        (resp) => resp.url().includes('/images/primary') && resp.request().method() === 'POST',
        { timeout: 20000 }
      )

      // 并发上传: 两个 context 同时 setInputFiles (触发 input.change → uploadPrimaryImage)
      //   主图 input 是第一个 input[type="file"] (详情图是第 2-6 个)
      //   selectedOemNo3ForPrimary 在 load 时自动选第一个有 oemNo3 的 xref, 两个 context 选相同 OEM 3
      const setInputA = pageA.locator('input[type="file"]').first().setInputFiles({
        name: 'test-primary-a.png',
        mimeType: 'image/png',
        buffer: PIXEL_PNG
      })
      const setInputB = pageB.locator('input[type="file"]').first().setInputFiles({
        name: 'test-primary-b.png',
        mimeType: 'image/png',
        buffer: PIXEL_PNG
      })
      await Promise.all([setInputA, setInputB])

      // 等待两个响应 (并发上传结果)
      const [respA, respB] = await Promise.all([respAPromise, respBPromise])

      // 断言: 一个成功 (200), 一个失败 (409)
      //   主图 slot=1 按 OEM 3 命名, 同一 OEM 3 仅 1 张, 并发上传必有一个失败
      const statuses = [respA.status(), respB.status()].sort()
      expect(statuses[0]).toBe(200)
      expect(statuses[1]).toBe(409)

      // 断言: 失败的那个响应体含 errorCode (ERR_DB_CONFLICT 或 IMAGE_PRIMARY_DUPLICATE)
      //   23505 → ERR_DB_CONFLICT (并发 INSERT 撞唯一约束)
      //   IMAGE_PRIMARY_DUPLICATE (检查发现主图已存在, 非并发场景)
      const failedResp = respA.status() === 409 ? respA : respB
      const failedBody = await failedResp.json().catch(() => ({}))
      const errorCode = (failedBody as any)?.errorCode
      expect(['ERR_DB_CONFLICT', 'IMAGE_PRIMARY_DUPLICATE']).toContain(errorCode)

      // 断言: 失败的 context 出现错误提示 (.el-message--error)
      //   文案: "数据冲突 (可能被其他用户修改),请刷新重试" (ERR_DB_CONFLICT)
      //        或 "主图已存在 (每个产品仅允许 1 张主图)" (IMAGE_PRIMARY_DUPLICATE)
      const failedPage = respA.status() === 409 ? pageA : pageB
      await expect(failedPage.locator('.el-message--error').filter({
        hasText: /冲突|已存在|重复|409/
      })).toBeVisible({ timeout: 5000 })

      await failedPage.screenshot({ path: 'test-results/real-lock-4-image-conflict.png' })

      // 清理: 删除成功上传的主图 (避免污染)
      //   afterAll 也会兜底清理, 这里显式删除确保用例间状态干净
      await cleanupPrimaryImage(request, token, testMr1)
    } finally {
      await ctxA.close()
      await ctxB.close()
    }
  })
})
