// axios 封装 (JWT 改造版)
//   - 后端 baseURL: /api (Vite dev 代理到 http://localhost:5000)
//   - 优先注入 Authorization: Bearer <accessToken>
//   - 兼容兜底: 旧 dev token (非 JWT 格式) 走 X-Admin-Token (后端 DevTokenAuthMiddleware 双轨)
//   - 401 自动 refresh: 用全局 Promise 防并发, 多个 401 共享一次 refresh
//   - ProblemDetails (RFC 7807) 统一错误处理
//   - 限流 429 弹窗提示
//   - P2-8.1: 请求取消 (AbortController) 静默处理
//   - P2-8.2: 错误码映射表 + 500+ 不透传 detail (防 SQL/堆栈泄露)
import axios, { AxiosError, type AxiosInstance, type AxiosRequestConfig, type InternalAxiosRequestConfig } from 'axios'
import { ElMessage } from 'element-plus'
import i18n from '@/i18n'
import { useAdminAuthStore } from '@/composables/useAdminAuth'
import type { LoginResponse } from '@/api/types'
import { captureException, addBreadcrumb } from './errorMonitor'

const TOKEN_HEADER_LEGACY = 'X-Admin-Token'
const TOKEN_HEADER_BEARER = 'Authorization'

// JWT 形态判定: 三段式 eyXXX.eyXXX.xxx (区分旧 dev token)
function isJwtLike(t: string): boolean {
  return /^eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/.test(t)
}

export const http: AxiosInstance = axios.create({
  baseURL: '/api',
  timeout: 30000,
  headers: { 'Content-Type': 'application/json' }
})

// 请求拦截: 优先 Authorization Bearer, 旧 dev token 兜底 X-Admin-Token
http.interceptors.request.use((cfg: InternalAxiosRequestConfig) => {
  const auth = useAdminAuthStore()
  if (auth.token) {
    if (isJwtLike(auth.token)) {
      cfg.headers.set(TOKEN_HEADER_BEARER, `Bearer ${auth.token}`)
    } else {
      // 旧 dev token (非 JWT): 后端 DevTokenAuthMiddleware 仍接受 X-Admin-Token
      cfg.headers.set(TOKEN_HEADER_LEGACY, auth.token)
    }
  }
  // V24-F44 (spec F3-13 修复方案 4): 携带前端版本号, 后端可选择性路由到对应 API
  //   WHY: 灰度发布期间, 旧前端版本调用旧 API, 新前端版本调用新 API
  //   __API_VERSION__ 由 vite.config.ts define 注入 (package.json version)
  //   后端可不读取此头 (向后兼容), 也可根据版本路由 (灰度发布)
  cfg.headers.set('X-Client-Version', __API_VERSION__)
  return cfg
})

// ProblemDetails 类型
export interface ProblemDetails {
  type?: string
  title: string
  status: number
  detail?: string
  instance?: string
  [key: string]: any
}

// P2-8.2: 错误码 → 友好提示映射表 (Record<number, string>, 便于扩展)
// V24-F35 (spec Task 4.9.1): 导出便于单元测试覆盖
export const ERROR_CODE_MAP: Record<number, string> = {
  400: '请求参数错误',
  401: '未登录或登录已过期',
  403: '没有权限执行此操作',
  404: '请求的资源不存在',
  409: '资源已存在 (冲突)',
  422: '请求参数验证失败',
  429: '请求过于频繁,请稍后重试'
}

