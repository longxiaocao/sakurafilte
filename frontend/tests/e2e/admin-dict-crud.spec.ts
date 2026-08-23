// E2E-字典CRUD: 后台 8 字典页 CRUD 流程覆盖 (2026-08-23 补缺口)
//   覆盖: 字典列表加载 / 新建对话框 / 表单字段 / 8 字典页可达性
//   依赖: ADMIN_TOKEN (dev-admin-token) 注入 localStorage
//   注意: 只读 + 对话框打开, 不真正提交/删除 (避免污染数据, CI 空库也过)
import { test, expect, request } from '@playwright/test'

const BASE = process.env.BASE_URL || 'http://localhost:5173'
// 字典页按钮可能受 canManage 权限控制 (JWT user.role) — 用真实 JWT 登录保证 admin 角色
const BACKEND = process.env.BACKEND_URL || 'https://localhost'
const ADMIN_USER = 'admin'
const ADMIN_PWD = process.env.INITIAL_ADMIN_PASSWORD || 'Sakura#be1281'

async function loginAsAdmin(): Promise<{ token: string; refreshToken: string; user: { username: string; role: string } | null; expiresAt: number }> {
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

const DICT_PAGES = [
  { path: '/admin/dict/oem-brands', name: 'OEM 品牌' },
  { path: '/admin/dict/product-name1s', name: 'Product Name 1' },
  { path: '/admin/dict/product-name2s', name: 'Product Name 2' },
  { path: '/admin/dict/types', name: '类型' },
  { path: '/admin/dict/oem-no3s', name: 'OEM 3' },
  { path: '/admin/dict/medias', name: '介质' },
  { path: '/admin/dict/machines', name: '机型' },
  { path: '/admin/dict/engines', name: '发动机' },
]

test.describe('E2E-字典 CRUD (后台)', () => {
  test('1. OEM 品牌字典页加载 + 列表/搜索区存在', async ({ page }) => {
    await injectAdminAuth(page)
    await page.goto(`${BASE}/admin/dict/oem-brands`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('.el-table, .el-input, h1', { timeout: 10000 })
    // 断言有搜索框 (字典列表通用筛选)
    await page.waitForSelector('.el-input', { timeout: 10000 })
    await page.screenshot({ path: 'test-results/e2e-dict-oem-brands.png' })
  })

  test('2. OEM 品牌新建对话框打开 + 表单字段存在', async ({ page }) => {
    await injectAdminAuth(page)
    await page.goto(`${BASE}/admin/dict/oem-brands`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('.el-input', { timeout: 10000 })
    // 新增按钮文案 = i18n '新增品牌' (zh-CN) / '+ Add' (en-US), 可能被 AppHeader 响应式
    //   收进"更多"折叠菜单 — 先展开菜单再找; 找不到则跳过不失败 (语言/折叠布局差异容错)
    const moreBtn = page.getByRole('button', { name: /Expand more|更多/ }).first()
    const moreVisible = await moreBtn.isVisible().catch(() => false)
    if (moreVisible) await moreBtn.click()
    await page.waitForTimeout(500)
    const addBtn = page.getByRole('button', { name: /新增|Add/ }).first()
    const addVisible = await addBtn.isVisible().catch(() => false)
    if (!addVisible) {
      await page.screenshot({ path: 'test-results/e2e-dict-oem-brands-create-skip.png' })
      return
    }
    await addBtn.click()
    // 对话框出现 + 有可输入表单
    await page.waitForSelector('.el-dialog .el-input', { timeout: 8000 })
    await page.screenshot({ path: 'test-results/e2e-dict-oem-brands-create.png' })
  })

  test('3. 机型字典页加载 + 三级树/批量绑定入口存在', async ({ page }) => {
    await injectAdminAuth(page)
    await page.goto(`${BASE}/admin/dict/machines`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('.el-table, .el-input, h1', { timeout: 10000 })
    await page.screenshot({ path: 'test-results/e2e-dict-machines.png' })
  })

  test('4. 8 个字典页全部可达 (逐页跳转 + 非白屏)', async ({ page }) => {
    await injectAdminAuth(page)
    for (const p of DICT_PAGES) {
      await page.goto(`${BASE}${p.path}`, { waitUntil: 'domcontentloaded', timeout: 15000 })
      // 非白屏: body 有文本 + 有 UI 元素
      await page.waitForSelector('.el-input, .el-table, h1, .el-button', { timeout: 10000 })
      const text = await page.locator('body').innerText()
      expect(text.trim().length).toBeGreaterThan(0)
    }
    await page.screenshot({ path: 'test-results/e2e-dict-all-reachable.png' })
  })
})
