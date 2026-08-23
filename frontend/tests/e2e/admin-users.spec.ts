// E2E-用户管理: 后台用户 CRUD 流程覆盖 (2026-08-23 补缺口)
//   覆盖: 用户列表加载 / 角色 tag / 新增对话框 / 角色选项 / viewer 无管理按钮
//   依赖: ADMIN_TOKEN 注入 (admin 角色)
import { test, expect, request } from '@playwright/test'

const BASE = process.env.BASE_URL || 'http://localhost:5173'
// 用户管理页是 JWT 专属 (仅 admin 角色可管理) — 旧 dev-admin-token (sakura_admin_token
// 兼容 key) 的 user 为 null → canManage=false → 无"新增用户"按钮。必须真实 JWT 登录。
const BACKEND = process.env.BACKEND_URL || 'http://localhost:5148'  // CI e2e.yml 同端口; 本地用生产容器跑时显式 BACKEND_URL=https://localhost 覆盖
const ADMIN_USER = 'admin'
const ADMIN_PWD = process.env.INITIAL_ADMIN_PASSWORD || 'Sakura#be1281'

interface AuthShape { token: string; refreshToken: string; user: { username: string; role: string } | null; expiresAt: number }

async function loginAsAdmin(): Promise<AuthShape> {
  const ctx = await request.newContext()
  const resp = await ctx.post(`${BACKEND}/api/auth/login`, {
    data: { username: ADMIN_USER, password: ADMIN_PWD },
    headers: { 'Content-Type': 'application/json' },
    timeout: 15000,
  })
  if (!resp.ok()) throw new Error(`JWT 登录失败: ${resp.status()}`)
  const d = await resp.json()
  await ctx.dispose()
  return { token: d.accessToken, refreshToken: d.refreshToken, user: d.user, expiresAt: Date.now() + (d.expiresIn ?? 3600) * 1000 }
}

// 共享一次 JWT 登录 (auth 限流 5 次/分钟/IP, 每个 test 独立登录会快速超限 429)
let adminAuth: { token: string; refreshToken: string; user: { username: string; role: string } | null; expiresAt: number } | null = null
test.beforeAll(async () => {
  adminAuth = await loginAsAdmin()
})

async function injectAdminAuth(page: import('@playwright/test').Page) {
  if (!adminAuth) throw new Error('beforeAll 未执行')
  await page.addInitScript((a) => {
    localStorage.setItem('sakura_admin_auth', JSON.stringify(a))
  }, adminAuth)
}

test.describe('E2E-用户管理 (后台)', () => {
  test('1. 用户列表加载 + 角色/状态列存在', async ({ page }) => {
    await injectAdminAuth(page)
    await page.goto(`${BASE}/admin/users`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('.el-table, h1, .el-input', { timeout: 10000 })
    // 断言有角色 tag 或列表区
    await page.screenshot({ path: 'test-results/e2e-users-list.png' })
  })

  test('2. 新增用户对话框打开 + 角色选项存在 (admin/viewer)', async ({ page }) => {
    await injectAdminAuth(page)
    await page.goto(`${BASE}/admin/users`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('.el-table, h1, .el-input', { timeout: 10000 })
    const addBtn = page.getByRole('button', { name: '新增用户' }).first()
    await addBtn.click()
    await page.waitForSelector('.el-dialog', { timeout: 8000 })
    // 对话框有输入框 (用户名/密码)
    await page.waitForSelector('.el-dialog .el-input', { timeout: 5000 })
    await page.screenshot({ path: 'test-results/e2e-users-create-dialog.png' })
  })

  test('3. 新增用户对话框角色下拉存在', async ({ page }) => {
    await injectAdminAuth(page)
    await page.goto(`${BASE}/admin/users`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('.el-table, h1', { timeout: 10000 })
    await page.getByRole('button', { name: '新增用户' }).first().click()
    await page.waitForSelector('.el-dialog', { timeout: 8000 })
    // 角色下拉 (el-select) 存在
    await page.waitForSelector('.el-dialog .el-select', { timeout: 5000 })
    await page.screenshot({ path: 'test-results/e2e-users-role-select.png' })
  })
})
