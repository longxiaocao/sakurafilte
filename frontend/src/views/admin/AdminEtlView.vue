<script setup lang="ts">
// AdminEtlView — ETL 触发与监控 (P1 重构版 2026-07-06)
//
// 重构要点:
//   - 拆出 useEtlProgress composable (SSE + 审计 + 持久化)
//   - 拆出 EtlPipeline 组件 (6 阶段流程图)
//   - 拆出 EtlKpiCards 组件 (4 张 KPI 概览)
//   - 拆出 EtlAlertStatus 组件 (P2 告警占位)
//   - 保留所有现有功能: 触发/暂停/取消/恢复/dry-run/拖拽/历史审计
//
// 视觉调整:
//   - 移除 "Day 8.4" 装饰标签
//   - 6 卡片纵向布局: KPI → Pipeline → 触发 → 告警 → 最近完成 → 审计
//   - Musk 风格 hairline border + 0 shadow (组件已实现)
import { ref, reactive, onMounted, onBeforeUnmount, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { etlApi } from '@/api'
import type { EtlDryRunResult, EtlProgress, ReindexResult } from '@/api/types'
import EtlReasonCodePie from '@/components/EtlReasonCodePie.vue'
import EtlPipeline from '@/components/EtlPipeline.vue'
import EtlKpiCards from '@/components/EtlKpiCards.vue'
import EtlAlertStatus from '@/components/EtlAlertStatus.vue'
import { useGlobalDragDrop, DEFAULT_ADMIN_ACCEPT } from '@/composables/useGlobalDragDrop'
import { useEtlProgress } from '@/composables/useEtlProgress'

const { t } = useI18n()

// ===== 表单 =====
const form = reactive({
  entity: 'products' as 'products' | 'xrefs' | 'apps',
  mode: 'upsert' as 'full-load' | 'insert-only' | 'upsert',
  dryRun: false,
  cascade: true,
  jsonlPath: 'D:/data/sakurafilter/products.jsonl'
})

const entityPaths: Record<string, string> = {
  products: 'D:/data/sakurafilter/products.jsonl',
  xrefs: 'D:/data/sakurafilter/xrefs.jsonl',
  apps: 'D:/data/sakurafilter/apps.jsonl'
}

function changeEntity(v: 'products' | 'xrefs' | 'apps') {
  form.entity = v
  form.jsonlPath = entityPaths[v]
}

// ===== 全局拖拽 =====
const { register: registerDrag, unregister: unregisterDrag } = useGlobalDragDrop()
const SERVER_BASE_DIR = 'D:/data/sakurafilter'

function inferEntityByName(name: string): 'products' | 'xrefs' | 'apps' | null {
  const lower = name.toLowerCase()
  if (lower.includes('product')) return 'products'
  if (lower.includes('xref') || lower.includes('cross')) return 'xrefs'
  if (lower.includes('app') || lower.includes('machine')) return 'apps'
  return null
}

function handleFilesDropped(files: File[]) {
  if (files.length === 0) return
  const f = files[0]
  const inferred = inferEntityByName(f.name)
  const serverPath = `${SERVER_BASE_DIR}/${f.name}`
  if (inferred) {
    form.entity = inferred
    form.jsonlPath = serverPath
    ElMessage.success(t('admin.etlview.string.auto_recognized_entity_entity', { entity: inferred, name: f.name }))
  } else {
    form.jsonlPath = serverPath
    ElMessage.info(t('admin.etlview.string.file_filled_name_entity', { name: f.name }))
  }
  if (files.length > 1) {
    ElMessage.warning(t('admin.etlview.string.dropped_total_files_only', { total: files.length, name: f.name }))
  }
}

onMounted(() => {
  registerDrag({
    onFilesDropped: handleFilesDropped,
    acceptRoute: DEFAULT_ADMIN_ACCEPT,
    hintText: t('admin.etlview.string.on_etl_file')
  })
})

onBeforeUnmount(() => {
  unregisterDrag()
})

// ===== Composable: 实时进度 + 审计 =====
const {
  task,
  lastFinished,
  reasonCodeAgg,
  historyItems,
  historyLoading,
  hasPausedTask,
  refreshAudit,
  clearLastFinished
} = useEtlProgress()

// ===== 触发 =====
const submitting = ref(false)
const cancelling = ref(false)
const pausing = ref(false)
const resuming = ref(false)
const lastDryRun = ref<EtlDryRunResult | null>(null)
const showAllSamples = ref(false)

async function doTrigger() {
  try {
    await ElMessageBox.confirm(
      `即将触发 ${form.entity} ETL (${form.mode}${form.dryRun ? ' - dry-run' : ''}), 是否继续?`,
      t('common.action.confirm'),
      { type: 'warning' }
    )
  } catch {
    return
  }
  submitting.value = true
  try {
    const r = await etlApi.trigger({
      jsonlPath: form.jsonlPath,
      mode: form.mode,
      entityType: form.entity,
      cascade: form.cascade,
      dryRun: form.dryRun
    })
    ElMessage.success(form.dryRun
      ? t('admin.etlview.success.dry_run_validation_completed')
      : t('admin.etlview.success.triggered_etl_background_execute'))
    if (form.dryRun) {
      lastDryRun.value = r as any
    }
  } catch (e) {
    // V24-F101 (P2-2, 规则 8): 拦截器已弹 toast, 这里 console.warn 便于排查 (如 504 网关超时拦截器可能未捕获)
    console.warn('[AdminEtlView] doTrigger 失败:', e)
  } finally {
    submitting.value = false
  }
}

// ===== 取消原因白名单 (与后端 AllowedReasonCodes 对齐) =====
const reasonCodeOptions = [
  { value: 'USER_REQUEST', label: t('common.field.user_cancelled'), defaultReason: t('common.field.user_cancelled') },
  { value: 'ADMIN_OVERRIDE', label: t('common.field.admin_force_cancel'), defaultReason: t('common.field.admin_force_cancel') },
  { value: 'TIMEOUT', label: t('admin.etlview.string.task_timeout'), defaultReason: t('admin.etlview.string.task_execute') },
  { value: 'SYSTEM_SHUTDOWN', label: t('admin.etlview.string.system_shutdown_restart'), defaultReason: t('admin.etlview.string.service_close_restart') },
  { value: 'OTHER', label: t('common.field.other_reason'), defaultReason: t('common.field.other_reason') }
] as const

async function doCancel() {
  let reasonCode: string = 'USER_REQUEST'
  let reason: string = reasonCodeOptions[0].defaultReason
  try {
    await ElMessageBox({
      title: t('admin.etlview.string.cancel_etl_task'),
      message: `
        <div style="text-align:left;font-size:13px;line-height:1.6">
          <div style="margin-bottom:8px;color:#606266">请选择取消原因 (会写入历史审计, 按此码聚合):</div>
          <div id="cancel-reason-list" style="display:flex;flex-direction:column;gap:6px">
            ${reasonCodeOptions.map((o, i) => `
              <label style="display:flex;align-items:flex-start;gap:6px;cursor:pointer;padding:6px 8px;border:1px solid #ebeef5;border-radius:4px;${i === 0 ? 'background:#ecf5ff;border-color:#409eff' : ''}">
                <input type="radio" name="cancel-reason-code" value="${o.value}" ${i === 0 ? 'checked' : ''} style="margin-top:3px" />
                <div>
                  <div style="font-weight:600">${o.label}</div>
                  <div style="color:#909399;font-size:12px">${o.value} — ${o.defaultReason}</div>
                </div>
              </label>
            `).join('')}
          </div>
        </div>
      `,
      showCancelButton: true,
      confirmButtonText: t('admin.etlview.buttontext.next'),
      cancelButtonText: t('common.field.no_cancel'),
      type: 'warning',
      dangerouslyUseHTMLString: true
    })
    const checked = document.querySelector<HTMLInputElement>('input[name="cancel-reason-code"]:checked')
    reasonCode = checked?.value || 'USER_REQUEST'
    const picked = reasonCodeOptions.find(o => o.value === reasonCode) || reasonCodeOptions[0]
    reason = picked.defaultReason
  } catch {
    return
  }
  try {
    const r = await ElMessageBox.prompt(
      t('admin.etlview.info.description_empty_default'),
      t('admin.etlview.info.cancel_note'),
      {
        confirmButtonText: t('admin.etlview.buttontext.confirm_cancel'),
        cancelButtonText: t('common.field.no_cancel'),
        inputPlaceholder: reason,
        inputValue: reason
      }
    )
    const v = (r.value || '').trim()
    if (v) reason = v
  } catch {
    return
  }
  cancelling.value = true
  try {
    const r = await etlApi.cancel(reason, reasonCode)
    if (r.cancelled) {
      ElMessage.warning(t('admin.etlview.string.cancel_signal_sent_code', { code: reasonCode }))
    } else {
      ElMessage.info(r.reason || t('common.field.no_active_task_to_cancel'))
    }
  } catch (e) {
    // V24-F101 (P2-2, 规则 8): 拦截器已弹 toast, console.warn 便于排查
    console.warn('[AdminEtlView] doCancel 失败:', e)
  } finally {
    cancelling.value = false
  }
}

async function doPause() {
  try {
    await ElMessageBox.confirm(
      t('admin.etlview.string.pause_current_etl_task', {
        resume: t('common.action.resume'),
        cancel: t('common.field.cancel')
      }),
      t('admin.etlview.string.pause_etl_task'),
      { type: 'warning', confirmButtonText: t('admin.etlview.buttontext.pause'), cancelButtonText: t('admin.etlview.buttontext.no_pause') }
    )
  } catch { return }
  pausing.value = true
  try {
    const r = await etlApi.pause()
    if (r.paused) {
      ElMessage.warning(`已发送暂停信号, checkpoint_id=${r.checkpointId ?? '?'}, 当前批次跑完后退出`)
    } else {
      ElMessage.info(r.reason || t('admin.etlview.info.task_pause'))
    }
  } catch (e) {
    // V24-F101 (P2-2, 规则 8): 拦截器已弹 toast, console.warn 便于排查
    console.warn('[AdminEtlView] doPause 失败:', e)
  } finally {
    pausing.value = false
  }
}

async function doResume() {
  try {
    await ElMessageBox.confirm(
      t('admin.etlview.string.resume_pause_etl_task'),
      t('admin.etlview.string.resume_etl_task'),
      { type: 'info', confirmButtonText: t('common.action.resume'), cancelButtonText: t('admin.etlview.buttontext.no_resume') }
    )
  } catch { return }
  resuming.value = true
  try {
    const r = await etlApi.resume()
    if (r.resumed) {
      ElMessage.success(t('admin.etlview.string.resume_triggered_entity_entity', { entity: r.entity, checkpoint: r.checkpointId, line: r.nextLineNo }))
      hasPausedTask.value = false
    } else {
      ElMessage.warning(r.error || t('common.action.restore_failed'))
    }
  } catch (e) {
    // V24-F101 (P2-2, 规则 8): 拦截器已弹 toast, console.warn 便于排查
    console.warn('[AdminEtlView] doResume 失败:', e)
  } finally {
    resuming.value = false
  }
}

// ===== 全量重建 Meilisearch 索引 (V17-3.1) =====
//   后端 ReindexAllAsync:
//     - AcquireActiveCts 与 ETL 互斥, 冲突返回 409
//     - advisory_lock 防止并发重建
//     - DeleteAllDocumentsAsync → TruncateSearchIndexPending → SyncAllSearchIndexAsync
//   前端职责: 二次确认 + loading + 409 错误码映射 + 结果展示
const reindexing = ref(false)
const lastReindex = ref<ReindexResult | null>(null)

async function doReindexAll() {
  // 二次确认: 全量重建会清空 Meilisearch 全部文档, 期间搜索不可用
  try {
    await ElMessageBox.confirm(
      '全量重建将清空 Meilisearch 全部文档并从 PostgreSQL 重新同步, 期间搜索将短暂不可用。是否继续?',
      '危险操作确认',
      { type: 'warning', confirmButtonText: '执行全量重建', cancelButtonText: '取消' }
    )
  } catch {
    return
  }
  reindexing.value = true
  try {
    const r = await etlApi.reindexAll()
    lastReindex.value = r
    if (r.error === 'CANCELLED') {
      ElMessage.warning('全量重建已被取消')
    } else if (r.error) {
      ElMessage.error(`全量重建失败: ${r.error}`)
    } else {
      ElMessage.success(`全量重建完成: ${r.message}`)
    }
  } catch (err: any) {
    // V17-3.1: 后端 409 表示已有 ETL 任务在运行 (互斥冲突)
    //   拦截器默认会弹通用错误, 此处补充业务语义提示
    const status = err?.response?.status
    if (status === 409) {
      ElMessage.warning('已有 ETL 任务在运行, 请等待完成后再触发全量重建')
    } else if (lastReindex.value == null) {
      // 未拿到结果对象, 显示原始错误兜底
      const msg = err?.response?.data?.error || err?.message || '未知错误'
      lastReindex.value = {
        message: '全量重建失败',
        direct: 0,
        queued: 0,
        elapsedMs: 0,
        error: msg
      }
    }
  } finally {
    reindexing.value = false
  }
}

// ===== 计算属性 (供 EtlPipeline 传入) =====
const status = computed(() => task.value.activeTask?.status ?? (task.value.inProgress ? 'running' : 'idle'))
const stage = computed(() => task.value.activeTask?.stage ?? '-')
const progressPct = computed(() => task.value.activeTask?.progressPct ?? null)

const pipelineRows = computed(() => {
  if (task.value.activeTask) {
    return {
      read: task.value.activeTask.read,
      inserted: task.value.activeTask.inserted,
      updated: task.value.activeTask.updated,
      indexed: task.value.activeTask.indexed,
      errors: task.value.activeTask.errors
    }
  }
  if (lastFinished.value) {
    return {
      read: lastFinished.value.read,
      inserted: lastFinished.value.inserted,
      updated: lastFinished.value.updated,
      indexed: lastFinished.value.indexed,
      errors: lastFinished.value.errors
    }
  }
  return {}
})

const elapsedSec = computed(() => task.value.activeTask?.elapsedSec)

// ===== 工具 =====
function fmt(n?: number) {
  if (n === undefined || n === null) return '-'
  return n.toLocaleString()
}
function fmtBytes(n?: number) {
  if (!n) return '-'
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / 1024 / 1024).toFixed(2)} MB`
}
function prettyJson(raw: string): string {
  try { return JSON.stringify(JSON.parse(raw), null, 2) } catch { return raw }
}
function statusTagType(s: string): 'success' | 'warning' | 'info' | 'danger' | 'primary' {
  if (s === 'running') return 'primary'
  if (s === 'completed') return 'success'
  if (s === 'failed') return 'danger'
  if (s === 'idle') return 'info'
  return 'warning'
}
</script>

<template>
  <div class="p-3 max-w-screen-2xl mx-auto">
    <h1 class="text-lg font-medium mb-3">{{ t('admin.etlview.page_title') }}</h1>

    <!-- 1. KPI 概览 -->
    <div class="mb-3">
      <EtlKpiCards />
    </div>

    <!-- 2. Pipeline 流程图 -->
    <el-card shadow="never" class="mb-3">
      <template #header>
        <div class="flex items-center gap-2">
          <span class="font-semibold">{{ t('admin.etlview.section.pipeline') }}</span>
          <el-tag v-if="status !== 'idle'" :type="statusTagType(status)" size="small">
            {{ t(`admin.etlview.pipeline.status_${status}`) }}
          </el-tag>
        </div>
      </template>
      <EtlPipeline
        :stage="stage"
        :status="status as any"
        :rows="pipelineRows"
        :progress-pct="progressPct"
        :elapsed-sec="elapsedSec"
      />
    </el-card>

    <!-- 3. 触发卡片 -->
    <el-card shadow="never" class="mb-3">
      <template #header>
        <div class="flex items-center gap-2">
          <span class="font-semibold">{{ t('admin.etlview.section.trigger') }}</span>
        </div>
      </template>

      <el-form :inline="false" label-width="100px" size="default">
        <el-form-item :label="t('admin.etlview.label.entity')">
          <el-radio-group v-model="form.entity" @change="changeEntity">
            <el-radio-button value="products">products</el-radio-button>
            <el-radio-button value="xrefs">xrefs</el-radio-button>
            <el-radio-button value="apps">apps</el-radio-button>
          </el-radio-group>
        </el-form-item>

        <el-form-item :label="t('common.field.mode')">
          <el-radio-group v-model="form.mode">
            <el-radio-button value="full-load">full-load (TRUNCATE+INSERT)</el-radio-button>
            <el-radio-button value="insert-only">insert-only (ON CONFLICT DO NOTHING)</el-radio-button>
            <el-radio-button value="upsert">upsert (ON CONFLICT DO UPDATE)</el-radio-button>
          </el-radio-group>
        </el-form-item>

        <el-form-item :label="t('admin.etlview.label.file')">
          <el-input
            v-model="form.jsonlPath"
            :placeholder="t('admin.etlview.placeholder.jsonl_absolute_path')"
            style="width: 500px"
            clearable
          />
        </el-form-item>

        <el-form-item label=" ">
          <div class="flex items-center gap-3">
            <el-checkbox v-model="form.dryRun">dry-run (仅校验文件)</el-checkbox>
            <el-tooltip
              v-if="form.entity === 'products' && form.mode === 'full-load' && !form.dryRun"
              :content="t('admin.etlview.string.on_truncate_clear_xrefs')"
              placement="top"
            >
              <el-checkbox v-model="form.cascade">cascade (清空关联表)</el-checkbox>
            </el-tooltip>
            <el-button
              type="primary"
              :loading="submitting"
              :disabled="status === 'running'"
              @click="doTrigger"
            >
              {{ form.dryRun ? t('admin.etlview.templatetext.execute_dry_run') : t('admin.etlview.templatetext.immediately_import') }}
            </el-button>
            <el-button
              v-if="status === 'running'"
              type="warning"
              :loading="pausing"
              @click="doPause"
            >
              {{ t('admin.etlview.buttontext.pause') }}
            </el-button>
            <el-button
              v-if="status === 'running'"
              type="danger"
              :loading="cancelling"
              @click="doCancel"
            >
              {{ t('admin.etlview.templatetext.cancel_task') }}
            </el-button>
            <el-button
              v-if="status !== 'running' && hasPausedTask"
              type="success"
              :loading="resuming"
              @click="doResume"
            >
              {{ t('admin.etlview.buttontext.resume') }}
            </el-button>
          </div>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 3.5 全量重建 Meilisearch 索引 (V17-3.1 危险操作) -->
    <el-card shadow="never" class="mb-3">
      <template #header>
        <div class="flex items-center gap-2">
          <span class="font-semibold">全量重建</span>
          <el-tag size="small" type="danger">DANGER</el-tag>
          <el-tooltip
            content="清空 Meilisearch 全部文档并从 PostgreSQL 全量同步, 与 ETL 任务互斥"
            placement="top"
          >
            <el-icon class="text-gray-400 cursor-help"><InfoFilled /></el-icon>
          </el-tooltip>
        </div>
      </template>
      <el-alert
        type="warning"
        :closable="false"
        class="mb-3"
        title="执行后将清空 Meilisearch 全部文档并重新同步, 期间搜索短暂不可用"
        description="适用场景: 索引结构变更后强制重建 / 数据漂移修复 / schema 字段更新后生效"
      />
      <div class="flex items-center gap-3 mb-3">
        <el-button
          type="danger"
          :loading="reindexing"
          :disabled="status === 'running'"
          @click="doReindexAll"
        >
          执行全量重建
        </el-button>
        <span v-if="status === 'running'" class="text-xs text-[var(--color-text-muted)]">
          ETL 任务进行中, 全量重建不可用
        </span>
      </div>
      <el-descriptions
        v-if="lastReindex"
        :column="4"
        size="small"
        border
      >
        <el-descriptions-item label="message">{{ lastReindex.message }}</el-descriptions-item>
        <el-descriptions-item label="direct">{{ fmt(lastReindex.direct) }}</el-descriptions-item>
        <el-descriptions-item label="queued">{{ fmt(lastReindex.queued) }}</el-descriptions-item>
        <el-descriptions-item label="elapsed">{{ (lastReindex.elapsedMs / 1000).toFixed(2) }}s</el-descriptions-item>
      </el-descriptions>
      <el-alert
        v-if="lastReindex && lastReindex.error"
        class="mt-3"
        :title="lastReindex.error"
        type="error"
        :closable="false"
      />
    </el-card>

    <!-- 4. 告警状态 (P2 占位) -->
    <el-card shadow="never" class="mb-3">
      <template #header>
        <div class="flex items-center gap-2">
          <span class="font-semibold">{{ t('admin.etlview.section.alert_status') }}</span>
        </div>
      </template>
      <EtlAlertStatus />
    </el-card>

    <!-- 5. 最近一次完成结果 -->
    <el-card v-if="lastFinished" shadow="never" class="mb-3">
      <template #header>
        <div class="flex items-center gap-2">
          <span class="font-semibold">{{ t('admin.etlview.section.last_finished') }}</span>
          <el-tag :type="statusTagType(lastFinished.status)" size="small">{{ lastFinished.status }}</el-tag>
          <div class="flex-1" />
          <el-button size="small" text @click="clearLastFinished">{{ t('admin.etlview.success.phrase_21459') }}</el-button>
        </div>
      </template>
      <el-descriptions :column="4" size="small" border>
        <el-descriptions-item label="read">{{ fmt(lastFinished.read) }}</el-descriptions-item>
        <el-descriptions-item label="inserted">{{ fmt(lastFinished.inserted) }}</el-descriptions-item>
        <el-descriptions-item label="updated">{{ fmt(lastFinished.updated) }}</el-descriptions-item>
        <el-descriptions-item label="skipped">{{ fmt(lastFinished.skipped) }}</el-descriptions-item>
        <el-descriptions-item label="skipped_missing_oem">{{ fmt(lastFinished.skippedMissingOem) }}</el-descriptions-item>
        <el-descriptions-item label="skipped_null_field">{{ fmt(lastFinished.skippedNullField) }}</el-descriptions-item>
        <el-descriptions-item label="skipped_duplicate">{{ fmt(lastFinished.skippedDuplicate) }}</el-descriptions-item>
        <el-descriptions-item label="errors">{{ fmt(lastFinished.errors) }}</el-descriptions-item>
        <el-descriptions-item label="indexed">{{ fmt(lastFinished.indexed) }}</el-descriptions-item>
        <el-descriptions-item label="indexPending">{{ fmt(lastFinished.indexPending) }}</el-descriptions-item>
        <el-descriptions-item label="elapsed">{{ lastFinished.elapsedSec ?? 0 }}s</el-descriptions-item>
        <el-descriptions-item label="finishedAt">{{ lastFinished.finishedAt ?? '-' }}</el-descriptions-item>
      </el-descriptions>

      <div v-if="lastFinished.lastError" class="mt-3">
        <el-alert :title="lastFinished.lastError" type="error" :closable="false" />
      </div>

      <div v-if="lastFinished.recentErrors && lastFinished.recentErrors.length > 0" class="mt-3">
        <div class="text-sm font-semibold mb-1">{{ t('admin.etlview.section.recent_errors') }}</div>
        <el-table :data="lastFinished.recentErrors" size="small" max-height="240" border>
          <el-table-column prop="at" :label="t('admin.etlview.label.timestamp')" width="200" />
          <el-table-column prop="message" :label="t('admin.etlview.label.error')" show-overflow-tooltip />
        </el-table>
      </div>
    </el-card>

    <!-- 6. dry-run 结果 (含样本预览) -->
    <el-card v-if="lastDryRun" shadow="never" class="mb-3">
      <template #header>
        <div class="flex items-center gap-2">
          <span class="font-semibold">{{ t('admin.etlview.section.dry_run') }}</span>
          <el-tag size="small" type="info">{{ lastDryRun.mode }}</el-tag>
          <el-tag v-if="lastDryRun.samples && lastDryRun.samples.length > 0" size="small" type="success">
            {{ t('admin.etlview.dry_run.samples_count', { count: lastDryRun.samples.length }) }}
          </el-tag>
        </div>
      </template>
      <el-descriptions :column="2" size="small" border>
        <el-descriptions-item :label="t('admin.etlview.label.file_v2')">{{ lastDryRun.file }}</el-descriptions-item>
        <el-descriptions-item :label="t('admin.etlview.label.en')">{{ fmtBytes(lastDryRun.sizeBytes) }}</el-descriptions-item>
        <el-descriptions-item :label="t('admin.etlview.label.rows_count')">{{ fmt(lastDryRun.lines) }}</el-descriptions-item>
        <el-descriptions-item :label="t('common.field.mode')">{{ lastDryRun.mode }}</el-descriptions-item>
      </el-descriptions>

      <div v-if="lastDryRun.samples && lastDryRun.samples.length > 0" class="mt-3">
        <div class="text-sm font-semibold mb-1">
          {{ t('admin.etlview.dry_run.samples_preview', { count: lastDryRun.samples.length }) }}
        </div>
        <el-table
          :data="(showAllSamples ? lastDryRun.samples : lastDryRun.samples.slice(0, 10)).map((s, i) => ({ idx: i + 1, raw: s }))"
          size="small" border max-height="320"
        >
          <el-table-column prop="idx" label="#" width="50" />
          <el-table-column :label="t('admin.etlview.label.original_json')">
            <template #default="{ row }">
              <pre class="text-xs whitespace-pre-wrap break-all m-0">{{ prettyJson(row.raw) }}</pre>
            </template>
          </el-table-column>
        </el-table>
        <div class="mt-2 flex justify-end">
          <el-button text size="small" @click="showAllSamples = !showAllSamples">
            {{ showAllSamples
              ? t('admin.etlview.templatetext.collapse_show_front_rows')
              : t('admin.etlview.templatetext.expand_all', { count: lastDryRun.samples.length }) }}
          </el-button>
        </div>
      </div>
    </el-card>

    <!-- 7. 取消审计 (reason_code 饼图 + 历史) -->
    <el-card shadow="never">
      <template #header>
        <div class="flex items-center gap-2">
          <span class="font-semibold">{{ t('admin.etlview.section.audit') }}</span>
          <el-tag size="small" type="info">{{ t('admin.etlview.audit.observable_tag') }}</el-tag>
          <div class="flex-1" />
          <el-button size="small" :loading="historyLoading" @click="refreshAudit">{{ t('common.action.refresh') }}</el-button>
        </div>
      </template>
      <div class="audit-grid">
        <div class="audit-pie">
          <EtlReasonCodePie :data="reasonCodeAgg" />
        </div>
        <div class="audit-table">
          <div class="text-xs text-[var(--color-text-muted)] mb-2">
            {{ t('admin.etlview.audit.recent_20_cancelled') }}
          </div>
          <el-table :data="historyItems" size="small" max-height="380" border stripe>
            <el-table-column prop="id" label="#" width="60" />
            <el-table-column prop="entityType" label="entity" width="90" />
            <el-table-column prop="mode" label="mode" width="100" />
            <el-table-column :label="t('admin.etlview.audit.reason_code')" width="160">
              <template #default="{ row }">
                <el-tag v-if="row.reasonCode" size="small" type="info">{{ row.reasonCode }}</el-tag>
                <span v-else class="text-gray-400 text-xs">{{ t('admin.etlview.audit.legacy') }}</span>
              </template>
            </el-table-column>
            <el-table-column prop="cancelReason" :label="t('admin.etlview.label.en_v2')" show-overflow-tooltip min-width="180" />
            <el-table-column :label="t('admin.etlview.label.phrase_63454')" width="120">
              <template #default="{ row }">
                <span class="text-xs">
                  {{ row.readCount }} / {{ row.insertedCount }} / {{ row.updatedCount }}
                </span>
              </template>
            </el-table-column>
            <el-table-column :label="t('admin.etlview.label.en_v3')" width="80">
              <template #default="{ row }">
                <span class="text-xs">{{ row.durationSec.toFixed(1) }}s</span>
              </template>
            </el-table-column>
            <el-table-column :label="t('admin.etlview.label.cancel_timestamp')" width="170">
              <template #default="{ row }">
                <span class="text-xs text-[var(--color-text-muted)]">{{ (row.cancelledAt || row.finishedAt).slice(0, 19) }}</span>
              </template>
            </el-table-column>
          </el-table>
        </div>
      </div>
    </el-card>
  </div>
</template>

<style scoped>
.audit-grid {
  display: grid;
  grid-template-columns: minmax(280px, 380px) 1fr;
  gap: 24px;
  align-items: flex-start;
}
.audit-pie { padding: 8px 0; }
.audit-table { min-width: 0; }
@media (max-width: 1024px) {
  .audit-grid { grid-template-columns: 1fr; }
}
</style>
