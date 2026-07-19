<script setup lang="ts">
// AdminAlertsView — 告警历史与配置 (P2-1)
// WHY 新增: 配合后端 AlertCenter, 提供告警审计与规则管理 UI
// 5 个区域:
//   1. 顶部 KPI 卡片 (7 日: 总数/成功/失败/抑制/P0/P1)
//   2. 筛选栏 (时间/类型/严重度/状态) + 测试告警按钮
//   3. 告警历史表格 (分页)
//   4. 详情抽屉 (点击行打开, 显示完整 payload + 渠道响应)
//   5. 规则列表抽屉 (点击 "规则" 标签打开)
import { ref, reactive, onMounted, onBeforeUnmount, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { alertsApi } from '@/api'
import type { AlertHistoryItem, AlertHistoryDetail, AlertStats, AlertRuleItem } from '@/api/types'

const { t } = useI18n()

// ===== 筛选 =====
const filter = reactive({
  type: '',
  severity: '',
  status: ''
})
const pagination = reactive({ limit: 50, offset: 0 })

const items = ref<AlertHistoryItem[]>([])
const total = ref(0)
const loading = ref(false)
// V24-F101 (P2-2, 规则 8): 加 loadError 用于显示加载失败 + 重试
const loadError = ref<string | null>(null)
let pollTimer: number | null = null

// ===== KPI =====
const stats = ref<AlertStats>({
  total: 0, sent: 0, failed: 0, suppressed: 0,
  p0: 0, p1: 0, p2: 0, warn: 0, info: 0
})

// ===== 详情抽屉 =====
const detailDrawerOpen = ref(false)
const detail = ref<AlertHistoryDetail | null>(null)
const detailLoading = ref(false)

// ===== 规则抽屉 =====
const rulesDrawerOpen = ref(false)
const rules = ref<AlertRuleItem[]>([])
const rulesLoading = ref(false)

const currentPage = computed(() => Math.floor(pagination.offset / pagination.limit) + 1)

async function fetchData() {
  loading.value = true
  loadError.value = null
  try {
    const r = await alertsApi.history({
      type: filter.type || undefined,
      severity: filter.severity || undefined,
      status: filter.status || undefined,
      limit: pagination.limit,
      offset: pagination.offset
    })
    items.value = r.items
    total.value = r.total
  } catch (e: any) {
    // V24-F101 (P2-2, 规则 8): 显式赋值 loadError, 让表格上方显示重试 UI
    loadError.value = e?.response?.data?.message || e?.message || '告警历史加载失败'
  } finally {
    loading.value = false
  }
}

async function fetchStats() {
  try {
    stats.value = await alertsApi.stats()
  } catch (e) {
    // V24-F101 (P2-2, 规则 8): 统计加载失败不影响主表, 但需提示用户
    console.warn('[AdminAlertsView] fetchStats 失败:', e)
    ElMessage.warning('告警统计加载失败, 不影响主表')
  }
}

function resetFilter() {
  filter.type = ''
  filter.severity = ''
  filter.status = ''
  pagination.offset = 0
  fetchData()
}

function pageChange(p: number) {
  pagination.offset = (p - 1) * pagination.limit
  fetchData()
}

async function openDetail(row: AlertHistoryItem) {
  detailDrawerOpen.value = true
  detailLoading.value = true
  try {
    detail.value = await alertsApi.detail(row.id)
  } catch {
    detail.value = null
  } finally {
    detailLoading.value = false
  }
}

async function openRules() {
  rulesDrawerOpen.value = true
  rulesLoading.value = true
  try {
    rules.value = await alertsApi.rules()
  } catch {
    rules.value = []
  } finally {
    rulesLoading.value = false
  }
}

async function toggleRuleEnabled(rule: AlertRuleItem) {
  try {
    await alertsApi.updateRule(rule.id, { enabled: !rule.enabled })
    rule.enabled = !rule.enabled
    ElMessage.success(`已${rule.enabled ? '启用' : '禁用'} 规则 ${rule.type}`)
  } catch {
    // 拦截器处理
  }
}

async function sendTestAlert() {
  try {
    const r = await alertsApi.test({
      type: 'test.manual',
      severity: 'INFO',
      title: '[Test] 手动测试告警',
      markdown: `**手动测试告警**\n\n时间: ${new Date().toISOString()}\n触发人: ${t('admin.alertsview.test.triggered_by_user')}`
    })
    if (r.disabled) {
      ElMessage.warning('告警系统未启用 (alert.enabled=false)')
    } else if (r.noChannel) {
      ElMessage.warning('无可用渠道 (请配置 alert.webhook_url 等)')
    } else if (r.success) {
      ElMessage.success(`测试告警已发送: ${r.sentCount} 个渠道成功`)
    } else {
      ElMessage.error(`测试告警失败: ${r.failedCount} 个渠道失败`)
    }
  } catch {
    // 拦截器处理
  }
}

// ===== 格式化 =====
function fmtTime(s: string) {
  return s ? s.slice(0, 19).replace('T', ' ') : '-'
}

function severityTagType(sev: string): 'danger' | 'warning' | 'info' | 'primary' | 'success' {
  if (sev === 'P0') return 'danger'
  if (sev === 'P1') return 'warning'
  if (sev === 'P2') return 'info'
  if (sev === 'ERROR') return 'danger'
  if (sev === 'WARN') return 'warning'
  return 'success'
}

function statusTagType(s: string): 'success' | 'danger' | 'info' | 'warning' {
  if (s === 'sent') return 'success'
  if (s === 'failed') return 'danger'
  if (s === 'suppressed') return 'warning'
  return 'info'
}

// P2-1: 渠道图标抽到 composables/useAlertFormat.ts 复用 (L4 改进建议)
import { channelIcon } from '@/composables/useAlertFormat'

const severityOptions = ['P0', 'P1', 'P2', 'ERROR', 'WARN', 'INFO']
const statusOptions = ['sent', 'failed', 'suppressed']

// 已知告警类型 (admin 视图主要关注这几类, 后续 P3 接入更多)
const knownTypeOptions = [
  'etl.failed', 'etl.cancelled', 'etl.paused',
  'perf.threshold', 'perf.error_rate',
  'admin.login', 'login.brute_force', 'permission.change',
  'rate_limit.exceeded', 'crawler.detected',
  'hosted.dead', 'disk.high', 'memory.high',
  'test.manual'
]

onMounted(() => {
  fetchData()
  fetchStats()
  // 15s 自动刷新 (比 KPI 慢, 减少后端压力)
  pollTimer = window.setInterval(() => {
    fetchData()
    fetchStats()
  }, 15000)
})

onBeforeUnmount(() => {
  if (pollTimer) {
    clearInterval(pollTimer)
    pollTimer = null
  }
})
</script>

<template>
  <div class="p-3 max-w-screen-2xl mx-auto">
    <div class="flex items-center justify-between mb-3">
      <h1 class="text-lg font-medium">{{ t('admin.alertsview.page_title') }}</h1>
      <div class="flex items-center gap-2">
        <el-button @click="openRules">{{ t('admin.alertsview.btn.rules') }}</el-button>
        <el-button type="primary" plain @click="sendTestAlert">{{ t('admin.alertsview.btn.test') }}</el-button>
      </div>
    </div>

    <!-- 1. KPI 卡片 (7 日) -->
    <div class="kpi-grid mb-3">
      <div class="kpi-card">
        <div class="kpi-label">{{ t('admin.alertsview.kpi.total_7d') }}</div>
        <div class="kpi-value">{{ stats.total.toLocaleString() }}</div>
        <div class="kpi-foot"><span class="kpi-foot-dot dot-neutral" />{{ t('admin.alertsview.kpi.last_7d') }}</div>
      </div>
      <div class="kpi-card">
        <div class="kpi-label">{{ t('admin.alertsview.kpi.sent') }}</div>
        <div class="kpi-value kpi-value-success">{{ stats.sent.toLocaleString() }}</div>
        <div class="kpi-foot"><span class="kpi-foot-dot dot-success" />{{ t('admin.alertsview.kpi.send_success') }}</div>
      </div>
      <div class="kpi-card">
        <div class="kpi-label">{{ t('admin.alertsview.kpi.failed') }}</div>
        <div class="kpi-value kpi-value-danger">{{ stats.failed.toLocaleString() }}</div>
        <div class="kpi-foot"><span class="kpi-foot-dot dot-danger" />{{ t('admin.alertsview.kpi.send_failed') }}</div>
      </div>
      <div class="kpi-card">
        <div class="kpi-label">{{ t('admin.alertsview.kpi.suppressed') }}</div>
        <div class="kpi-value">{{ stats.suppressed.toLocaleString() }}</div>
        <div class="kpi-foot"><span class="kpi-foot-dot dot-neutral" />{{ t('admin.alertsview.kpi.suppressed_in_window') }}</div>
      </div>
      <div class="kpi-card">
        <div class="kpi-label">{{ t('admin.alertsview.kpi.p0') }}</div>
        <div class="kpi-value kpi-value-danger">{{ stats.p0.toLocaleString() }}</div>
        <div class="kpi-foot"><span class="kpi-foot-dot dot-danger" />{{ t('admin.alertsview.kpi.severity_p0') }}</div>
      </div>
      <div class="kpi-card">
        <div class="kpi-label">{{ t('admin.alertsview.kpi.p1') }}</div>
        <div class="kpi-value kpi-value-warning">{{ stats.p1.toLocaleString() }}</div>
        <div class="kpi-foot"><span class="kpi-foot-dot dot-warning" />{{ t('admin.alertsview.kpi.severity_p1') }}</div>
      </div>
    </div>

    <!-- 2. 筛选栏 -->
    <el-card shadow="never" class="mb-3">
      <div class="filter-bar">
        <div class="filter-item">
          <span class="filter-label">{{ t('admin.alertsview.filter.type') }}</span>
          <el-select v-model="filter.type" :placeholder="t('admin.alertsview.filter.all')" clearable filterable style="width: 220px">
            <el-option v-for="opt in knownTypeOptions" :key="opt" :label="opt" :value="opt" />
          </el-select>
        </div>
        <div class="filter-item">
          <span class="filter-label">{{ t('admin.alertsview.filter.severity') }}</span>
          <el-select v-model="filter.severity" :placeholder="t('admin.alertsview.filter.all')" clearable style="width: 120px">
            <el-option v-for="opt in severityOptions" :key="opt" :label="opt" :value="opt" />
          </el-select>
        </div>
        <div class="filter-item">
          <span class="filter-label">{{ t('admin.alertsview.filter.status') }}</span>
          <el-select v-model="filter.status" :placeholder="t('admin.alertsview.filter.all')" clearable style="width: 140px">
            <el-option v-for="opt in statusOptions" :key="opt" :label="opt" :value="opt" />
          </el-select>
        </div>
        <div class="filter-actions">
          <el-button type="primary" @click="(pagination.offset = 0, fetchData())">{{ t('common.action.search') }}</el-button>
          <el-button @click="resetFilter">{{ t('common.action.reset') }}</el-button>
        </div>
      </div>
    </el-card>

    <!-- 3. 告警历史表格 -->
    <el-card shadow="never">
      <template #header>
        <div class="flex items-center gap-2">
          <span class="font-semibold">{{ t('admin.alertsview.table.title') }}</span>
          <el-tag size="small">{{ total.toLocaleString() }} {{ t('admin.alertsview.table.records') }}</el-tag>
        </div>
      </template>
      <!-- V24-F101 (P2-2, 规则 8): 加载失败时显示 error UI + 重试按钮 -->
      <el-alert
        v-if="loadError"
        type="error"
        :title="loadError"
        show-icon
        :closable="false"
        class="mb-2"
      >
        <template #default>
          <div class="flex items-center justify-between">
            <span>{{ loadError }}</span>
            <el-button size="small" @click="fetchData" :disabled="loading">
              {{ loading ? '重试中…' : '重试' }}
            </el-button>
          </div>
        </template>
      </el-alert>
      <el-table :data="items" v-loading="loading" size="small" border stripe>
        <el-table-column prop="id" label="#" width="70" />
        <el-table-column :label="t('admin.alertsview.table.severity')" width="80">
          <template #default="{ row }">
            <el-tag :type="severityTagType(row.severity)" size="small">{{ row.severity }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="type" :label="t('admin.alertsview.table.type')" width="220" show-overflow-tooltip />
        <el-table-column prop="title" :label="t('admin.alertsview.table.title_col')" min-width="200" show-overflow-tooltip />
        <el-table-column :label="t('admin.alertsview.table.channel')" width="110">
          <template #default="{ row }">
            <span>{{ channelIcon(row.channel) }} {{ row.channel }}</span>
          </template>
        </el-table-column>
        <el-table-column :label="t('admin.alertsview.table.status')" width="100">
          <template #default="{ row }">
            <el-tag :type="statusTagType(row.status)" size="small">{{ row.status }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('admin.alertsview.table.sent_at')" width="170">
          <template #default="{ row }">
            <span class="text-xs">{{ fmtTime(row.sentAt) }}</span>
          </template>
        </el-table-column>
        <el-table-column :label="t('admin.alertsview.table.actions')" width="80" fixed="right">
          <template #default="{ row }">
            <el-button text size="small" @click="openDetail(row)">{{ t('admin.alertsview.table.detail') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
      <div class="flex justify-end mt-3">
        <el-pagination
          :current-page="currentPage"
          :page-size="pagination.limit"
          :total="total"
          :page-sizes="[20, 50, 100, 200]"
          layout="total, sizes, prev, pager, next"
          @current-change="pageChange"
          @size-change="(s: number) => { pagination.limit = s; pagination.offset = 0; fetchData() }"
        />
      </div>
    </el-card>

    <!-- 4. 详情抽屉 -->
    <el-drawer v-model="detailDrawerOpen" :title="t('admin.alertsview.detail.title')" size="640px">
      <div v-loading="detailLoading">
        <template v-if="detail">
          <div class="detail-section">
            <div class="detail-label">{{ t('admin.alertsview.detail.id') }}</div>
            <div class="detail-value">#{{ detail.id }}</div>
          </div>
          <div class="detail-section">
            <div class="detail-label">{{ t('admin.alertsview.detail.severity_type') }}</div>
            <div class="detail-value">
              <el-tag :type="severityTagType(detail.severity)" size="small">{{ detail.severity }}</el-tag>
              <span class="ml-2">{{ detail.type }}</span>
            </div>
          </div>
          <div class="detail-section">
            <div class="detail-label">{{ t('admin.alertsview.detail.title_col') }}</div>
            <div class="detail-value">{{ detail.title }}</div>
          </div>
          <div class="detail-section">
            <div class="detail-label">{{ t('admin.alertsview.detail.channel_status') }}</div>
            <div class="detail-value">
              <span>{{ channelIcon(detail.channel) }} {{ detail.channel }}</span>
              <el-tag :type="statusTagType(detail.status)" size="small" class="ml-2">{{ detail.status }}</el-tag>
            </div>
          </div>
          <div class="detail-section">
            <div class="detail-label">{{ t('admin.alertsview.detail.sent_at') }}</div>
            <div class="detail-value text-xs">{{ fmtTime(detail.sentAt) }}</div>
          </div>
          <div v-if="detail.recipients" class="detail-section">
            <div class="detail-label">{{ t('admin.alertsview.detail.recipients') }}</div>
            <div class="detail-value text-xs">{{ detail.recipients }}</div>
          </div>
          <div v-if="detail.error" class="detail-section">
            <div class="detail-label">{{ t('admin.alertsview.detail.error') }}</div>
            <el-alert :title="detail.error" type="error" :closable="false" />
          </div>
          <div class="detail-section">
            <div class="detail-label">{{ t('admin.alertsview.detail.content') }}</div>
            <pre class="json-block">{{ detail.content }}</pre>
          </div>
          <div v-if="detail.response" class="detail-section">
            <div class="detail-label">{{ t('admin.alertsview.detail.response') }}</div>
            <pre class="json-block">{{ detail.response }}</pre>
          </div>
        </template>
      </div>
    </el-drawer>

    <!-- 5. 规则抽屉 -->
    <el-drawer v-model="rulesDrawerOpen" :title="t('admin.alertsview.rules.title')" size="640px">
      <div v-loading="rulesLoading">
        <el-empty v-if="!rulesLoading && rules.length === 0" :description="t('admin.alertsview.rules.empty')" />
        <el-table v-else :data="rules" size="small" border>
          <el-table-column prop="type" :label="t('admin.alertsview.rules.type')" min-width="200" show-overflow-tooltip />
          <el-table-column :label="t('admin.alertsview.rules.severity')" width="80">
            <template #default="{ row }">
              <el-tag :type="severityTagType(row.severity)" size="small">{{ row.severity }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column :label="t('admin.alertsview.rules.channels')" width="200">
            <template #default="{ row }">
              <span class="text-xs">{{ row.channels }}</span>
            </template>
          </el-table-column>
          <el-table-column :label="t('admin.alertsview.rules.enabled')" width="80">
            <template #default="{ row }">
              <el-switch :model-value="row.enabled" @change="toggleRuleEnabled(row)" />
            </template>
          </el-table-column>
        </el-table>
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
.kpi-grid {
  display: grid;
  grid-template-columns: repeat(6, 1fr);
  gap: 12px;
}
.kpi-card {
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
  padding: 14px 16px;
  background: var(--el-bg-color);
  transition: border-color 0.15s;
}
.kpi-card:hover { border-color: var(--el-color-primary-light-5); }
.kpi-label {
  font-size: 11px;
  color: var(--color-text-muted);
  margin-bottom: 6px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
.kpi-value {
  font-size: 24px;
  font-weight: 600;
  line-height: 1.1;
  font-variant-numeric: tabular-nums;
}
.kpi-value-success { color: var(--el-color-success); }
.kpi-value-danger { color: var(--el-color-danger); }
.kpi-value-warning { color: var(--el-color-warning); }
.kpi-foot {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 6px;
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
.dot-warning { background: var(--el-color-warning); }
.dot-neutral { background: var(--el-border-color-darker); }

.filter-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
}
.filter-item { display: flex; align-items: center; gap: 6px; }
.filter-label { font-size: 13px; color: var(--el-text-color-regular); }
.filter-actions { margin-left: auto; display: flex; gap: 8px; }

.detail-section { margin-bottom: 16px; }
.detail-label {
  font-size: 12px;
  color: var(--color-text-muted);
  margin-bottom: 4px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
.detail-value { font-size: 13px; }
.json-block {
  background: #fafafa;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 4px;
  padding: 10px;
  font-size: 12px;
  max-height: 280px;
  overflow: auto;
  white-space: pre-wrap;
  word-break: break-all;
  margin: 0;
  font-family: 'SF Mono', 'Cascadia Code', 'Consolas', monospace;
}

@media (max-width: 1280px) {
  .kpi-grid { grid-template-columns: repeat(3, 1fr); }
}
@media (max-width: 768px) {
  .kpi-grid { grid-template-columns: repeat(2, 1fr); }
  .filter-bar { flex-direction: column; align-items: stretch; }
  .filter-actions { margin-left: 0; }
}
</style>
