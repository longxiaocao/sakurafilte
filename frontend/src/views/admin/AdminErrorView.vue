<script setup lang="ts">
// 批次 6c: 错误日志管理页
//   - 展示前端错误监控捕获的事件 (localStorage 200 条)
//   - 支持搜索/筛选/排序/复制/导出/清空
//   - 一键复制事件详情, 便于贴到 issue/工单
//   - 手动触发测试错误, 验证监控链路
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import {
  getEvents, clearEvents, exportEvents,
  captureException, captureMessage,
  type ErrorEvent
} from '@/utils/errorMonitor'

const { t } = useI18n()

const events = ref<ErrorEvent[]>([])
const filter = ref<'all' | 'error' | 'warning' | 'info' | 'fatal' | 'debug'>('all')
const search = ref('')
const selected = ref<ErrorEvent | null>(null)
const autoRefreshSec = ref(0)
let timer: number | null = null

function refresh() {
  events.value = getEvents().slice().reverse()  // 最新在前
}

const filtered = computed(() => {
  let list = events.value
  if (filter.value !== 'all') {
    list = list.filter((e) => e.level === filter.value)
  }
  if (search.value.trim()) {
    const q = search.value.toLowerCase()
    list = list.filter((e) =>
      e.message.toLowerCase().includes(q) ||
      e.exception?.type.toLowerCase().includes(q) ||
      Object.values(e.tags).some((v) => v.toLowerCase().includes(q))
    )
  }
  return list
})

const stats = computed(() => {
  const all = events.value
  return {
    total: all.length,
    error: all.filter((e) => e.level === 'error').length,
    warning: all.filter((e) => e.level === 'warning').length,
    info: all.filter((e) => e.level === 'info').length,
    fatal: all.filter((e) => e.level === 'fatal').length,
  }
})

function formatTime(ts: number): string {
  return new Date(ts).toLocaleString('zh-CN', { hour12: false })
}

function levelColor(level: string): string {
  return {
    fatal: 'text-red-700 font-medium',
    error: 'text-red-600',
    warning: 'text-yellow-600',
    info: 'text-blue-600',
    debug: 'text-neutral-500',
  }[level] || 'text-neutral-700'
}

async function copyEvent(e: ErrorEvent) {
  const text = JSON.stringify(e, null, 2)
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success(t('common.feedback.success_014'))
  } catch {
    ElMessage.error(t('common.feedback.error_009'))
  }
}

