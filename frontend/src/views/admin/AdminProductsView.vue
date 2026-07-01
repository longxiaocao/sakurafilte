<script setup lang="ts">
// Day 9: 后台产品管理列表
//   - 高级搜索 (17 字段 + 尺寸范围 + 批量 OEM)
//   - 关键字段筛选 + 分页
//   - 行操作: 编辑 / 软删 / 恢复 / 查看历史
//   - 批量对比
import { ref, reactive, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { adminProductApi } from '@/api'
import type { AdminSearchRequest, ProductListItem, ProductHistoryItem } from '@/api/types'

const router = useRouter()

const loading = ref(false)
const items = ref<ProductListItem[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(50)
const hasMore = ref(false)
const countModeUsed = ref('exact')

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

// 批量选择
const selected = ref<ProductListItem[]>([])

async function load() {
  loading.value = true
  try {
    const req = { ...filter, page: page.value, pageSize: pageSize.value }
    const data = await adminProductApi.search(req)
    items.value = data.items
    total.value = data.total
    hasMore.value = !!data.hasMore
    countModeUsed.value = data.countModeUsed || 'exact'
  } catch (e: any) {
    // 错误已被拦截器处理
  } finally {
    loading.value = false
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
    await ElMessageBox.confirm(`确定停售产品 "${row.oemNoDisplay}" 吗?`, '确认', { type: 'warning' })
    await adminProductApi.discontinue(row.id, 'admin')
    ElMessage.success('已停售')
    load()
  } catch (e: any) {
    if (e !== 'cancel') {
      // 错误已被拦截器
    }
  }
}

async function restore(row: ProductListItem) {
  try {
    await adminProductApi.restore(row.id, 'admin')
    ElMessage.success('已恢复')
    load()
  } catch (e: any) {}
}

async function viewHistory(row: ProductListItem) {
  try {
    const { items } = await adminProductApi.history(row.id, 50)
    historyItems.value = items
    historyOpen.value = true
  } catch (e: any) {}
}

async function batchCompare() {
  if (selected.value.length < 2) {
    ElMessage.warning('请选择 2-6 个产品')
    return
  }
  if (selected.value.length > 6) {
    ElMessage.warning('最多对比 6 个')
    return
  }
  try {
    const { count, items } = await adminProductApi.compare(selected.value.map((p) => p.id))
    ElMessageBox.alert(
      `对比 ${count} 个产品, 详情请到详情页查看。\n` + items.map((p) => `${p.id}: ${p.oemNoDisplay}`).join('\n'),
      '批量对比结果',
      { type: 'info' }
    )
  } catch (e: any) {}
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
</script>

<template>
  <div class="p-3 max-w-screen-2xl mx-auto">
    <!-- 顶部工具条 -->
    <div class="flex items-center gap-2 mb-3 flex-wrap">
      <el-input v-model="filter.oem2" placeholder="OEM 2" clearable size="small" style="width: 160px" @keyup.enter="quickSearch" />
      <el-input v-model="filter.mr1" placeholder="MR.1" clearable size="small" style="width: 120px" @keyup.enter="quickSearch" />
      <el-input v-model="filter.productName1" placeholder="产品名" clearable size="small" style="width: 160px" @keyup.enter="quickSearch" />
      <el-select v-model="filter.type" placeholder="类型" clearable size="small" style="width: 100px">
        <el-option label="oil" value="oil" />
        <el-option label="fuel" value="fuel" />
        <el-option label="air" value="air" />
        <el-option label="cabin" value="cabin" />
        <el-option label="others" value="others" />
      </el-select>
      <el-input v-model="filter.oem3Batch" placeholder="OEM 3 批量 (逗号分隔)" clearable size="small" style="width: 220px" @keyup.enter="quickSearch" />
      <el-button type="primary" size="small" @click="quickSearch">搜索</el-button>
      <el-button size="small" @click="openAdv">高级筛选</el-button>
      <span class="text-xs text-muted">count: {{ countModeUsed }}</span>
      <div class="flex-1" />
      <el-button size="small" @click="batchCompare" :disabled="selected.length < 2">批量对比 ({{ selected.length }})</el-button>
      <el-button type="primary" size="small" @click="newProduct">新增产品</el-button>
    </div>

    <!-- 表格 -->
    <div class="hairline">
      <el-table
        :data="items"
        v-loading="loading"
        size="small"
        @selection-change="(rows: ProductListItem[]) => (selected = rows)"
        max-height="calc(100vh - 240px)"
      >
        <el-table-column type="selection" width="36" />
        <el-table-column prop="id" label="ID" width="60" />
        <el-table-column prop="oemNoDisplay" label="OEM" width="160" fixed />
        <el-table-column prop="mr1" label="MR.1" width="100" show-overflow-tooltip />
        <el-table-column prop="oem2" label="OEM 2" width="120" show-overflow-tooltip />
        <el-table-column prop="type" label="类型" width="60" />
        <el-table-column prop="d1Mm" label="D1" width="50" align="right" />
        <el-table-column prop="d2Mm" label="D2" width="50" align="right" />
        <el-table-column prop="d3Mm" label="D3" width="50" align="right" />
        <el-table-column prop="d4Mm" label="D4" width="50" align="right" />
        <el-table-column prop="h1Mm" label="H1" width="50" align="right" />
        <el-table-column prop="h2Mm" label="H2" width="50" align="right" />
        <el-table-column prop="h3Mm" label="H3" width="50" align="right" />
        <el-table-column prop="h4Mm" label="H4" width="50" align="right" />
        <el-table-column prop="d7Thread" label="D7" width="70" />
        <el-table-column prop="d8Thread" label="D8" width="70" />
        <el-table-column prop="media" label="Media" width="100" show-overflow-tooltip />
        <el-table-column prop="mediaModel" label="MediaModel" width="100" show-overflow-tooltip />
        <el-table-column prop="qtyPerCarton" label="箱/件" width="60" align="right" />
        <el-table-column prop="weightKgs" label="kg" width="60" align="right" />
        <el-table-column prop="isPublished" label="发布" width="50">
          <template #default="{ row }">
            <el-tag v-if="row.isPublished" type="success" size="small">✓</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="isDiscontinued" label="停售" width="50">
          <template #default="{ row }">
            <el-tag v-if="row.isDiscontinued" type="info" size="small">已停</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="更新" width="120">
          <template #default="{ row }">{{ fmtDate(row.updatedAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="200" fixed="right">
          <template #default="{ row }">
            <el-button size="small" text @click="editProduct(row)">编辑</el-button>
            <el-button v-if="!row.isDiscontinued" size="small" text type="warning" @click="discontinue(row)">停售</el-button>
            <el-button v-else size="small" text type="success" @click="restore(row)">恢复</el-button>
            <el-button size="small" text @click="viewHistory(row)">历史</el-button>
          </template>
        </el-table-column>
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
    <el-drawer v-model="drawerOpen" title="高级筛选" size="640px" direction="rtl">
      <div class="p-3 space-y-3">
        <div class="text-sm font-medium">文本字段</div>
        <div class="grid grid-cols-2 gap-2">
          <el-input v-model="advFilter.productName1" placeholder="产品名 1" size="small" />
          <el-input v-model="advFilter.productName2" placeholder="产品名 2" size="small" />
          <el-input v-model="advFilter.mr1" placeholder="MR.1" size="small" />
          <el-input v-model="advFilter.oem2" placeholder="OEM 2" size="small" />
          <el-input v-model="advFilter.oemBrand" placeholder="OEM 品牌" size="small" />
          <el-input v-model="advFilter.mediaName" placeholder="Media" size="small" />
          <el-input v-model="advFilter.mediaModel" placeholder="MediaModel" size="small" />
          <el-input v-model="advFilter.sealingMaterial" placeholder="密封材料" size="small" />
          <el-input v-model="advFilter.efficiency1" placeholder="效率" size="small" />
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
          <el-input v-model="advFilter.machineBrand" placeholder="品牌" size="small" />
          <el-input v-model="advFilter.machineModel" placeholder="型号" size="small" />
          <el-input v-model="advFilter.modelName" placeholder="名称" size="small" />
          <el-input v-model="advFilter.engineBrand" placeholder="发动机品牌" size="small" />
        </div>

        <div class="flex justify-end gap-2 pt-3">
          <el-button @click="drawerOpen = false">取消</el-button>
          <el-button type="primary" @click="applyAdv">应用</el-button>
        </div>
      </div>
    </el-drawer>

    <!-- 历史抽屉 (Day 9.1: 解析 changedFields JSON, 按字段展示) -->
    <el-drawer v-model="historyOpen" title="变更历史" size="700px" direction="rtl">
      <div class="p-3" v-if="historyItems.length > 0">
        <el-timeline>
          <el-timeline-item
            v-for="h in historyItems"
            :key="h.id"
            :timestamp="fmtDate(h.changedAt)"
            placement="top"
          >
            <div class="text-sm mb-1">
              <el-tag size="small" :type="h.changeType === 'discontinue' ? 'warning' : h.changeType === 'create' ? 'success' : 'info'">
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
              <el-table-column prop="key" label="字段" width="160" />
              <el-table-column label="新值">
                <template #default="{ row }">
                  <code class="text-xs">{{ row.newVal === null || row.newVal === undefined ? 'null' : typeof row.newVal === 'object' ? JSON.stringify(row.newVal) : String(row.newVal) }}</code>
                </template>
              </el-table-column>
            </el-table>
            <div v-else class="text-xs text-muted">无字段级变更</div>
          </el-timeline-item>
        </el-timeline>
      </div>
      <div v-else class="text-center text-muted py-8">暂无历史</div>
    </el-drawer>
  </div>
</template>
