<script setup lang="ts">
// EtlPipeline — 横向 6 阶段数据流图
// WHY 新增 (P1 重构 2026-07-06):
//   原 AdminEtlView 用 el-progress 进度条 + el-descriptions 字段表展示进度,
//   用户难以理解"数据是怎么从文件流到数据库的"。
//   新组件用横向流程图,每个节点对应一个 stage,直观展示数据流。
//
// 6 阶段定义 (与后端 EtlImportService 一致):
//   1. read     — 流式读取 JSONL 文件
//   2. staging  — COPY 到 PG 临时表 (staging_xxx)
//   3. insert   — INSERT/UPDATE 主表 (依赖 mode)
//   4. commit   — 提交事务
//   5. meili    — 同步到 MeiliSearch 索引
//   6. done     — 任务完成
//
// 状态判断 (优先级):
//   failed > active > done > pending
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { CircleCheckFilled, CircleCloseFilled, Loading, Minus } from '@element-plus/icons-vue'

export type EtlStage = 'read' | 'staging' | 'insert' | 'commit' | 'meili' | 'done' | 'idle'
export type EtlStatus = 'running' | 'completed' | 'failed' | 'paused' | 'cancelled' | 'idle'

interface Props {
  stage: string
  status: EtlStatus
  rows: {
    read?: number
    inserted?: number
    updated?: number
    indexed?: number
    errors?: number
  }
  progressPct?: number | null
  elapsedSec?: number
}

const props = defineProps<Props>()
const { t } = useI18n()

const STAGE_ORDER: EtlStage[] = ['read', 'staging', 'insert', 'commit', 'meili', 'done']

const currentStage = computed<EtlStage>(() => {
  const raw = (props.stage || '').toLowerCase()
  const s = raw as EtlStage
  if (STAGE_ORDER.includes(s)) return s
  if (raw.startsWith('read')) return 'read'
  if (raw.startsWith('stag') || raw.startsWith('copy')) return 'staging'
  if (raw.startsWith('insert') || raw.startsWith('updat')) return 'insert'
  if (raw.startsWith('commit')) return 'commit'
  if (raw.startsWith('meili') || raw.startsWith('index')) return 'meili'
  if (raw.startsWith('done') || raw === 'completed') return 'done'
  return 'idle'
})

const currentIndex = computed(() => STAGE_ORDER.indexOf(currentStage.value))
const isFailed = computed(() => props.status === 'failed')

function nodeState(stage: EtlStage): 'pending' | 'active' | 'done' | 'failed' {
  if (isFailed.value) {
    const idx = STAGE_ORDER.indexOf(stage)
    if (idx === currentIndex.value) return 'failed'
    if (idx < currentIndex.value) return 'done'
    return 'pending'
  }
  if (props.status === 'completed') return 'done'
  if (props.status === 'idle' || currentIndex.value < 0) return 'pending'
  const idx = STAGE_ORDER.indexOf(stage)
  if (idx < currentIndex.value) return 'done'
  if (idx === currentIndex.value) return 'active'
  return 'pending'
}

const STAGE_LABEL_KEY: Record<EtlStage, string> = {
  read: 'admin.etlview.pipeline.stage_read',
  staging: 'admin.etlview.pipeline.stage_staging',
  insert: 'admin.etlview.pipeline.stage_insert',
  commit: 'admin.etlview.pipeline.stage_commit',
  meili: 'admin.etlview.pipeline.stage_meili',
  done: 'admin.etlview.pipeline.stage_done',
  idle: 'admin.etlview.pipeline.stage_idle'
}

function nodeValue(stage: EtlStage): string {
  const r = props.rows
  if (stage === 'read') return fmt(r.read)
  if (stage === 'insert') return fmt((r.inserted ?? 0) + (r.updated ?? 0))
  if (stage === 'meili') return fmt(r.indexed)
  return '—'
}

function fmt(n?: number): string {
  if (n === undefined || n === null) return '—'
  return n.toLocaleString()
}

const statusBadge = computed(() => {
  if (props.status === 'running') return { type: 'primary', label: t('admin.etlview.pipeline.status_running') }
  if (props.status === 'completed') return { type: 'success', label: t('admin.etlview.pipeline.status_completed') }
  if (props.status === 'failed') return { type: 'danger', label: t('admin.etlview.pipeline.status_failed') }
  if (props.status === 'paused') return { type: 'warning', label: t('admin.etlview.pipeline.status_paused') }
  if (props.status === 'cancelled') return { type: 'info', label: t('admin.etlview.pipeline.status_cancelled') }
  return { type: 'info', label: t('admin.etlview.pipeline.status_idle') }
})

