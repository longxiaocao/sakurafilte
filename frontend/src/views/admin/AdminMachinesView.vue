<script setup lang="ts">
// Day 10+ P2.2: Machine 字典管理页 (3 字段: machine_brand + machine_model + machine_name)
// P2.3: 新增 machine_category 编辑 (4 大类: Agriculture/Commercial/Construction/others)
import { ref, reactive, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { dictApi, type MachineItem, type MachineReorderItem } from '@/api'

const { t } = useI18n()

const items = ref<MachineItem[]>([])
const loading = ref(false)
const includeDeleted = ref(false)
const searchKw = ref('')

// P2.3: 4 大类常量, 给 <el-select> 用
const CATEGORY_OPTIONS = ['Agriculture', 'Commercial', 'Construction', 'others'] as const
type Category = (typeof CATEGORY_OPTIONS)[number]

const dialogOpen = ref(false)
const dialogMode = ref<'create' | 'edit'>('create')
const dialogForm = reactive<{
  id?: number
  machineBrand: string
  machineModel: string
  machineName: string
  machineCategory: Category
  sortOrder: number
}>({
  machineBrand: '', machineModel: '', machineName: '', machineCategory: 'others', sortOrder: 0
})

const draggingId = ref<number | null>(null)
const dragOverId = ref<number | null>(null)

async function load() {
  loading.value = true
  try {
    const { items: list } = await dictApi.machines.list(searchKw.value || undefined, includeDeleted.value, 500)
    items.value = list
  } catch (e: any) { ElMessage.error(t('admin.machinesview.error.l38_') + (e?.message || '')) }
  finally { loading.value = false }
}
function onSearch() { load() }

function openCreate() {
  dialogMode.value = 'create'; dialogForm.id = undefined
  dialogForm.machineBrand = ''; dialogForm.machineModel = ''; dialogForm.machineName = ''
  dialogForm.machineCategory = 'others'
  const maxSort = items.value.filter((x) => !x.deletedAt).reduce((m, x) => Math.max(m, x.sortOrder), 0)
  dialogForm.sortOrder = maxSort + 10
  dialogOpen.value = true
}
function openEdit(row: MachineItem) {
  dialogMode.value = 'edit'; dialogForm.id = row.id
  dialogForm.machineBrand = row.machineBrand
  dialogForm.machineModel = row.machineModel ?? ''
  dialogForm.machineName = row.machineName ?? ''
  // P2.3: 兜底 'others' (兼容老数据无 category 字段)
  dialogForm.machineCategory = (row.machineCategory as Category) ?? 'others'
  dialogForm.sortOrder = row.sortOrder
  dialogOpen.value = true
}
async function saveDialog() {
  const b = dialogForm.machineBrand.trim()
  if (!b) { ElMessage.warning(t('admin.machinesview.warning.l63_')); return }
  if (b.length > 200) { ElMessage.warning(t('admin.machinesview.warning.l64_200')); return }
  const model = dialogForm.machineModel.trim() || undefined
  const name = dialogForm.machineName.trim() || undefined
  try {
    if (dialogMode.value === 'create') {
      // Day 11 Phase 1 BUG FIX B: create 时也传 machineCategory (之前漏传, 后端默认 "others")
      await dictApi.machines.create(b, model, name, dialogForm.sortOrder, dialogForm.machineCategory); ElMessage.success(t('admin.machinesview.success.l70_'))
    } else if (dialogForm.id != null) {
      // P2.3: 提交时把 machineCategory 一并 PUT
      await dictApi.machines.update(dialogForm.id, {
        machineBrand: b, machineModel: model, machineName: name,
        sortOrder: dialogForm.sortOrder, machineCategory: dialogForm.machineCategory
      })
      ElMessage.success(t('admin.machinesview.success.l77_'))
    }
    dialogOpen.value = false; await load()
  } catch (e: any) { ElMessage.error(e?.response?.data?.detail || e?.message || t('admin.machinesview.error.l80_')) }
}
async function softDelete(row: MachineItem) {
  const label = `${row.machineBrand}${row.machineModel ? ' / ' + row.machineModel : ''}${row.machineName ? ' / ' + row.machineName : ''}`
  try { await ElMessageBox.confirm(`确定删除 "${label}" 吗? (软删除)`, t('admin.machinesview.warning.l84_'), { type: 'warning' }) } catch { return }
  try { await dictApi.machines.delete(row.id); ElMessage.success(t('admin.machinesview.success.l85_')); await load() }
  catch (e: any) { ElMessage.error(e?.response?.data?.detail || e?.message || t('admin.machinesview.error.l86_')) }
}
async function restore(row: MachineItem) {
  try { await dictApi.machines.restore(row.id); ElMessage.success(t('admin.machinesview.success.l89_')); await load() }
  catch (e: any) { ElMessage.error(e?.response?.data?.detail || e?.message || t('admin.machinesview.error.l90_')) }
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
  const updates: MachineReorderItem[] = items.value.map((it, idx) => ({ id: it.id, sortOrder: (idx + 1) * 10 }))
  items.value.forEach((it, idx) => { it.sortOrder = (idx + 1) * 10 })
  try { await dictApi.machines.reorder(updates); ElMessage.success(t('admin.machinesview.success.l106_')) }
  catch (e: any) { ElMessage.error(e?.response?.data?.detail || e?.message || t('admin.machinesview.error.l107_')); await load() }
}
function onDragEnd() { draggingId.value = null; dragOverId.value = null }

function fmtDate(iso?: string) { return iso ? iso.substring(0, 16).replace('T', ' ') : '' }
function isDraggable(row: MachineItem) { return !row.deletedAt }
function rowClass(row: MachineItem): string {
  const c: string[] = []
  if (row.deletedAt) c.push('dict-row--deleted')
  if (draggingId.value === row.id) c.push('dict-row--dragging')
  if (dragOverId.value === row.id) c.push('dict-row--dragover')
  return c.join(' ')
}
// P2.3: category 标签颜色 (4 大类各一色)
function categoryTagType(cat?: string): 'success' | 'warning' | 'info' | 'primary' {
  switch (cat) {
    case 'Agriculture': return 'success'   // 绿 (农林)
    case 'Commercial': return 'primary'   // 蓝 (商用)
    case 'Construction': return 'warning' // 橙 (工程)
    default: return 'info'                 // 灰 (others)
  }
}
const total = computed(() => items.value.length)
const activeCount = computed(() => items.value.filter((x) => !x.deletedAt).length)
onMounted(load)
</script>

<template>
  <div class="p-3 max-w-screen-xl mx-auto">
    <div class="flex items-center gap-2 mb-3 flex-wrap">
      <h1 class="text-lg font-medium">机型字典 (Machine)</h1>
      <span class="text-xs text-muted">P2.2 后台管理 · 3 字段: 品牌 + 型号 + 名称 · 用于产品表单分区 7 适用车型</span>
      <div class="flex-1" />
      <el-input v-model="searchKw" :placeholder="t('admin.machinesview.placeholder.l140_')" clearable size="small" style="width: 200px" @keyup.enter="onSearch" />
      <el-button size="small" @click="onSearch">搜索</el-button>
      <el-checkbox v-model="includeDeleted" @change="load" size="small">含已删</el-checkbox>
      <el-button type="primary" size="small" @click="openCreate">新增机型</el-button>
    </div>

    <div class="hairline" v-loading="loading">
      <div class="dict-head">
        <div class="cell-drag"></div>
        <div class="cell-id">ID</div>
        <div class="cell-brand">品牌</div>
        <div class="cell-model">型号</div>
        <div class="cell-name">名称</div>
        <!-- P2.3: 新增分类列 -->
        <div class="cell-category">分类</div>
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
        <div class="cell-brand">{{ row.machineBrand }}</div>
        <div class="cell-model">{{ row.machineModel || '—' }}</div>
        <div class="cell-name">{{ row.machineName || '—' }}</div>
        <!-- P2.3: 显示分类 tag -->
        <div class="cell-category">
          <el-tag :type="categoryTagType(row.machineCategory)" size="small">{{ row.machineCategory || 'others' }}</el-tag>
        </div>
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
      <div v-if="!loading && items.length === 0" class="dict-empty" > {{ t('admin.machinesview.string.l187_') }}新增机型开始</div>
    </div>

    <div class="mt-2 text-xs text-muted">{{ t("common.dictviewcommon.total_drag", { total, active: activeCount, soft: total - activeCount }) }}</div>

    <el-dialog v-model="dialogOpen" :title="dialogMode === 'create' ? t('admin.machinesview.title.l192_') : t('admin.machinesview.title.l192__2')" width="560px">
      <el-form :model="dialogForm" label-width="120px" size="small">
        <el-form-item :label="t('admin.machinesview.label.l194_')" required>
          <el-input v-model="dialogForm.machineBrand" :placeholder="t('admin.machinesview.placeholder.l195_bosch')" maxlength="200" show-word-limit />
        </el-form-item>
        <el-form-item :label="t('admin.machinesview.label.l197_')">
          <el-input v-model="dialogForm.machineModel" :placeholder="t('admin.machinesview.placeholder.l198_0_451_103_001')" maxlength="200" show-word-limit />
        </el-form-item>
        <el-form-item :label="t('admin.machinesview.label.l200_')">
          <el-input v-model="dialogForm.machineName" :placeholder="t('admin.machinesview.placeholder.l201_tractor_x300')" maxlength="200" show-word-limit />
          <div class="text-xs text-muted mt-1">3 字段组成 UNIQUE 索引, 任一字段可空</div>
        </el-form-item>
        <!-- P2.3: 分类下拉 (4 大类) -->
        <el-form-item :label="t('admin.machinesview.label.l205_')">
          <el-select v-model="dialogForm.machineCategory" :placeholder="t('admin.machinesview.placeholder.l206_4')" style="width: 100%">
            <el-option v-for="opt in CATEGORY_OPTIONS" :key="opt" :label="opt" :value="opt" />
          </el-select>
          <div class="text-xs text-muted mt-1">P2.3: 4 大类 (Agriculture/Commercial/Construction/others) 用于前台按场景聚合品牌</div>
        </el-form-item>
        <el-form-item :label="t('admin.machinesview.label.l211_')">
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
/* P2.3: 加 1 列 (cell-category 80px), 总宽度 = 32+60+1fr+1.2fr+1.2fr+80+80+100+140+80+200 = 拖列 + 9 数据列 */
.dict-head, .dict-row { display: grid; grid-template-columns: 32px 60px 1fr 1.2fr 1.2fr 100px 80px 100px 140px 80px 200px; align-items: center; font-size: 12px; border-bottom: 1px solid var(--color-border); }
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
