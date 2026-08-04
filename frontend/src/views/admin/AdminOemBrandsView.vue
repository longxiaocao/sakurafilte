<script setup lang="ts">
// Day 10: OEM 品牌字典管理页 (P1.3)
//   - 列表 (默认仅未删, 开关切换看已删)
//   - 增 / 改 / 删 (软) / 恢复
//   - HTML5 原生拖拽排序 (无新依赖, 避免引入 sortablejs)
//     设计: 每行 draggable=true, dragstart 记录源 id, dragover 阻止默认 + 高亮目标行
//     drop 时本地重排 → 重新分配 sortOrder (步长 10) → 调 reorder API 持久化
//   - 不写 product_history: 字典变更不属产品业务变更
//   - typeahead 数据由 G1.6 dictApi 提供, 在 AdminProductFormView 分区 2 用到
// P1-1 DictManagerLayout 提取: 用 useDictManager + DictManagerLayout 替代手写 state + CRUD + 拖拽 + 模板
//   行数: 399 → ~60 (减少 85%)
//   顺便统一底部硬编码文案为 i18n key common.dictviewcommon.total_drag
import { useI18n } from 'vue-i18n'
import { ref } from 'vue'
import { ElMessage } from 'element-plus'
import { useDictManager } from '@/composables/useDictManager'
import DictManagerLayout from '@/components/DictManagerLayout.vue'
import { dictApi, type OemBrandItem, type OemBrandReorderItem } from '@/api'

const { t } = useI18n()

const mgr = useDictManager<OemBrandItem, OemBrandReorderItem>({
  api: dictApi.oemBrands,
  emptyForm: () => ({ brand: '', sortOrder: 0 }),
  rowToForm: (row) => ({ id: row.id, brand: row.brand, sortOrder: row.sortOrder }),
  validate: (form) => {
    const v = (form.brand as string).trim()
    if (!v) return { ok: false, errMsg: t('admin.oembrandsview.warning.brand_cannot_be_empty') }
    if (v.length > 100) return { ok: false, errMsg: t('admin.oembrandsview.warning.brand_length') }
    return { ok: true }
  },
  formToCreatePayload: (form) => [(form.brand as string).trim(), form.sortOrder],
  formToUpdatePayload: (form) => ({ brand: (form.brand as string).trim(), sortOrder: form.sortOrder }),
  softDeleteMessage: (row) => `确定删除品牌 "${row.brand}" 吗? (软删除)`,
})

// 列定义 (1 字段: brand)
const columns = [
  { label: '品牌', width: '1fr', render: (row: OemBrandItem) => row.brand },
]

// 🔧 fix(审查): 批量导入导出 — CSV 格式: brand[,sortOrder[,deleted]] 每行一个品牌
const fileInput = ref<HTMLInputElement | null>(null)
function triggerImport() {
  fileInput.value?.click()
}
async function onExportCsv() {
  try {
    const csv = await dictApi.oemBrands.exportCsv()
    // \ufeff BOM: 让 Excel 正确识别 UTF-8 (避免中文乱码)
    const blob = new Blob(['\ufeff' + csv], { type: 'text/csv;charset=utf-8' })
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob)
    a.download = `oem-brands-${new Date().toISOString().slice(0, 10)}.csv`
    a.click()
    URL.revokeObjectURL(a.href)
  } catch {
    ElMessage.error(t('admin.oembrandsview.export_failed'))
  }
}
async function onImportFile(e: Event) {
  const input = e.target as HTMLInputElement
  const f = input.files?.[0]
  if (!f) return
  try {
    const text = await f.text()
    const res = await dictApi.oemBrands.importCsv(text)
    ElMessage.success(`${t('admin.oembrandsview.import_done')}: 新增 ${res.created}, 更新 ${res.updated}, 删除 ${res.deleted}, 跳过 ${res.skipped}`)
    if (res.errors?.length) ElMessage.warning(`${t('admin.oembrandsview.import_partial_fail')}: ${res.errors.slice(0, 3).join('; ')}`)
    mgr.load()
  } catch {
    ElMessage.error(t('admin.oembrandsview.import_failed'))
  } finally {
    input.value = ''
  }
}
</script>

<template>
  <DictManagerLayout
    :mgr="mgr"
    :columns="columns"
    title="OEM 品牌字典"
    subtitle="P1.3 后台管理 · 用于产品表单分区 2 自动补全"
    dialog-title-create-key="admin.oembrandsview.string.add_brand"
    dialog-title-edit-key="admin.oembrandsview.title.edit_brand"
    dialog-width="480px"
    dialog-label-width="100px"
    empty-text="新增品牌开始"
    :search-placeholder="t('admin.oembrandsview.placeholder.search_brand')"
    create-button-text="新增品牌"
  >
    <!-- 🔧 fix(审查): 批量导入导出 (用户反馈: 无数据导入导出入口) -->
    <template #toolbar-extra>
      <el-button size="small" @click="onExportCsv">
        <el-icon class="mr-1" aria-hidden="true"><Download /></el-icon>
        {{ t('admin.oembrandsview.export_csv') }}
      </el-button>
      <el-button size="small" @click="triggerImport">
        <el-icon class="mr-1" aria-hidden="true"><Upload /></el-icon>
        {{ t('admin.oembrandsview.import_csv') }}
      </el-button>
      <input ref="fileInput" type="file" accept=".csv,text/csv" class="hidden" @change="onImportFile" />
    </template>
    <template #dialog-form="{ form }">
      <el-form-item :label="t('common.action.brand')" required>
        <el-input
          v-model="form.brand"
          :placeholder="t('common.field.e_g_bosch')"
          maxlength="100"
          show-word-limit
        />
      </el-form-item>
      <el-form-item :label="t('common.action.sort_order')">
        <el-input-number v-model="form.sortOrder" :min="0" :step="10" style="width: 100%" />
      </el-form-item>
    </template>
  </DictManagerLayout>
</template>
