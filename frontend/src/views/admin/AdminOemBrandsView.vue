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
  { label: t('dict.columnLabels.brand'), width: '1fr', render: (row: OemBrandItem) => row.brand },
]
</script>

<template>
  <DictManagerLayout
    :mgr="mgr"
    :columns="columns"
    :title="t('dict.pageTitles.oemBrands.title')"
    :subtitle="t('dict.pageTitles.oemBrands.subtitle')"
    dialog-title-create-key="admin.oembrandsview.string.add_brand"
    dialog-title-edit-key="admin.oembrandsview.title.edit_brand"
    dialog-width="480px"
    dialog-label-width="100px"
    :empty-text="t('admin.oembrandsview.string.add_brand') + t('dict.empty_start')"
    :search-placeholder="t('admin.oembrandsview.placeholder.search_brand')"
    :create-button-text="t('admin.oembrandsview.string.add_brand')"
    :bulk-api="dictApi.oemBrands"
    bulk-template="brand,sortOrder,deleted&#10;示例品牌,1,0&#10;示例品牌2,2,0"
  >
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
