<script setup lang="ts">
// P1-1 DictManagerLayout 提取: 字典管理页公共布局组件
//   WHY: 8 个字典页模板结构 (el-alert + SkeletonCard + hairline + dict-head + dict-row
//        + dict-empty + el-dialog) 完全相同, 仅数据列和 dialog 表单不同
//   设计: 用 3 个 slot 承接差异 (#toolbar-extra / #row-cells / #dialog-form)
//        公共样式集中到 scoped style, 用 --dict-grid CSS 变量传入列宽
//   关联 ADR: #13
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import SkeletonCard from '@/components/SkeletonCard.vue'
import type { DictManagerReturn } from '@/composables/useDictManager'
import { ref } from 'vue'
import { ElMessage } from 'element-plus'

const { t } = useI18n()

/** 列定义 (用于简单字典页, 复杂页用 #row-cells slot) */
export interface DictColumn {
  /** 列标题 (i18n key 或纯文本) */
  label: string
  /** 列宽 (CSS grid 单位, 如 '1fr' / '80px' / '1.4fr') */
  width: string
  /** 列单元格渲染函数 (返回 string) */
  render: (row: any) => string
  /** 可选: 列单元格渲染为 el-tag (用于 AdminMachinesView 的 category 列) */
  renderTag?: (
    row: any
  ) => { text: string; type: 'success' | 'warning' | 'info' | 'primary' }
  /** 可选: 文本对齐, 默认 'left' */
  align?: 'left' | 'right' | 'center'
}

interface Props {
  /** useDictManager 返回值 */
  mgr: DictManagerReturn<any>
  /** 标题 (如 "类型字典 (Type)") */
  title: string
  /** 副标题 (如 "P2.2 后台管理 · 固定 5 值...") */
  subtitle?: string
  /** 表格数据列 (不含 cell-drag + cell-id + cell-sort + cell-xref + cell-updated + cell-status + cell-action)
   *  复杂页 (如 AdminMachinesView) 传 [] 并用 #row-cells slot */
  columns?: DictColumn[]
  /** dialog 标题 i18n key (create 模式) */
  dialogTitleCreateKey: string
  /** dialog 标题 i18n key (edit 模式) */
  dialogTitleEditKey: string
  /** dialog 宽度, 默认 '480px' */
  dialogWidth?: string
  /** dialog label 宽度, 默认 '100px' */
  dialogLabelWidth?: string
  /** 🔧 fix(审查): 批量导入导出 — 传入字典 API 对象后自动渲染 下载模板/导出 CSV/导入 CSV 按钮 */
  bulkApi?: {
    exportCsv(): Promise<string>
    importCsv(csv: string): Promise<{ created: number; updated: number; deleted: number; skipped: number; errors: string[] }>
  }
  /** 🔧 fix(审查): 导入模板 CSV 内容 (表头 + 示例行) */
  bulkTemplate?: string
  /** 空状态文案 (如 "新增 Type开始"), 与 i18n key common.action.no_data_click_top_right 拼接 */
  emptyText: string
  /** 搜索框 placeholder i18n key 或纯文本 */
  searchPlaceholder?: string
  /** 新增按钮文案 (如 "新增 Type") */
  createButtonText?: string
  /** grid-template-columns 完整定义
   *  默认根据 columns 推导: 32px 60px + columns.width... + 80px 100px 140px 80px 200px
   *  复杂页 (如 AdminMachinesView) 可显式传入 */
  gridTemplate?: string
}

const props = withDefaults(defineProps<Props>(), {
  columns: () => [],
  dialogWidth: '480px',
  dialogLabelWidth: '100px',
  createButtonText: '新增',
})

