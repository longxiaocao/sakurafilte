<script setup lang="ts">
// P5.5+: 后端性能监控面板
//   - 实时展示 P50/P95/P99/ErrorRate/SampleCount (5s 自动刷新)
//   - 健康探针状态 (Liveness + Readiness)
//   - Token 轮转状态 (current/previous/rotatedAt)
//   - Musk 风格: 纯黑白 + 单强调色, 无阴影, 1px hairline, 8px 网格
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { http } from '@/utils/http'

interface PerfSnapshot {
  sampleCount: number
  totalRequests: number
  errorRequests: number
  errorRate: number
  p50Ms: number
  p95Ms: number
  p99Ms: number
  maxMs: number
  generatedAt: string
}

interface AuthStatus {
  currentLen: number
  currentPrefix: string
  previousLen: number
  hasPrevious: boolean
  lastRotatedAt: string | null
  lastRotatedBy: string | null
  loadedFromDb: boolean
}

const perf = ref<PerfSnapshot | null>(null)
const auth = ref<AuthStatus | null>(null)
const liveOk = ref<boolean | null>(null)
const readyOk = ref<boolean | null>(null)
const readyDegraded = ref(false)
const loading = ref(false)
const error = ref<string | null>(null)
const autoRefresh = ref(true)
const refreshSec = ref(5)
let timer: number | null = null

async function fetchPerf() {
  try {
    const r = await http.get<PerfSnapshot>('/perf')
    perf.value = r.data
  } catch {
    // 拦截器处理
  }
}

async function fetchAuth() {
  try {
    const r = await http.get<AuthStatus>('/admin/auth/status')
    auth.value = r.data
  } catch {
    // 401 等已处理
  }
}

async function fetchHealth() {
  try {
    const r = await fetch('/health/live')
    liveOk.value = r.ok
  } catch {
    liveOk.value = false
  }
  try {
    const r = await fetch('/health/ready')
    readyOk.value = r.ok
    readyDegraded.value = r.status === 503
  } catch {
    readyOk.value = false
    readyDegraded.value = false
  }
}

async function refreshAll() {
  loading.value = true
  error.value = null
  try {
    await Promise.all([fetchPerf(), fetchAuth(), fetchHealth()])
  } catch (e: any) {
    error.value = e?.message || '刷新失败'
  } finally {
    loading.value = false
  }
}

function startTimer() {
  stopTimer()
  if (autoRefresh.value) {
    timer = window.setInterval(refreshAll, refreshSec.value * 1000)
  }
}

function stopTimer() {
  if (timer !== null) {
    clearInterval(timer)
    timer = null
  }
}

function toggleAutoRefresh() {
  autoRefresh.value = !autoRefresh.value
  if (autoRefresh.value) startTimer()
  else stopTimer()
}

function changeInterval(sec: number) {
  refreshSec.value = sec
  if (autoRefresh.value) startTimer()
}

onMounted(() => {
  refreshAll()
  startTimer()
})

onBeforeUnmount(() => {
  stopTimer()
})

// P95 颜色分级: <100ms 绿, <500ms 黄, >=500ms 红
const p95Color = computed(() => {
  const v = perf.value?.p95Ms ?? 0
  if (v === 0) return 'text-neutral-500'
  if (v < 100) return 'text-green-600'
  if (v < 500) return 'text-yellow-600'
  return 'text-red-600'
})

const errorColor = computed(() => {
  const v = perf.value?.errorRate ?? 0
  if (v === 0) return 'text-green-600'
  if (v < 1) return 'text-yellow-600'
  return 'text-red-600'
})

// P5.5+: 告警计算 — P95>500ms 或 ErrorRate>5% 触发
//   WHY 纯前端计算: 后端 /api/perf 已提供原始指标, 告警逻辑无状态, 前端算即可
//   WHY 阈值 500ms/5%: 与 P5.5 中间件颜色分级一致, 运维直观
const alerts = computed<{ level: 'warning' | 'critical'; msg: string }[]>(() => {
  const list: { level: 'warning' | 'critical'; msg: string }[] = []
  const p = perf.value
  if (!p) return list
  if (p.p95Ms >= 1000) {
    list.push({ level: 'critical', msg: `P95 = ${p.p95Ms.toFixed(0)}ms (≥1000ms 严重)` })
  } else if (p.p95Ms >= 500) {
    list.push({ level: 'warning', msg: `P95 = ${p.p95Ms.toFixed(0)}ms (≥500ms 警告)` })
  }
  if (p.errorRate >= 10) {
    list.push({ level: 'critical', msg: `错误率 = ${p.errorRate.toFixed(1)}% (≥10% 严重)` })
  } else if (p.errorRate >= 5) {
    list.push({ level: 'warning', msg: `错误率 = ${p.errorRate.toFixed(1)}% (≥5% 警告)` })
  }
  return list
})