function exportJson() {
  const data = exportEvents()
  const blob = new Blob([data], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `sakurafilter-errors-${new Date().toISOString().slice(0, 19).replace(/[T:]/g, '-')}.json`
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

async function clearAll() {
  try {
    await ElMessageBox.confirm('确认清空所有错误日志? 此操作不可恢复。', '清空确认', {
      type: 'warning',
      confirmButtonText: '确认清空',
      cancelButtonText: '取消',
    })
  } catch { return }
  clearEvents()
  refresh()
  ElMessage.success(t('common.feedback.success_015'))
}

function triggerTestError() {
  try {
    throw new Error('[TEST] Manual test error from admin page')
  } catch (e) {
    captureException(e, {
      tags: { source: 'admin-test', kind: 'manual' },
      extra: { triggeredAt: new Date().toISOString() },
    })
  }
  ElMessage.success(t('common.feedback.error_018'))
  setTimeout(refresh, 100)
}

function triggerTestPromise() {
  // 未处理的 Promise 拒绝, 验证 unhandledrejection 捕获
  Promise.reject(new Error('[TEST] Unhandled promise rejection'))
  setTimeout(refresh, 100)
}

function triggerTestMessage() {
  captureMessage('[TEST] Manual info message from admin page', {
    level: 'info',
    tags: { source: 'admin-test', kind: 'message' },
  })
  setTimeout(refresh, 100)
}

function changeAutoRefresh(sec: number) {
  autoRefreshSec.value = sec
  if (timer) { clearInterval(timer); timer = null }
  if (sec > 0) {
    timer = window.setInterval(refresh, sec * 1000)
  }
}

onMounted(() => {
  refresh()
})
</script>

<template>
  <div class="p-3 max-w-screen-2xl mx-auto">
    <div class="flex items-center justify-between mb-3 flex-wrap gap-2">
      <div>
        <h1 class="text-lg font-medium">错误日志</h1>
        <p class="text-xs text-muted">
          批次 6c — 前端错误监控 (离线优先, localStorage 持久化, 最多 200 条)
        </p>
      </div>
      <div class="flex items-center gap-2 flex-wrap">
        <button
          @click="triggerTestError"
          class="px-2 py-1 text-xs hairline hover:bg-[var(--color-bg-hover)]"
          :aria-label="t('admin.errorview.aria.trigger_test_error', '触发测试错误')"
        >触发测试错误</button>
        <button
          @click="triggerTestPromise"
          class="px-2 py-1 text-xs hairline hover:bg-[var(--color-bg-hover)]"
        >触发 Promise 拒绝</button>
        <button
          @click="triggerTestMessage"
          class="px-2 py-1 text-xs hairline hover:bg-[var(--color-bg-hover)]"
        >触发消息</button>
        <button
          @click="refresh"
          class="px-2 py-1 text-xs hairline hover:bg-[var(--color-bg-hover)]"
        >↻ 刷新</button>
        <button
          @click="exportJson"
          class="px-2 py-1 text-xs hairline hover:bg-[var(--color-bg-hover)]"
          :disabled="events.length === 0"
        >导出 JSON</button>
        <button
          @click="clearAll"
          class="px-2 py-1 text-xs hairline text-red-600 hover:bg-red-50 dark:hover:bg-red-950/20"
          :disabled="events.length === 0"
        >清空</button>
        <select
          :value="autoRefreshSec"
          @change="changeAutoRefresh(($event.target as HTMLSelectElement).value as unknown as number)"
          class="px-2 py-1 text-xs hairline bg-[var(--color-bg-elevated)]"
          aria-label="自动刷新间隔"
        >
          <option :value="0">手动</option>
          <option :value="3">3s</option>
          <option :value="10">10s</option>
          <option :value="30">30s</option>
        </select>
      </div>
    </div>

    <!-- 统计卡片 -->
    <div class="grid grid-cols-2 md:grid-cols-5 gap-2 mb-3">
      <div class="hairline p-2">
        <div class="text-xs text-muted">总计</div>
        <div class="text-lg font-medium">{{ stats.total }}</div>
      </div>
      <div class="hairline p-2">
        <div class="text-xs text-muted">Fatal</div>
        <div class="text-lg font-medium text-red-700">{{ stats.fatal }}</div>
      </div>
      <div class="hairline p-2">
        <div class="text-xs text-muted">Error</div>
        <div class="text-lg font-medium text-red-600">{{ stats.error }}</div>
      </div>
      <div class="hairline p-2">
        <div class="text-xs text-muted">Warning</div>
        <div class="text-lg font-medium text-yellow-600">{{ stats.warning }}</div>
      </div>
      <div class="hairline p-2">
        <div class="text-xs text-muted">Info</div>
        <div class="text-lg font-medium text-blue-600">{{ stats.info }}</div>
      </div>
    </div>

    <!-- 过滤 -->
    <div class="flex items-center gap-2 mb-3 flex-wrap">
      <div class="flex items-center gap-1">
        <label class="text-xs text-muted">级别:</label>
        <select
          v-model="filter"
          class="px-2 py-1 text-xs hairline bg-[var(--color-bg-elevated)]"
          aria-label="按级别筛选"
        >
          <option value="all">全部</option>
          <option value="fatal">Fatal</option>
          <option value="error">Error</option>
          <option value="warning">Warning</option>
          <option value="info">Info</option>
          <option value="debug">Debug</option>
        </select>
      </div>
      <input
        v-model="search"
        type="text"
        placeholder="搜索 message / type / tags…"
        class="px-2 py-1 text-xs hairline bg-[var(--color-bg-elevated)] flex-1 min-w-[200px]"
        aria-label="搜索错误"
      />
      <span class="text-xs text-muted">显示 {{ filtered.length }} / {{ events.length }}</span>
    </div>

    <!-- 列表 -->
    <div class="grid grid-cols-1 lg:grid-cols-2 gap-3">
      <!-- 左: 列表 -->
      <div class="hairline">
        <div v-if="filtered.length === 0" class="p-4 text-center text-sm text-muted">
          暂无错误日志
        </div>
        <ul v-else class="divide-y divide-[var(--color-border)]">
          <li
            v-for="e in filtered"
            :key="e.id"
            @click="selected = e"
            :class="[
              'p-2 cursor-pointer hover:bg-[var(--color-bg-hover)]',
              selected?.id === e.id ? 'bg-[var(--color-bg-hover)]' : ''
            ]"
          >
            <div class="flex items-center gap-2 mb-1">
              <span :class="['text-xs uppercase', levelColor(e.level)]">{{ e.level }}</span>
              <span class="text-xs text-muted">{{ formatTime(e.timestamp) }}</span>
              <span v-for="(v, k) in e.tags" :key="k" class="text-[10px] px-1 py-0.5 hairline">
                {{ k }}={{ v }}
              </span>
            </div>
            <div class="text-sm truncate" :title="e.message">{{ e.message }}</div>
            <div class="text-xs text-muted truncate">{{ e.exception?.type || 'Message' }}</div>
          </li>
        </ul>
      </div>

      <!-- 右: 详情 -->
      <div class="hairline p-3 sticky top-3 self-start max-h-[80vh] overflow-auto">
        <div v-if="!selected" class="text-center text-sm text-muted py-8">
          ← 选择左侧事件查看详情
        </div>
        <div v-else>
          <div class="flex items-center justify-between mb-2">
            <span :class="['text-sm uppercase font-medium', levelColor(selected.level)]">
              {{ selected.level }}
            </span>
            <button
              @click="copyEvent(selected)"
              class="px-2 py-1 text-xs hairline hover:bg-[var(--color-bg-hover)]"
            >复制 JSON</button>
          </div>
          <dl class="text-xs space-y-1 mb-3">
            <div><dt class="inline text-muted">时间: </dt><dd class="inline">{{ formatTime(selected.timestamp) }}</dd></div>
            <div><dt class="inline text-muted">类型: </dt><dd class="inline">{{ selected.exception?.type || '—' }}</dd></div>
            <div><dt class="inline text-muted">URL: </dt><dd class="inline break-all">{{ selected.url }}</dd></div>
            <div v-if="Object.keys(selected.tags).length">
              <dt class="inline text-muted">Tags: </dt>
              <dd class="inline">
                <span v-for="(v, k) in selected.tags" :key="k" class="mr-2">{{ k }}={{ v }}</span>
              </dd>
            </div>
          </dl>
          <div class="mb-2">
            <div class="text-xs text-muted mb-1">Message</div>
            <div class="text-sm p-2 hairline break-all">{{ selected.message }}</div>
          </div>
          <div v-if="selected.exception?.stacktrace" class="mb-2">
            <div class="text-xs text-muted mb-1">Stack Trace</div>
            <pre class="text-[10px] p-2 hairline overflow-auto max-h-40 whitespace-pre-wrap break-all">{{ selected.exception.stacktrace }}</pre>
          </div>
          <div v-if="Object.keys(selected.extra).length" class="mb-2">
            <div class="text-xs text-muted mb-1">Extra</div>
            <pre class="text-[10px] p-2 hairline overflow-auto max-h-32 whitespace-pre-wrap break-all">{{ JSON.stringify(selected.extra, null, 2) }}</pre>
          </div>
          <div v-if="selected.breadcrumbs.length" class="mb-2">
            <div class="text-xs text-muted mb-1">Breadcrumbs (最近 {{ selected.breadcrumbs.length }} 条)</div>
            <ul class="text-[10px] space-y-0.5">
              <!-- V24-F86 (P2-1): 复合 key 防同 timestamp 重复场景下 key 冲突 -->
              <li v-for="(b, i) in selected.breadcrumbs" :key="`${b.timestamp}-${i}`" class="flex gap-2">
                <span class="text-muted shrink-0">{{ formatTime(b.timestamp).slice(-8) }}</span>
                <span class="px-1 hairline shrink-0">{{ b.type || 'default' }}</span>
                <span class="break-all">{{ b.message }}</span>
              </li>
            </ul>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