// V24-F43 (spec Task 0.5.5/F3-4): errorCode → 中文友好提示 静态映射 (不依赖 i18n)
//   WHY 静态映射: i18n locale 文件异步加载期间, t(key) 返回 key 本身, 需要静态兜底
//   fallback 链: data.errorCode → ERROR_CODE_I18N[errorCode] → i18n.global.t('common.error.' + errorCode)
//                → ERROR_CODE_MAP[status] → data.title → 请求失败 (status)
//   与 zh-CN.ts common.error 保持同步 (此处为 i18n 未加载完成时的兜底)
export const ERROR_CODE_I18N: Record<string, string> = {
  // ===== 旧 ERR_ 前缀错误码 (10 个) =====
  ERR_VALIDATION_FAILED: '请求参数验证失败',
  ERR_NOT_FOUND: '请求的资源不存在',
  ERR_CONFLICT: '资源已存在或冲突',
  ERR_FORBIDDEN: '没有权限执行此操作',
  ERR_CANCELLED: '请求已取消',
  ERR_INTERNAL: '服务器内部错误,请稍后重试',
  ERR_DB_CONFLICT: '数据冲突 (可能被其他用户修改),请刷新重试',
  ERR_DB_CONSTRAINT: '数据约束失败 (外键或非空校验)',
  ERR_DB_TIMEOUT: '数据库繁忙,请稍后重试',
  ERR_AUTH_FAILED: '用户名或密码错误',
  // ===== V2 错误码 (15 个,无 ERR_ 前缀) =====
  MR1_REQUIRED: 'MR.1 编号必填',
  MR1_FORMAT_INVALID: 'MR.1 编号格式无效',
  MR1_ALREADY_EXISTS: 'MR.1 编号已存在',
  OEM3_ALREADY_EXISTS: 'OEM 3 编号已存在',
  MACHINE_TYPE_INVALID: '机型类型无效',
  XREF_CONFLICT: '交叉引用冲突 (可能被其他用户修改),请刷新重试',
  SEARCH_PAGE_TOO_DEEP: '搜索页数过深,请重新搜索',
  CURSOR_INVALID: '分页游标无效,已重置到第 1 页',
  CURSOR_EXPIRED: '分页游标已过期,已重置到第 1 页',
  IMAGE_ROLE_SLOT_MISMATCH: '图片角色与槽位不匹配',
  IMAGE_DETAIL_SLOT_INVALID: '图片详情槽位无效 (必须在 1-6 之间)',
  IMAGE_PRIMARY_DUPLICATE: '主图已存在 (每个产品仅允许 1 张主图)',
  IMAGE_DETAIL_SLOT_DUPLICATE: '图片详情槽位重复',
  MR1_NOT_FOUND: 'MR.1 编号不存在',
  OEM3_NOT_FOUND: 'OEM 3 编号不存在'
}

/**
 * V24-F43 (spec F3-11): 安全 i18n 翻译
 *   WHY: i18n locale 文件异步加载期间, t(key) 返回 key 本身, 需要 fallback
 *   @param key i18n key (如 'common.error.ERR_AUTH_FAILED')
 *   @param fallback i18n 未命中时的兜底文案 (空字符串表示无兜底, 调用方自行处理)
 *   @returns 翻译后的文案; 未命中返回 fallback
 */
function safeT(key: string, fallback: string): string {
  const msg = i18n.global.t(key)
  return msg === key ? fallback : msg
}

/**
 * V24-F43 (spec F3-4): 错误消息 fallback 链
 *   优先级: data.errorCode → ERROR_CODE_I18N[errorCode] → i18n.global.t('common.error.' + errorCode)
 *           → ERROR_CODE_MAP[status] → data.title → fallback
 *   WHY fallback 链: 旧前端版本收到新错误码不会白屏, 至少有通用提示
 *   @param errorCode 后端 ProblemDetails.extensions.errorCode
 *   @param status HTTP 状态码
 *   @param data ProblemDetails 响应体
 *   @param fallback 最终兜底文案 (如 `请求失败 (${status})`)
 *   @returns 友好的错误提示文案
 */
