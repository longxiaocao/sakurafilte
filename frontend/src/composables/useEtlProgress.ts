// useEtlProgress — ETL 实时进度与审计 Composable
// WHY 抽取: AdminEtlView.vue 重构 P1 阶段 (2026-07-06)
//   拆出 SSE 连接 + 审计刷新 + 最近完成持久化等横切关注点,
//   让视图层只关心"组合卡片 + 表单交互"。
//
// 职责:
//   1. SSE 实时订阅: /api/admin/etl/progress/stream
//   2. 任务进入终态时拉 legacyStatus 拿最终结果
//   3. reason_code 饼图 + 最近 20 条 cancelled 历史
//   4. 最近完成结果持久化到 localStorage (刷新不丢)
//   5. 检测 paused 任务 (用于显示"恢复"按钮)
//
// 复用:
//   - 监听器 onMounted 注册, onBeforeUnmount 关闭 fetch 流
//   - 网络错误指数退避重连 (1s→2s→4s→8s→16s→30s 封顶)
//
// V24-F78 (2026-07-18): 用 fetch + ReadableStream 替代 EventSource
//   WHY 改造: 浏览器 EventSource API 不支持自定义 Header, 无法携带 JWT Authorization,
//   导致 /api/admin/etl/progress/stream 返回 401, AdminEtlView 进度无法实时推送 (spec 26.16.5)
//   方案: fetch 携带 Authorization Bearer + ReadableStream 流式解析 SSE 格式
//   保留: 指数退避自动重连 (替代 EventSource 浏览器内建重连)
import { ref, onMounted, onBeforeUnmount } from 'vue'
import { etlApi } from '@/api'
import { buildAuthHeaders } from '@/utils/http'
import { useAdminAuthStore } from '@/composables/useAdminAuth'
import type { EtlActiveTaskInfo, EtlProgress, EtlHistoryItem, EtlReasonCodeAggregate } from '@/api/types'

// localStorage key: 持久化最近一次完成结果
const LS_KEY_FINISHED = 'sakura_etl_last_finished'

// SSE 端点 (后端 AdminEtlEndpoints.cs L208, 在 group 外直接挂载)
const SSE_ENDPOINT = '/api/admin/etl/progress/stream'

// 指数退避重连参数 (ms): 1s → 2s → 4s → 8s → 16s → 30s 封顶
const RECONNECT_BASE_MS = 1000
const RECONNECT_MAX_MS = 30_000

// V24-F78: 导出 SSE chunk 解析纯函数 (供单元测试, 不依赖 Vue 运行时)
//   输入: text (本次 chunk 文本), lineBuffer (跨 chunk 行缓冲, 引用对象)
//   输出: 解析出的 EtlActiveTaskInfo[] (调用方自行处理回调)
//   副作用: 更新 lineBuffer.buf (保留最后不完整行)
//   SSE 格式: "data: {json}\n\n" (业务消息) 或 ": keepalive\n\n" (注释心跳, 忽略)
export function parseSseChunk<T = EtlActiveTaskInfo>(
  text: string,
  lineBuffer: { buf: string }
): T[] {
  const results: T[] = []
  lineBuffer.buf += text
  // 按 \n 分行, 最后一行可能不完整 (无结尾 \n), 留到下次处理
  const lines = lineBuffer.buf.split('\n')
  lineBuffer.buf = lines.pop() ?? ''
  for (const line of lines) {
    const trimmed = line.trim()
    if (!trimmed) continue  // 空行 (SSE 消息分隔符)
    if (trimmed.startsWith(':')) continue  // 注释行 (keepalive)
    if (trimmed.startsWith('data:')) {
      const payload = trimmed.slice(5).trim()
      if (!payload) continue
      try {
        results.push(JSON.parse(payload) as T)
      } catch {
        // JSON 解析失败忽略 (后端格式异常, 不影响后续消息)
      }
    }
  }
  return results
}

// V24-F78: 导出指数退避延迟计算纯函数 (供单元测试)
//   attempts: 第几次重连 (从 1 开始)
//   返回: 延迟 ms (1s → 2s → 4s → 8s → 16s → 30s 封顶)
export function computeReconnectDelay(attempts: number): number {
  const safeAttempts = Math.max(1, attempts)
  return Math.min(RECONNECT_BASE_MS * Math.pow(2, safeAttempts - 1), RECONNECT_MAX_MS)
}

