// v27-5: 管理员产品图片上传 E2E (V24-F83 前端路径补全)
//
// 目的:
//   后端 V24-F83 (AdminProductImageServiceIntegrationTests.cs) 已用 raw SQL 稳定复现 23505 → 409 ERR_DB_CONFLICT 映射.
//   本脚本补全前端路径: 验证 axios 拦截器 (http.ts) 收到 409 + errorCode 时, ElMessage 弹出正确友好文案.
//
// 设计:
//   - 用 page.route mock 所有后端 API, 不依赖真实后端 23505 触发 (并发场景不稳定)
//   - WHY mock: 真实 23505 触发需两个并发请求撞 unique 约束, 时序难以稳定复现;
//                后端 V24-F83 已用 raw SQL 验证 23505 → 409 映射, 前端 E2E 只需验证前端层 (axios 拦截器 + ElMessage)
//   - 复用 admin-products-flow.spec.ts 的 token 注入 + page.goto 模式
//   - 覆盖 6 个测试用例: 表单加载 / 上传成功 / 409 ERR_DB_CONFLICT / 409 IMAGE_DETAIL_SLOT_DUPLICATE / 删除 / 新建模式
//
// 依赖:
//   - 前端 dev server 跑在 http://localhost:5173 (vite dev)
//   - ADMIN_TOKEN 与后端 dev-admin-token 一致 (本地默认 dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C)
//   - 无需后端服务, 所有 API 用 page.route mock
//
// 使用:
//   cd frontend
//   npx playwright test tests/e2e/admin-product-image-upload.spec.ts
//   npx playwright test tests/e2e/admin-product-image-upload.spec.ts --headed  # 调试时观察 UI
//
// 覆盖需求: spec v27-5 (chapter 27.9)
import { test, expect, type Page } from '@playwright/test'

const BASE = process.env.BASE_URL || 'http://localhost:5175'
const ADMIN_TOKEN = process.env.ADMIN_TOKEN || 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'

// 1x1 透明 PNG (用于 setInputFiles 上传)
const PIXEL_PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64'
)

// mock ProductDetail (最小有效结构, 含 1 个 crossReference 带 oemNo3 供主图关联)
const MOCK_PRODUCT = {
  id: 123,
  oemNoDisplay: 'P-TEST-001',
  mr1: 'MR1-TEST',
  productName1: 'Test Product',
  productName2: '',
  type: 'OIL FILTER',
  oem2: '',
  isPublished: true,
  remark: '',
  rowVersion: 1,
  crossReferences: [
    { id: 1, productName1: 'Test Product', oemBrand: 'BOSCH', oemNo3: 'OEM3-001' }
  ],
  machineApplications: [],
  images: []
}

// mock 主图上传成功响应 (AdminProductImageService.UploadAsync 返回结构)
const MOCK_PRIMARY_UPLOAD_RESP = {
  slot: 1,
  imageKey: 'test/primary.jpg',
  imageUrl: `${BASE}/test-primary.jpg`,
  contentType: 'image/jpeg',
  sizeBytes: 1024,
  oemNo3: 'OEM3-001',
  imageRole: 'primary'
}

// mock 详情图上传成功响应
const MOCK_DETAIL_UPLOAD_RESP = (slot: number) => ({
  slot,
  imageKey: `test/detail-${slot}.jpg`,
  imageUrl: `${BASE}/test-detail-${slot}.jpg`,
  contentType: 'image/jpeg',
  sizeBytes: 1024,
  oemNo3: null,
  imageRole: 'detail'
})

// ProblemDetails 409 响应构造器 (与后端 ProblemDetailsFactory 输出结构一致)
function problemDetails(status: number, errorCode: string, title: string) {
  return {
    type: `https://httpstatuses.io/${status}`,
    title,
    status,
    detail: title,
    instance: '/api/admin/products',
    errorCode
  }
}

async function injectAdminToken(page: Page) {
  await page.addInitScript((token) => {
    localStorage.setItem('sakura_admin_token', token)
    // 同时写新 key (useAdminAuth.ts 优先读)
    localStorage.setItem('sakura_admin_auth', JSON.stringify({
      token: token,
      refreshToken: '',
      user: { id: 1, username: 'admin', role: 'admin' },
      expiresAt: Date.now() + 3600000
    }))
    // 强制中文 (Playwright Chromium 默认 en-US, 会让 i18n 切到英文, 折叠区标题变 "Image" 而非 "图片")
    localStorage.setItem('sakura_locale', 'zh-CN')
  }, ADMIN_TOKEN)
}

