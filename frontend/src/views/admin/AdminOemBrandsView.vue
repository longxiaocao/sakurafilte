<script setup lang="ts">
// Day 10: OEM 品牌字典管理页 (P1.3)
//   - 列表 (默认仅未删, 开关切换看已删)
//   - 增 / 改 / 删 (软) / 恢复
//   - HTML5 原生拖拽排序 (无新依赖, 避免引入 sortablejs)
//     设计: 每行 draggable=true, dragstart 记录源 id, dragover 阻止默认 + 高亮目标行
//     drop 时本地重排 → 重新分配 sortOrder (步长 10) → 调 reorder API 持久化
//   - 不写 product_history: 字典变更不属产品业务变更
//   - typeahead 数据由 G1.6 dictApi 提供, 在 AdminProductFormView 分区 2 用到
import { ref, reactive, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { dictApi, type OemBrandItem, type OemBrandReorderItem } from '@/api'
import SkeletonCard from '@/components/SkeletonCard.vue'
import { useVisibilityRefresh } from '@/composables/useVisibilityRefresh'

const { t } = useI18n()

const items = ref<OemBrandItem[]>([])
const loading = ref(false)
// V24-F102 (P2-2, 规则 8): 加 loadError, 加载失败时显示持久 el-alert + 重试按钮
const loadError = ref<string | null>(null)
const includeDeleted = ref(false)
const searchKw = ref('')

// 新增 / 编辑 弹窗
const dialogOpen = ref(false)
const dialogMode = ref<'create' | 'edit'>('create')
const dialogForm = reactive<{ id?: number; brand: string; sortOrder: number }>({
  brand: '',
  sortOrder: 0
})

// 拖拽状态 (HTML5 原生, 不引 Sortable.js)
const draggingId = ref<number | null>(null)
const dragOverId = ref<number | null>(null)

async function load() {
  loading.value = true
  // V24-F102 (P2-2, 规则 8): 进入 load 时清空 loadError, 避免上次失败提示残留
  loadError.value = null
  try {
    const { items: list } = await dictApi.oemBrands.list(searchKw.value || undefined, includeDeleted.value, 500)
    items.value = list
  } catch (e: any) {
    ElMessage.error(t('common.action.load_failed') + (e?.message || ''))
    // V24-F102 (P2-2, 规则 8): 持久 error UI, 让用户能看到错误并重试
    loadError.value = e?.response?.data?.detail || e?.message || '字典加载失败'
  } finally {
    loading.value = false
  }
}

function onSearch() { load() }

function openCreate() {
  dialogMode.value = 'create'
  dialogForm.id = undefined
  dialogForm.brand = ''
  // 默认 sortOrder = max + 10 (与后端 CreateOemBrandAsync 一致)
  const maxSort = items.value.filter((x) => !x.deletedAt).reduce((m, x) => Math.max(m, x.sortOrder), 0)
  dialogForm.sortOrder = maxSort + 10
  dialogOpen.value = true
}

function openEdit(row: OemBrandItem) {
  dialogMode.value = 'edit'
  dialogForm.id = row.id
  dialogForm.brand = row.brand
  dialogForm.sortOrder = row.sortOrder
  dialogOpen.value = true
}

async function saveDialog() {
  if (!dialogForm.brand.trim()) {
    ElMessage.warning(t('admin.oembrandsview.warning.brand_cannot_be_empty'))
    return
  }
  if (dialogForm.brand.length > 100) {
    ElMessage.warning(t('admin.oembrandsview.warning.brand_length'))
    return
  }
  try {
    if (dialogMode.value === 'create') {
      await dictApi.oemBrands.create(dialogForm.brand.trim(), dialogForm.sortOrder)
      ElMessage.success(t('common.action.created'))
    } else if (dialogForm.id != null) {
      await dictApi.oemBrands.update(dialogForm.id, {
        brand: dialogForm.brand.trim(),
        sortOrder: dialogForm.sortOrder
      })
      ElMessage.success(t('common.action.updated'))
    }
    dialogOpen.value = false
    await load()
  } catch (e: any) {
    // 后端 ProblemDetails 的 detail 字段优先显示
    const detail = e?.response?.data?.detail || e?.message || t('common.action.operation_failed')
    ElMessage.error(detail)
  }
}

async function softDelete(row: OemBrandItem) {
  try {
    await ElMessageBox.confirm(
      `确定删除品牌 "${row.brand}" 吗? (软删除)`,
      t('common.action.confirm'),
      { type: 'warning' }
    )
  } catch {
    return  // 用户取消
  }
  try {
    await dictApi.oemBrands.delete(row.id)
    ElMessage.success(t('common.action.deleted'))
    await load()
  } catch (e: any) {
    const detail = e?.response?.data?.detail || e?.message || t('common.action.delete_failed')
    ElMessage.error(detail)
  }
}

async function restore(row: OemBrandItem) {
  try {
    await dictApi.oemBrands.restore(row.id)
    ElMessage.success(t('common.action.restored'))
    await load()
  } catch (e: any) {
    const detail = e?.response?.data?.detail || e?.message || t('common.action.restore_failed')
    ElMessage.error(detail)
  }
}

// ========== 拖拽排序 (HTML5 原生) ==========
//   dragstart: 记录源行 id (必 setData 否则 Firefox 不触发 drop)
//   dragover:  preventDefault 允许 drop + 高亮目标行
//   dragleave: 清除高亮
//   drop:       本地重排 → 重新分配 sortOrder → 调 API
function onDragStart(e: DragEvent, id: number) {
  draggingId.value = id
  if (e.dataTransfer) {
    e.dataTransfer.effectAllowed = 'move'
    e.dataTransfer.setData('text/plain', String(id))
  }
}
function onDragOver(e: DragEvent, id: number) {
  e.preventDefault()
  if (e.dataTransfer) e.dataTransfer.dropEffect = 'move'
  if (draggingId.value !== id) dragOverId.value = id
}
function onDragLeave(_e: DragEvent, id: number) {
  if (dragOverId.value === id) dragOverId.value = null
}
async function onDrop(e: DragEvent, targetId: number) {
  e.preventDefault()
  const sourceId = draggingId.value
  dragOverId.value = null
  draggingId.value = null
  if (sourceId == null || sourceId === targetId) return
  const sourceIdx = items.value.findIndex((x) => x.id === sourceId)
  const targetIdx = items.value.findIndex((x) => x.id === targetId)
  if (sourceIdx < 0 || targetIdx < 0) return
  // 1) 本地重排
  const moved = items.value.splice(sourceIdx, 1)[0]
  items.value.splice(targetIdx, 0, moved)
  // 2) 重新分配 sortOrder (步长 10, 与后端默认一致; 已删的也分配, 保持连续)
  const updates: OemBrandReorderItem[] = items.value.map((it, idx) => ({
    id: it.id,
    sortOrder: (idx + 1) * 10
  }))
  // 3) 先把 sortOrder 写回本地, 避免 UI 闪烁
  items.value.forEach((it, idx) => { it.sortOrder = (idx + 1) * 10 })
  // 4) 调 API
  try {
    await dictApi.oemBrands.reorder(updates)
    ElMessage.success(t('common.action.sort_order_saved'))
  } catch (e: any) {
    const detail = e?.response?.data?.detail || e?.message || t('common.action.sort_failed')
    ElMessage.error(detail)
    // 失败回滚: 重新加载
    await load()
  }
}
function onDragEnd() {
  draggingId.value = null
  dragOverId.value = null
}

function fmtDate(iso?: string) {
  if (!iso) return ''
  return iso.substring(0, 16).replace('T', ' ')
}

// 拖拽禁用已删项 (避免打乱含已删集合的视觉顺序)
function isDraggable(row: OemBrandItem) {
  return !row.deletedAt
}

// 表格行视觉态
function rowClass(row: OemBrandItem): string {
  const classes: string[] = []
  if (row.deletedAt) classes.push('dict-row--deleted')
  if (draggingId.value === row.id) classes.push('dict-row--dragging')
  if (dragOverId.value === row.id) classes.push('dict-row--dragover')
  return classes.join(' ')
}

const total = computed(() => items.value.length)
const activeCount = computed(() => items.value.filter((x) => !x.deletedAt).length)

// V24-F103 (P2-2): 跨标签页 stale 数据感知, 页面重新可见时自动刷新
useVisibilityRefresh(load)
onMounted(load)
</script>

<template>
  <div class="p-3 w-full">
    <!-- 顶部工具条 -->
    <div class="flex items-center gap-2 mb-3 flex-wrap">
      <h1 class="text-lg font-medium">OEM 品牌字典</h1>
      <span class="text-xs text-muted">P1.3 后台管理 · 用于产品表单分区 2 自动补全</span>
      <div class="flex-1" />
      <el-input
        v-model="searchKw"
        :placeholder="t('admin.oembrandsview.placeholder.search_brand')"
        clearable
        size="small"
        style="width: 200px"
        @keyup.enter="onSearch"
      />
      <el-button size="small" @click="onSearch">搜索</el-button>
      <el-checkbox v-model="includeDeleted" @change="load" size="small">含已删</el-checkbox>
      <el-button type="primary" size="small" @click="openCreate">新增品牌</el-button>
    </div>

    <!-- V24-F102 (P2-2, 规则 8): 加载失败时显示持久 el-alert + 重试按钮 -->
    <el-alert
      v-if="loadError"
      type="error"
      show-icon
      :closable="false"
      class="mb-2"
    >
      <template #default>
        <div class="flex items-center justify-between">
          <span>{{ loadError }}</span>
          <el-button size="small" @click="load" :disabled="loading">
            {{ loading ? '重试中…' : '重试' }}
          </el-button>
        </div>
      </template>
    </el-alert>
    <!-- V24-F102 (P1-2): 首屏骨架屏, 仅首次加载且无数据时显示 -->
    <SkeletonCard v-if="loading && items.length === 0 && !loadError" variant="table-row" :count="5" />
    <!-- 自定义可拖拽列表 (不用 el-table, 因为 el-table 行事件绑定复杂) -->
    <div class="hairline" v-loading="loading">
      <!-- 表头 -->
      <div class="dict-head">
        <div class="cell-drag"></div>
        <div class="cell-id">ID</div>
        <div class="cell-brand">品牌</div>
        <div class="cell-sort">排序</div>
        <div class="cell-xref">xref 引用</div>
        <div class="cell-updated">更新</div>
        <div class="cell-status">状态</div>
        <div class="cell-action">操作</div>
      </div>
      <!-- 表体: v-for 自定义行, 每行 draggable -->
      <div
        v-for="row in items"
        :key="row.id"
        :class="['dict-row', rowClass(row)]"
        :draggable="isDraggable(row)"
        @dragstart="onDragStart($event, row.id)"
        @dragover="onDragOver($event, row.id)"
        @dragleave="onDragLeave($event, row.id)"
        @drop="onDrop($event, row.id)"
        @dragend="onDragEnd"
      >
        <div class="cell-drag">
          <span v-if="isDraggable(row)" class="drag-handle" :title="t('common.field.drag_to_sort')">≡</span>
        </div>
        <div class="cell-id">{{ row.id }}</div>
        <div class="cell-brand">{{ row.brand }}</div>
        <div class="cell-sort">{{ row.sortOrder }}</div>
        <div class="cell-xref">{{ row.xrefCount }}</div>
        <div class="cell-updated">{{ fmtDate(row.updatedAt) }}</div>
        <div class="cell-status">
          <el-tag v-if="row.deletedAt" type="info" size="small">已删</el-tag>
          <el-tag v-else type="success" size="small">启用</el-tag>
        </div>
        <div class="cell-action">
          <el-button size="small" text @click="openEdit(row)" :disabled="!!row.deletedAt">编辑</el-button>
          <el-button
            v-if="!row.deletedAt"
            size="small"
            text
            type="warning"
            @click="softDelete(row)"
          >删除</el-button>
          <el-button
            v-else
            size="small"
            text
            type="success"
            @click="restore(row)"
          >恢复</el-button>
        </div>
      </div>
      <!-- V24-F103 (P2-1): 空状态文案与其他 7 个字典页统一, 修复 i18n key 字面量 BUG -->
      <div v-if="!loading && items.length === 0" class="dict-empty">
        {{ t('common.action.no_data_click_top_right') }}新增品牌开始
      </div>
    </div>

    <!-- 底部统计 -->
    <div class="mt-2 text-xs text-muted">
      共 {{ total }} 条 (启用 {{ activeCount }}, 软删 {{ total - activeCount }}) · 拖动"≡"列重排, 释放后自动保存
    </div>

    <!-- 新增 / 编辑 弹窗 -->
    <el-dialog
      v-model="dialogOpen"
      :title="dialogMode === 'create' ? t('admin.oembrandsview.title.add_oem_brand') : t('admin.oembrandsview.title.edit_oem_brand')"
      width="480px"
    >
      <el-form :model="dialogForm" label-width="80px" size="small">
        <el-form-item :label="t('common.action.brand')" required>
          <el-input v-model="dialogForm.brand" :placeholder="t('common.field.e_g_bosch')" maxlength="100" show-word-limit />
        </el-form-item>
        <el-form-item :label="t('common.action.sort_order')">
          <el-input-number v-model="dialogForm.sortOrder" :min="0" :step="10" style="width: 100%" />
          <div class="text-xs text-muted mt-1">数字越小越靠前; 拖拽排序会自动分配</div>
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
/* Day 10: 字典列表样式 (Musk 风格: 1px hairline + 高密度 + 无阴影) */
.dict-head,
.dict-row {
  display: grid;
  grid-template-columns: 32px 60px 1fr 80px 100px 140px 80px 200px;
  align-items: center;
  font-size: 12px;
  border-bottom: 1px solid var(--color-border);
}
.dict-head {
  font-weight: 500;
  color: var(--color-text-muted);
  background: var(--color-bg-hover);
  height: 32px;
}
.dict-row {
  height: 36px;
  background: var(--color-bg-elevated);
  transition: background-color 0.15s, border-top 0.1s;
  cursor: default;
}
.dict-head > div,
.dict-row > div {
  padding: 0 8px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.cell-drag {
  text-align: center;
}
.drag-handle {
  cursor: move;
  color: var(--color-text-muted);
  font-size: 16px;
  user-select: none;
  display: inline-block;
  padding: 0 4px;
}
.drag-handle:hover { color: var(--color-accent); }
.cell-id, .cell-sort, .cell-xref { text-align: right; }
.dict-row:hover { background: var(--color-bg-hover); }
.dict-row--dragging { opacity: 0.4; }
.dict-row--dragover {
  border-top: 2px solid var(--color-accent) !important;
  background: var(--color-bg-hover);
}
.dict-row--deleted { color: var(--color-text-muted); background: var(--color-bg-hover); }
.dict-empty {
  padding: 24px 0;
  text-align: center;
  color: var(--color-text-muted);
  font-size: 12px;
}
</style>
