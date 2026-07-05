<script setup lang="ts">
// Day 10+ P2.2: OEM 3 字典管理页 (复用单字段模板)
import { ref, reactive, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { dictApi, type OemNo3Item, type OemNo3ReorderItem } from '@/api'

const { t } = useI18n()

const items = ref<OemNo3Item[]>([])
const loading = ref(false)
const includeDeleted = ref(false)
const searchKw = ref('')

const dialogOpen = ref(false)
const dialogMode = ref<'create' | 'edit'>('create')
const dialogForm = reactive<{ id?: number; oemNo3: string; sortOrder: number }>({
  oemNo3: '', sortOrder: 0
})

const draggingId = ref<number | null>(null)
const dragOverId = ref<number | null>(null)

async function load() {
  loading.value = true
  try {
    const { items: list } = await dictApi.oemNo3s.list(searchKw.value || undefined, includeDeleted.value, 500)
    items.value = list
  } catch (e: any) { ElMessage.error(t('admin.oemno3sview.error.l26_') + (e?.message || '')) }
  finally { loading.value = false }
}
function onSearch() { load() }

function openCreate() {
  dialogMode.value = 'create'; dialogForm.id = undefined; dialogForm.oemNo3 = ''
  const maxSort = items.value.filter((x) => !x.deletedAt).reduce((m, x) => Math.max(m, x.sortOrder), 0)
  dialogForm.sortOrder = maxSort + 10
  dialogOpen.value = true
}
function openEdit(row: OemNo3Item) {
  dialogMode.value = 'edit'; dialogForm.id = row.id; dialogForm.oemNo3 = row.oemNo3; dialogForm.sortOrder = row.sortOrder
  dialogOpen.value = true
}
async function saveDialog() {
  const v = dialogForm.oemNo3.trim()
  if (!v) { ElMessage.warning(t('admin.oemno3sview.warning.l43_oem_3')); return }
  if (v.length > 200) { ElMessage.warning(t('admin.oemno3sview.warning.l44_oem_3_200')); return }
  try {
    if (dialogMode.value === 'create') {
      await dictApi.oemNo3s.create(v, dialogForm.sortOrder); ElMessage.success(t('admin.oemno3sview.success.l47_'))
    } else if (dialogForm.id != null) {
      await dictApi.oemNo3s.update(dialogForm.id, { oemNo3: v, sortOrder: dialogForm.sortOrder })
      ElMessage.success(t('admin.oemno3sview.success.l50_'))
    }
    dialogOpen.value = false; await load()
  } catch (e: any) { ElMessage.error(e?.response?.data?.detail || e?.message || t('admin.oemno3sview.error.l53_')) }
}
async function softDelete(row: OemNo3Item) {
  try { await ElMessageBox.confirm(`确定删除 "${row.oemNo3}" 吗? (软删除)`, t('admin.oemno3sview.warning.l56_'), { type: 'warning' }) } catch { return }
  try { await dictApi.oemNo3s.delete(row.id); ElMessage.success(t('admin.oemno3sview.success.l57_')); await load() }
  catch (e: any) { ElMessage.error(e?.response?.data?.detail || e?.message || t('admin.oemno3sview.error.l58_')) }
}
async function restore(row: OemNo3Item) {
  try { await dictApi.oemNo3s.restore(row.id); ElMessage.success(t('admin.oemno3sview.success.l61_')); await load() }
  catch (e: any) { ElMessage.error(e?.response?.data?.detail || e?.message || t('admin.oemno3sview.error.l62_')) }
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
  const updates: OemNo3ReorderItem[] = items.value.map((it, idx) => ({ id: it.id, sortOrder: (idx + 1) * 10 }))
  items.value.forEach((it, idx) => { it.sortOrder = (idx + 1) * 10 })
  try { await dictApi.oemNo3s.reorder(updates); ElMessage.success(t('admin.oemno3sview.success.l78_')) }
  catch (e: any) { ElMessage.error(e?.response?.data?.detail || e?.message || t('admin.oemno3sview.error.l79_')); await load() }
}
function onDragEnd() { draggingId.value = null; dragOverId.value = null }

function fmtDate(iso?: string) { return iso ? iso.substring(0, 16).replace('T', ' ') : '' }
function isDraggable(row: OemNo3Item) { return !row.deletedAt }
function rowClass(row: OemNo3Item): string {
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
      <h1 class="text-lg font-medium">OEM 3 字典</h1>
      <span class="text-xs text-muted">P2.2 后台管理 · 用于交叉引用分区 2 oem_no_3 自动补全</span>
      <div class="flex-1" />
      <el-input v-model="searchKw" :placeholder="t('admin.oemno3sview.placeholder.l103_oem_3')" clearable size="small" style="width: 200px" @keyup.enter="onSearch" />
      <el-button size="small" @click="onSearch">搜索</el-button>
      <el-checkbox v-model="includeDeleted" @change="load" size="small">含已删</el-checkbox>
      <el-button type="primary" size="small" @click="openCreate">新增 OEM 3</el-button>
    </div>

    <div class="hairline" v-loading="loading">
      <div class="dict-head">
        <div class="cell-drag"></div>
        <div class="cell-id">ID</div>
        <div class="cell-name">OEM 3</div>
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
        <div class="cell-name">{{ row.oemNo3 }}</div>
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
      <div v-if="!loading && items.length === 0" class="dict-empty" > {{ t('admin.oemno3sview.string.l140_') }}新增 OEM 3开始</div>
    </div>

    <div class="mt-2 text-xs text-muted">{{ t("common.dictviewcommon.total_drag", { total, active: activeCount, soft: total - activeCount }) }}</div>

    <el-dialog v-model="dialogOpen" :title="dialogMode === 'create' ? t('admin.oemno3sview.title.l145_oem_3') : t('admin.oemno3sview.title.l145_oem_3_2')" width="480px">
      <el-form :model="dialogForm" label-width="100px" size="small">
        <el-form-item label="OEM 3" required>
          <el-input v-model="dialogForm.oemNo3" :placeholder="t('admin.oemno3sview.placeholder.l148_11427622448')" maxlength="200" show-word-limit />
        </el-form-item>
        <el-form-item :label="t('admin.oemno3sview.label.l150_')">
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
.dict-head, .dict-row { display: grid; grid-template-columns: 32px 60px 1fr 80px 100px 140px 80px 200px; align-items: center; font-size: 12px; border-bottom: 1px solid var(--color-border); }
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
