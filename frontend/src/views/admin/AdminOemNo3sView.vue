<script setup lang="ts">
// Day 10+ P2.2: OEM 3 字典管理页 (复用单字段模板)
//   - 复用 AdminOemBrandsView 模板: 列表/CRUD/软删/恢复/拖拽排序
//   - typeahead 数据供 AdminXrefFormView 分区 2 oem_no_3 用
// P1-1 DictManagerLayout 提取: 用 useDictManager + DictManagerLayout 替代手写 state + CRUD + 拖拽 + 模板
//   行数: 210 → ~55 (减少 74%)
import { useI18n } from 'vue-i18n'
import { useDictManager } from '@/composables/useDictManager'
import DictManagerLayout from '@/components/DictManagerLayout.vue'
import { dictApi, type OemNo3Item, type OemNo3ReorderItem } from '@/api'

const { t } = useI18n()

const mgr = useDictManager<OemNo3Item, OemNo3ReorderItem>({
  api: dictApi.oemNo3s,
  emptyForm: () => ({ oemNo3: '', sortOrder: 0 }),
  rowToForm: (row) => ({ id: row.id, oemNo3: row.oemNo3, sortOrder: row.sortOrder }),
  validate: (form) => {
    const v = (form.oemNo3 as string).trim()
    if (!v) return { ok: false, errMsg: t('admin.oemno3sview.warning.oem_cannot_be_empty') }
    if (v.length > 200) return { ok: false, errMsg: t('admin.oemno3sview.warning.oem_length') }
    return { ok: true }
  },
  formToCreatePayload: (form) => [(form.oemNo3 as string).trim(), form.sortOrder],
  formToUpdatePayload: (form) => ({ oemNo3: (form.oemNo3 as string).trim(), sortOrder: form.sortOrder }),
  softDeleteMessage: (row) => `确定删除 "${row.oemNo3}" 吗? (软删除)`,
})

// 列定义 (1 字段: oemNo3)
const columns = [
  { label: 'OEM 3', width: '1fr', render: (row: OemNo3Item) => row.oemNo3 },
]
</script>

<template>
  <DictManagerLayout
    :mgr="mgr"
    :columns="columns"
    title="OEM 3 字典"
    subtitle="P2.2 后台管理 · 用于交叉引用分区 2 oem_no_3 自动补全"
    dialog-title-create-key="admin.oemno3sview.title.add_oem"
    dialog-title-edit-key="admin.oemno3sview.title.edit_oem"
    dialog-width="480px"
    dialog-label-width="100px"
    empty-text="新增 OEM 3开始"
    :search-placeholder="t('admin.oemno3sview.placeholder.search_oem')"
    create-button-text="新增 OEM 3"
  >
    <template #dialog-form="{ form }">
      <el-form-item label="OEM 3" required>
        <el-input
          v-model="form.oemNo3"
          :placeholder="t('admin.oemno3sview.placeholder.e_g')"
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
