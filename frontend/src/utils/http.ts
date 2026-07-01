// Day 9: axios 封装
//   - 后端 baseURL: /api (Vite dev 代理到 http://localhost:5000)
//   - 自动注入 X-Admin-Token (Pinia store)
//   - ProblemDetails (RFC 7807) 统一错误处理
//   - 限流 429 弹窗提示
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

// 响应拦截: 错误统一处理
http.interceptors.response.use(
  (r) => r,
  (err: AxiosError<ProblemDetails>) => {
    if (err.response) {
      const { status, data } = err.response
      // ProblemDetails 格式
      if (data && typeof data === 'object' && 'title' in data) {
        if (status === 401) {
          ElMessage.error(`鉴权失败: ${data.detail || '请检查 X-Admin-Token'}`)
        } else if (status === 429) {
          const retryAfter = err.response.headers['retry-after']
          ElMessage.warning(`请求频率超限, 请 ${retryAfter || 60}s 后重试`)
        } else if (status === 404) {
          ElMessage.error(`未找到: ${data.detail || data.title}`)
        } else if (status >= 500) {
          ElMessage.error(`服务器错误: ${data.detail || data.title}`)
        } else {
          ElMessage.error(`${data.title}: ${data.detail || ''}`)
        }
        return Promise.reject(Object.assign(err, { problem: data }))
      }
      ElMessage.error(`HTTP ${status}: ${err.message}`)
    } else {
      ElMessage.error(`网络错误: ${err.message}`)
    }
    return Promise.reject(err)
  }
)