export function useEtlProgress() {
  // 当前活跃任务 (SSE 推送)
  const task = ref<EtlActiveTaskInfo>({ inProgress: false })
  // 最近一次完成结果 (legacy 完整字段, 含 skipped_* 细分)
  const lastFinished = ref<EtlProgress | null>(null)
  // reason_code 饼图数据
  const reasonCodeAgg = ref<EtlReasonCodeAggregate | null>(null)
  // 最近 20 条 cancelled 历史
  const historyItems = ref<EtlHistoryItem[]>([])
  // 历史加载状态
  const historyLoading = ref(false)
  // 是否有 paused 任务 (显示"恢复"按钮)
  const hasPausedTask = ref(false)

  // ===== localStorage 恢复 =====
  try {
    const cached = localStorage.getItem(LS_KEY_FINISHED)
    if (cached) lastFinished.value = JSON.parse(cached)
  } catch {
    // 解析失败忽略
  }

  // ===== 审计刷新 (饼图 + 历史) =====
  async function refreshAudit() {
    historyLoading.value = true
    try {
      const [agg, hist] = await Promise.all([
        etlApi.reasonCodeAggregate(),
        etlApi.history(20, 'cancelled')
      ])
      reasonCodeAgg.value = agg
      historyItems.value = hist.items
    } catch {
      // 拦截器已处理
    } finally {
      historyLoading.value = false
    }
  }

  // ===== paused 检测 =====
  async function checkPausedTask() {
    try {
      const hist = await etlApi.history(20, 'paused')
      hasPausedTask.value = (hist?.items?.length ?? 0) > 0
    } catch {
      hasPausedTask.value = false
    }
  }

  // ===== 清除最近完成 =====
  function clearLastFinished() {
    lastFinished.value = null
    try {
      localStorage.removeItem(LS_KEY_FINISHED)
    } catch {
      // 容量/隐私模式忽略
    }
  }

  // ===== SSE 连接管理 (V24-F78: fetch + ReadableStream 替代 EventSource) =====
  let abortController: AbortController | null = null
  let reconnectTimer: number | null = null
  let reconnectAttempts = 0
  let isUnmounted = false  // 防止 unmount 后还触发重连

  function persistLastFinished(legacy: EtlProgress) {
    lastFinished.value = legacy
    try {
      localStorage.setItem(LS_KEY_FINISHED, JSON.stringify(legacy))
    } catch {
      // 容量/隐私模式忽略
    }
  }

  function handleSsePayload(r: EtlActiveTaskInfo) {
    task.value = r
    // 任务刚结束 (inProgress=true → false) → 拉一次 legacy status 拿最终结果
    if (!r.inProgress && r.activeTask && r.activeTask.status === 'completed') {
      etlApi.legacyStatus()
        .then((legacy) => {
          if (legacy && (legacy as any).status !== 'running') {
            persistLastFinished(legacy)
          }
        })
        .catch(() => {})
    }
    // 进入终态 → 刷新审计 + paused 检查
    if (!r.inProgress && r.activeTask &&
        ['completed', 'failed', 'cancelled'].includes(r.activeTask.status)) {
      refreshAudit()
      checkPausedTask()
    }
  }

  // 解析 SSE chunk: 复用导出的 parseSseChunk 纯函数 (V24-F78 抽取, 规则 3.2 复用优先)
  function processSseChunk(text: string, lineBuffer: { buf: string }) {
    const messages = parseSseChunk(text, lineBuffer)
    for (const r of messages) {
      handleSsePayload(r)
    }
  }

  async function connectSSE() {
    if (isUnmounted) return
    if (abortController) {
      abortController.abort()
      abortController = null
    }
    if (reconnectTimer) {
      clearTimeout(reconnectTimer)
      reconnectTimer = null
    }

    const auth = useAdminAuthStore()
    // 无 token: 不连接 (后端必然 401), 由调用方决定是否跳登录
    if (!auth.token) {
      console.debug('useEtlProgress: 无 auth token, 跳过 SSE 连接')
      return
    }

    const controller = new AbortController()
    abortController = controller

    try {
      const resp = await fetch(SSE_ENDPOINT, {
        method: 'GET',
        headers: {
          ...buildAuthHeaders(),
          'Accept': 'text/event-stream',
          'Cache-Control': 'no-cache'
        },
        signal: controller.signal
      })

      if (!resp.ok) {
        // 401: token 失效, 不重连 (http.ts 拦截器会处理 refresh/跳登录)
        // 5xx: 服务端错误, 指数退避重连
        if (resp.status === 401) {
          console.debug('useEtlProgress: SSE 401, token 可能已失效, 不重连')
          return
        }
        throw new Error(`SSE HTTP ${resp.status}`)
      }

      if (!resp.body) {
        throw new Error('SSE 响应无 body stream')
      }

      // 重连成功: 重置退避计数
      reconnectAttempts = 0

      const reader = resp.body.getReader()
      const decoder = new TextDecoder('utf-8')
      const lineBuffer: { buf: string } = { buf: '' }

      // 循环读取 chunk, 直到流结束或 abort
      // 流结束 (服务端关闭) → 指数退避重连
      while (true) {
        const { done, value } = await reader.read()
        if (done) break
        const text = decoder.decode(value, { stream: true })
        processSseChunk(text, lineBuffer)
      }
      // 流正常结束: 尝试重连 (服务端可能重启)
      scheduleReconnect()
    } catch (err: any) {
      if (err?.name === 'AbortError') {
        // 主动 abort (disconnectSSE 或组件 unmount), 不重连
        return
      }
      // 网络错误 / fetch 失败: 指数退避重连
      console.debug('useEtlProgress: SSE 连接失败, 准备重连', err?.message)
      scheduleReconnect()
    }
  }

  function scheduleReconnect() {
    if (isUnmounted) return
    if (reconnectTimer) return  // 已有重连任务在等待
    reconnectAttempts++
    // V24-F78: 复用导出的纯函数 (供单元测试)
    const delay = computeReconnectDelay(reconnectAttempts)
    console.debug(`useEtlProgress: SSE 将在 ${delay}ms 后重连 (第 ${reconnectAttempts} 次)`)
    reconnectTimer = window.setTimeout(() => {
      reconnectTimer = null
      connectSSE()
    }, delay)
  }

  function disconnectSSE() {
    if (abortController) {
      abortController.abort()
      abortController = null
    }
    if (reconnectTimer) {
      clearTimeout(reconnectTimer)
      reconnectTimer = null
    }
    reconnectAttempts = 0
  }

  onMounted(() => {
    connectSSE()
    refreshAudit()
    checkPausedTask()
  })

  onBeforeUnmount(() => {
    isUnmounted = true
    disconnectSSE()
  })

  return {
    // state
    task,
    lastFinished,
    reasonCodeAgg,
    historyItems,
    historyLoading,
    hasPausedTask,
    // actions
    refreshAudit,
    checkPausedTask,
    clearLastFinished,
    // 仅在外部特殊场景使用 (例如手动断线重连)
    connectSSE,
    disconnectSSE
  }
}
