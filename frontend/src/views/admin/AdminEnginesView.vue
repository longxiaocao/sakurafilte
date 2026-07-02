<script setup lang="ts">
// Day 10+ P2.2: Engine 字典管理页 (2 字段: engine_brand + engine_type)
import { ref, reactive, onMounted, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { dictApi, type EngineItem, type EngineReorderItem } from '@/api'

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
  } catch (e: any) { ElMessage.error('加载失败: ' + (e?.message || '')) }
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
  if (!b) { ElMessage.warning('发动机品牌不能为空'); return }
  if (b.length > 200) { ElMessage.warning('发动机品牌长度不能超过 200'); return }
  const t = dialogForm.engineType.trim() || undefined
  try {
    if (dialogMode.value === 'create') {
      await dictApi.engines.create(b, t, dialogForm.sortOrder); ElMessage.success('已新增')
    } else if (dialogForm.id != null) {
      await dictApi.engines.update(dialogForm.id, { engineBrand: b, engineType: t, sortOrder: dialogForm.sortOrder })
      ElMessage.success('已更新')
    }
    dialogOpen.value = false; await load()
  } catch (e: any) { ElMessage.error(e?.response?.data?.detail || e?.message || '操作失败') }
}
async function softDelete(row: EngineItem) {
  const label = `${row.engineBrand}${row.engineType ? ' / ' + row.engineType : ''}`
  try { await ElMessageBox.confirm(`确定删除 "${label}" 吗? (软删除)`, '确认', { type: 'warning' }) } catch { return }
  try { await dictApi.engines.delete(row.id); ElMessage.success('已删除'); await load() }
  catch (e: any) { ElMessage.error(e?.response?.data?.detail || e?.message || '删除失败') }
}
async function restore(row: EngineItem) {
  try { await dictApi.engines.restore(row.id); ElMessage.success('已恢复'); await load() }
  catch (e: any) { ElMessage.error(e?.response?.data?.detail || e?.message || '恢复失败') }
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
  try { await dictApi.engines.reorder(updates); ElMessage.success('排序已保存') }
  catch (e: any) { ElMessage.error(e?.response?.data?.detail || e?.message || '排序失败'); await load() }
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
      <el-input v-model="searchKw" placeholder="搜索任一字段" clearable size="small" style="width: 200px" @keyup.enter="onSearch" />
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
      <div v-if="!loading && items.length === 0" class="dict-empty">暂无数据, 点击右上"新增发动机"开始</div>
    </div>

    <div class="mt-2 text-xs text-muted">共 {{ total }} 条 (启用 {{ activeCount }}, 软删 {{ total - activeCount }}) · 拖动"≡"列重排</div>

    <el-dialog v-model="dialogOpen" :title="dialogMode === 'create' ? '新增发动机' : '编辑发动机'" width="540px">
      <el-form :model="dialogForm" label-width="120px" size="small">
        <el-form-item label="品牌" required>
          <el-input v-model="dialogForm.engineBrand" placeholder="例: CUMMINS" maxlength="200" show-word-limit />
        </el-form-item>
        <el-form-item label="型号">
          <el-input v-model="dialogForm.engineType" placeholder="例: ISB 4.5L (可空)" maxlength="200" show-word-limit />
          <div class="text-xs text-muted mt-1">2 字段组成 UNIQUE 索引, 型号可空</div>
        </el-form-item>
        <el-form-item label="排序">
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
.dict-head, .dict-row { display: grid; grid-template-columns: 32px 60px 1.4fr 1.4fr 80px 100px 140px 80px 200px; align-items: center; font-size: 12px; border-bottom: 1px solid #e5e7eb; }
.dict-head { font-weight: 500; color: #6b7280; background: #f9fafb; height: 32px; }
.dict-row { height: 36px; background: #fff; transition: background-color 0.15s, border-top 0.1s; }
.dict-head > div, .dict-row > div { padding: 0 8px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.cell-drag { text-align: center; }
.drag-handle { cursor: move; color: #9ca3af; font-size: 16px; user-select: none; display: inline-block; padding: 0 4px; }
.drag-handle:hover { color: #2563eb; }
.cell-id, .cell-sort, .cell-xref { text-align: right; }
.dict-row:hover { background: #f9fafb; }
.dict-row--dragging { opacity: 0.4; }
.dict-row--dragover { border-top: 2px solid #2563eb !important; background: #eff6ff; }
.dict-row--deleted { color: #9ca3af; background: #fafafa; }
.dict-empty { padding: 24px 0; text-align: center; color: #9ca3af; font-size: 12px; }
</style>
