<script setup lang="ts">
// Day 10+ P2.2: Machine 字典管理页 (3 字段: machine_brand + machine_model + machine_name)
// P2.3: 新增 machine_category 编辑 (4 大类: Agriculture/Commercial/Construction/others)
// P1-1 DictManagerLayout 提取: 用 useDictManager + DictManagerLayout 替代手写 state + CRUD + 拖拽 + 模板
//   行数: 272 → ~110 (减少 60%)
//   用 #row-cells-header + #row-cells slot 承接 cell-category el-tag 复杂渲染
//   用 #dialog-form slot 承接 machineCategory el-select
import { useI18n } from 'vue-i18n'
import { useDictManager } from '@/composables/useDictManager'
import DictManagerLayout from '@/components/DictManagerLayout.vue'
import { dictApi, type MachineItem, type MachineReorderItem } from '@/api'

const { t } = useI18n()

// P2.3: 4 大类常量, 给 <el-select> 用
const CATEGORY_OPTIONS = ['Agriculture', 'Commercial', 'Construction', 'others'] as const
type Category = (typeof CATEGORY_OPTIONS)[number]

// P2.3: category 标签颜色 (4 大类各一色)
function categoryTagType(cat?: string): 'success' | 'warning' | 'info' | 'primary' {
  switch (cat) {
    case 'Agriculture': return 'success'   // 绿 (农林)
    case 'Commercial': return 'primary'   // 蓝 (商用)
    case 'Construction': return 'warning' // 橙 (工程)
    default: return 'info'                 // 灰 (others)
  }
}

const mgr = useDictManager<MachineItem, MachineReorderItem>({
  api: dictApi.machines,
  emptyForm: () => ({
    machineBrand: '',
    machineModel: '',
    machineName: '',
    machineCategory: 'others' as Category,
    sortOrder: 0,
  }),
  rowToForm: (row) => ({
    id: row.id,
    machineBrand: row.machineBrand,
    machineModel: row.machineModel ?? '',
    machineName: row.machineName ?? '',
    // P2.3: 兜底 'others' (兼容老数据无 category 字段)
    machineCategory: (row.machineCategory as Category) ?? 'others',
    sortOrder: row.sortOrder,
  }),
  validate: (form) => {
    const b = (form.machineBrand as string).trim()
    if (!b) return { ok: false, errMsg: t('admin.machinesview.warning.machine_model_brand_cannot_be') }
    if (b.length > 200) return { ok: false, errMsg: t('admin.machinesview.warning.machine_model_brand_length') }
    return { ok: true }
  },
  formToCreatePayload: (form) => {
    const b = (form.machineBrand as string).trim()
    const model = (form.machineModel as string).trim() || undefined
    const name = (form.machineName as string).trim() || undefined
    // Day 11 Phase 1 BUG FIX B: create 时也传 machineCategory (之前漏传, 后端默认 "others")
    return [b, model, name, form.sortOrder, form.machineCategory]
  },
  formToUpdatePayload: (form) => {
    const b = (form.machineBrand as string).trim()
    const model = (form.machineModel as string).trim() || undefined
    const name = (form.machineName as string).trim() || undefined
    // P2.3: 提交时把 machineCategory 一并 PUT
    return {
      machineBrand: b,
      machineModel: model,
      machineName: name,
      sortOrder: form.sortOrder,
      machineCategory: form.machineCategory,
    }
  },
  softDeleteMessage: (row) => {
    const label = `${row.machineBrand}${row.machineModel ? ' / ' + row.machineModel : ''}${row.machineName ? ' / ' + row.machineName : ''}`
    return `确定删除 "${label}" 吗? (软删除)`
  },
})

// P1-1: 显式 gridTemplate (4 数据列 + cell-category 80px, 11 列总宽)
// 32px 60px 1fr 1.2fr 1.2fr 100px(category) 80px(sort) 100px(xref) 140px(updated) 80px(status) 200px(action)
const gridTemplate = '32px 60px 1fr 1.2fr 1.2fr 100px 80px 100px 140px 80px 200px'
</script>

<template>
  <DictManagerLayout
    :mgr="mgr"
    :grid-template="gridTemplate"
    title="机型字典 (Machine)"
    subtitle="P2.2 后台管理 · 3 字段: 品牌 + 型号 + 名称 · 用于产品表单分区 7 适用车型"
    dialog-title-create-key="admin.machinesview.title.add_machine_model"
    dialog-title-edit-key="admin.machinesview.title.edit_machine_model"
    dialog-width="560px"
    dialog-label-width="120px"
    empty-text="新增机型开始"
    :search-placeholder="t('common.field.search_any_field')"
    create-button-text="新增机型"
  >
    <!-- 复杂表头: cell-brand + cell-model + cell-name + cell-category (4 数据列) -->
    <template #row-cells-header>
      <div>品牌</div>
      <div>型号</div>
      <div>名称</div>
      <div>分类</div>
    </template>

    <!-- 复杂行渲染: 4 数据列, cell-category 用 el-tag 显示分类色 -->
    <template #row-cells="{ row }">
      <div>{{ row.machineBrand }}</div>
      <div>{{ row.machineModel || '—' }}</div>
      <div>{{ row.machineName || '—' }}</div>
      <div>
        <el-tag :type="categoryTagType(row.machineCategory)" size="small">
          {{ row.machineCategory || 'others' }}
        </el-tag>
      </div>
    </template>

    <!-- dialog 表单: 3 字段 + category el-select -->
    <template #dialog-form="{ form }">
      <el-form-item :label="t('common.action.brand')" required>
        <el-input
          v-model="form.machineBrand"
          :placeholder="t('common.field.e_g_bosch')"
          maxlength="200"
          show-word-limit
        />
      </el-form-item>
      <el-form-item :label="t('common.action.model')">
        <el-input
          v-model="form.machineModel"
          :placeholder="t('admin.machinesview.placeholder.e_g_empty')"
          maxlength="200"
          show-word-limit
        />
      </el-form-item>
      <el-form-item :label="t('common.action.name')">
        <el-input
          v-model="form.machineName"
          :placeholder="t('admin.machinesview.placeholder.e_g_tractor_x')"
          maxlength="200"
          show-word-limit
        />
        <div class="text-xs text-muted mt-1">3 字段组成 UNIQUE 索引, 任一字段可空</div>
      </el-form-item>
      <!-- P2.3: 分类下拉 (4 大类) -->
      <el-form-item :label="t('admin.machinesview.label.category')">
        <el-select
          v-model="form.machineCategory"
          :placeholder="t('admin.machinesview.placeholder.select')"
          style="width: 100%"
        >
          <el-option
            v-for="opt in CATEGORY_OPTIONS"
            :key="opt"
            :label="opt"
            :value="opt"
          />
        </el-select>
        <div class="text-xs text-muted mt-1">P2.3: 4 大类 (Agriculture/Commercial/Construction/others) 用于前台按场景聚合品牌</div>
      </el-form-item>
      <el-form-item :label="t('common.action.sort_order')">
        <el-input-number v-model="form.sortOrder" :min="0" :step="10" style="width: 100%" />
      </el-form-item>
    </template>
  </DictManagerLayout>
</template>