export function resolveErrorMessage(
  errorCode: string | undefined,
  status: number | undefined,
  data: ProblemDetails | undefined,
  fallback: string
): string {
  // 1. data.errorCode → ERROR_CODE_I18N 静态映射 (不依赖 i18n, 最快)
  if (errorCode && ERROR_CODE_I18N[errorCode]) {
    return ERROR_CODE_I18N[errorCode]
  }
  // 2. i18n.global.t('common.error.' + errorCode) (依赖 i18n 加载完成)
  if (errorCode) {
    const i18nMsg = safeT(`common.error.${errorCode}`, '')
    if (i18nMsg) return i18nMsg
  }
  // 3. ERROR_CODE_MAP[status] (HTTP status 级别兜底)
  if (status !== undefined && ERROR_CODE_MAP[status]) {
    return ERROR_CODE_MAP[status]
  }
  // 4. data.title (ProblemDetails 业务可读标题, 不含堆栈)
  if (data?.title) {
    return data.title
  }
  // 5. 最终兜底
  return fallback
}

// ===== 401 自动 refresh (防并发) =====
//   全局 Promise: 多个 401 同时触发时, 共享同一次 refresh 调用
//   _retry 标记: 防止 refresh 后重试的请求再次 401 时无限循环
let refreshPromise: Promise<string | null> | null = null

// V24-F33 (spec F5-4/F5-9/Task 4.5.22/4.5.23): 401 重定向防重入标志
//   - 同步设置: 在 router.replace 之前立即 true, 避免导航过程中 watch 触发 URL sync loop
//   - 延迟重置: setTimeout 1500ms, 让导航完成 + watch 触发完毕
//   - 导出 isHttpRedirecting() 供 PublicSearchView 检查, 避免 401 重定向时 URL 同步循环
let isRedirecting = false

async function doRefresh(): Promise<string | null> {
  const auth = useAdminAuthStore()
  if (!auth.refreshToken) {
    return null
  }
  try {
    // 直接用 axios 原始实例, 避免走拦截器循环; 后端 /api/auth/refresh 不要求 Authorization
    const resp = await axios.post<LoginResponse>('/api/auth/refresh', {
      refreshToken: auth.refreshToken
    })
    auth.setAuth(resp.data)
    return resp.data.accessToken
  } catch {
    return null
  }
}

/**
 * V24-F33 (spec F5-9/Task 4.5.23): 异步执行 401 重定向
 *   - 动态 import router 避免 ESM 循环依赖 (http → router → SearchView → api → http)
 *   - 保留 returnUrl (pathname + search) 完整路径, 登录后回跳原页面
 *   - chunk 加载失败时降级到 window.location.href (网络问题/部署中)
 *   - 单独抽出为 async 函数, 便于 try/catch + console.warn 上报
 */
async function handle401Redirect(): Promise<void> {
  // V24-F33 (spec F5-9): returnUrl 保留 pathname + search 完整路径
  //   WHY pathname + search: 用户在第 50 页时 URL 含 ?page=50&cursor=xxx, 仅 pathname 会丢失分页状态
  const returnUrl = encodeURIComponent(window.location.pathname + window.location.search)
  const loginUrl = `/login?return=${returnUrl}`

  try {
    // V24-F33 (spec F4-4/Task 4.5.16): 动态 import router 避免循环依赖
    //   WHY 动态 import: 静态 import 会形成 http → router → SearchView → api → http 循环
    //                    运行时 router 可能为 undefined (ESM 模块加载顺序)
    const { default: router } = await import('@/router')
    await router.replace(loginUrl)
  } catch (chunkError) {
    // V24-F33 (spec F5-9): chunk 加载失败 (网络问题/部署中), 用原生跳转兜底
    //   WHY: 动态 import 失败时不能让用户卡死, 必须降级到原生 location.href
    console.warn('[http] router chunk 加载失败, 用原生跳转', chunkError)
    window.location.href = loginUrl
  }
}

/**
 * V24-F33 (spec F5-4/Task 4.5.22): 401 重定向处理 (防重入)
 *   - 同步设置 isRedirecting = true (在 router.replace 之前), 避免 watch 触发 URL sync loop
 *   - 多次 401 共享同一次重定向 (幂等)
 *   - 延迟 1500ms 重置 isRedirecting, 让导航完成 + watch 触发完毕
 *
 * @param _reason 重定向原因 (日志用, 默认 'refresh-failed')
 */
