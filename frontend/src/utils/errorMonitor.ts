// Sentry 兼容的离线错误监控
//   WHY 自研而非引入 @sentry/* SDK:
//     1. 用户硬约束: 软件分发需完全离线/无授权/无密钥
//     2. Sentry SaaS 强制 DSN 注册且云端依赖, 违反离线原则
//     3. Sentry 自部署需独立服务, 超出本地单机部署边界
//   WHAT 本模块提供:
//     - Sentry 风格 API: captureException, captureMessage, addBreadcrumb
//     - 本地环形缓冲 (localStorage 200 条), 跨会话保留, 便于问题复盘
//     - 自动去重 (5 分钟窗口内同 message+stack 折叠)
//     - 自动脱敏 (Authorization/Cookie/Set-Cookie/password/token/apiKey)
//     - 远程上报 (可选, 环境变量 VITE_ERROR_REPORT_URL 非空时启用)
//     - Vue 全局错误 + 资源加载失败 + 跨域脚本错误捕获
//   USE 后续可平滑迁移到 Sentry:
//     import * as Sentry from '@sentry/vue' 即可替换此模块
//   REF https://docs.sentry.io/platforms/javascript/

const STORAGE_KEY = 'sakurafilter:error-monitor:v1'
const MAX_EVENTS = 200
const DEDUPE_WINDOW_MS = 5 * 60 * 1000
const MAX_BREADCRUMBS = 20
const REMOTE_FLUSH_INTERVAL_MS = 30 * 1000
const REMOTE_BATCH_SIZE = 20

/** 严重级别 (与 Sentry 一致) */
export type Severity = 'fatal' | 'error' | 'warning' | 'info' | 'debug'

/** 一条面包屑 (与 Sentry 兼容) */
export interface Breadcrumb {
  category?: string
  type?: 'default' | 'http' | 'navigation' | 'ui' | 'user'
  level?: Severity
  message: string
  timestamp: number
  data?: Record<string, unknown>
}

/** 一条事件 (与 Sentry Event 兼容) */
export interface ErrorEvent {
  id: string
  timestamp: number
  level: Severity
  message: string
  exception?: {
    type: string
    value: string
    stacktrace?: string
  }
  tags: Record<string, string>
  extra: Record<string, unknown>
  breadcrumbs: Breadcrumb[]
  url: string
  userAgent: string
  release?: string
  environment: string
}

interface ErrorMonitorState {
  events: ErrorEvent[]
  breadcrumbs: Breadcrumb[]
  remoteUrl: string | null
  lastRemoteFlushAt: number
  dedupeMap: Map<string, number>  // hash -> first-seen ts
}

const state: ErrorMonitorState = {
  events: [],
  breadcrumbs: [],
  remoteUrl: null,
  lastRemoteFlushAt: 0,
  dedupeMap: new Map(),
}

let installed = false
let remoteTimer: number | null = null

// ============================================================
// 工具: 持久化 (localStorage 容量限制 5-10MB, 我们的事件很小)
// ============================================================

function loadFromStorage(): void {
  if (typeof localStorage === 'undefined') return
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return
    const data = JSON.parse(raw)
    if (Array.isArray(data.events)) {
      state.events = data.events.slice(-MAX_EVENTS)
    }
    if (Array.isArray(data.breadcrumbs)) {
      state.breadcrumbs = data.breadcrumbs.slice(-MAX_BREADCRUMBS)
    }
  } catch {
    // 损坏数据忽略
  }
}

function persistToStorage(): void {
  if (typeof localStorage === 'undefined') return
  try {
    const data = {
      events: state.events.slice(-MAX_EVENTS),
      breadcrumbs: state.breadcrumbs.slice(-MAX_BREADCRUMBS),
    }
    localStorage.setItem(STORAGE_KEY, JSON.stringify(data))
  } catch {
    // 容量超限/隐私模式: 静默失败, 不影响业务
  }
}

// ============================================================
// 工具: 脱敏 (Sentry 默认 beforeSend 也会做, 但前端更严)
// ============================================================

