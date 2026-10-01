// E2E 共享鉴权工具: 真实 JWT 登录 + 注入 localStorage
//   WHY (2026-09-13 QA 定位): 多数 spec 注入旧 dev token (perf-import-token-not-for-production-use),
//     该值走 X-Admin-Token, 需与后端 Auth__DevStaticToken 严格一致; 部署配置不一致时
//     所有后台 API 401 → 页面跳登录页, 且宽松断言 (h1/.el-input 登录页也有) 造成"假通过"。
//     统一改用真实 JWT 登录 (admin-users.spec.ts 已验证模式), 与后端配置解耦, 稳定可靠。
//   登录限流: 由 docker-compose.yml RateLimit__AuthPermitsPerMinute=999 (dev 部署) 兜底,
//     但各 spec 仍应在 beforeAll 缓存复用一次登录, 避免无效重复调用。
import { type APIRequestContext, type Page } from '@playwright/test'

export interface AdminAuth {
  token: string
  refreshToken: string
  user: { id?: number; username: string; role: string } | null
  expiresAt: number
}

const BACKEND = process.env.BACKEND_URL || 'http://localhost:5148'
const ADMIN_USER = 'admin'
const ADMIN_PWD = process.env.INITIAL_ADMIN_PASSWORD || 'Admin@2026'

// 通过 API 登录获取 JWT (beforeAll 调用一次, 结果缓存复用)
export async function loginAsAdmin(request: APIRequestContext): Promise<AdminAuth> {
  const resp = await request.post(`${BACKEND}/api/auth/login`, {
    data: { username: ADMIN_USER, password: ADMIN_PWD },
    headers: { 'Content-Type': 'application/json' },
    timeout: 15000
  })
  if (!resp.ok()) {
    throw new Error(`JWT 登录失败: ${resp.status()} ${await resp.text().catch(() => '')}`)
  }
  const d = await resp.json()
  return {
    token: d.accessToken,
    refreshToken: d.refreshToken,
    user: d.user ?? { username: ADMIN_USER, role: 'admin' },
    expiresAt: Date.now() + (d.expiresIn ?? 3600) * 1000
  }
}

// 注入完整鉴权状态到 localStorage (useAdminAuth 新 key, 强制 zh-CN locale)
export async function injectAdminAuth(page: Page, auth: AdminAuth): Promise<void> {
  await page.addInitScript((a) => {
    localStorage.setItem('sakura_locale', 'zh-CN')
    localStorage.setItem('sakura_admin_auth', JSON.stringify(a))
  }, auth)
}