// 🔧 fix(审查): 批量导入导出 — 模板下载 / 导出 CSV / 导入 CSV (CSV 格式: value,sortOrder,deleted)
const fileInput = ref<HTMLInputElement | null>(null)
function downloadTemplate() {
  if (!props.bulkTemplate) return
  const blob = new Blob(['\ufeff' + props.bulkTemplate], { type: 'text/csv;charset=utf-8' })
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = `dict-template-${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(a.href)
}
function triggerImport() {
  fileInput.value?.click()
}
async function onExportCsv() {
  if (!props.bulkApi) return
  try {
    const csv = await props.bulkApi.exportCsv()
    const blob = new Blob(['\ufeff' + csv], { type: 'text/csv;charset=utf-8' })
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob)
    a.download = `dict-export-${new Date().toISOString().slice(0, 10)}.csv`
    a.click()
    URL.revokeObjectURL(a.href)
  } catch {
    ElMessage.error(t('dict.bulk.export_failed'))
  }
}
async function onImportFile(e: Event) {
  if (!props.bulkApi) return
  const input = e.target as HTMLInputElement
  const f = input.files?.[0]
  if (!f) return
  try {
    const text = await f.text()
    const res = await props.bulkApi.importCsv(text)
    ElMessage.success(`${t('dict.bulk.import_done')}: 新增 ${res.created}, 更新 ${res.updated}, 删除 ${res.deleted}, 跳过 ${res.skipped}`)
    if (res.errors?.length) ElMessage.warning(`${t('dict.bulk.import_partial_fail')}: ${res.errors.slice(0, 3).join('; ')}`)
    props.mgr.load()
  } catch {
    ElMessage.error(t('dict.bulk.import_failed'))
  } finally {
    input.value = ''
  }
}

// 根据 columns 推导 grid-template-columns
const gridTemplate = computed(() => {
  if (props.gridTemplate) return props.gridTemplate
  const dataCols = props.columns.map((c) => c.width).join(' ')
  return `32px 60px ${dataCols} 80px 100px 140px 80px 200px`
})

function cellClass(col: DictColumn): string {
  const align = col.align ?? 'left'
  return align === 'right'
    ? 'cell-num'
    : align === 'center'
    ? 'cell-center'
    : ''
}
</script>

<template>
  <div class="p-3 w-full">
    <!-- 顶部工具条 -->
    <div class="flex items-center gap-2 mb-3 flex-wrap">
      <h1 class="text-lg font-medium">{{ title }}</h1>
      <span v-if="subtitle" class="text-xs text-muted">{{ subtitle }}</span>
      <div class="flex-1" />
      <template v-if="bulkApi">
        <!-- 🔧 fix(审查): 批量导入导出通用按钮 (下载模板 / 导出 / 导入) -->
        <el-button size="small" @click="downloadTemplate">
          <el-icon class="mr-1" aria-hidden="true"><Document /></el-icon>
          {{ t('dict.bulk.download_template') }}
        </el-button>
        <el-button size="small" @click="onExportCsv">
          <el-icon class="mr-1" aria-hidden="true"><Download /></el-icon>
          {{ t('dict.bulk.export_csv') }}
        </el-button>
        <el-button size="small" @click="triggerImport">
          <el-icon class="mr-1" aria-hidden="true"><Upload /></el-icon>
          {{ t('dict.bulk.import_csv') }}
        </el-button>
        <input ref="fileInput" type="file" accept=".csv,text/csv" class="hidden" @change="onImportFile" />
      </template>
      <slot name="toolbar-extra" />
      <el-input
        v-model="mgr.searchKw.value"
        :placeholder="searchPlaceholder"
        clearable
        size="small"
        style="width: 200px"
        @keyup.enter="mgr.onSearch"
      />
      <el-button size="small" @click="mgr.onSearch">{{ t('common.search') }}</el-button>
      <el-checkbox v-model="mgr.includeDeleted.value" @change="mgr.load" size="small">
        {{ t('dict.includeDeleted') }}
      </el-checkbox>
      <el-button type="primary" size="small" @click="mgr.openCreate">
        {{ createButtonText }}
      </el-button>
    </div>

    <!-- V24-F102 (P2-2, 规则 8): 加载失败时显示持久 el-alert + 重试按钮 -->
    <el-alert
      v-if="mgr.loadError.value"
      type="error"
      show-icon
      :closable="false"
      class="mb-2"
    >
      <template #default>
        <div class="flex items-center justify-between">
          <span>{{ mgr.loadError.value }}</span>
          <el-button
            size="small"
            @click="mgr.load"
            :disabled="mgr.loading.value"
          >
            {{ mgr.loading.value ? t('dict.retrying') : t('dict.retry') }}
          </el-button>
        </div>
      </template>
    </el-alert>

    <!-- V24-F102 (P1-2): 首屏骨架屏, 仅首次加载且无数据时显示 -->
    <SkeletonCard
      v-if="mgr.loading.value && mgr.items.value.length === 0 && !mgr.loadError.value"
      variant="table-row"
      :count="5"
    />

    <!-- 表格 (hairline 容器) -->
    <div class="hairline" v-loading="mgr.loading.value" :style="{ '--dict-grid': gridTemplate }">
      <!-- 表头 -->
      <div class="dict-head">
        <div class="cell-drag"></div>
        <div class="cell-id">{{ t('dict.colId') }}</div>
        <!-- 数据列标题: 优先用 #row-cells-header slot, 否则用 columns 配置 -->
        <slot name="row-cells-header">
          <div v-for="col in columns" :key="col.label" :class="cellClass(col)">
            {{ col.label }}
          </div>
        </slot>
        <div class="cell-sort">{{ t('dict.colSort') }}</div>
        <div class="cell-xref">{{ t('dict.colXref') }}</div>
        <div class="cell-updated">{{ t('dict.colUpdated') }}</div>
        <div class="cell-status">{{ t('dict.colStatus') }}</div>
        <div class="cell-action">{{ t('dict.colAction') }}</div>
      </div>
      <!-- 表行 -->
      <div
        v-for="row in mgr.items.value"
        :key="row.id"
        :class="['dict-row', mgr.rowClass(row)]"
        :draggable="mgr.isDraggable(row)"
        @dragstart="mgr.onDragStart($event, row.id)"
        @dragover="mgr.onDragOver($event, row.id)"
        @dragleave="mgr.onDragLeave($event, row.id)"
        @drop="mgr.onDrop($event, row.id)"
        @dragend="mgr.onDragEnd"
      >
        <div class="cell-drag">
          <span v-if="mgr.isDraggable(row)" class="drag-handle">≡</span>
        </div>
        <div class="cell-id">{{ row.id }}</div>
        <!-- 数据列: 优先用 #row-cells slot, 否则用 columns 配置 -->
        <slot name="row-cells" :row="row">
          <div v-for="col in columns" :key="col.label" :class="cellClass(col)">
            <el-tag
              v-if="col.renderTag"
              :type="col.renderTag(row).type"
              size="small"
            >{{ col.renderTag(row).text }}</el-tag>
            <template v-else>{{ col.render(row) }}</template>
          </div>
        </slot>
        <div class="cell-sort">{{ row.sortOrder }}</div>
        <div class="cell-xref">{{ row.xrefCount }}</div>
        <div class="cell-updated">{{ mgr.fmtDate(row.updatedAt) }}</div>
        <div class="cell-status">
          <el-tag v-if="row.deletedAt" type="info" size="small">{{ t('dict.statusDeleted') }}</el-tag>
          <el-tag v-else type="success" size="small">{{ t('dict.statusActive') }}</el-tag>
        </div>
        <div class="cell-action">
          <el-button
            size="small"
            text
            @click="mgr.openEdit(row)"
            :disabled="!!row.deletedAt"
          >{{ t('common.edit') }}</el-button>
          <el-button
            v-if="!row.deletedAt"
            size="small"
            text
            type="warning"
            @click="mgr.softDelete(row)"
          >{{ t('common.delete') }}</el-button>
          <el-button
            v-else
            size="small"
            text
            type="success"
            @click="mgr.restore(row)"
          >{{ t('common.action.restored') }}</el-button>
        </div>
      </div>
      <!-- 空状态 -->
      <div v-if="!mgr.loading.value && mgr.items.value.length === 0" class="dict-empty">
        {{ t('common.action.no_data_click_top_right') }}{{ emptyText }}
      </div>
    </div>

    <!-- 底部统计 (统一用 i18n key, 修复 AdminOemBrandsView/AdminProductName1sView 硬编码) -->
    <div class="mt-2 text-xs text-muted">
      {{ t('common.dictviewcommon.total_drag', {
        total: mgr.total.value,
        active: mgr.activeCount.value,
        soft: mgr.total.value - mgr.activeCount.value
      }) }}
    </div>

    <!-- 新增 / 编辑 dialog -->
    <el-dialog
      v-model="mgr.dialogOpen.value"
      :title="mgr.dialogMode.value === 'create' ? t(dialogTitleCreateKey) : t(dialogTitleEditKey)"
      :width="dialogWidth"
    >
      <el-form
        :model="mgr.dialogForm"
        :label-width="dialogLabelWidth"
        size="small"
      >
        <slot name="dialog-form" :form="mgr.dialogForm" :mode="mgr.dialogMode.value" />
      </el-form>
      <template #footer>
        <el-button @click="mgr.dialogOpen.value = false">{{ t('common.cancel') }}</el-button>
        <el-button type="primary" @click="mgr.saveDialog">{{ t('common.save') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
/* grid-template-columns 由 --dict-grid CSS 变量传入 */
.dict-head,
.dict-row {
  display: grid;
  grid-template-columns: var(--dict-grid);
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
.drag-handle:hover {
  color: var(--color-accent);
}
.cell-id,
.cell-sort,
.cell-xref,
.cell-num {
  text-align: right;
}
.cell-center {
  text-align: center;
}
.dict-row:hover {
  background: var(--color-bg-hover);
}
.dict-row--dragging {
  opacity: 0.4;
}
.dict-row--dragover {
  border-top: 2px solid var(--color-accent) !important;
  background: var(--color-bg-hover);
}
.dict-row--deleted {
  color: var(--color-text-muted);
  background: var(--color-bg-hover);
}
.dict-empty {
  padding: 24px 0;
  text-align: center;
  color: var(--color-text-muted);
  font-size: 12px;
}
</style>
