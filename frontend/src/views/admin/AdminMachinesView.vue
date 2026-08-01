<script setup lang="ts">
// Day 10+ P2.2: Machine 字典管理页 (3 字段: machine_brand + machine_model + machine_name)
// P2.3: 新增 machine_category 编辑 (4 大类: Agriculture/Commercial/Construction/others)
// P1-1 DictManagerLayout 提取: 用 useDictManager + DictManagerLayout 替代手写 state + CRUD + 拖拽 + 模板
//   行数: 272 → ~110 (减少 60%)
//   用 #row-cells-header + #row-cells slot 承接 cell-category el-tag 复杂渲染
//   用 #dialog-form slot 承接 machineCategory el-select
// P1 Task 3: 新增 "查看三级树" + "批量绑定 MR.1" 两个按钮 + 对话框
//   对接后端 GET /api/admin/machine-tree + POST /api/admin/machine-apps/batch-bind
import { ref, reactive, computed, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { useDictManager } from '@/composables/useDictManager'
import DictManagerLayout from '@/components/DictManagerLayout.vue'
import {
  dictApi,
  machineApi,
  type MachineItem,
  type MachineReorderItem,
  type MachineTreeNode,
  type BatchBindRequest,
  type BatchBindResponse
} from '@/api'

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

// ===== P1 Task 3: 三级树对话框 =====
const treeDialogOpen = ref(false)
const treeLoading = ref(false)
const treeData = ref<MachineTreeNode[]>([])

// el-tree 渲染节点 (把后端三级结构转换为 el-tree 期望的统一 {label, children} 结构)
interface TreeDisplayNode {
  id: string
  label: string
  children?: TreeDisplayNode[]
  machineId?: number
  isLeaf?: boolean
}
const treeNodes = computed<TreeDisplayNode[]>(() =>
  treeData.value.map((cat) => ({
    id: `cat-${cat.category}`,
    label: cat.category,
    children: cat.brands.map((b) => ({
      id: `brand-${cat.category}-${b.brand}`,
      label: b.brand,
      children: b.models.map((m) => ({
        id: `model-${m.machineId}`,
        label: `${m.modelName} (#${m.machineId})`,
        machineId: m.machineId,
        isLeaf: true,
      })),
    })),
  }))
)
// 统计叶子节点 (机型) 总数
const treeMachineCount = computed(() =>
  treeData.value.reduce(
    (sum, cat) => sum + cat.brands.reduce((s, b) => s + b.models.length, 0),
    0
  )
)
const treeProps = { label: 'label', children: 'children' }

async function openTreeDialog() {
  treeDialogOpen.value = true
  // 首次打开才加载, 避免重复请求
  if (treeData.value.length === 0) {
    await loadTree()
  }
}
async function loadTree() {
  treeLoading.value = true
  try {
    treeData.value = await machineApi.getTree()
  } catch {
    // http 拦截器已弹错误 toast, 此处静默 (不重复提示)
  } finally {
    treeLoading.value = false
  }
}

// ===== P1 Task 3: 批量绑定 MR.1 对话框 =====
interface BindForm {
  machineId: number | null
  mr1Text: string
  replace: boolean
}
const bindDialogOpen = ref(false)
const bindLoading = ref(false)
const bindForm = reactive<BindForm>({
  machineId: null,
  mr1Text: '',
  replace: false,
})
const bindResult = ref<BatchBindResponse | null>(null)
const bindError = ref<string | null>(null)

// 机型下拉选项: 复用 mgr.items (页面已加载的机型列表, 含品牌/型号/名称)
const machineOptions = computed(() =>
  mgr.items.value
    .filter((m) => !m.deletedAt)
    .map((m) => ({
      value: m.id,
      label: `${m.machineBrand}${m.machineModel ? ' / ' + m.machineModel : ''}${m.machineName ? ' / ' + m.machineName : ''} (#${m.id})`,
    }))
)

function openBindDialog() {
  // 重置表单 + 结果 + 错误
  bindForm.machineId = null
  bindForm.mr1Text = ''
  bindForm.replace = false
  bindResult.value = null
  bindError.value = null
  bindDialogOpen.value = true
}

async function submitBind() {
  // 表单校验
  if (bindForm.machineId == null) {
    bindError.value = t('admin.machinesview.bind_dialog.error_machine_required')
    return
  }
  const mr1List = bindForm.mr1Text
    .split('\n')
    .map((s) => s.trim())
    .filter((s) => s.length > 0)
  if (mr1List.length === 0) {
    bindError.value = t('admin.machinesview.bind_dialog.error_mr1_empty')
    return
  }
  bindError.value = null
  bindResult.value = null
  bindLoading.value = true
  try {
    const req: BatchBindRequest = {
      machineId: bindForm.machineId,
      mr1List,
      replace: bindForm.replace,
    }
    bindResult.value = await machineApi.batchBind(req)
    // notFound 非空表示部分成功 (后端返 207), 否则全成功 (200)
    if (bindResult.value.notFound.length > 0) {
      ElMessage.warning(t('admin.machinesview.bind_dialog.partial'))
    } else {
      ElMessage.success(t('admin.machinesview.bind_dialog.success'))
    }
  } catch (e: any) {
    // http 拦截器已弹错误 toast, 此处仅设置内联错误供对话框内显示
    bindError.value =
      e?.response?.data?.detail || e?.message || t('common.action.operation_failed')
  } finally {
    bindLoading.value = false
  }
}

// 副作用清理 (规则 7.2: 防 memory leak, 组件卸载时关闭对话框)
onUnmounted(() => {
  treeDialogOpen.value = false
  bindDialogOpen.value = false
})
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
    <!-- P1 Task 3: 顶部工具条额外按钮 (查看三级树 + 批量绑定 MR.1) -->
    <template #toolbar-extra>
      <el-button
        size="small"
        :aria-label="t('admin.machinesview.btn.view_tree')"
        @click="openTreeDialog"
      >
        {{ t('admin.machinesview.btn.view_tree') }}
      </el-button>
      <el-button
        size="small"
        type="warning"
        :aria-label="t('admin.machinesview.btn.batch_bind_mr1')"
        @click="openBindDialog"
      >
        {{ t('admin.machinesview.btn.batch_bind_mr1') }}
      </el-button>
    </template>

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

  <!-- P1 Task 3: 三级树对话框 (category → brand → model) -->
  <el-dialog
    v-model="treeDialogOpen"
    :title="t('admin.machinesview.tree_dialog.title')"
    width="640px"
    append-to-body
  >
    <div v-loading="treeLoading">
      <div v-if="treeMachineCount > 0" class="mb-2 text-xs text-muted">
        {{ t('admin.machinesview.tree_dialog.node_count', { count: treeMachineCount }) }}
      </div>
      <el-tree
        v-if="treeNodes.length > 0"
        :data="treeNodes"
        :props="treeProps"
        default-expand-all
        :expand-on-click-node="true"
        node-key="id"
      />
      <el-empty
        v-else-if="!treeLoading"
        :description="t('common.noData')"
      />
    </div>
    <template #footer>
      <el-button @click="treeDialogOpen = false">{{ t('common.cancel') }}</el-button>
      <el-button :loading="treeLoading" @click="loadTree">
        {{ t('common.refresh') }}
      </el-button>
    </template>
  </el-dialog>

  <!-- P1 Task 3: 批量绑定 MR.1 对话框 -->
  <el-dialog
    v-model="bindDialogOpen"
    :title="t('admin.machinesview.bind_dialog.title')"
    width="560px"
    append-to-body
  >
    <el-form :model="bindForm" label-width="120px" size="small">
      <el-form-item :label="t('admin.machinesview.bind_dialog.label_machine')" required>
        <el-select
          v-model="bindForm.machineId"
          filterable
          :placeholder="t('admin.machinesview.placeholder.select')"
          style="width: 100%"
          :aria-label="t('admin.machinesview.bind_dialog.label_machine')"
        >
          <el-option
            v-for="opt in machineOptions"
            :key="opt.value"
            :label="opt.label"
            :value="opt.value"
          />
        </el-select>
      </el-form-item>
      <el-form-item :label="t('admin.machinesview.bind_dialog.label_mr1_list')" required>
        <el-input
          v-model="bindForm.mr1Text"
          type="textarea"
          :rows="8"
          :placeholder="t('admin.machinesview.bind_dialog.placeholder_mr1')"
          :aria-label="t('admin.machinesview.bind_dialog.label_mr1_list')"
        />
      </el-form-item>
      <el-form-item :label="t('admin.machinesview.bind_dialog.label_replace')">
        <el-radio-group v-model="bindForm.replace">
          <el-radio :value="false">{{ t('admin.machinesview.bind_dialog.replace_append') }}</el-radio>
          <el-radio :value="true">{{ t('admin.machinesview.bind_dialog.replace_replace') }}</el-radio>
        </el-radio-group>
      </el-form-item>
    </el-form>

    <!-- 内联错误提示 (http 拦截器已弹 toast, 此处供对话框内可见) -->
    <el-alert
      v-if="bindError"
      type="error"
      show-icon
      :closable="false"
      class="mb-3"
      :title="bindError"
    />

    <!-- 绑定结果摘要 (bound/skipped/removed/notFound) -->
    <template v-if="bindResult">
      <el-divider />
      <div class="mb-2 font-medium">
        {{ t('admin.machinesview.bind_dialog.result_title') }}
      </div>
      <el-descriptions :column="3" border size="small">
        <el-descriptions-item :label="t('admin.machinesview.bind_dialog.result_bound')">
          {{ bindResult.bound }}
        </el-descriptions-item>
        <el-descriptions-item :label="t('admin.machinesview.bind_dialog.result_skipped')">
          {{ bindResult.skipped }}
        </el-descriptions-item>
        <el-descriptions-item :label="t('admin.machinesview.bind_dialog.result_removed')">
          {{ bindResult.removed }}
        </el-descriptions-item>
      </el-descriptions>
      <!-- notFound 非空时高亮显示不存在的 MR.1 列表 -->
      <el-alert
        v-if="bindResult.notFound.length > 0"
        type="warning"
        show-icon
        :closable="false"
        class="mt-2"
        :title="t('admin.machinesview.bind_dialog.result_not_found', { count: bindResult.notFound.length })"
      >
        <template #default>
          <div class="mt-1">
            <el-tag
              v-for="mr1 in bindResult.notFound"
              :key="mr1"
              type="danger"
              size="small"
              class="mr-1 mb-1"
            >{{ mr1 }}</el-tag>
          </div>
        </template>
      </el-alert>
    </template>

    <template #footer>
      <el-button @click="bindDialogOpen = false">{{ t('common.cancel') }}</el-button>
      <el-button
        type="primary"
        :loading="bindLoading"
        @click="submitBind"
      >
        {{ t('admin.machinesview.bind_dialog.submit') }}
      </el-button>
    </template>
  </el-dialog>
</template>
