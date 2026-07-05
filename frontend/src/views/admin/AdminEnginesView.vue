<script setup lang="ts">
// Day 10+ P2.2: Engine 字典管理页 (2 字段: engine_brand + engine_type)
import { ref, reactive, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { dictApi, type EngineItem, type EngineReorderItem } from '@/api'

const { t } = useI18n()

const items = ref<EngineItem[]>([])
const loading = ref(false)
const includeDeleted = ref(false)
const searchKw = ref('')

const dialogOpen = ref(false)
const dialogMode = ref<'create' | 'edit'>('create')
const dialogForm = reactive<{ id?: number; engineBrand: string; engineType: string; sortOrder: number }>({
  engineBrand: '', engineType: '', sortOrder: 0
})

const draggingId = ref<number | null>(null)
const dragOverId = ref<number | null>(null)

async function load() {
  loading.value = true
  try {
    const { items: list } = await dictApi.engines.list(searchKw.value || undefined, includeDeleted.value, 500)
    items.value = list
  } catch (e: any) { ElMessage.error(t('common.action.load_failed') + (e?.message || '')) }
  finally { loading.value = false }
}
function onSearch() { load() }

function openCreate() {
  dialogMode.value = 'create'; dialogForm.id = undefined
  dialogForm.engineBrand = ''; dialogForm.engineType = ''
  const maxSort = items.value.filter((x) => !x.deletedAt).reduce((m, x) => Math.max(m, x.sortOrder), 0)
  dialogForm.sortOrder = maxSort + 10
  dialogOpen.value = true
}
function openEdit(row: EngineItem) {
  dialogMode.value = 'edit'; dialogForm.id = row.id
  dialogForm.engineBrand = row.engineBrand
  dialogForm.engineType = row.engineType ?? ''
  dialogForm.sortOrder = row.sortOrder
  dialogOpen.value = true
}
async function saveDialog() {
  const b = dialogForm.engineBrand.trim()
  if (!b) { ElMessage.warning(t('admin.enginesview.warning.engine_brand_cannot_be_empty')); return }
  if (b.length > 200) { ElMessage.warning(t('admin.enginesview.warning.engine_brand_length')); return }
  const t2 = dialogForm.engineType.trim() || undefined
  try {
    if (dialogMode.value === 'create') {
      await dictApi.engines.create(b, t2, dialogForm.sortOrder); ElMessage.success(t('common.action.created'))
    } else if (dialogForm.id != null) {
      await dictApi.engines.update(dialogForm.id, { engineBrand: b, engineType: t2, sortOrder: dialogForm.sortOrder })
      ElMessage.success(t('common.action.updated'))
    }
    dialogOpen.value = false; await load()
  } catch (e: any) { ElMessage.error(e?.response?.data?.detail || e?.message || t('common.action.operation_failed')) }
}
async function softDelete(row: EngineItem) {
  const label = `${row.engineBrand}${row.engineType ? ' / ' + row.engineType : ''}`
  try { await ElMessageBox.confirm(`确定删除 "${label}" 吗? (软删除)`, t('common.action.confirm'), { type: 'warning' }) } catch { return }
  try { await dictApi.engines.delete(row.id); ElMessage.success(t('common.action.deleted')); await load() }
  catch (e: any) { ElMessage.error(e?.response?.data?.detail || e?.message || t('common.action.delete_failed')) }
}
async function restore(row: EngineItem) {
  try { await dictApi.engines.restore(row.id); ElMessage.success(t('common.action.restored')); await load() }
  catch (e: any) { ElMessage.error(e?.response?.data?.detail || e?.message || t('common.action.restore_failed')) }
}

function onDragStart(e: DragEvent, id: number) { draggingId.value = id; if (e.dataTransfer) { e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', String(id)) } }
function onDragOver(e: DragEvent, id: number) { e.preventDefault(); if (e.dataTransfer) e.dataTransfer.dropEffect = 'move'; if (draggingId.value !== id) dragOverId.value = id }
function onDragLeave(_e: DragEvent, id: number) { if (dragOverId.value === id) dragOverId.value = null }
async function onDrop(e: DragEvent, targetId: number) {
  e.preventDefault()
  const sourceId = draggingId.value; dragOverId.value = null; draggingId.value = null
  if (sourceId == null || sourceId === targetId) return
  const sourceIdx = items.value.findIndex((x) => x.id === sourceId)
  const targetIdx = items.value.findIndex((x) => x.id === targetId)
  if (sourceIdx < 0 || targetIdx < 0) return
  const moved = items.value.splice(sourceIdx, 1)[0]; items.value.splice(targetIdx, 0, moved)
  const updates: EngineReorderItem[] = items.value.map((it, idx) => ({ id: it.id, sortOrder: (idx + 1) * 10 }))
  items.value.forEach((it, idx) => { it.sortOrder = (idx + 1) * 10 })
  try { await dictApi.engines.reorder(updates); ElMessage.success(t('common.action.sort_order_saved')) }
  catch (e: any) { ElMessage.error(e?.response?.data?.detail || e?.message || t('common.action.sort_failed')); await load() }
}
function onDragEnd() { draggingId.value = null; dragOverId.value = null }

function fmtDate(iso?: string) { return iso ? iso.substring(0, 16).replace('T', ' ') : '' }
function isDraggable(row: EngineItem) { return !row.deletedAt }
function rowClass(row: EngineItem): string {
  const c: string[] = []
  if (row.deletedAt) c.push('dict-row--deleted')
  if (draggingId.value === row.id) c.push('dict-row--dragging')
  if (dragOverId.value === row.id) c.push('dict-row--dragover')
  return c.join(' ')
}
const total = computed(() => items.value.length)
const activeCount = computed(() => items.value.filter((x) => !x.deletedAt).length)
onMounted(load)
</script>

<template>
  <div class="p-3 max-w-screen-xl mx-auto">
    <div class="flex items-center gap-2 mb-3 flex-wrap">
      <h1 class="text-lg font-medium">发动机字典 (Engine)</h1>
      <span class="text-xs text-muted">P2.2 后台管理 · 2 字段: 品牌 + 型号 · 用于产品表单分区 7 发动机信息</span>
      <div class="flex-1" />
      <el-input v-model="searchKw" :placeholder="t('common.field.search_any_field')" clearable size="small" style="width: 200px" @keyup.enter="onSearch" />
      <el-button size="small" @click="onSearch">搜索</el-button>
      <el-checkbox v-model="includeDeleted" @change="load" size="small">含已删</el-checkbox>
      <el-button type="primary" size="small" @click="openCreate">新增发动机</el-button>
    </div>

    <div class="hairline" v-loading="loading">
      <div class="dict-head">
        <div class="cell-drag"></div>
        <div class="cell-id">ID</div>
        <div class="cell-brand">品牌</div>
        <div class="cell-type">型号</div>
        <div class="cell-sort">排序</div>
        <div class="cell-xref">引用</div>
        <div class="cell-updated">更新</div>
        <div class="cell-status">状态</div>
        <div class="cell-action">操作</div>
      </div>
      <div v-for="row in items" :key="row.id"
        :class="['dict-row', rowClass(row)]" :draggable="isDraggable(row)"
        @dragstart="onDragStart($event, row.id)" @dragover="onDragOver($event, row.id)"
        @dragleave="onDragLeave($event, row.id)" @drop="onDrop($event, row.id)" @dragend="onDragEnd">
        <div class="cell-drag"><span v-if="isDraggable(row)" class="drag-handle">≡</span></div>
        <div class="cell-id">{{ row.id }}</div>
        <div class="cell-brand">{{ row.engineBrand }}</div>
        <div class="cell-type">{{ row.engineType || '—' }}</div>
        <div class="cell-sort">{{ row.sortOrder }}</div>
        <div class="cell-xref">{{ row.xrefCount }}</div>
        <div class="cell-updated">{{ fmtDate(row.updatedAt) }}</div>
        <div class="cell-status">
          <el-tag v-if="row.deletedAt" type="info" size="small">已删</el-tag>
          <el-tag v-else type="success" size="small">启用</el-tag>
        </div>
        <div class="cell-action">
          <el-button size="small" text @click="openEdit(row)" :disabled="!!row.deletedAt">编辑</el-button>
          <el-button v-if="!row.deletedAt" size="small" text type="warning" @click="softDelete(row)">删除</el-button>
          <el-button v-else size="small" text type="success" @click="restore(row)">恢复</el-button>
        </div>
      </div>
      <div v-if="!loading && items.length === 0" class="dict-empty" > {{ t('common.action.no_data_click_top_right') }}新增发动机开始</div>
    </div>

    <div class="mt-2 text-xs text-muted">{{ t("common.dictviewcommon.total_drag", { total, active: activeCount, soft: total - activeCount }) }}</div>

    <el-dialog v-model="dialogOpen" :title="dialogMode === 'create' ? t('admin.enginesview.title.add_engine') : t('admin.enginesview.title.edit_engine')" width="540px">
      <el-form :model="dialogForm" label-width="120px" size="small">
        <el-form-item :label="t('common.action.brand')" required>
          <el-input v-model="dialogForm.engineBrand" :placeholder="t('admin.enginesview.placeholder.e_g_cummins')" maxlength="200" show-word-limit />
        </el-form-item>
        <el-form-item :label="t('common.action.model')">
          <el-input v-model="dialogForm.engineType" :placeholder="t('admin.enginesview.placeholder.e_g_isb_l')" maxlength="200" show-word-limit />
          <div class="text-xs text-muted mt-1">2 字段组成 UNIQUE 索引, 型号可空</div>
        </el-form-item>
        <el-form-item :label="t('common.action.sort_order')">
          <el-input-number v-model="dialogForm.sortOrder" :min="0" :step="10" style="width: 100%" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogOpen = false">取消</el-button>
        <el-button type="primary" @click="saveDialog">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.dict-head, .dict-row { display: grid; grid-template-columns: 32px 60px 1.4fr 1.4fr 80px 100px 140px 80px 200px; align-items: center; font-size: 12px; border-bottom: 1px solid var(--color-border); }
.dict-head { font-weight: 500; color: var(--color-text-muted); background: var(--color-bg-hover); height: 32px; }
.dict-row { height: 36px; background: var(--color-bg-elevated); transition: background-color 0.15s, border-top 0.1s; }
.dict-head > div, .dict-row > div { padding: 0 8px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.cell-drag { text-align: center; }
.drag-handle { cursor: move; color: var(--color-text-muted); font-size: 16px; user-select: none; display: inline-block; padding: 0 4px; }
.drag-handle:hover { color: var(--color-accent); }
.cell-id, .cell-sort, .cell-xref { text-align: right; }
.dict-row:hover { background: var(--color-bg-hover); }
.dict-row--dragging { opacity: 0.4; }
.dict-row--dragover { border-top: 2px solid var(--color-accent) !important; background: var(--color-bg-hover); }
.dict-row--deleted { color: var(--color-text-muted); background: var(--color-bg-hover); }
.dict-empty { padding: 24px 0; text-align: center; color: var(--color-text-muted); font-size: 12px; }
</style>
