// P5.5: 前端性能埋点 - 批量收集 + 上报
// 设计:
//   - 内存 buffer 累计样本, 达到 10 条 或 30s 自动 flush
//   - 用 navigator.sendBeacon 保活 (页面卸载时也能发出)
//   - flush 失败时样本保留在 buffer, 下次 flush 重试 (网络抖动不丢数据)
//   - 排除 /api/perf/ingest 自身 (避免递归上报)
// WHY 不每次请求都上报:
//   - 单条 POST 浪费连接, 批量合并更省
//   - 10/30s 平衡实时性 vs 网络开销
//   - 后端 /api/perf/ingest 上限 100 条/批, 防止恶意大批量
import { http } from './http'

export interface PerfSample {
  path: string
  method: string
  statusCode: number
  durationMs: number
  ts: string  // ISO8601
}

const BUFFER_LIMIT = 10
const FLUSH_INTERVAL_MS = 30_000
const INGEST_PATH = '/api/perf/ingest'
const STORAGE_KEY = 'sakurafilter_perf_buffer'

let buffer: PerfSample[] = []
let flushTimer: number | null = null
let installed = false
// P2-7: 保存 interceptor id 用于 uninstall 时 eject
let interceptorIds: { request: number; response: number } = { request: -1, response: -1 }

function loadFromStorage(): PerfSample[] {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    if (!raw) return []
    const arr = JSON.parse(raw)
    if (Array.isArray(arr)) return arr as PerfSample[]
  } catch {
    // 解析失败时忽略
  }
  return []
}

function saveToStorage() {
  try {
    // WHY sessionStorage: 跨页面刷新保留, 关闭标签页自动清, 避免长期堆积
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(buffer))
  } catch {
    // quota 超限或隐私模式, 静默忽略
  }
}

/**
 * 上报 buffer 到后端 /api/perf/ingest
 * WHY 优先 sendBeacon:
 *   - 浏览器关闭/页面卸载时仍能发出请求
 *   - 后端无需 CORS preflight (Content-Type: text/plain)
 * WHY 失败时不清空 buffer:
 *   - 网络抖动场景下次 flush 重试
 *   - 上限 1000 条防止内存泄漏
 */
async function flush() {
  if (buffer.length === 0) return
  const samples = buffer.slice()
  const body = JSON.stringify({ samples })

  // 优先 sendBeacon (页面卸载时仍有效)
  let ok = false
  if (typeof navigator !== 'undefined' && typeof navigator.sendBeacon === 'function') {
    try {
      const blob = new Blob([body], { type: 'application/json' })
      ok = navigator.sendBeacon(INGEST_PATH, blob)
    } catch {
      ok = false
    }
  }
  // 降级: 普通 fetch (异步, 不 await — 不阻塞 UI)
  // P2-4 说明: 此处绕过 http.ts 是为了 keepalive: true (模拟 sendBeacon), http.ts 的 axios 不支持 keepalive
  //   ingest 端点在 DevTokenAuthMiddleware.ExemptPaths 中已豁免 X-Admin-Token (P5.5 设计), 故无需注入 token
  if (!ok) {
    try {
      fetch(INGEST_PATH, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body,
        keepalive: true,  // 类似 sendBeacon 行为
      }).then((r) => {
        if (r.ok) {
          // 成功: 移除已发送的样本
          buffer = buffer.filter((s) => !samples.includes(s))
          saveToStorage()
        }
      }).catch(() => {
        // 失败: 保留 buffer 等下次
      })
      return  // 异步路径直接返回, 不立即清空 buffer
    } catch {
      // 同步异常, 保留 buffer
      return
    }
  }
  // sendBeacon 路径: 立即清空 (浏览器负责送达)
  buffer = []
  saveToStorage()
}

function scheduleFlush() {
  if (flushTimer !== null) return
  flushTimer = window.setTimeout(() => {
    flushTimer = null
    void flush()
  }, FLUSH_INTERVAL_MS)
}

/**
 * 记录一个请求样本
 * - buffer 满 10 条立即 flush
 * - 任何时候都启动 30s flush 定时器
 */
