<script setup lang="ts">
// Day 10+ P2.2: Media 字典管理页 (2 字段: media_name + media_model)
//   - 多字段: list/typeahead 走 media_name + media_model OR 匹配
//   - 二合一展示: 同一行同时显示 name + model, 拖动按 media_name 主值排序
// P1-1 DictManagerLayout 提取: 用 useDictManager + DictManagerLayout 替代手写 state + CRUD + 拖拽 + 模板
//   行数: 223 → ~70 (减少 69%)
import { useI18n } from 'vue-i18n'
import { useDictManager } from '@/composables/useDictManager'
import DictManagerLayout from '@/components/DictManagerLayout.vue'
import { dictApi, type MediaItem, type MediaReorderItem } from '@/api'

const { t } = useI18n()

const mgr = useDictManager<MediaItem, MediaReorderItem>({
  api: dictApi.medias,
  emptyForm: () => ({ mediaName: '', mediaModel: '', sortOrder: 0 }),
  rowToForm: (row) => ({
    id: row.id,
    mediaName: row.mediaName,
    mediaModel: row.mediaModel ?? '',
    sortOrder: row.sortOrder,
  }),
  validate: (form) => {
    const n = (form.mediaName as string).trim()
    if (!n) return { ok: false, errMsg: t('admin.mediasview.warning.media_name_cannot_be') }
    if (n.length > 100) return { ok: false, errMsg: t('admin.mediasview.warning.media_name_length') }
    return { ok: true }
  },
  formToCreatePayload: (form) => {
    const n = (form.mediaName as string).trim()
    const m = (form.mediaModel as string).trim() || undefined
    return [n, m, form.sortOrder]
  },
  formToUpdatePayload: (form) => {
    const n = (form.mediaName as string).trim()
    const m = (form.mediaModel as string).trim() || undefined
    return { mediaName: n, mediaModel: m, sortOrder: form.sortOrder }
  },
  softDeleteMessage: (row) =>
    `确定删除 "${row.mediaName}${row.mediaModel ? ' / ' + row.mediaModel : ''}" 吗? (软删除)`,
})

// 列定义 (2 字段: mediaName + mediaModel, mediaModel 可空显示 '—')
const columns = [
  { label: 'Media 名称', width: '1.4fr', render: (row: MediaItem) => row.mediaName },
  { label: '型号', width: '1fr', render: (row: MediaItem) => row.mediaModel || '—' },
]
</script>

<template>
  <DictManagerLayout
    :mgr="mgr"
    :columns="columns"
    title="介质字典 (Media)"
    subtitle="P2.2 后台管理 · 2 字段: Media 名称 + 型号 · 用于产品表单分区 4 media/media_model 二合一"
    dialog-title-create-key="admin.mediasview.title.add_media"
    dialog-title-edit-key="admin.mediasview.title.edit_media"
    dialog-width="540px"
    dialog-label-width="120px"
    empty-text="新增 Media开始"
    :search-placeholder="t('admin.mediasview.placeholder.search_media_name_or')"
    create-button-text="新增 Media"
  >
    <template #dialog-form="{ form }">
      <el-form-item :label="t('admin.mediasview.label.media_name')" required>
        <el-input
          v-model="form.mediaName"
          :placeholder="t('admin.mediasview.placeholder.e_g_cellulose_synthetic')"
          maxlength="100"
          show-word-limit
        />
      </el-form-item>
      <el-form-item :label="t('admin.mediasview.label.media_model')">
        <el-input
          v-model="form.mediaModel"
          :placeholder="t('admin.mediasview.placeholder.e_g_m_m')"
          maxlength="100"
          show-word-limit
        />
        <div class="text-xs text-muted mt-1">可空; (name, model) 二者组成 UNIQUE 索引</div>
      </el-form-item>
      <el-form-item :label="t('common.action.sort_order')">
        <el-input-number v-model="form.sortOrder" :min="0" :step="10" style="width: 100%" />
      </el-form-item>
    </template>
  </DictManagerLayout>
</template>
