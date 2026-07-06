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
//   - 监听器 onMounted 注册, onBeforeUnmount 关闭 EventSource
//   - SSE 断线不报错 (浏览器自动重连)
import { ref, onMounted, onBeforeUnmount } from 'vue'
import { etlApi } from '@/api'
import type { EtlActiveTaskInfo, EtlProgress, EtlHistoryItem, EtlReasonCodeAggregate } from '@/api/types'

// localStorage key: 持久化最近一次完成结果
const LS_KEY_FINISHED = 'sakura_etl_last_finished'

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

  // ===== SSE 连接管理 =====
  let eventSource: EventSource | null = null
  let pollTimer: number | null = null  // 兼容旧的轮询定时器清理

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

  function connectSSE() {
    if (eventSource) eventSource.close()
    const es = new EventSource('/api/admin/etl/progress/stream')
    es.onmessage = (e) => {
      try {
        const r = JSON.parse(e.data)
        handleSsePayload(r)
      } catch {
        // 解析失败忽略
      }
    }
    es.onerror = () => {
      // 浏览器自动重连, 仅 debug 提示 (SSE 临时断开是常态)
      console.debug('SSE 临时断开, 浏览器将自动重连')
    }
    eventSource = es
  }

  function disconnectSSE() {
    if (eventSource) {
      eventSource.close()
      eventSource = null
    }
    if (pollTimer) {
      clearInterval(pollTimer)
      pollTimer = null
    }
  }

  onMounted(() => {
    connectSSE()
    refreshAudit()
    checkPausedTask()
  })

  onBeforeUnmount(() => {
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