export function recordPerf(sample: PerfSample) {
  // 排除 ingest 自身, 避免递归
  if (sample.path.startsWith('/api/perf/ingest')) return
  buffer.push(sample)
  // 上限保护
  if (buffer.length > 1000) {
    buffer = buffer.slice(-500)  // 保留最近 500 条
  }
  saveToStorage()
  if (buffer.length >= BUFFER_LIMIT) {
    void flush()
  } else {
    scheduleFlush()
  }
}

/**
 * 安装 axios 请求拦截器
 * - 请求开始时记录时间戳
 * - 响应/错误时计算耗时并 recordPerf
 * - 重试请求各算各的 (不重复)
 */
export function installPerfInterceptor() {
  if (installed) return
  installed = true

  // 从 sessionStorage 恢复未发送的 buffer (页面刷新后)
  buffer = loadFromStorage()

  // P2-7: 保存 interceptor id 用于 uninstall 时 eject
  interceptorIds.request = http.interceptors.request.use((cfg) => {
    // WHY 用 internal property: 避免污染用户数据
    ;(cfg as any).__t0 = performance.now()
    return cfg
  })

  interceptorIds.response = http.interceptors.response.use(
    (r) => {
      const t0 = (r.config as any).__t0
      if (typeof t0 === 'number') {
        recordPerf({
          path: r.config.url || '?',
          method: (r.config.method || 'GET').toUpperCase(),
          statusCode: r.status,
          durationMs: Math.round((performance.now() - t0) * 100) / 100,
          ts: new Date().toISOString(),
        })
      }
      return r
    },
    (err) => {
      const cfg = err?.config
      if (cfg) {
        const t0 = (cfg as any).__t0
        if (typeof t0 === 'number') {
          recordPerf({
            path: cfg.url || '?',
            method: (cfg.method || 'GET').toUpperCase(),
            statusCode: err.response?.status ?? 0,
            durationMs: Math.round((performance.now() - t0) * 100) / 100,
            ts: new Date().toISOString(),
          })
        }
      }
      return Promise.reject(err)
    }
  )

  // 页面卸载时强制 flush (sendBeacon 路径) + 清理 flushTimer
  if (typeof window !== 'undefined') {
    // P2-7 修复 v2: 保存 handler 引用, 卸载时 removeEventListener, 避免内存泄漏
    beforeUnloadHandler = () => {
      void flush()
      // P2-7 修复: 卸载时清理 flushTimer, 防止 HMR/测试场景的幽灵定时器
      if (flushTimer !== null) {
        window.clearTimeout(flushTimer)
        flushTimer = null
      }
    }
    window.addEventListener('beforeunload', beforeUnloadHandler)
    // 定时器已通过 scheduleFlush 自启
  }
}

// P2-7 修复 v2: 模块级保存 beforeunload handler 引用, 供 uninstallPerfInterceptor 移除
let beforeUnloadHandler: (() => void) | null = null

/**
 * P2-7 修复: 卸载 perf 拦截器 (HMR/测试场景使用)
 * - 清理 flushTimer
 * - 移除 axios interceptors (需保存 id)
 * - P2-7 修复 v2: 移除 beforeunload 事件监听器
 * 注意: 生产 SPA 不需要调用, 仅开发/测试用
 */
export function uninstallPerfInterceptor() {
  if (flushTimer !== null) {
    window.clearTimeout(flushTimer)
    flushTimer = null
  }
  // P2-7 修复 v2: 移除 beforeunload 监听器, 避免内存泄漏
  if (beforeUnloadHandler !== null && typeof window !== 'undefined') {
    window.removeEventListener('beforeunload', beforeUnloadHandler)
    beforeUnloadHandler = null
  }
  if (installed && interceptorIds.request !== -1 && interceptorIds.response !== -1) {
    http.interceptors.request.eject(interceptorIds.request)
    http.interceptors.response.eject(interceptorIds.response)
    interceptorIds = { request: -1, response: -1 }
  }
  installed = false
}