// mock GET /api/admin/products/123 → 返回产品数据 (让表单进入 isEdit 状态)
async function mockProductGet(page: Page) {
  await page.route('**/api/admin/products/123', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(MOCK_PRODUCT)
    })
  })
}

// 展开 el-collapse-item name="8" (图片区, 默认折叠)
async function expandImageSection(page: Page) {
  // el-collapse-item header 是 .el-collapse-item__header, 通过文本匹配
  // WHY 不用 activeNames: 直接点击 header 模拟用户行为, 更真实
  const header = page.locator('.el-collapse-item__header').filter({ hasText: '图片' }).first()
  await header.click()
  // 等待折叠区内容可见
  await page.waitForSelector('input[type="file"]', { timeout: 5000 })
}

test.describe('v27-5 管理员产品图片上传 E2E (V24-F83 前端路径补全)', () => {
  test('1. 编辑模式表单加载 + 图片折叠区可见 (默认折叠)', async ({ page }) => {
    await injectAdminToken(page)
    await mockProductGet(page)
    // 路由: /admin/products/:id/edit (router.ts L100), 不是 /admin/products/:id
    await page.goto(`${BASE}/admin/products/123/edit`, { waitUntil: 'networkidle', timeout: 15000 })
    // 验证表单标题 (编辑模式含 id)
    await page.waitForSelector('h1', { timeout: 10000 })
    // 验证图片折叠区 header 存在 (isEdit=true 时 v-if 显示)
    const imgHeader = page.locator('.el-collapse-item__header').filter({ hasText: '图片' })
    await expect(imgHeader).toBeVisible()
    await page.screenshot({ path: 'test-results/e2e-v27-5-product-form-edit.png' })
  })

  test('2. 上传主图成功 → ElMessage 提示 + 图片显示', async ({ page }) => {
    await injectAdminToken(page)
    await mockProductGet(page)
    // mock 主图上传接口 (POST primary)
    await page.route('**/api/admin/products/MR1-TEST/images/primary**', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(MOCK_PRIMARY_UPLOAD_RESP)
      })
    })
    await page.goto(`${BASE}/admin/products/123/edit`, { waitUntil: 'networkidle', timeout: 15000 })
    await expandImageSection(page)
    // 选 OEM 3 (默认 selectedOemNo3ForPrimary 在 load 时自动选中第一个有 oemNo3 的 xref, 这里验证)
    // 上传主图: input[type="file"] 是裸 input, 用 setInputFiles
    const fileInput = page.locator('input[type="file"]').first()
    await fileInput.setInputFiles({ name: 'test.png', mimeType: 'image/png', buffer: PIXEL_PNG })
    // 验证 ElMessage 成功提示
    const successMsg = page.locator('.el-message__content').filter({ hasText: '主图上传成功' })
    await expect(successMsg).toBeVisible({ timeout: 5000 })
    // 验证图片显示 (上传成功后 images[1] 渲染 <img>)
    const uploadedImg = page.locator('img[src*="test-primary.jpg"]')
    await expect(uploadedImg).toBeVisible({ timeout: 5000 })
    await page.screenshot({ path: 'test-results/e2e-v27-5-upload-primary-success.png' })
  })

  test('3. 上传主图 409 ERR_DB_CONFLICT → ElMessage 数据冲突提示 (V24-F83 前端路径)', async ({ page }) => {
    await injectAdminToken(page)
    await mockProductGet(page)
    // mock 主图上传返回 409 ERR_DB_CONFLICT (模拟后端 23505 → 409 映射)
    await page.route('**/api/admin/products/MR1-TEST/images/primary**', async (route) => {
      await route.fulfill({
        status: 409,
        contentType: 'application/json',
        body: JSON.stringify(problemDetails(409, 'ERR_DB_CONFLICT', 'DB Conflict'))
      })
    })
    await page.goto(`${BASE}/admin/products/123/edit`, { waitUntil: 'networkidle', timeout: 15000 })
    await expandImageSection(page)
    const fileInput = page.locator('input[type="file"]').first()
    await fileInput.setInputFiles({ name: 'test.png', mimeType: 'image/png', buffer: PIXEL_PNG })
    // 验证 ElMessage 错误提示 — 与 http.ts ERROR_CODE_I18N.ERR_DB_CONFLICT 一致
    const errorMsg = page.locator('.el-message__content').filter({
      hasText: '数据冲突 (可能被其他用户修改),请刷新重试'
    })
    await expect(errorMsg).toBeVisible({ timeout: 5000 })
    await page.screenshot({ path: 'test-results/e2e-v27-5-upload-primary-409-db-conflict.png' })
  })

  test('4. 上传详情图 409 IMAGE_DETAIL_SLOT_DUPLICATE → ElMessage 槽位重复提示', async ({ page }) => {
    await injectAdminToken(page)
    await mockProductGet(page)
    // mock 详情图上传返回 409 IMAGE_DETAIL_SLOT_DUPLICATE
    await page.route('**/api/admin/products/MR1-TEST/images/detail**', async (route) => {
      await route.fulfill({
        status: 409,
        contentType: 'application/json',
        body: JSON.stringify(problemDetails(409, 'IMAGE_DETAIL_SLOT_DUPLICATE', 'Detail slot duplicate'))
      })
    })
    await page.goto(`${BASE}/admin/products/123/edit`, { waitUntil: 'networkidle', timeout: 15000 })
    await expandImageSection(page)
    // 详情图 input 是第 2 个 input[type="file"] (主图第 1, 详情图第 2-6)
    const detailFileInput = page.locator('input[type="file"]').nth(1)
    await detailFileInput.setInputFiles({ name: 'test.png', mimeType: 'image/png', buffer: PIXEL_PNG })
    // 验证 ElMessage 错误提示 — 与 http.ts ERROR_CODE_I18N.IMAGE_DETAIL_SLOT_DUPLICATE 一致
    const errorMsg = page.locator('.el-message__content').filter({
      hasText: '图片详情槽位重复'
    })
    await expect(errorMsg).toBeVisible({ timeout: 5000 })
    await page.screenshot({ path: 'test-results/e2e-v27-5-upload-detail-409-slot-duplicate.png' })
  })

  test('5. 删除主图成功 → ElMessage 已删除提示', async ({ page }) => {
    await injectAdminToken(page)
    // mock GET 返回带主图的 product (images[0] 为 primary)
    await page.route('**/api/admin/products/123', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          ...MOCK_PRODUCT,
          images: [
            { slot: 1, imageKey: 'test/primary.jpg', imageUrl: `${BASE}/test-primary.jpg`, contentType: 'image/jpeg', sizeBytes: 1024, oemNo3: 'OEM3-001', imageRole: 'primary' }
          ]
        })
      })
    })
    // mock DELETE 主图接口 (用通配符 * 匹配 mr1, 避免 encodeURIComponent 编码差异导致路由不匹配)
    await page.route('**/api/admin/products/*/images/primary/1', async (route) => {
      if (route.request().method() === 'DELETE') {
        await route.fulfill({ status: 200, contentType: 'application/json', body: '{}' })
      } else {
        await route.continue()
      }
    })
    await page.goto(`${BASE}/admin/products/123/edit`, { waitUntil: 'networkidle', timeout: 15000 })
    await expandImageSection(page)
    // 验证主图已渲染 (有删除按钮)
    const deleteBtn = page.locator('button').filter({ hasText: '删除主图' })
    await expect(deleteBtn).toBeVisible({ timeout: 5000 })
    await deleteBtn.click()
    // 验证 ElMessage 成功提示
    const successMsg = page.locator('.el-message__content').filter({ hasText: '主图已删除' })
    await expect(successMsg).toBeVisible({ timeout: 5000 })
    await page.screenshot({ path: 'test-results/e2e-v27-5-delete-primary-success.png' })
  })

  test('6. 新建模式 (/admin/products/new) 图片折叠区不显示 (isEdit=false)', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/products/new`, { waitUntil: 'networkidle', timeout: 15000 })
    await page.waitForSelector('h1', { timeout: 10000 })
    // 验证图片折叠区 header 不存在 (v-if="isEdit" 为 false)
    const imgHeader = page.locator('.el-collapse-item__header').filter({ hasText: '图片' })
    await expect(imgHeader).toHaveCount(0)
    await page.screenshot({ path: 'test-results/e2e-v27-5-product-form-new.png' })
  })
})