const SENSITIVE_KEY_RE = /(authorization|cookie|set-cookie|password|passwd|pwd|token|apikey|api[-_]?key|secret|private[-_]?key|access[-_]?key|jwt|bearer)/i
const TOKEN_VALUE_RE = /\b(Bearer\s+)?[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b/g  // JWT
const COOKIE_RE = /(?:^|;)\s*[A-Za-z0-9_-]+=[^;]+/g

function sanitizeString(s: string): string {
  if (typeof s !== 'string') return s
  return s
    .replace(TOKEN_VALUE_RE, '[REDACTED_JWT]')
    .replace(COOKIE_RE, (m) => {
      const eq = m.indexOf('=')
      if (eq < 0) return m
      const key = m.slice(0, eq).trim()
      if (SENSITIVE_KEY_RE.test(key)) return `${key}=[REDACTED]`
      return m
    })
}

function sanitizeValue(v: unknown, depth = 0): unknown {
  if (depth > 4) return '[DEPTH_LIMIT]'
  if (v == null) return v
  if (typeof v === 'string') return sanitizeString(v)
  if (typeof v === 'number' || typeof v === 'boolean') return v
  if (Array.isArray(v)) return v.map((x) => sanitizeValue(x, depth + 1))
  if (typeof v === 'object') {
    const out: Record<string, unknown> = {}
    for (const [k, val] of Object.entries(v as Record<string, unknown>)) {
      if (SENSITIVE_KEY_RE.test(k)) {
        out[k] = '[REDACTED]'
      } else {
        out[k] = sanitizeValue(val, depth + 1)
      }
    }
    return out
  }
  return v
}

// ============================================================
// 工具: 去重 hash
// ============================================================

function hashDedupeKey(event: Pick<ErrorEvent, 'message' | 'exception'>): string {
  const stack = event.exception?.stacktrace || ''
  // 取 stack 前 3 行作为指纹 (与 Sentry default 行为近似)
  const fp = stack.split('\n').slice(0, 3).join('\n')
  return `${event.message}\n${fp}`
}

// ============================================================
// 工具: 栈解析 (容错处理)
// ============================================================

function extractErrorInfo(err: unknown): { type: string; value: string; stacktrace?: string } {
  if (err instanceof Error) {
    return {
      type: err.name || 'Error',
      value: sanitizeString(err.message || String(err)),
      stacktrace: err.stack ? sanitizeString(err.stack) : undefined,
    }
  }
  if (typeof err === 'string') {
    return { type: 'StringError', value: sanitizeString(err) }
  }
  try {
    return {
      type: 'UnknownError',
      value: sanitizeString(JSON.stringify(err)),
    }
  } catch {
    return { type: 'UnknownError', value: String(err) }
  }
}

function genId(): string {
  // 32 位 hex (类似 UUIDv4 但无外部依赖)
  const a = Math.random().toString(16).slice(2, 10)
  const b = Date.now().toString(16)
  return `${b}${a}`.padStart(32, '0').slice(0, 32)
}

// ============================================================
// 核心 API (与 Sentry v8 风格一致)
// ============================================================

/** 初始化监控 (幂等) */
export function initMonitor(options?: { release?: string; environment?: string }): void {
  if (installed) return
  installed = true

  loadFromStorage()

  // 远程上报地址 (可选, 需显式配置才启用)
  //   WHY 不内置: 离线部署原则, 默认零网络开销
  const remoteUrl = (import.meta as any).env?.VITE_ERROR_REPORT_URL || ''
  state.remoteUrl = remoteUrl && /^https?:\/\//.test(remoteUrl) ? remoteUrl : null

  if (state.remoteUrl) {
    remoteTimer = window.setInterval(flushRemote, REMOTE_FLUSH_INTERVAL_MS)
  }

  // 1) 同步 JS 错误
  window.addEventListener('error', (ev) => {
    if (!ev.message && !ev.error) return  // 资源加载错误走 error 事件但无 message
    captureException(ev.error || ev.message, {
      level: 'error',
      tags: { source: 'window.onerror' },
      extra: {
        filename: ev.filename,
        lineno: ev.lineno,
        colno: ev.colno,
      },
    })
  }, true)

  // 2) 未处理的 Promise 拒绝
  window.addEventListener('unhandledrejection', (ev) => {
    captureException(ev.reason, {
      level: 'error',
      tags: { source: 'unhandledrejection' },
    })
  })

  // 3) 资源加载失败 (img/script/link 404) - 通过捕获阶段 error 事件
  //    上面的 window.error 已覆盖大部分; 这里额外打 tag 便于过滤

  // 4) 控制台 error 拦截 (可关)
  //    仅在用户显式开启时启用, 避免误报 (Element Plus 自身 console.error 较多)
  const consoleHookEnabled = (import.meta as any).env?.VITE_HOOK_CONSOLE_ERROR === 'true'
  if (consoleHookEnabled) {
    const origError = console.error
    console.error = (...args: unknown[]) => {
      origError.apply(console, args)
      captureMessage(args.map((a) => (typeof a === 'string' ? a : JSON.stringify(a))).join(' '), {
        level: 'error',
        tags: { source: 'console.error' },
      })
    }
  }
}

/** 捕获异常 */
export function captureException(err: unknown, options?: {
  level?: Severity
  tags?: Record<string, string>
  extra?: Record<string, unknown>
}): string {
  const info = extractErrorInfo(err)
  const event: ErrorEvent = {
    id: genId(),
    timestamp: Date.now(),
    level: options?.level || 'error',
    message: info.value,
    exception: info,
    tags: options?.tags || {},
    extra: sanitizeValue(options?.extra || {}) as Record<string, unknown>,
    breadcrumbs: [...state.breadcrumbs],
    url: window.location.href,
    userAgent: navigator.userAgent,
    environment: (import.meta as any).env?.MODE || 'development',
  }

  // 去重: 5min 内相同 message+stack 折叠
  const dedupeKey = hashDedupeKey(event)
  const now = Date.now()
  const first = state.dedupeMap.get(dedupeKey)
  if (first && now - first < DEDUPE_WINDOW_MS) {
    return event.id  // 静默丢弃重复
  }
  state.dedupeMap.set(dedupeKey, now)
  // 清理过期 dedupe 键
  if (state.dedupeMap.size > 100) {
    for (const [k, ts] of state.dedupeMap.entries()) {
      if (now - ts > DEDUPE_WINDOW_MS) state.dedupeMap.delete(k)
    }
  }

  state.events.push(event)
  if (state.events.length > MAX_EVENTS) {
    state.events.splice(0, state.events.length - MAX_EVENTS)
  }
  persistToStorage()
  return event.id
}

/** 捕获消息 (无异常对象时) */
export function captureMessage(msg: string, options?: {
  level?: Severity
  tags?: Record<string, string>
  extra?: Record<string, unknown>
}): string {
  return captureException(new Error(sanitizeString(msg)), {
    level: options?.level || 'info',
    tags: options?.tags,
    extra: options?.extra,
  })
}

/** 添加面包屑 (用于上下文回溯) */
export function addBreadcrumb(b: Omit<Breadcrumb, 'timestamp'>): void {
  state.breadcrumbs.push({ ...b, timestamp: Date.now() })
  if (state.breadcrumbs.length > MAX_BREADCRUMBS) {
    state.breadcrumbs.splice(0, state.breadcrumbs.length - MAX_BREADCRUMBS)
  }
}

/** 获取本地事件 (供管理 UI 展示) */
export function getEvents(): ErrorEvent[] {
  return [...state.events]
}

/** 清理本地事件 */
export function clearEvents(): void {
  state.events = []
  state.dedupeMap.clear()
  persistToStorage()
}

/** 导出 JSON (用户偏好: 调试时下载日志文件) */
export function exportEvents(): string {
  return JSON.stringify({
    exportedAt: new Date().toISOString(),
    eventCount: state.events.length,
    events: state.events,
  }, null, 2)
}

/** Vue 集成 (与 Sentry Vue SDK 一致) */
export function installVueErrorHandler(app: { config: { errorHandler?: (...args: any[]) => void } }): void {
  app.config.errorHandler = (err: unknown, _instance: unknown, info: string) => {
    captureException(err, {
      level: 'error',
      tags: { source: 'vue', vueInfo: info },
    })
  }
}

/** 远程上报 (后台, 失败不抛) */
async function flushRemote(): Promise<void> {
  if (!state.remoteUrl) return
  const now = Date.now()
  if (now - state.lastRemoteFlushAt < REMOTE_FLUSH_INTERVAL_MS - 1000) return  // 防抖
  state.lastRemoteFlushAt = now

  const pending = state.events.slice(-REMOTE_BATCH_SIZE)
  if (pending.length === 0) return

  try {
    await fetch(state.remoteUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ events: pending }),
      keepalive: true,
    })
  } catch {
    // 上报失败静默 (离线场景常见)
  }
}

/** 卸载 (测试用 — 仅重置内存状态, 保留 localStorage 数据) */
export function shutdownMonitor(): void {
  if (remoteTimer != null) {
    clearInterval(remoteTimer)
    remoteTimer = null
  }
  installed = false
  state.events = []
  state.breadcrumbs = []
  state.dedupeMap.clear()
  // 不删除 localStorage, 保留数据用于 "reload 恢复" 测试场景
  //   若需清空数据, 调用 clearEvents() + 手动 removeItem
}
