<script setup lang="ts">
// Day 10+ P2.2: Engine 字典管理页 (2 字段: engine_brand + engine_type)
//   - 多字段: brand 必填, type 可空
//   - 二合一展示: 同一行同时显示 brand + type, 拖动按 engine_brand 主值排序
// P1-1 DictManagerLayout 提取: 用 useDictManager + DictManagerLayout 替代手写 state + CRUD + 拖拽 + 模板
//   行数: 222 → ~70 (减少 68%)
import { useI18n } from 'vue-i18n'
import { useDictManager } from '@/composables/useDictManager'
import DictManagerLayout from '@/components/DictManagerLayout.vue'
import { dictApi, type EngineItem, type EngineReorderItem } from '@/api'

const { t } = useI18n()

const mgr = useDictManager<EngineItem, EngineReorderItem>({
  api: dictApi.engines,
  emptyForm: () => ({ engineBrand: '', engineType: '', sortOrder: 0 }),
  rowToForm: (row) => ({
    id: row.id,
    engineBrand: row.engineBrand,
    engineType: row.engineType ?? '',
    sortOrder: row.sortOrder,
  }),
  validate: (form) => {
    const b = (form.engineBrand as string).trim()
    if (!b) return { ok: false, errMsg: t('admin.enginesview.warning.engine_brand_cannot_be_empty') }
    if (b.length > 200) return { ok: false, errMsg: t('admin.enginesview.warning.engine_brand_length') }
    return { ok: true }
  },
  formToCreatePayload: (form) => {
    const b = (form.engineBrand as string).trim()
    const t2 = (form.engineType as string).trim() || undefined
    return [b, t2, form.sortOrder]
  },
  formToUpdatePayload: (form) => {
    const b = (form.engineBrand as string).trim()
    const t2 = (form.engineType as string).trim() || undefined
    return { engineBrand: b, engineType: t2, sortOrder: form.sortOrder }
  },
  softDeleteMessage: (row) =>
    `确定删除 "${row.engineBrand}${row.engineType ? ' / ' + row.engineType : ''}" 吗? (软删除)`,
})

// 列定义 (2 字段: engineBrand + engineType, engineType 可空显示 '—')
const columns = [
  { label: '品牌', width: '1.4fr', render: (row: EngineItem) => row.engineBrand },
  { label: '型号', width: '1.4fr', render: (row: EngineItem) => row.engineType || '—' },
]
</script>

<template>
  <DictManagerLayout
    :mgr="mgr"
    :columns="columns"
    title="发动机字典 (Engine)"
    subtitle="P2.2 后台管理 · 2 字段: 品牌 + 型号 · 用于产品表单分区 7 发动机信息"
    dialog-title-create-key="admin.enginesview.title.add_engine"
    dialog-title-edit-key="admin.enginesview.title.edit_engine"
    dialog-width="540px"
    dialog-label-width="120px"
    empty-text="新增发动机开始"
    :search-placeholder="t('common.field.search_any_field')"
    create-button-text="新增发动机"
  >
    <template #dialog-form="{ form }">
      <el-form-item :label="t('common.action.brand')" required>
        <el-input
          v-model="form.engineBrand"
          :placeholder="t('admin.enginesview.placeholder.e_g_cummins')"
          maxlength="200"
          show-word-limit
        />
      </el-form-item>
      <el-form-item :label="t('common.action.model')">
        <el-input
          v-model="form.engineType"
          :placeholder="t('admin.enginesview.placeholder.e_g_isb_l')"
          maxlength="200"
          show-word-limit
        />
        <div class="text-xs text-muted mt-1">2 字段组成 UNIQUE 索引, 型号可空</div>
      </el-form-item>
      <el-form-item :label="t('common.action.sort_order')">
        <el-input-number v-model="form.sortOrder" :min="0" :step="10" style="width: 100%" />
      </el-form-item>
    </template>
  </DictManagerLayout>
</template>
