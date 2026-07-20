<script setup lang="ts">
// Day 10+ P2.2: 产品名 1 字典管理页
//   - 复用 AdminOemBrandsView 模板: 列表/CRUD/软删/恢复/拖拽排序
//   - HTML5 原生拖拽 (无新依赖)
//   - typeahead 数据供 AdminProductFormView 分区 1 product_name_1 用
// P1-1 DictManagerLayout 提取: 用 useDictManager + DictManagerLayout 替代手写 state + CRUD + 拖拽 + 模板
//   行数: 300 → ~55 (减少 82%)
//   顺便统一底部硬编码文案为 i18n key common.dictviewcommon.total_drag
import { useI18n } from 'vue-i18n'
import { useDictManager } from '@/composables/useDictManager'
import DictManagerLayout from '@/components/DictManagerLayout.vue'
import { dictApi, type ProductName1Item, type ProductName1ReorderItem } from '@/api'

const { t } = useI18n()

const mgr = useDictManager<ProductName1Item, ProductName1ReorderItem>({
  api: dictApi.productName1s,
  emptyForm: () => ({ productName1: '', sortOrder: 0 }),
  rowToForm: (row) => ({ id: row.id, productName1: row.productName1, sortOrder: row.sortOrder }),
  validate: (form) => {
    const v = (form.productName1 as string).trim()
    if (!v) return { ok: false, errMsg: t('admin.productname1sview.warning.product_name_cannot_be') }
    if (v.length > 200) return { ok: false, errMsg: t('admin.productname1sview.warning.product_name_length') }
    return { ok: true }
  },
  formToCreatePayload: (form) => [(form.productName1 as string).trim(), form.sortOrder],
  formToUpdatePayload: (form) => ({ productName1: (form.productName1 as string).trim(), sortOrder: form.sortOrder }),
  softDeleteMessage: (row) => `确定删除 "${row.productName1}" 吗? (软删除)`,
})

// 列定义 (1 字段: productName1)
const columns = [
  { label: '产品名 1', width: '1fr', render: (row: ProductName1Item) => row.productName1 },
]
</script>

<template>
  <DictManagerLayout
    :mgr="mgr"
    :columns="columns"
    title="产品名 1 字典"
    subtitle="P2.2 后台管理 · 用于产品表单分区 1 自动补全"
    dialog-title-create-key="admin.productname1sview.title.add_product"
    dialog-title-edit-key="admin.productname1sview.title.edit_product"
    dialog-width="480px"
    dialog-label-width="100px"
    empty-text="新增产品名开始"
    :search-placeholder="t('admin.productname1sview.placeholder.search_product_name')"
    create-button-text="新增产品名"
  >
    <template #dialog-form="{ form }">
      <el-form-item :label="t('common.action.product_name_1')" required>
        <el-input
          v-model="form.productName1"
          :placeholder="t('admin.productname1sview.placeholder.e_g_oil_filter')"
          maxlength="200"
          show-word-limit
        />
      </el-form-item>
      <el-form-item :label="t('common.action.sort_order')">
        <el-input-number v-model="form.sortOrder" :min="0" :step="10" style="width: 100%" />
      </el-form-item>
    </template>
  </DictManagerLayout>
</template>
