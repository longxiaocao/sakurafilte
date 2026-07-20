<script setup lang="ts">
// Day 10+ P2.2: 产品名 2 字典管理页 (复用 ProductName1 模板)
//   - 复用 AdminProductName1sView 模板: 列表/CRUD/软删/恢复/拖拽排序
//   - typeahead 数据供 AdminProductFormView 分区 1 product_name_2 用
// P1-1 DictManagerLayout 提取: 用 useDictManager + DictManagerLayout 替代手写 state + CRUD + 拖拽 + 模板
//   行数: 197 → ~55 (减少 72%)
import { useI18n } from 'vue-i18n'
import { useDictManager } from '@/composables/useDictManager'
import DictManagerLayout from '@/components/DictManagerLayout.vue'
import { dictApi, type ProductName2Item, type ProductName2ReorderItem } from '@/api'

const { t } = useI18n()

const mgr = useDictManager<ProductName2Item, ProductName2ReorderItem>({
  api: dictApi.productName2s,
  emptyForm: () => ({ productName2: '', sortOrder: 0 }),
  rowToForm: (row) => ({ id: row.id, productName2: row.productName2, sortOrder: row.sortOrder }),
  validate: (form) => {
    const v = (form.productName2 as string).trim()
    if (!v) return { ok: false, errMsg: t('admin.productname2sview.warning.product_name_cannot_be') }
    if (v.length > 200) return { ok: false, errMsg: t('admin.productname2sview.warning.product_name_length') }
    return { ok: true }
  },
  formToCreatePayload: (form) => [(form.productName2 as string).trim(), form.sortOrder],
  formToUpdatePayload: (form) => ({ productName2: (form.productName2 as string).trim(), sortOrder: form.sortOrder }),
  softDeleteMessage: (row) => `确定删除 "${row.productName2}" 吗? (软删除)`,
})

// 列定义 (1 字段: productName2)
const columns = [
  { label: '产品名 2', width: '1fr', render: (row: ProductName2Item) => row.productName2 },
]
</script>

<template>
  <DictManagerLayout
    :mgr="mgr"
    :columns="columns"
    title="产品名 2 字典"
    subtitle="P2.2 后台管理 · 用于产品表单分区 1 product_name_2 自动补全"
    dialog-title-create-key="admin.productname2sview.title.add_product"
    dialog-title-edit-key="admin.productname2sview.title.edit_product"
    dialog-width="480px"
    dialog-label-width="100px"
    empty-text="新增产品名 2开始"
    :search-placeholder="t('admin.productname2sview.placeholder.search_product_name')"
    create-button-text="新增产品名 2"
  >
    <template #dialog-form="{ form }">
      <el-form-item :label="t('common.action.product_name_2')" required>
        <el-input
          v-model="form.productName2"
          :placeholder="t('admin.productname2sview.placeholder.e_g_spin_on')"
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
