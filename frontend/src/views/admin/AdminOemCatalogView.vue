<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Refresh, Search } from '@element-plus/icons-vue'
import { oemCatalogApi } from '@/api'
import type { OemCatalogProduct, OemCatalogProductDetail, OemCatalogSummary, OemCatalogCrossReference, OemCatalogApplication } from '@/api/types'

const loading = ref(false)
const detailLoading = ref(false)
const summary = ref<OemCatalogSummary | null>(null)
const rows = ref([] as Awaited<ReturnType<typeof oemCatalogApi.list>>['items'])
const total = ref(0)
const page = ref(1)
const pageSize = 50
const query = ref('')
const drawerOpen = ref(false)
const detail = ref<OemCatalogProductDetail | null>(null)
const mr1Input = ref('')
const changeReason = ref('')
const savingMr1 = ref(false)
const detailTab = ref('xrefs')
const xrefRows = ref<OemCatalogCrossReference[]>([])
const xrefTotal = ref(0)
const xrefPage = ref(1)
const applicationRows = ref<OemCatalogApplication[]>([])
const applicationTotal = ref(0)
const applicationPage = ref(1)
const detailListPageSize = 20
const xrefLoading = ref(false)
const applicationLoading = ref(false)

const specEntries = computed(() => Object.entries(detail.value?.specPayload ?? {}).filter(([, value]) => value !== null && value !== ''))
function errorDetail(error: unknown, fallback: string): string {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: unknown } } }).response
    if (typeof response?.data?.detail === 'string') return response.data.detail
  }
  return fallback
}

async function load() {
  loading.value = true
  try {
    const [nextSummary, result] = await Promise.all([
      oemCatalogApi.summary(),
      oemCatalogApi.list({ q: query.value.trim() || undefined, page: page.value, pageSize })
    ])
    summary.value = nextSummary
    rows.value = result.items
    total.value = result.total
  } catch (error: unknown) {
    ElMessage.error(errorDetail(error, 'OEM 目录暂不可用'))
  } finally {
    loading.value = false
  }
}

function search() { page.value = 1; load() }
async function openDetail(oemNo1: string) {
  drawerOpen.value = true
  detail.value = null
  detailTab.value = 'xrefs'
  xrefRows.value = []
  xrefTotal.value = 0
  xrefPage.value = 1
  applicationRows.value = []
  applicationTotal.value = 0
  applicationPage.value = 1
  detailLoading.value = true
  try {
    detail.value = await oemCatalogApi.detail(oemNo1)
    mr1Input.value = detail.value.activeMr1 ?? ''
    changeReason.value = ''
    xrefTotal.value = detail.value.crossReferenceCount
    applicationTotal.value = detail.value.machineApplicationCount
    await loadXrefs()
  }
  catch (error: unknown) { ElMessage.error(errorDetail(error, '加载 OEM 详情失败')) }
  finally { detailLoading.value = false }
}
async function loadXrefs() {
  if (!detail.value) return
  xrefLoading.value = true
  try {
    const result = await oemCatalogApi.xrefs(detail.value.oemNo1, { page: xrefPage.value, pageSize: detailListPageSize })
    xrefRows.value = result.items
    xrefTotal.value = result.total
  } catch (error: unknown) { ElMessage.error(errorDetail(error, '加载交叉号失败')) }
  finally { xrefLoading.value = false }
}
async function loadApplications() {
  if (!detail.value) return
  applicationLoading.value = true
  try {
    const result = await oemCatalogApi.applications(detail.value.oemNo1, { page: applicationPage.value, pageSize: detailListPageSize })
    applicationRows.value = result.items
    applicationTotal.value = result.total
  } catch (error: unknown) { ElMessage.error(errorDetail(error, '加载机型适配失败')) }
  finally { applicationLoading.value = false }
}
function onDetailTabChange(tab: string | number) {
  detailTab.value = String(tab)
  if (detailTab.value === 'xrefs' && !xrefRows.value.length && xrefTotal.value > 0) loadXrefs()
  if (detailTab.value === 'applications' && !applicationRows.value.length && applicationTotal.value > 0) loadApplications()
}
function closeDetail() {
  drawerOpen.value = false
  detail.value = null
  xrefRows.value = []
  applicationRows.value = []
}
async function saveMr1() {
  if (!detail.value) return
  savingMr1.value = true
  try {
    await oemCatalogApi.setMr1(detail.value.oemNo1, { mr1: mr1Input.value.trim() || null, changeReason: changeReason.value.trim() || null })
    const oemNo1 = detail.value.oemNo1
    detail.value = await oemCatalogApi.detail(oemNo1)
    mr1Input.value = detail.value.activeMr1 ?? ''
    await Promise.all([load(), detailTab.value === 'xrefs' ? loadXrefs() : loadApplications()])
    ElMessage.success('MR.1 映射已更新')
  } catch (error: unknown) { ElMessage.error(errorDetail(error, 'MR.1 映射更新失败')) }
  finally { savingMr1.value = false }
}

onMounted(load)
</script>

