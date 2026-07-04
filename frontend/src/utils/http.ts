// Day 9: axios 封装
//   - 后端 baseURL: /api (Vite dev 代理到 http://localhost:5000)
//   - 自动注入 X-Admin-Token (Pinia store)
//   - ProblemDetails (RFC 7807) 统一错误处理
//   - 限流 429 弹窗提示
//   - P2-8.1: 请求取消 (AbortController) 静默处理
//   - P2-8.2: 错误码映射表 + 500+ 不透传 detail (防 SQL/堆栈泄露)
import axios, { AxiosError, type AxiosInstance, type InternalAxiosRequestConfig } from 'axios'
import { ElMessage } from 'element-plus'
import { useAdminAuthStore } from '@/composables/useAdminAuth'

const TOKEN_HEADER = 'X-Admin-Token'

export const http: AxiosInstance = axios.create({
  baseURL: '/api',
  timeout: 30000,
  headers: { 'Content-Type': 'application/json' }
})

// 请求拦截: 注入 token
http.interceptors.request.use((cfg: InternalAxiosRequestConfig) => {
  const auth = useAdminAuthStore()
  if (auth.token) {
    cfg.headers.set(TOKEN_HEADER, auth.token)
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

// 响应拦截: 错误统一处理
http.interceptors.response.use(
  (r) => r,
  (err: AxiosError<ProblemDetails>) => {
    // P2-8.1: 请求取消 (AbortController) 静默处理, 不弹错误提示
    if (err?.code === 'ERR_CANCELED' || err?.name === 'CanceledError') {
      return Promise.reject(err)
    }

    const status = err.response?.status
    const data = err.response?.data
    const isProblemDetails = !!(data && typeof data === 'object' && 'title' in data)
    const detail = isProblemDetails ? (data.detail || data.title) : undefined

    if (status === undefined) {
      // 网络层错误 (无响应)
      if (err?.code === 'ECONNABORTED') {
        ElMessage.error('请求超时,请检查网络后重试')
      } else if (err?.code === 'ERR_NETWORK') {
        ElMessage.error('网络连接失败,请检查网络')
      } else {
        ElMessage.error(`网络异常: ${err.message || '请稍后重试'}`)
      }
    } else if (status >= 500) {
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
