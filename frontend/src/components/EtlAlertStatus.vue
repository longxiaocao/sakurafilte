<script setup lang="ts">
// EtlAlertStatus — ETL 页面内告警状态卡片 (P2-1)
// WHY 重构: 之前 P1 阶段是占位卡, 现在接入真实告警系统
//   - 展示全局开关 + 7 日 KPI
//   - 跳转到 /admin/alerts 查看历史与规则
//   - 显示最近一次告警 (失败/成功/抑制)
// 复用 alertsApi (P2-1 新增)
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { alertsApi } from '@/api'
import type { AlertStats, AlertHistoryItem } from '@/api/types'

const { t } = useI18n()
const router = useRouter()

const stats = ref<AlertStats | null>(null)
const latest = ref<AlertHistoryItem | null>(null)
const loading = ref(false)
let timer: number | null = null

async function fetchData() {
  loading.value = true
  try {
    const [s, h] = await Promise.all([
      alertsApi.stats(),
      alertsApi.history({ limit: 1, offset: 0 })
    ])
    stats.value = s
    latest.value = h.items[0] ?? null
  } catch {
    // 拦截器处理
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  fetchData()
  // 30s 刷新 (KPI 卡片不需秒级)
  timer = window.setInterval(fetchData, 30000)
})

onBeforeUnmount(() => {
  if (timer) {
    clearInterval(timer)
    timer = null
  }
})

const hasFailure = computed(() => (stats.value?.failed ?? 0) > 0)
const hasCritical = computed(() => (stats.value?.p0 ?? 0) > 0)

function severityTagType(sev: string): 'danger' | 'warning' | 'info' | 'success' {
  if (sev === 'P0') return 'danger'
  if (sev === 'P1') return 'warning'
  if (sev === 'P2') return 'info'
  return 'success'
}

function fmtTime(s: string | undefined) {
  return s ? s.slice(0, 19).replace('T', ' ') : '-'
}

function goToAlerts() {
  router.push('/admin/alerts')
}
</script>

<template>
  <div class="alert-status" v-loading="loading">
    <div class="alert-summary">
      <!-- 关键指标 (左) -->
      <div class="status-grid">
        <div class="status-block">
          <div class="status-num" :class="{ 'status-num-danger': hasFailure }">
            {{ stats?.failed ?? 0 }}
          </div>
          <div class="status-cap">{{ t('admin.etlview.alert.7d_failed') }}</div>
        </div>
        <div class="status-block">
          <div class="status-num" :class="{ 'status-num-danger': hasCritical }">
            {{ stats?.p0 ?? 0 }}
          </div>
          <div class="status-cap">{{ t('admin.etlview.alert.7d_p0') }}</div>
        </div>
        <div class="status-block">
          <div class="status-num">{{ stats?.sent ?? 0 }}</div>
          <div class="status-cap">{{ t('admin.etlview.alert.7d_sent') }}</div>
        </div>
      </div>

      <!-- 最新告警 (中) -->
      <div class="status-latest" v-if="latest">
        <div class="latest-label">{{ t('admin.etlview.alert.latest') }}</div>
        <div class="latest-content">
          <el-tag :type="severityTagType(latest.severity)" size="small">{{ latest.severity }}</el-tag>
          <span class="latest-type">{{ latest.type }}</span>
          <el-tag v-if="latest.status !== 'sent'" size="small" type="warning">{{ latest.status }}</el-tag>
        </div>
        <div class="latest-time">{{ fmtTime(latest.sentAt) }}</div>
      </div>
      <div class="status-latest" v-else>
        <div class="latest-label">{{ t('admin.etlview.alert.latest') }}</div>
        <div class="latest-empty">{{ t('admin.etlview.alert.no_history') }}</div>
      </div>

      <!-- 操作 (右) -->
      <div class="status-actions">
        <el-button type="primary" plain size="small" @click="goToAlerts">
          {{ t('admin.etlview.alert.view_all_btn') }}
        </el-button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.alert-status { padding: 4px 0; }
.alert-summary {
  display: flex;
  align-items: center;
  gap: 24px;
}
.status-grid {
  display: flex;
  gap: 24px;
  flex: 0 0 auto;
}
.status-block { text-align: center; }
.status-num {
  font-size: 24px;
  font-weight: 600;
  line-height: 1;
  font-variant-numeric: tabular-nums;
}
.status-num-danger { color: var(--el-color-danger); }
.status-cap {
  font-size: 11px;
  color: var(--color-text-muted);
  margin-top: 4px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
.status-latest {
  flex: 1 1 auto;
  min-width: 0;
  padding: 0 16px;
  border-left: 1px solid var(--el-border-color-lighter);
  border-right: 1px solid var(--el-border-color-lighter);
}
.latest-label {
  font-size: 11px;
  color: var(--color-text-muted);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  margin-bottom: 4px;
}
.latest-content {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
}
.latest-type {
  font-family: 'SF Mono', 'Cascadia Code', 'Consolas', monospace;
  font-size: 12px;
}
.latest-time {
  font-size: 11px;
  color: var(--color-text-muted);
  margin-top: 2px;
}
.latest-empty {
  font-size: 12px;
  color: var(--color-text-muted);
  padding: 8px 0;
}
.status-actions { flex: 0 0 auto; }
@media (max-width: 768px) {
  .alert-summary { flex-direction: column; align-items: stretch; gap: 12px; }
  .status-grid { justify-content: space-between; }
  .status-latest {
    padding: 12px 0;
    border-left: 0;
    border-right: 0;
    border-top: 1px solid var(--el-border-color-lighter);
    border-bottom: 1px solid var(--el-border-color-lighter);
  }
  .status-actions :deep(.el-button) { width: 100%; }
}
</style>