<template>
  <main class="p-4 max-w-7xl mx-auto" data-testid="oem-catalog-page">
    <div class="flex items-start justify-between gap-3 mb-4">
      <div><h1 class="text-lg font-medium">OEM 目录核验</h1><p class="text-sm text-[var(--color-text-muted)]">以 OEM NO 1 为锚点；MR.1 当前仅为可选映射。</p></div>
      <el-button :loading="loading" circle aria-label="刷新" @click="load"><el-icon><Refresh /></el-icon></el-button>
    </div>
    <section v-if="summary" class="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-6 gap-px bg-[var(--color-border)] hairline mb-4">
      <div v-for="item in [ ['OEM', summary.oemProductCount], ['有规格', summary.productWithSpecCount], ['交叉号', summary.crossReferenceCount], ['机型适配', summary.machineApplicationCount], ['MR.1 映射', summary.activeMr1MappingCount], ['发布批次', summary.lastPublishedBatchId ?? '-'] ]" :key="item[0]" class="bg-[var(--color-bg)] p-3">
        <div class="text-xs text-[var(--color-text-muted)]">{{ item[0] }}</div><div class="font-mono text-base mt-1">{{ item[1] }}</div>
      </div>
    </section>
    <section class="hairline p-3">
      <div class="flex gap-2 mb-3"><el-input v-model="query" clearable placeholder="搜索 OEM NO 1 或产品名称" @keyup.enter="search"><template #prefix><el-icon><Search /></el-icon></template></el-input><el-button type="primary" @click="search">搜索</el-button></div>
      <el-table v-loading="loading" :data="rows" row-key="oemNo1" @row-click="(row: OemCatalogProduct) => openDetail(row.oemNo1)">
        <el-table-column prop="oemNo1Display" label="OEM NO 1" min-width="170"><template #default="{ row }"><span class="font-mono">{{ row.oemNo1Display }}</span></template></el-table-column>
        <el-table-column prop="crossReferenceCount" label="交叉号" width="90" align="right" /><el-table-column prop="machineApplicationCount" label="机型" width="90" align="right" />
        <el-table-column prop="activeMr1" label="MR.1" width="130"><template #default="{ row }">{{ row.activeMr1 || '-' }}</template></el-table-column>
        <el-table-column label="冲突" width="75" align="right"><template #default="{ row }">{{ row.specConflictFieldCount || '-' }}</template></el-table-column>
      </el-table>
      <div class="flex justify-end mt-3"><el-pagination v-model:current-page="page" :page-size="pageSize" :total="total" layout="total, prev, pager, next" @current-change="load" /></div>
    </section>
    <el-drawer v-model="drawerOpen" size="min(760px, 96vw)" title="OEM 目录详情" @close="closeDetail"><el-skeleton v-if="detailLoading" :rows="8" animated /><template v-else-if="detail"><div class="font-mono text-lg">{{ detail.oemNo1Display }}</div><div class="grid grid-cols-3 gap-2 my-4 text-sm"><div>交叉号<br><b>{{ detail.crossReferenceCount }}</b></div><div>机型<br><b>{{ detail.machineApplicationCount }}</b></div><div>MR.1<br><b>{{ detail.activeMr1 || '-' }}</b></div></div><el-form label-position="top" class="hairline-t pt-3 mb-4"><el-form-item label="MR.1"><el-input v-model="mr1Input" clearable placeholder="留空则解除当前映射" /></el-form-item><el-form-item label="变更原因"><el-input v-model="changeReason" maxlength="500" show-word-limit /></el-form-item><el-button type="primary" :loading="savingMr1" @click="saveMr1">更新 MR.1</el-button></el-form><el-alert v-if="detail.specConflictFieldCount" type="warning" :closable="false" :title="`存在 ${detail.specConflictFieldCount} 个规格字段冲突，需保留来源核验。`" class="mb-3" /><el-tabs v-model="detailTab" @tab-change="onDetailTabChange"><el-tab-pane label="交叉号" name="xrefs"><el-table v-loading="xrefLoading" :data="xrefRows" size="small"><el-table-column prop="productName1" label="产品名" min-width="150" /><el-table-column prop="oemBrand" label="OEM 品牌" min-width="110" /><el-table-column prop="oemNo3" label="OEM NO 3" min-width="130" /><el-table-column prop="sourceRowNos" label="来源行" min-width="100"><template #default="{ row }">{{ row.sourceRowNos.join(', ') }}</template></el-table-column><el-table-column prop="mergedSourceRowCount" label="合并行数" width="90" align="right" /></el-table><div class="flex justify-end mt-2"><el-pagination v-model:current-page="xrefPage" :page-size="detailListPageSize" :total="xrefTotal" layout="total, prev, pager, next" @current-change="loadXrefs" /></div></el-tab-pane><el-tab-pane label="机型适配" name="applications"><el-table v-loading="applicationLoading" :data="applicationRows" size="small"><el-table-column prop="machineBrand" label="机器品牌" min-width="110" /><el-table-column prop="machineModel" label="机器型号" min-width="120" /><el-table-column prop="modelName" label="车型名" min-width="120" /><el-table-column prop="engineBrand" label="发动机品牌" min-width="110" /><el-table-column prop="engineModel" label="发动机型号" min-width="120" /><el-table-column prop="productionDate" label="生产日期" min-width="100" /><el-table-column prop="power" label="功率" min-width="80" /><el-table-column prop="sourceRowNo" label="来源行" width="80" align="right" /></el-table><div class="flex justify-end mt-2"><el-pagination v-model:current-page="applicationPage" :page-size="detailListPageSize" :total="applicationTotal" layout="total, prev, pager, next" @current-change="loadApplications" /></div></el-tab-pane></el-tabs><el-descriptions title="规格" :column="1" border class="mt-4"><el-descriptions-item v-for="[key, value] in specEntries" :key="key" :label="key">{{ value }}</el-descriptions-item><el-descriptions-item v-if="!specEntries.length" label="规格">暂无</el-descriptions-item></el-descriptions></template></el-drawer>
  </main>
</template>