export function handle401(_reason: string = 'refresh-failed'): void {
  // 幂等: 多次 401 共享同一次重定向
  if (isRedirecting) return
  // F5-4: 同步设置, 在 router.replace 之前 (避免导航过程中 watch 触发)
  isRedirecting = true

  // 调用异步重定向, finally 延迟重置 isRedirecting
  handle401Redirect().finally(() => {
    // F5-4: 延迟 1500ms 重置, 避免导航过程中触发的 watch
    //   WHY 1500ms: router.replace 异步完成 + Vue watch 触发 + URL sync 完成, 经验值
    setTimeout(() => { isRedirecting = false }, 1500)
  })
}

/**
 * V24-F33 (spec F5-4/Task 4.5.22.3): 查询 http 是否正在 401 重定向
 *   - 供 PublicSearchView watch route.query 检查, 避免 URL sync loop
 *   - watch 内同步检查, 触发时跳过 URL 同步逻辑
 */
export function isHttpRedirecting(): boolean {
  return isRedirecting
}

/**
 * V24-F33 兼容: 旧 redirectToLogin 内联实现 (已废弃, 内部转调 handle401)
 *   WHY 保留: 避免破坏其他可能引用 redirectToLogin 的代码 (虽然当前仅 http.ts 内部用)
 *   @deprecated 改用 handle401()
 */
function redirectToLogin(): void {
  const auth = useAdminAuthStore()
  auth.clearAuth()
  handle401()
}

/**
 * V24-F37 (spec F3-5/Task 4.5.14): CURSOR 过期/无效自动重置到第 1 页
 *   - 后端返回 400 + errorCode=CURSOR_EXPIRED 或 CURSOR_INVALID 时触发
 *   - 改用 SPA router.replace (不触发整页刷新), 保留 SPA 状态 (滚动位置/对比列表/搜索条件)
 *   - 同步 sessionStorage 设置 'cursor-reset-toast' 标记, App.vue mounted 读取并显示一次性 toast
 *   - 仅在 /search 路径触发重置 (其他路径忽略, 避免误清 URL)
 *
 * spec F3-5 漏洞: 旧方案用 window.location.href 整页刷新, 丢失 SPA 状态 + ElMessage 提示消失
 *
 * @param errorCode 后端错误码 ('CURSOR_EXPIRED' | 'CURSOR_INVALID')
 */
async function handleCursorExpired(errorCode: string): Promise<void> {
  // V24-F37: sessionStorage 标记, App.vue mounted 显示一次性 toast
  //   WHY sessionStorage: 整页刷新后 toast 不丢失 (ElMessage 在刷新后消失)
  //   WHY 不用 localStorage: cursor 重置是会话级事件, 关闭标签页后不应再提示
  try {
    sessionStorage.setItem('cursor-reset-toast', errorCode)
  } catch {
    // Safari 隐私模式 sessionStorage.setItem 可能抛 QuotaExceededError
    //   注: 这里不用 safeStorage, 因为 cursor 重置是低频事件, 失败时仅丢失 toast 提示, 不影响功能
  }

  // 仅在搜索相关路径触发重置 (其他路径如 /admin/products 也可能用 cursor, 但重置策略不同)
  //   V24-F41 (spec 改进建议): 改用精确匹配, 避免 /admin/search 等非预期路径被误清 URL
  //   WHY 旧实现 pathname.includes('/search') 会匹配 /admin/search, 误清后台搜索的 cursor
  //   精确匹配 /search 和 /search/aggregate (前端两个公开搜索页)
  //   不匹配 /admin/search (后台搜索有自己的分页策略, 不应被公开搜索的 cursor 重置影响)
  const { pathname, search } = window.location
  const isPublicSearchPath = pathname === '/search' || pathname === '/search/aggregate'
  if (!isPublicSearchPath) return

  // 解析当前 URL query, 移除 cursor, 重置 page=1
  const url = new URL(pathname + search, window.location.origin)
  url.searchParams.delete('cursor')
  url.searchParams.set('page', '1')
  const newPath = url.pathname + url.search

  // V24-F37 (spec F3-5): 改用 SPA router.replace (不触发整页刷新)
  //   WHY router.replace: 保留 SPA 状态 (已勾选对比列表、已填搜索条件、滚动位置)
  //   WHY 不用 window.location.href: 整页刷新丢失所有 SPA 状态 + ElMessage 提示消失
  try {
    const { default: router } = await import('@/router')
    await router.replace(newPath)
  } catch (chunkError) {
    // chunk 加载失败兜底: 降级到 window.location.href (极端情况)
    console.warn('[http] router chunk 加载失败, cursor 重置降级到原生跳转', chunkError)
    window.location.href = newPath
  }
}

