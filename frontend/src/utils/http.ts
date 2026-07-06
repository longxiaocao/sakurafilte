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
const ERROR_CODE_MAP: Record<number, string> = {
  400: '请求参数错误',
  401: '未登录或登录已过期',
  403: '没有权限执行此操作',
  404: '请求的资源不存在',
  409: '资源已存在 (冲突)',
  422: '请求参数验证失败',
  429: '请求过于频繁,请稍后重试'
}

// ===== 401 自动 refresh (防并发) =====
//   全局 Promise: 多个 401 同时触发时, 共享同一次 refresh 调用
//   _retry 标记: 防止 refresh 后重试的请求再次 401 时无限循环
let refreshPromise: Promise<string | null> | null = null

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

function redirectToLogin() {
  const auth = useAdminAuthStore()
  auth.clearAuth()
  // 避免在登录页本身 401 时再带 redirect=login 死循环
  if (window.location.pathname !== '/login') {
    const redirect = window.location.pathname + window.location.search
    window.location.href = `/login?redirect=${encodeURIComponent(redirect)}`
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
      // 后端 ProblemDetails.errorCode 映射 (如 ERR_AUTH_FAILED)
      const errorCode = data?.errorCode
      if (errorCode === 'ERR_AUTH_FAILED') {
        ElMessage.error(i18n.global.t('common.feedback.error_029'))
      } else if (isProblemDetails && data.title) {
        ElMessage.error(data.title)
      } else {
        ElMessage.error(ERROR_CODE_MAP[401])
      }
    } else if (ERROR_CODE_MAP[status]) {
      ElMessage.error(ERROR_CODE_MAP[status])
    } else if (isProblemDetails) {
      // 其他 ProblemDetails 响应: 透传 title (业务可读, 不含堆栈)
      ElMessage.error(data.title)
    } else {
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
