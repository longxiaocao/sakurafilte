<script setup lang="ts">
// Day 9: 后台产品管理列表
//   - 高级搜索 (17 字段 + 尺寸范围 + 批量 OEM)
//   - 关键字段筛选 + 分页
//   - 行操作: 编辑 / 软删 / 恢复 / 查看历史
//   - 批量对比
import { ref, reactive, onMounted, onBeforeUnmount, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { adminProductApi } from '@/api'
import type { AdminSearchRequest, ProductListItem, ProductHistoryItem } from '@/api/types'

const { t } = useI18n()

const router = useRouter()

const loading = ref(false)
// V24-F101 (P2-2, 规则 8): 加 loadError, 列表加载失败时显示重试 UI
const loadError = ref<string | null>(null)
const items = ref<ProductListItem[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(50)
const hasMore = ref(false)
const countModeUsed = ref('exact')

// E2E UI.1 修复: 列设置 — 默认隐藏次要列, 降低信息密度 (24 列 → 13 列)
//   WHY: 24 列超出运维一眼扫读上限 (≤8 列为佳), 次要列 (D3/D4/H3/H4/D7/D8/Media 等) 默认隐藏
//   核心列 (13): selection/ID/OEM/OEM2/t('common.action.type')/D1/D2/H1/H2/发布/停售/更新/操作
//   次要列 (11): MR1/D3/D4/H3/H4/D7/D8/Media/MediaModel/箱件/kg
const showAllColumns = ref(false)

// 筛选条件 (精简版, 完整 17 字段版在抽屉)
const filter = reactive<AdminSearchRequest>({
  page: 1,
  pageSize: 50,
  countMode: 'exact',
  pagingMode: 'offset',
  sizeTolerance: 5,
  sortBy: 'updated_at',
  sortDesc: true
})

// 抽屉筛选 (高级)
const drawerOpen = ref(false)
const advFilter = reactive<AdminSearchRequest>({})

// 历史抽屉
const historyOpen = ref(false)
const historyItems = ref<ProductHistoryItem[]>([])
// Day 9.4: cursor 累积 + 加载更多
const historyNextCursor = ref<string | null>(null)
const historyHasMore = ref(false)
// Day 9.3: 历史分页响应 (含 total, 反映筛选后真实条数)
const historyTotal = ref(0)
// Day 9.3: 筛选条件变化时存 localStorage (放在 historyFilter 定义之后)
// Day 9.2: 历史抽屉筛选 (changeType / since / until / limit)
const historyFilter = reactive<{
  changeType: string
  since: string
  until: string
  limit: number
}>({ changeType: '', since: '', until: '', limit: 50 })
const historyLoading = ref(false)
// Day 9.3: history 筛选条件 localStorage 持久化 (跨产品查看时保留)
const HISTORY_FILTER_KEY = 'sakura_admin_history_filter'
const resetHistoryFilter = () => {
  historyFilter.changeType = ''
  historyFilter.since = ''
  historyFilter.until = ''
  historyFilter.limit = 50
  saveHistoryFilter()
}
function saveHistoryFilter() {
  try { localStorage.setItem(HISTORY_FILTER_KEY, JSON.stringify({...historyFilter})) } catch {}
}
function loadHistoryFilter() {
  try {
    const raw = localStorage.getItem(HISTORY_FILTER_KEY)
    if (!raw) return
    const saved = JSON.parse(raw)
    if (typeof saved.changeType === 'string') historyFilter.changeType = saved.changeType
    if (typeof saved.since === 'string') historyFilter.since = saved.since
    if (typeof saved.until === 'string') historyFilter.until = saved.until
    if (typeof saved.limit === 'number') historyFilter.limit = saved.limit
  } catch {}
}
loadHistoryFilter()
// Day 9.3: 筛选条件变化时存 localStorage
watch(historyFilter, () => saveHistoryFilter(), { deep: true })
// (上面这行是第 2 个 watch,删除后保留 1 个)

// 批量选择
const selected = ref<ProductListItem[]>([])

// P2.7: 产品状态变更 (停售/恢复) 统一 loading, 防止重复点击
const productMutating = ref(false)

// P2-8.1: 列表请求取消控制器
//   快速翻页/筛选切换时取消上一次未完成请求, 避免并发竞争导致旧结果覆盖新结果
let loadAbort: AbortController | null = null

async function load() {
  // P2-8.1: 取消上一次未完成的列表请求
  loadAbort?.abort()
  const myAbort = new AbortController()
  loadAbort = myAbort
  loading.value = true
  // V24-F101 (P2-2, 规则 8): 清除上次的 loadError (重试场景)
  loadError.value = null
  try {
    const req = { ...filter, page: page.value, pageSize: pageSize.value }
    const data = await adminProductApi.search(req, { signal: myAbort.signal })
    items.value = data.items
    total.value = data.total
    hasMore.value = !!data.hasMore
    countModeUsed.value = data.countModeUsed || 'exact'
  } catch (e: any) {
    // P2-8.1: 请求被取消时静默返回 (用户主动翻页/筛选切换/卸载组件触发)
    if (e?.code === 'ERR_CANCELED' || e?.name === 'CanceledError') return
    // V24-F101 (P2-2, 规则 8): 显式记录 loadError, 让表格上方显示重试 UI
    loadError.value = e?.response?.data?.message || e?.message || '产品列表加载失败'
  } finally {
    // P2-8.1: 仅当前请求未被新请求取代时才重置 loading, 避免旧请求 finally 覆盖新请求的 loading 状态
    if (loadAbort === myAbort) loading.value = false
  }
}

function quickSearch() {
  page.value = 1
  load()
}

function openAdv() {
  Object.assign(advFilter, filter)
  drawerOpen.value = true
}

function applyAdv() {
  Object.assign(filter, advFilter)
  page.value = 1
  drawerOpen.value = false
  load()
}

function newProduct() {
  router.push('/admin/products/new')
}

function editProduct(row: ProductListItem) {
  router.push(`/admin/products/${row.id}/edit`)
}

async function discontinue(row: ProductListItem) {
  try {
    await ElMessageBox.confirm(`确定停售产品 "${row.oemNoDisplay}" 吗?`, t('common.action.confirm'), { type: 'warning' })
  } catch {
    return
  }
  if (productMutating.value) return
  productMutating.value = true
  try {
    await adminProductApi.discontinue(row.id, 'admin')
    ElMessage.success(t('admin.productsview.success.discontinued_v2'))
    load()
  } catch (e: any) {
    // V24-F101 (P2-2, 规则 8): 显式 ElMessage.error 兜底, 防止拦截器异常时操作无反馈
    ElMessage.error(e?.response?.data?.message || e?.message || '停售失败')
  } finally {
    productMutating.value = false
  }
}

async function restore(row: ProductListItem) {
  if (productMutating.value) return
  productMutating.value = true
  try {
    await adminProductApi.restore(row.id, 'admin')
    ElMessage.success(t('common.action.restored'))
    load()
  } catch (e: any) {
    // V24-F101 (P2-2, 规则 8): 显式 ElMessage.error 兜底
    ElMessage.error(e?.response?.data?.message || e?.message || '恢复失败')
  } finally {
    productMutating.value = false
  }
}

// 当前查看历史的产品 id (用于筛选条件变化时 reload)
const currentHistoryProductId = ref<number | null>(null)

async function viewHistory(row: ProductListItem) {
  // Day 9.2: 用 historyFilter 调 API (支持 changeType/since/until/limit)
  //   打开抽屉前先 load, 避免空闪烁
  //   Day 9.4: 切换产品时重置 cursor, 不然下一页的 cursor 是上个产品的
  currentHistoryProductId.value = row.id
  historyNextCursor.value = null
  historyHasMore.value = false
  historyOpen.value = true
  await loadHistory(row.id)
}

async function reloadCurrentHistory() {
  // Day 9.2: 筛选项 change 时自动 reload
  if (currentHistoryProductId.value !== null) {
    await loadHistory(currentHistoryProductId.value)
  }
}

async function loadHistory(productId: number, append = false) {
  historyLoading.value = true
  try {
    const params: any = { limit: historyFilter.limit }
    if (historyFilter.changeType) params.changeType = historyFilter.changeType
    if (historyFilter.since) params.since = new Date(historyFilter.since).toISOString()
    if (historyFilter.until) params.until = new Date(historyFilter.until).toISOString()
    if (append && historyNextCursor.value) params.cursor = historyNextCursor.value
    const result = await adminProductApi.history(productId, params)
    if (append) {
      historyItems.value = historyItems.value.concat(result.items)
    } else {
      historyItems.value = result.items
    }
    historyTotal.value = result.total
    // Day 9.4: 翻页 cursor
    historyNextCursor.value = result.nextCursor ?? null
    historyHasMore.value = !!result.nextCursor
  } catch (e: any) {
    // V24-F101 (P2-2, 规则 8): 历史加载失败时显式提示, 不再静默吞
    ElMessage.error(e?.response?.data?.message || e?.message || '历史加载失败')
  } finally {
    historyLoading.value = false
  }
}

async function loadMoreHistory() {
  if (!historyHasMore.value || historyLoading.value) return
  if (currentHistoryProductId.value == null) return
  await loadHistory(currentHistoryProductId.value, true)
}

async function batchCompare() {
  if (selected.value.length < 2) {
    ElMessage.warning(t('admin.productsview.warning.please_select_pcs_product'))
    return
  }
  if (selected.value.length > 6) {
    ElMessage.warning(t('admin.productsview.warning.at_most_compare_pcs'))
    return
  }
  // P3.5 (Task 12): 跳转到 /admin/compare?ids=1,2,3,4,5,6
  const ids = selected.value.slice(0, 6).map((p) => p.id).join(',')
  router.push(`/admin/compare?ids=${ids}`)
}

function parseChangedFields(raw?: string): { key: string; oldVal: any; newVal: any }[] {
  if (!raw) return []
  try {
    const obj = JSON.parse(raw)
    if (obj && typeof obj === 'object' && !Array.isArray(obj)) {
      return Object.entries(obj).map(([k, v]) => ({ key: k, oldVal: undefined, newVal: v }))
    }
    return []
  } catch {
    return []
  }
}

function fmtDate(iso?: string) {
  if (!iso) return ''
  return iso.substring(0, 16).replace('T', ' ')
}

function numOrDash(v?: number | string) {
  if (v === null || v === undefined || v === '') return '—'
  return v
}

function onPageChange(p: number) {
  page.value = p
  load()
}

function onSizeChange(s: number) {
  pageSize.value = s
  page.value = 1
  load()
}

onMounted(load)

// P2-8.1: 组件卸载时取消未完成的列表请求, 防止内存泄漏与卸载后状态写入
onBeforeUnmount(() => {
  loadAbort?.abort()
})
</script>

<template>
  <!-- P-Admin-UX: 改 max-w-screen-2xl mx-auto → w-full, 让 el-table 撑满容器
       WHY: 原 1536px 限制下 13 列表格 (宽 800+px) 只占左侧 ~50%, 右侧大量留白 -->
  <div class="p-3 w-full">
    <!-- A11y axe: h1 标题 (page-has-heading-one) -->
    <h1 class="text-lg font-medium mb-3">产品管理</h1>
    <!-- 顶部工具条 -->
    <!-- P1-4 修复: 工具条移动端折叠 - 次要控件 (countMode 标签, t('common.field.all')列 switch) 在 sm 以下隐藏 -->
    <div class="flex items-center gap-2 mb-3 flex-wrap">
      <el-input v-model="filter.oem2" placeholder="OEM 2" clearable size="small" style="width: 160px" :aria-label="t('admin.productsview.aria.oem_search')" data-testid="admin-search-oem2" @keyup.enter="quickSearch" />
      <el-input v-model="filter.mr1" placeholder="MR.1" clearable size="small" style="width: 120px" :aria-label="t('admin.productsview.aria.mr_search')" @keyup.enter="quickSearch" />
      <el-input v-model="filter.productName1" :placeholder="t('common.field.product_name')" clearable size="small" style="width: 160px" :aria-label="t('admin.productsview.aria.product_name_search')" @keyup.enter="quickSearch" />
      <el-select v-model="filter.type" :placeholder="t('common.action.type')" clearable size="small" style="width: 100px" :aria-label="t('admin.productsview.aria.filter_by_type')">
        <el-option label="oil" value="oil" />
        <el-option label="fuel" value="fuel" />
        <el-option label="air" value="air" />
        <el-option label="cabin" value="cabin" />
        <el-option label="others" value="others" />
      </el-select>
      <el-input v-model="filter.oem3Batch" :placeholder="t('admin.productsview.placeholder.oem_batch_count')" clearable size="small" class="hidden sm:inline-block" style="width: 220px" :aria-label="t('admin.productsview.aria.oem_batch_search')" @keyup.enter="quickSearch" />
      <el-button type="primary" size="small" @click="quickSearch">搜索</el-button>
      <el-button size="small" @click="openAdv" class="hidden sm:inline-flex">高级筛选</el-button>
      <span class="text-xs text-muted hidden sm:inline">count: {{ countModeUsed }}</span>
      <div class="flex-1" />
      <!-- E2E UI.1 修复: 列设置开关 — 默认隐藏次要列, 点击显示全部 24 列 -->
      <el-switch v-model="showAllColumns" size="small" :active-text="t('admin.productsview.string.all_columns')" :inactive-text="t('admin.productsview.string.columns')" inline-prompt class="hidden sm:inline-flex" />
      <el-button size="small" @click="batchCompare" :disabled="selected.length < 2">批量对比 ({{ selected.length }})</el-button>
      <el-button type="primary" size="small" @click="newProduct">新增产品</el-button>
    </div>

    <!-- 表格 -->
    <!-- V24-F101 (P2-2, 规则 8): 列表加载失败时显示重试 UI -->
    <el-alert
      v-if="loadError"
      type="error"
      show-icon
      :closable="false"
      class="mb-2"
    >
      <template #default>
        <div class="flex items-center justify-between">
          <span>{{ loadError }}</span>
          <el-button size="small" @click="load" :disabled="loading">
            {{ loading ? '重试中…' : '重试' }}
          </el-button>
        </div>
      </template>
    </el-alert>
    <!-- P1-5 修复: 表格容器加 overflow-x-auto - 13 列总宽 800+px, 移动端触发水平滚动而非列被裁切 -->
    <!-- P-Admin-UX v2: tableLayout="auto" 让列按内容分配, 加一个无 width 的弹性列填满右侧空区 -->
    <div class="hairline overflow-x-auto">
      <el-table
        :data="items"
        v-loading="loading"
        size="small"
        table-layout="auto"
        @selection-change="(rows: ProductListItem[]) => (selected = rows)"
        max-height="calc(100vh - 240px)"
      >
        <el-table-column type="selection" width="36" />
        <el-table-column prop="id" label="ID" width="60" />
        <el-table-column prop="oemNoDisplay" label="OEM" width="160" fixed />
        <el-table-column v-if="showAllColumns" prop="mr1" label="MR.1" width="100" show-overflow-tooltip />
        <el-table-column prop="oem2" label="OEM 2" width="120" show-overflow-tooltip />
        <el-table-column prop="type" :label="t('common.action.type')" width="60" />
        <el-table-column prop="d1Mm" label="D1" width="50" align="right" />
        <el-table-column prop="d2Mm" label="D2" width="50" align="right" />
        <el-table-column v-if="showAllColumns" prop="d3Mm" label="D3" width="50" align="right" />
        <el-table-column v-if="showAllColumns" prop="d4Mm" label="D4" width="50" align="right" />
        <el-table-column prop="h1Mm" label="H1" width="50" align="right" />
        <el-table-column prop="h2Mm" label="H2" width="50" align="right" />
        <el-table-column v-if="showAllColumns" prop="h3Mm" label="H3" width="50" align="right" />
        <el-table-column v-if="showAllColumns" prop="h4Mm" label="H4" width="50" align="right" />
        <el-table-column v-if="showAllColumns" prop="d7Thread" label="D7" width="70" />
        <el-table-column v-if="showAllColumns" prop="d8Thread" label="D8" width="70" />
        <el-table-column v-if="showAllColumns" prop="media" label="Media" width="100" show-overflow-tooltip />
        <el-table-column v-if="showAllColumns" prop="mediaModel" label="MediaModel" width="100" show-overflow-tooltip />
        <el-table-column v-if="showAllColumns" prop="qtyPerCarton" :label="t('common.action.carton_per_pcs')" width="60" align="right" />
        <el-table-column v-if="showAllColumns" prop="weightKgs" label="kg" width="60" align="right" />
        <el-table-column prop="isPublished" :label="t('common.field.publish')" width="50">
          <template #default="{ row }">
            <el-tag v-if="row.isPublished" type="success" size="small">✓</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="isDiscontinued" :label="t('admin.productsview.label.discontinued')" width="50">
          <template #default="{ row }">
            <el-tag v-if="row.isDiscontinued" type="info" size="small">已停</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('admin.productsview.label.update')" width="120">
          <template #default="{ row }">{{ fmtDate(row.updatedAt) }}</template>
        </el-table-column>
        <el-table-column :label="t('admin.productsview.label.action')" width="200" fixed="right">
          <template #default="{ row }">
            <el-button size="small" text @click="editProduct(row)">编辑</el-button>
            <el-button v-if="!row.isDiscontinued" size="small" text type="warning" @click="discontinue(row)">停售</el-button>
            <el-button v-else size="small" text type="success" @click="restore(row)">恢复</el-button>
            <el-button size="small" text @click="viewHistory(row)">历史</el-button>
            <!-- Day 9.2: history 打开后自动 reload, 避免先开再选筛选项空跑 -->

          </template>
        </el-table-column>
        <!-- P-Admin-UX v3: 无 width 弹性列, fixed 模式下自动填充剩余空间 -->
        <el-table-column label="" />
      </el-table>
    </div>

    <!-- 分页 -->
    <div class="flex items-center justify-between mt-3">
      <span class="text-xs text-muted">共 {{ total }} 条</span>
      <el-pagination
        :current-page="page"
        :page-size="pageSize"
        :page-sizes="[20, 50, 100, 200]"
        :total="total"
        layout="sizes, prev, pager, next, jumper"
        @current-change="onPageChange"
        @size-change="onSizeChange"
      />
    </div>

    <!-- 高级筛选抽屉 -->
    <el-drawer v-model="drawerOpen" :title="t('admin.productsview.title.filter')" size="640px" direction="rtl">
      <div class="p-3 space-y-3">
        <div class="text-sm font-medium">文本字段</div>
        <div class="grid grid-cols-2 gap-2">
          <el-input v-model="advFilter.productName1" :placeholder="t('common.action.product_name_1')" size="small" />
          <el-input v-model="advFilter.productName2" :placeholder="t('common.action.product_name_2')" size="small" />
          <el-input v-model="advFilter.mr1" placeholder="MR.1" size="small" />
          <el-input v-model="advFilter.oem2" placeholder="OEM 2" size="small" />
          <el-input v-model="advFilter.oemBrand" :placeholder="t('common.field.oem_brand')" size="small" />
          <el-input v-model="advFilter.mediaName" placeholder="Media" size="small" />
          <el-input v-model="advFilter.mediaModel" placeholder="MediaModel" size="small" />
          <el-input v-model="advFilter.sealingMaterial" :placeholder="t('common.action.seal_material')" size="small" />
          <el-input v-model="advFilter.efficiency1" :placeholder="t('admin.productsview.placeholder.efficiency')" size="small" />
        </div>

        <div class="text-sm font-medium">尺寸范围 (mm)</div>
        <div class="grid grid-cols-2 gap-2">
          <el-input-number v-model="advFilter.d1Min" placeholder="D1 Min" size="small" :min="0" />
          <el-input-number v-model="advFilter.d1Max" placeholder="D1 Max" size="small" :min="0" />
          <el-input-number v-model="advFilter.d2Min" placeholder="D2 Min" size="small" :min="0" />
          <el-input-number v-model="advFilter.d2Max" placeholder="D2 Max" size="small" :min="0" />
          <el-input-number v-model="advFilter.h1Min" placeholder="H1 Min" size="small" :min="0" />
          <el-input-number v-model="advFilter.h1Max" placeholder="H1 Max" size="small" :min="0" />
        </div>
        <div class="flex items-center gap-2">
          <span class="text-xs text-muted">容差 (mm):</span>
          <el-radio-group v-model="advFilter.sizeTolerance" size="small">
            <el-radio :value="1">±1</el-radio>
            <el-radio :value="5">±5</el-radio>
            <el-radio :value="10">±10</el-radio>
          </el-radio-group>
        </div>

        <div class="text-sm font-medium">车型适配</div>
        <div class="grid grid-cols-2 gap-2">
          <el-input v-model="advFilter.machineBrand" :placeholder="t('common.action.brand')" size="small" />
          <el-input v-model="advFilter.machineModel" :placeholder="t('common.action.model')" size="small" />
          <el-input v-model="advFilter.modelName" :placeholder="t('common.action.name')" size="small" />
          <el-input v-model="advFilter.engineBrand" :placeholder="t('common.field.engine_brand')" size="small" />
        </div>

        <div class="flex justify-end gap-2 pt-3">
          <el-button @click="drawerOpen = false">取消</el-button>
          <el-button type="primary" @click="applyAdv">应用</el-button>
        </div>
      </div>
    </el-drawer>

    <!-- 历史抽屉 (Day 9.1: 解析 changedFields JSON, 按字段展示) -->
    <!-- Day 9.2: 顶部加筛选 (changeType / since / until / limit) -->
    <el-drawer v-model="historyOpen" :title="t('admin.productsview.title.en_v6')" size="700px" direction="rtl" :close-on-click-modal="false">
      <!-- 筛选条 -->
      <div class="px-3 py-2 hairline-b bg-[var(--color-bg-hover)]">
        <div class="grid grid-cols-4 gap-2 items-end">
          <div>
            <div class="text-xs text-muted mb-1">类型</div>
            <el-select v-model="historyFilter.changeType" :placeholder="t('common.field.all')" clearable size="small" @change="reloadCurrentHistory">
              <el-option :label="t('common.field.all')" value="" />
              <el-option label="create" value="create" />
              <el-option label="update" value="update" />
              <el-option label="discontinue" value="discontinue" />
              <el-option label="restore" value="restore" />
            </el-select>
          </div>
          <div>
            <div class="text-xs text-muted mb-1">开始</div>
            <el-date-picker
              v-model="historyFilter.since"
              type="datetime"
              :placeholder="t('common.field.unlimited')"
              size="small"
              value-format="YYYY-MM-DDTHH:mm:ss"
              @change="reloadCurrentHistory"
            />
          </div>
          <div>
            <div class="text-xs text-muted mb-1">结束</div>
            <el-date-picker
              v-model="historyFilter.until"
              type="datetime"
              :placeholder="t('common.field.unlimited')"
              size="small"
              value-format="YYYY-MM-DDTHH:mm:ss"
              @change="reloadCurrentHistory"
            />
          </div>
          <div>
            <div class="text-xs text-muted mb-1">条数</div>
            <el-select v-model.number="historyFilter.limit" size="small" @change="reloadCurrentHistory">
              <el-option :value="20" label="20" />
              <el-option :value="50" label="50" />
              <el-option :value="100" label="100" />
              <el-option :value="200" label="200" />
            </el-select>
          </div>
        </div>
        <div class="mt-2 text-xs text-muted flex items-center gap-2">
          <!-- Day 9.3: total 反映筛选后真实总数, 不受 limit 影响 -->
          <span>共 <b class="text-fg">{{ historyTotal }}</b> 条
            <span v-if="historyTotal > historyItems.length" class="text-muted">(本页 {{ historyItems.length }} / 限制 {{ historyFilter.limit }})</span>
          </span>
          <span v-if="historyLoading">加载中...</span>
          <el-button size="small" text @click="resetHistoryFilter">重置</el-button>
        </div>
      </div>

      <div class="p-3" v-loading="historyLoading">
        <div v-if="historyItems.length === 0" class="text-center text-muted py-8">暂无历史</div>
        <el-timeline v-else>
          <el-timeline-item
            v-for="h in historyItems"
            :key="h.id"
            :timestamp="fmtDate(h.changedAt)"
            placement="top"
          >
            <div class="text-sm mb-1">
              <el-tag size="small" :type="h.changeType === 'discontinue' ? 'warning' : h.changeType === 'create' ? 'success' : h.changeType === 'restore' ? 'primary' : 'info'">
                {{ h.changeType }}
              </el-tag>
              <span class="ml-2 text-muted">by {{ h.changedBy || 'system' }}</span>
            </div>
            <el-table
              v-if="parseChangedFields(h.changedFields).length > 0"
              :data="parseChangedFields(h.changedFields)"
              size="small"
              border
              max-height="240"
            >
              <el-table-column prop="key" :label="t('admin.productsview.label.field')" width="160" />
              <el-table-column :label="t('admin.productsview.label.value')">
                <template #default="{ row }">
                  <code class="text-xs">{{ row.newVal === null || row.newVal === undefined ? 'null' : typeof row.newVal === 'object' ? JSON.stringify(row.newVal) : String(row.newVal) }}</code>
                </template>
        </el-table-column>
        <!-- P-Admin-UX v3: 历史表无 width 弹性列 (fixed 模式下自动填充剩余空间) -->
        <el-table-column label="" />
      </el-table>
            <div v-else class="text-xs text-muted">无字段级变更</div>
          </el-timeline-item>
        </el-timeline>
        <!-- Day 9.4: cursor 加载更多 (keyset 翻页) -->
        <div v-if="historyHasMore" class="text-center mt-3">
          <el-button :loading="historyLoading" @click="loadMoreHistory">加载更多</el-button>
        </div>
        <div v-else-if="historyItems.length > 0" class="text-center text-xs text-muted mt-3">已到底</div>
      </div>
    </el-drawer>
  </div>
</template>