interface RetriableConfig extends InternalAxiosRequestConfig {
  _retry?: boolean
}

// 响应拦截: 错误统一处理 + 401 refresh
http.interceptors.response.use(
  (r) => r,
  async (err: AxiosError<ProblemDetails>) => {
    // P2-8.1: 请求取消 (AbortController) 静默处理, 不弹错误提示
    if (err?.code === 'ERR_CANCELED' || err?.name === 'CanceledError') {
      return Promise.reject(err)
    }

    const status = err.response?.status
    const cfg = err.config as RetriableConfig | undefined
    const data = err.response?.data
    const isProblemDetails = !!(data && typeof data === 'object' && 'title' in data)
    const detail = isProblemDetails ? (data.detail || data.title) : undefined
    // V24-F37: 提取 errorCode 用于 CURSOR_EXPIRED/CURSOR_INVALID 分支判断
    const errorCode = (data as { errorCode?: string } | undefined)?.errorCode

    // ===== V24-F37 (spec F3-5/Task 4.5.14): CURSOR 过期/无效自动重置到第 1 页 =====
    //   - 后端返回 400 + errorCode=CURSOR_EXPIRED/CURSOR_INVALID (spec S13/F5)
    //   - 改用 SPA router.replace (不触发整页刷新, 保留 ElMessage + SPA 状态)
    //   - sessionStorage 标记 + App.vue mounted 显示一次性 toast
    //   - 触发后仍 reject (调用方 catch 可选处理, 默认显示错误提示)
    if (errorCode === 'CURSOR_EXPIRED' || errorCode === 'CURSOR_INVALID') {
      // 异步触发重置, 不阻塞 reject (避免调用方等待 router.replace 完成)
      handleCursorExpired(errorCode)
      ElMessage.warning(errorCode === 'CURSOR_EXPIRED' ? '分页游标已过期,已重置到第 1 页' : '分页游标无效,已重置到第 1 页')
      return Promise.reject(err)
    }

    // ===== 401 自动 refresh 流程 =====
    //   条件: 状态 401 + 配置存在 + 未重试过 + 不是 /auth/* 端点本身 (避免登录失败也触发 refresh)
    const url = cfg?.url || ''
    const isAuthEndpoint = url.startsWith('/auth/')
    if (
      status === 401 &&
      cfg &&
      !cfg._retry &&
      !isAuthEndpoint
    ) {
      cfg._retry = true
      // 防并发: 多个 401 共享同一个 refresh Promise
      if (!refreshPromise) {
        refreshPromise = doRefresh().finally(() => {
          // 短暂保留 Promise 引用到下一微任务, 让排队请求能拿到结果后再清空
          setTimeout(() => { refreshPromise = null }, 0)
        })
      }
      const newToken = await refreshPromise
      if (newToken) {
        // 重试原请求, 带新 token
        cfg.headers.set(TOKEN_HEADER_BEARER, `Bearer ${newToken}`)
        return http.request(cfg)
      }
      // refresh 失败: 跳登录页 + 提示
      ElMessage.error(i18n.global.t('common.feedback.info_030'))
      redirectToLogin()
      return Promise.reject(err)
    }

    if (status === undefined) {
      // 网络层错误 (无响应) — 批次 6c: 上报错误监控
      if (err?.code === 'ECONNABORTED') {
        ElMessage.error(i18n.global.t('common.feedback.info_043'))
        captureException(err, { level: 'warning', tags: { source: 'axios', type: 'timeout' } })
      } else if (err?.code === 'ERR_NETWORK') {
        ElMessage.error('网络连接失败,请检查网络')
        captureException(err, { level: 'error', tags: { source: 'axios', type: 'network' } })
      } else {
        ElMessage.error(`网络异常: ${err.message || '请稍后重试'}`)
        captureException(err, { level: 'error', tags: { source: 'axios', type: 'unknown' } })
      }
    } else if (status >= 500) {
      // 批次 6c: 5xx 上报 (含 detail 便于排查, 脱敏在 captureException 内部完成)
      captureException(err, {
        level: 'error',
        tags: { source: 'axios', status: String(status) },
        extra: { url: cfg?.url, method: cfg?.method, detail },
      })
      // P2-8.2: 500+ 绝对不透传 detail (可能含堆栈/SQL), 仅展示友好提示 + 错误码
      ElMessage.error(`服务器繁忙,请稍后重试 (错误码:${status})`)
      // 开发环境打印 detail 便于排查 (生产环境不打印, 避免泄露)
      //   WHY process.env.NODE_ENV: 项目未引入 vite/client 类型, 用 node 内置类型保证 type-check 通过
      if (process.env.NODE_ENV !== 'production' && detail) {
        console.warn('[DEV] 服务器错误详情:', detail)
      }
    } else if (status === 429) {
      // 保留 429 业务提示: retry-after 秒数对用户有指导意义
      const retryAfter = err.response?.headers?.['retry-after']
      ElMessage.warning(`请求频率超限, 请 ${retryAfter || 60}s 后重试`)
    } else if (status === 401) {
      // 401 但已重试过 / 是 auth 端点: 用业务可读提示
      // V24-F43 (spec F3-4): 改用 resolveErrorMessage fallback 链
      //   WHY: 统一错误提示逻辑, 避免散落判断; 旧前端版本收到新错误码也有兜底
      //   特殊处理: ERR_AUTH_FAILED 走 common.feedback.error_029 (历史兼容, 登录页专用文案)
      if (errorCode === 'ERR_AUTH_FAILED') {
        ElMessage.error(i18n.global.t('common.feedback.error_029'))
      } else {
        // V24-F43: fallback 链: errorCode → ERROR_CODE_I18N → i18n → ERROR_CODE_MAP[401] → data.title → 请求失败 (401)
        ElMessage.error(resolveErrorMessage(errorCode, status, isProblemDetails ? data : undefined, '请求失败 (401)'))
      }
    } else if (errorCode || ERROR_CODE_MAP[status] || isProblemDetails) {
      // V24-F43 (spec F3-4): 统一 4xx 错误提示 (合并原 ERROR_CODE_MAP[status] + isProblemDetails + else 分支)
      //   fallback 链: errorCode → ERROR_CODE_I18N → i18n → ERROR_CODE_MAP[status] → data.title → 请求失败 (status)
      //   WHY 合并: 原 3 分支逻辑等价于 resolveErrorMessage, 合并后更清晰且支持新错误码自动兜底
      ElMessage.error(resolveErrorMessage(errorCode, status, isProblemDetails ? data : undefined, `请求失败 (${status})`))
    } else {
      // 非 ProblemDetails 响应 (如纯文本/空响应): 用 detail 或状态码兜底
      ElMessage.error(detail || `请求失败 (${status})`)
    }

    // 保留 problem 附加属性, 便于调用方读取 ProblemDetails 结构
    if (isProblemDetails) {
      return Promise.reject(Object.assign(err, { problem: data }))
    }
    return Promise.reject(err)
  }
)

// 导出类型供调用方扩展 (避免循环依赖, 此处仅 re-export 类型)
export type { AxiosRequestConfig }
