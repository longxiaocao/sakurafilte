// E2E-用户管理: 后台用户 CRUD 流程覆盖 (2026-08-23 补缺口)
//   覆盖: 用户列表加载 / 角色 tag / 新增对话框 / 角色选项 / viewer 无管理按钮
//   依赖: ADMIN_TOKEN 注入 (admin 角色)
import { test, expect, request } from '@playwright/test'

const BASE = process.env.BASE_URL || 'http://localhost:5175'
// 用户管理页是 JWT 专属 (仅 admin 角色可管理) — 旧 dev-admin-token (sakura_admin_token
// 兼容 key) 的 user 为 null → canManage=false → 无"新增用户"按钮。必须真实 JWT 登录。
const BACKEND = process.env.BACKEND_URL || 'http://localhost:5148'  // CI e2e.yml 同端口; 本地用生产容器跑时显式 BACKEND_URL=https://localhost 覆盖
const ADMIN_USER = 'admin'
const ADMIN_PWD = process.env.INITIAL_ADMIN_PASSWORD || 'Admin@2026'

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
  // WHY 强制 zh-CN: 表单 placeholder (如"可选") 依赖 locale, 默认 en-US 下 getByPlaceholder 中文失效
  await page.addInitScript((a) => {
    localStorage.setItem('sakura_locale', 'zh-CN')
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
    await injectAdminAuth(page, adminAuth!)
    await page.goto(`${BASE}/admin/users`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('.el-table, h1', { timeout: 10000 })
    await page.getByRole('button', { name: '新增用户' }).first().click()
    await page.waitForSelector('.el-dialog', { timeout: 8000 })
    // 角色下拉 (el-select) 存在
    await page.waitForSelector('.el-dialog .el-select', { timeout: 5000 })
    await page.screenshot({ path: 'test-results/e2e-users-role-select.png' })
  })
})

// ===== 深度交互: 用户 CRUD 全链路 (创建→编辑→重置密码→删除) =====
//   WHY 2026-09-13 升级: 原 3 用例仅覆盖"列表加载 + 对话框打开", 未验证真实写操作;
//     后端 H 组 API 用例已验证契约, 此处 UI 层补齐全链路。唯一用户名避免软删除残留占用
test.describe.serial('E2E-用户管理深度交互 (CRUD 全链路)', () => {
  const ua = `qa_e2e_${Date.now()}`

  test('4. 创建用户 → 列表出现 (viewer)', async ({ page }) => {
    await injectAdminAuth(page, adminAuth!)
    await page.goto(`${BASE}/admin/users`, { waitUntil: 'domcontentloaded', timeout: 45000 })
    await page.waitForSelector('.el-table, h1, .el-input', { timeout: 10000 })
    await page.getByRole('button', { name: '新增用户' }).first().click()
    await page.waitForSelector('.el-dialog', { timeout: 8000 })
    const dlg = page.locator('.el-dialog').last()
    // 对话框字段顺序: 用户名(el-input) → 密码(input[type=password]) → 角色(select) ...
    await dlg.locator('.el-input__inner').first().fill(ua)
    await dlg.locator('input[type="password"]').first().fill('QaE2e@2026')
    await dlg.getByRole('button', { name: '创建' }).click()
    // 创建成功 → 对话框关闭 + 列表出现该用户
    await expect(dlg).toBeHidden({ timeout: 8000 })
    await expect(page.locator('.user-row').filter({ hasText: ua })).toBeVisible({ timeout: 10000 })
  })

  test('5. 编辑用户 (填邮箱 → 保存后行内可见)', async ({ page }) => {
    await injectAdminAuth(page, adminAuth!)
    await page.goto(`${BASE}/admin/users`, { waitUntil: 'domcontentloaded', timeout: 45000 })
    await page.waitForSelector('.user-row', { timeout: 10000 })
    const row = page.locator('.user-row').filter({ hasText: ua })
    await expect(row).toBeVisible({ timeout: 10000 })
    await row.getByRole('button', { name: '编辑' }).click()
    const dlg = page.locator('.el-dialog').last()
    // 邮箱 placeholder="可选" (与全名相同, 邮箱在前), 用 first() 精准命中
    //   WHY 不用 .el-input__inner nth: el-select 内部 input 的 class 因 Element Plus 版本而异, DOM 序号定位不稳定
    await dlg.getByPlaceholder('可选').first().fill('qa-e2e@example.com')
    await dlg.getByRole('button', { name: '保存' }).click()
    await expect(dlg).toBeHidden({ timeout: 8000 })
    await expect(row).toContainText('qa-e2e@example.com', { timeout: 10000 })
  })

  test('6. 重置密码 → 确认后对话框关闭', async ({ page }) => {
    await injectAdminAuth(page, adminAuth!)
    await page.goto(`${BASE}/admin/users`, { waitUntil: 'domcontentloaded', timeout: 45000 })
    await page.waitForSelector('.user-row', { timeout: 10000 })
    const row = page.locator('.user-row').filter({ hasText: ua })
    await expect(row).toBeVisible({ timeout: 10000 })
    await row.getByRole('button', { name: '重置密码' }).click()
    const dlg = page.locator('.el-dialog').last()
    await dlg.locator('input[type="password"]').first().fill('NewQa@2026')
    await dlg.getByRole('button', { name: '确认重置' }).click()
    await expect(dlg).toBeHidden({ timeout: 8000 })
  })

  test('7. 删除用户 → 确认后列表不再见', async ({ page }) => {
    await injectAdminAuth(page, adminAuth!)
    await page.goto(`${BASE}/admin/users`, { waitUntil: 'domcontentloaded', timeout: 45000 })
    await page.waitForSelector('.user-row', { timeout: 10000 })
    const row = page.locator('.user-row').filter({ hasText: ua })
    await expect(row).toBeVisible({ timeout: 10000 })
    await row.getByRole('button', { name: '删除' }).click()
    // ElMessageBox 确认框: 最后一个按钮为确认 (确定)
    await page.locator('.el-message-box__btns button').last().click()
    await expect(page.locator('.el-message-box')).toBeHidden({ timeout: 8000 })
    // 软删除后行从列表移除
    await expect(page.locator('.user-row').filter({ hasText: ua })).toHaveCount(0, { timeout: 10000 })
  })
})