const pct = computed(() => {
  if (props.progressPct !== undefined && props.progressPct !== null) return props.progressPct
  if (props.status === 'completed') return 100
  if (currentIndex.value < 0) return 0
  return Math.round((currentIndex.value / (STAGE_ORDER.length - 1)) * 100)
})

const barStatus = computed<'success' | 'exception' | undefined>(() => {
  if (props.status === 'completed') return 'success'
  if (props.status === 'failed') return 'exception'
  return undefined
})

const indeterminate = computed(() =>
  props.status === 'running' && (props.progressPct === null || props.progressPct === undefined)
)
</script>

<template>
  <div class="pipeline-wrap">
    <div class="pipeline-header">
      <el-tag :type="statusBadge.type as any" size="small">{{ statusBadge.label }}</el-tag>
      <span v-if="elapsedSec !== undefined" class="pipeline-elapsed">
        {{ t('admin.etlview.pipeline.elapsed_label') }}: {{ elapsedSec }}s
      </span>
      <span v-if="rows.errors && rows.errors > 0" class="pipeline-errors">
        {{ t('admin.etlview.pipeline.errors_label') }}: {{ fmt(rows.errors) }}
      </span>
    </div>

    <div class="pipeline-stages">
      <template v-for="(stage, i) in STAGE_ORDER" :key="stage">
        <div
          class="pipeline-node"
          :class="['state-' + nodeState(stage)]"
          :aria-current="nodeState(stage) === 'active' ? 'step' : undefined"
        >
          <div class="node-icon">
            <el-icon v-if="nodeState(stage) === 'done'"><CircleCheckFilled /></el-icon>
            <el-icon v-else-if="nodeState(stage) === 'failed'"><CircleCloseFilled /></el-icon>
            <el-icon v-else-if="nodeState(stage) === 'active'"><Loading /></el-icon>
            <el-icon v-else><Minus /></el-icon>
          </div>
          <div class="node-label">{{ t(STAGE_LABEL_KEY[stage]) }}</div>
          <div class="node-value">{{ nodeValue(stage) }}</div>
        </div>
        <div
          v-if="i < STAGE_ORDER.length - 1"
          class="pipeline-arrow"
          :class="{
            'arrow-done': STAGE_ORDER.indexOf(stage) < currentIndex,
            'arrow-active': STAGE_ORDER.indexOf(stage) === currentIndex,
            'arrow-pending': STAGE_ORDER.indexOf(stage) > currentIndex
          }"
        >
          →
        </div>
      </template>
    </div>

    <el-progress
      class="pipeline-progress"
      :percentage="pct"
      :stroke-width="8"
      :status="barStatus"
      :indeterminate="indeterminate"
    />
  </div>
</template>

<style scoped>
.pipeline-wrap { padding: 4px 0; }
.pipeline-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
  font-size: 13px;
}
.pipeline-elapsed { color: var(--color-text-muted); }
.pipeline-errors { color: var(--el-color-danger); font-weight: 500; }
.pipeline-stages {
  display: flex;
  align-items: center;
  gap: 0;
  margin: 16px 0;
  overflow-x: auto;
  padding: 4px 0;
}
.pipeline-node {
  flex: 0 0 auto;
  min-width: 96px;
  padding: 12px 8px;
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
  text-align: center;
  background: var(--el-bg-color);
  transition: all 0.2s;
}
.pipeline-node.state-done { border-color: var(--el-color-success); color: var(--el-color-success); }
.pipeline-node.state-active {
  border-color: var(--el-color-primary);
  color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
  font-weight: 500;
}
.pipeline-node.state-failed {
  border-color: var(--el-color-danger);
  color: var(--el-color-danger);
  background: var(--el-color-danger-light-9);
}
.node-icon { font-size: 20px; line-height: 1; margin-bottom: 6px; }
.node-label { font-size: 12px; margin-bottom: 4px; }
.node-value {
  font-size: 11px;
  color: var(--color-text-muted);
  font-variant-numeric: tabular-nums;
}
.pipeline-arrow {
  flex: 0 0 auto;
  padding: 0 4px;
  color: var(--el-border-color);
  font-size: 18px;
  font-weight: bold;
}
.pipeline-arrow.arrow-done { color: var(--el-color-success); }
.pipeline-arrow.arrow-active { color: var(--el-color-primary); }
.pipeline-progress { margin-top: 12px; }
@media (max-width: 768px) {
  .pipeline-node { min-width: 80px; padding: 8px 4px; }
  .node-icon { font-size: 16px; }
}
</style>
