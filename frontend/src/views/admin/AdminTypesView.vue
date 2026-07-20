<script setup lang="ts">
// Day 10+ P2.2: Type 字典管理页 (固定 5 值: oil/fuel/air/cabin/others)
//   - 默认按 sortOrder 排 (P2.3 联动: 拖动后前台产品页按 sortOrder 展示)
//   - 5 个固定值不允许硬删 (兜底 others)
// P1-1 DictManagerLayout 提取: 用 useDictManager + DictManagerLayout 替代手写 state + CRUD + 拖拽 + 模板
//   行数: 222 → ~55 (减少 75%)
import { useI18n } from 'vue-i18n'
import { useDictManager } from '@/composables/useDictManager'
import DictManagerLayout from '@/components/DictManagerLayout.vue'
import { dictApi, type TypeItem, type TypeReorderItem } from '@/api'

const { t } = useI18n()

// 固定 5 值: oil/fuel/air/cabin/others (软删时额外警告)
const FIXED_TYPES = ['oil', 'fuel', 'air', 'cabin', 'others']

const mgr = useDictManager<TypeItem, TypeReorderItem>({
  api: dictApi.types,
  emptyForm: () => ({ type: '', sortOrder: 0 }),
  rowToForm: (row) => ({ id: row.id, type: row.type, sortOrder: row.sortOrder }),
  validate: (form) => {
    const v = (form.type as string).trim()
    if (!v) return { ok: false, errMsg: t('admin.typesview.warning.type_cannot_be_empty') }
    if (v.length > 50) return { ok: false, errMsg: t('admin.typesview.warning.type_length') }
    return { ok: true }
  },
  formToCreatePayload: (form) => [(form.type as string).trim(), form.sortOrder],
  formToUpdatePayload: (form) => ({ type: (form.type as string).trim(), sortOrder: form.sortOrder }),
  // V24-F102 P0-1 修复后的硬编码模式 + 固定 5 值警告
  softDeleteMessage: (row) =>
    FIXED_TYPES.includes(row.type)
      ? `确定删除固定 Type "${row.type}" 吗? 建议保留 (作为 P2.3 兜底), 但仍支持软删恢复.`
      : `确定删除 "${row.type}" 吗? (软删除)`,
  // AdminTypesView 用专属 i18n key (与 common.action.sort_order_saved 略不同)
  reorderSuccessKey: 'admin.typesview.success.sort_order_saved_frontend',
})

// 列定义 (1 字段: type)
const columns = [
  { label: 'Type', width: '1fr', render: (row: TypeItem) => row.type },
]
</script>

<template>
  <DictManagerLayout
    :mgr="mgr"
    :columns="columns"
    title="类型字典 (Type)"
    subtitle="P2.2 后台管理 · 固定 5 值: oil / fuel / air / cabin / others · P2.3 拖动排序后前台立即生效"
    dialog-title-create-key="admin.typesview.title.add_type"
    dialog-title-edit-key="admin.typesview.title.edit_type"
    dialog-width="480px"
    dialog-label-width="100px"
    empty-text="新增 Type开始"
    :search-placeholder="t('admin.typesview.placeholder.search_type')"
    create-button-text="新增 Type"
  >
    <template #dialog-form="{ form }">
      <el-form-item label="Type" required>
        <el-input
          v-model="form.type"
          :placeholder="t('admin.typesview.placeholder.e_g_oil_fuel')"
          maxlength="50"
          show-word-limit
        />
      </el-form-item>
      <el-form-item :label="t('common.action.sort_order')">
        <el-input-number v-model="form.sortOrder" :min="0" :step="10" style="width: 100%" />
      </el-form-item>
    </template>
  </DictManagerLayout>
</template>
