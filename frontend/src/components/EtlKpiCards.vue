<script setup lang="ts">
// EtlKpiCards — 4 张 KPI 概览卡片 (Musk 风格)
// WHY 新增 (P1 重构 2026-07-06):
//   原 AdminEtlView 无总览入口, 用户进入页面只能看到当前任务或最近完成,
//   缺乏"今天整体表现"的快速一览。KPI 卡片弥补这一缺口。
//
// 4 张卡片:
//   1. 24h 触发总数 (含 completed/failed/cancelled)
//   2. 24h 成功数
//   3. 24h 失败数
//   4. 24h 平均耗时 (仅 completed)
//
// 数据来源: /api/admin/etl/history?limit=200 客户端聚合
//   WHY 客户端聚合: 不新增后端端点, 复用现有 history API,
//   200 条足以覆盖 24h (单日 ETL 触发远小于 200)
//
// 风格:
//   - 1px hairline border, 0 shadow
//   - 大数字 32px + 标签 12px
//   - 24h 趋势点 (成功绿/失败红/中性灰)
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { etlApi } from '@/api'
import type { EtlHistoryItem } from '@/api/types'

const { t } = useI18n()

const history = ref<EtlHistoryItem[]>([])
const loading = ref(false)
let timer: number | null = null

// 24h 窗口起点
const WINDOW_MS = 24 * 60 * 60 * 1000
const cutoff = computed(() => Date.now() - WINDOW_MS)

const recent24h = computed(() => {
  return history.value.filter((h) => {
    const ts = new Date(h.finishedAt || h.startedAt).getTime()
    return ts >= cutoff.value
  })
})

const total = computed(() => recent24h.value.length)
const success = computed(() => recent24h.value.filter((h) => h.status === 'completed').length)
const failed = computed(() => recent24h.value.filter((h) => h.status === 'failed').length)

const avgDuration = computed(() => {
  const completed = recent24h.value.filter((h) => h.status === 'completed' && h.durationSec > 0)
  if (completed.length === 0) return 0
  const sum = completed.reduce((s, h) => s + h.durationSec, 0)
  return sum / completed.length
})

const successRate = computed(() => {
  if (total.value === 0) return 0
  return Math.round((success.value / total.value) * 100)
})

async function fetchData() {
  loading.value = true
  try {
    const r = await etlApi.history(200)
    history.value = r.items
  } catch {
    // 拦截器处理
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  fetchData()
  // 30s 自动刷新 (比 5s 性能/网络开销更友好, KPI 不需要秒级)
  timer = window.setInterval(fetchData, 30000)
})

onBeforeUnmount(() => {
  if (timer) {
    clearInterval(timer)
    timer = null
  }
})

function fmtDuration(sec: number): string {
  if (sec <= 0) return '—'
  if (sec < 60) return `${sec.toFixed(0)}s`
  const m = Math.floor(sec / 60)
  const s = Math.floor(sec % 60)
  return `${m}m${s}s`
}
</script>

<template>
  <div class="kpi-grid">
    <div class="kpi-card">
      <div class="kpi-label">{{ t('admin.etlview.kpi.trigger_24h') }}</div>
      <div class="kpi-value">{{ total.toLocaleString() }}</div>
      <div class="kpi-foot">
        <span class="kpi-foot-dot dot-neutral" />
        <span class="kpi-foot-text">{{ t('admin.etlview.kpi.last_24h') }}</span>
      </div>
    </div>

    <div class="kpi-card">
      <div class="kpi-label">{{ t('admin.etlview.kpi.success_24h') }}</div>
      <div class="kpi-value kpi-value-success">{{ success.toLocaleString() }}</div>
      <div class="kpi-foot">
        <span class="kpi-foot-dot dot-success" />
        <span class="kpi-foot-text">{{ t('admin.etlview.kpi.success_rate', { rate: successRate }) }}</span>
      </div>
    </div>

    <div class="kpi-card">
      <div class="kpi-label">{{ t('admin.etlview.kpi.failed_24h') }}</div>
      <div class="kpi-value kpi-value-danger">{{ failed.toLocaleString() }}</div>
      <div class="kpi-foot">
        <span class="kpi-foot-dot" :class="failed > 0 ? 'dot-danger' : 'dot-neutral'" />
        <span class="kpi-foot-text">
          {{ failed > 0 ? t('admin.etlview.kpi.need_attention') : t('admin.etlview.kpi.all_ok') }}
        </span>
      </div>
    </div>

    <div class="kpi-card">
      <div class="kpi-label">{{ t('admin.etlview.kpi.avg_duration') }}</div>
      <div class="kpi-value">{{ fmtDuration(avgDuration) }}</div>
      <div class="kpi-foot">
        <span class="kpi-foot-dot dot-neutral" />
        <span class="kpi-foot-text">{{ t('admin.etlview.kpi.completed_only') }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.kpi-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}
.kpi-card {
  /* Musk 风格: 1px hairline + 0 shadow */
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
  padding: 16px 20px;
  background: var(--el-bg-color);
  transition: border-color 0.15s;
}
.kpi-card:hover { border-color: var(--el-color-primary-light-5); }
.kpi-label {
  font-size: 12px;
  color: var(--color-text-muted);
  margin-bottom: 8px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
.kpi-value {
  font-size: 32px;
  font-weight: 600;
  line-height: 1.1;
  font-variant-numeric: tabular-nums;
  color: var(--el-text-color-primary);
}
.kpi-value-success { color: var(--el-color-success); }
.kpi-value-danger { color: var(--el-color-danger); }
.kpi-foot {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 8px;
  font-size: 11px;
  color: var(--color-text-muted);
}
.kpi-foot-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  display: inline-block;
}
.dot-success { background: var(--el-color-success); }
.dot-danger { background: var(--el-color-danger); }
.dot-neutral { background: var(--el-border-color-darker); }
@media (max-width: 1024px) {
  .kpi-grid { grid-template-columns: repeat(2, 1fr); }
}
@media (max-width: 600px) {
  .kpi-grid { grid-template-columns: 1fr; }
  .kpi-value { font-size: 28px; }
}
</style>