const hasAlert = computed(() => alerts.value.length > 0)
const hasCritical = computed(() => alerts.value.some(a => a.level === 'critical'))

const readyText = computed(() => {
  if (readyOk.value === null) return '检测中'
  if (readyOk.value) return '就绪'
  if (readyDegraded.value) return '降级'
  return '故障'
})

const readyColor = computed(() => {
  if (readyOk.value === null) return 'text-neutral-500'
  if (readyOk.value) return 'text-green-600'
  if (readyDegraded.value) return 'text-yellow-600'
  return 'text-red-600'
})

// 格式化时间
function fmtTime(ts: string | null): string {
  if (!ts) return '—'
  try {
    return new Date(ts).toLocaleString('zh-CN', { hour12: false })
  } catch {
    return ts
  }
}
</script>

<template>
  <div class="p-3 max-w-screen-xl mx-auto">
    <div class="flex items-center justify-between mb-3">
      <div>
        <h1 class="text-lg font-medium">性能监控</h1>
        <p class="text-xs text-muted">
          P5.5 后端性能埋点 — P50/P95/P99 + 错误率 + 健康探针 + Token 轮转状态
        </p>
      </div>
      <div class="flex items-center gap-2">
        <button
          @click="toggleAutoRefresh"
          class="px-2 py-1 text-sm hairline hover:bg-neutral-100"
          :aria-label="autoRefresh ? '暂停自动刷新' : '开启自动刷新'"
        >
          {{ autoRefresh ? '⏸ 暂停' : '▶ 自动' }}
        </button>
        <select
          v-model="refreshSec"
          @change="changeInterval(refreshSec)"
          class="px-2 py-1 text-sm hairline bg-white dark:bg-neutral-900"
          aria-label="刷新间隔"
        >
          <option :value="3">3s</option>
          <option :value="5">5s</option>
          <option :value="10">10s</option>
          <option :value="30">30s</option>
        </select>
        <button
          @click="refreshAll"
          :disabled="loading"
          class="px-3 py-1 text-sm hairline hover:bg-neutral-100 disabled:opacity-50"
        >
          {{ loading ? '刷新中…' : '↻ 刷新' }}
        </button>
      </div>
    </div>

    <p v-if="error" class="text-xs text-red-600 mb-2">{{ error }}</p>

    <!-- P5.5+: 告警条 — P95≥500ms 或 ErrorRate≥5% 时显示 -->
    <div
      v-if="hasAlert"
      :class="[
        'hairline p-3 mb-3',
        hasCritical ? 'bg-red-50 dark:bg-red-950/20' : 'bg-yellow-50 dark:bg-yellow-950/20'
      ]"
      role="alert"
      aria-live="assertive"
    >
      <div class="flex items-center gap-2 mb-1">
        <span class="text-base font-medium" :class="hasCritical ? 'text-red-700' : 'text-yellow-700'">
          {{ hasCritical ? '⚠ 严重告警' : '⚠ 警告' }}
        </span>
      </div>
      <ul class="text-xs space-y-1">
        <li
          v-for="(a, i) in alerts"
          :key="i"
          :class="a.level === 'critical' ? 'text-red-700' : 'text-yellow-700'"
        >
          • {{ a.msg }}
        </li>
      </ul>
    </div>

    <!-- 性能指标卡片 -->
    <section class="hairline p-4 mb-3">
      <h2 class="text-base font-medium mb-3">响应时间 (最近 {{ perf?.sampleCount ?? 0 }} 条样本)</h2>
      <div v-if="perf" class="grid grid-cols-2 md:grid-cols-4 gap-3">
        <div class="hairline p-3">
          <div class="text-xs text-muted mb-1">P50 (中位数)</div>
          <div class="text-xl font-medium">{{ perf.p50Ms.toFixed(1) }}<span class="text-xs ml-1">ms</span></div>
        </div>
        <div class="hairline p-3">
          <div class="text-xs text-muted mb-1">P95</div>
          <div class="text-xl font-medium" :class="p95Color">{{ perf.p95Ms.toFixed(1) }}<span class="text-xs ml-1">ms</span></div>
        </div>
        <div class="hairline p-3">
          <div class="text-xs text-muted mb-1">P99</div>
          <div class="text-xl font-medium">{{ perf.p99Ms.toFixed(1) }}<span class="text-xs ml-1">ms</span></div>
        </div>
        <div class="hairline p-3">
          <div class="text-xs text-muted mb-1">Max</div>
          <div class="text-xl font-medium">{{ perf.maxMs.toFixed(1) }}<span class="text-xs ml-1">ms</span></div>
        </div>
      </div>
      <div v-else class="text-sm text-muted py-4 text-center">暂无数据</div>

      <div v-if="perf" class="grid grid-cols-2 md:grid-cols-3 gap-3 mt-3">
        <div class="hairline p-3">
          <div class="text-xs text-muted mb-1">总请求数</div>
          <div class="text-base font-medium">{{ perf.totalRequests.toLocaleString() }}</div>
        </div>
        <div class="hairline p-3">
          <div class="text-xs text-muted mb-1">错误请求</div>
          <div class="text-base font-medium">{{ perf.errorRequests.toLocaleString() }}</div>
        </div>
        <div class="hairline p-3">
          <div class="text-xs text-muted mb-1">错误率</div>
          <div class="text-base font-medium" :class="errorColor">{{ perf.errorRate.toFixed(2) }}%</div>
        </div>
      </div>
      <p v-if="perf" class="text-xs text-muted mt-2">
        采样时间: {{ fmtTime(perf.generatedAt) }}
      </p>
    </section>

    <!-- 健康探针 -->
    <section class="hairline p-4 mb-3">
      <h2 class="text-base font-medium mb-3">健康探针</h2>
      <div class="grid grid-cols-2 gap-3">
        <div class="hairline p-3">
          <div class="text-xs text-muted mb-1">Liveness (/health/live)</div>
          <div class="text-base font-medium" :class="liveOk === null ? 'text-neutral-500' : liveOk ? 'text-green-600' : 'text-red-600'">
            {{ liveOk === null ? '检测中' : liveOk ? '存活' : '故障' }}
          </div>
        </div>
        <div class="hairline p-3">
          <div class="text-xs text-muted mb-1">Readiness (/health/ready)</div>
          <div class="text-base font-medium" :class="readyColor">{{ readyText }}</div>
        </div>
      </div>
    </section>

    <!-- Token 轮转状态 -->
    <section class="hairline p-4 mb-3">
      <h2 class="text-base font-medium mb-3">X-Admin-Token 轮转状态</h2>
      <div v-if="auth" class="grid grid-cols-2 md:grid-cols-3 gap-3">
        <div class="hairline p-3">
          <div class="text-xs text-muted mb-1">当前 Token</div>
          <div class="text-base font-medium font-mono">
            {{ auth.currentPrefix }}…({{ auth.currentLen }} 字)
          </div>
        </div>
        <div class="hairline p-3">
          <div class="text-xs text-muted mb-1">Previous Key</div>
          <div class="text-base font-medium">
            <span v-if="auth.hasPrevious" class="text-yellow-600">
              {{ auth.previousLen }} 字 (过渡期)
            </span>
            <span v-else class="text-green-600">无 (稳定期)</span>
          </div>
        </div>
        <div class="hairline p-3">
          <div class="text-xs text-muted mb-1">数据来源</div>
          <div class="text-base font-medium">
            <span :class="auth.loadedFromDb ? 'text-green-600' : 'text-yellow-600'">
              {{ auth.loadedFromDb ? 'DB (已加载)' : 'appsettings.json (兜底)' }}
            </span>
          </div>
        </div>
        <div class="hairline p-3">
          <div class="text-xs text-muted mb-1">上次轮转时间</div>
          <div class="text-sm font-medium">{{ fmtTime(auth.lastRotatedAt) }}</div>
        </div>
        <div class="hairline p-3">
          <div class="text-xs text-muted mb-1">操作人</div>
          <div class="text-sm font-medium">{{ auth.lastRotatedBy || '—' }}</div>
        </div>
      </div>
      <div v-else class="text-sm text-muted py-4 text-center">
        无法获取 Token 状态 (需鉴权)
      </div>
    </section>

    <p class="text-xs text-muted text-center">
      💡 指标来自后端 PerfMetrics ring buffer (最近 1000 条请求), 每 {{ refreshSec }}s 刷新
    </p>
  </div>
</template>
