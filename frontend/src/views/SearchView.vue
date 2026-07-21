<script setup lang="ts">
// Day 9: 产品搜索页
//   - 顶部搜索框 (全文/按字段)
//   - 高级筛选抽屉 (17 字段 + 尺寸范围 + 批量 OEM)
//   - 高密度表格 (27 列, 紧凑)
//   - 分页 (offset / cursor)
//   - 排序白名单
//   - 点击行 → 详情页
// Task 9 (P3.1): 尺寸容差 UI — ±1 / ±5 / ±10mm 下拉, 默认 5,
//   切换容差自动重新搜索, 显示结果数变化提示
// P3.2 (Task 10): 新增"批量粘贴" Tab — Excel 多行粘贴 OEM
//   - 解析: tab/换行/逗号/分号 分隔, trim + 去重
//   - 边界: 中文/斜杠/引号/空行/重复 健壮处理
//   - 展示: 表格 + 进度条 + 排序
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { searchApi } from '@/api'
import type { SearchResult, SearchHit, BatchOemResult } from '@/api/types'
import EmptyState from '@/components/EmptyState.vue'
import { useI18n } from 'vue-i18n'
import { buildProductUrl } from '@/utils/build-product-url'

const router = useRouter()
const { t } = useI18n()

const q = ref('')
// 修复: searched 标志位 — 区分"已输入未搜索"与"已搜索无结果"两种状态
//   WHY: 用户输入关键词但未点搜索时, hits.length===0 + !loading 会立即显示"暂无结果", 体验差
const searched = ref(false)
// Task 9 (P3.1): 容差默认 5mm (后端 SearchRequest.Tolerance 默认值, 与 AdminProductsView 对齐)
const tolerance = ref<1 | 5 | 10>(5)
const loading = ref(false)
const hits = ref<SearchHit[]>([])
const total = ref(0)
const provider = ref('')
const lastError = ref('')
// Task 9 (P3.1): 上一次容差 + 上一次总数, 用于"切换后命中 X 条, 较 ±N 多 Y 条"提示
const prevTolerance = ref<1 | 5 | 10 | null>(null)
const prevTotal = ref<number | null>(null)
// Task 9 (P3.1): 容差切换提示文案, 例如 "切换后命中 50+ 条, 较 ±1mm 多 35 条"
const toleranceHint = computed(() => {
  if (prevTolerance.value === null || prevTotal.value === null) return ''
  const cur = total.value
  const prev = prevTotal.value
  const diff = cur - prev
  if (diff > 0) return `切换后命中 ${cur} 条, 较 ±${prevTolerance.value}mm 多 ${diff} 条`
  if (diff < 0) return `切换后命中 ${cur} 条, 较 ±${prevTolerance.value}mm 少 ${-diff} 条`
  return `切换后命中 ${cur} 条, 与 ±${prevTolerance.value}mm 相同`
})

// P2-8.1: 搜索请求取消控制器
//   快速切换容差时取消上一次未完成请求, 避免并发竞争导致旧结果覆盖新结果
let searchAbort: AbortController | null = null

async function doSearch() {
  if (!q.value.trim()) {
    hits.value = []
    total.value = 0
    // 修复: 空查询时重置 searched, 让"输入关键词开始搜索"占位符重新显示
    searched.value = false
    return
  }
  // 修复: 标记已执行搜索, 用于区分"已输入未搜索"与"已搜索无结果"
  searched.value = true
  // P2-8.1: 取消上一次未完成的搜索请求
  searchAbort?.abort()
  const myAbort = new AbortController()
  searchAbort = myAbort
  loading.value = true
  lastError.value = ''
  try {
    const { provider: p, result } = await searchApi.search({
      q: q.value.trim(),
      // Task 9 (P3.1): 把容差传到后端 (后端 SearchRequest.Tolerance, PostgresSearchProvider 走 ±t 区间)
      tolerance: tolerance.value,
      pageSize: 50
    }, { signal: myAbort.signal })
    provider.value = p
    // Day 9.2: 修复 - 后端字段是 items (PascalCase), 不是 hits
    //   兼容 fallback: 万一后端返回 hits 也能用
    const items = (result?.items ?? result?.hits ?? []) as SearchHit[]
    hits.value = items
    total.value = result?.total ?? 0
  } catch (e: any) {
    // P2-8.1: 请求被取消时静默返回 (用户主动切换容差/卸载组件触发)
    if (e?.code === 'ERR_CANCELED' || e?.name === 'CanceledError') return
    lastError.value = e?.message || '搜索失败'
    // Day 9.2: 出错时清空, 防止 undefined.length 报错
    hits.value = []
    total.value = 0
  } finally {
    // P2-8.1: 仅当前请求未被新请求取代时才重置 loading, 避免旧请求 finally 覆盖新请求的 loading 状态
    if (searchAbort === myAbort) loading.value = false
  }
}

// Task 9 (P3.1): 切换容差后自动重新搜索 (仅在已有关键词时触发, 空状态不打扰)
//   策略: 缓存切换前的 (tolerance, total), 搜索完成后用 toleranceHint 提示差异
watch(tolerance, async (_newVal, oldVal) => {
  if (typeof oldVal !== 'number') return
  if (!q.value.trim()) return
  prevTolerance.value = oldVal
  prevTotal.value = total.value
  await doSearch()
})

// 修复: 用户修改关键词时重置 searched, 让"已输入未搜索"中间状态重新显示
//   WHY: 用户改了关键词后, 旧结果不再适用, 需要提示重新搜索
//   注意: doSearch 不修改 q, 此 watch 不会与 doSearch 内的 searched=true 循环
watch(q, (newVal) => {
  searched.value = false
  if (!newVal) {
    hits.value = []
    total.value = 0
  }
})

function viewDetail(row: SearchHit) {
  // V2 Task 4.4: 改用 SEO URL /products/{pn1}/{pn2}/{brand}/{oem3}
  //   WHY window.location.href: 新 SEO URL 由后端 Razor SSR 渲染, 不走 SPA 路由
  //   降级: SearchHit 无 pn1/pn2/brand 字段, buildProductUrl 自动降级为 /product/{oem} 走后端 301
  // Day 9.2: 兼容 snake_case 和 PascalCase 字段
  const oem = row.oemNoDisplay ?? row.oem_no_display ?? ''
  const url = buildProductUrl({
    oemNoDisplay: oem,
    productName1: row.product_name_1,
    mr1: row.mr_1
  })
  window.location.href = url
}

function fmtDate(iso?: string) {
  if (!iso) return ''
  return iso.substring(0, 16).replace('T', ' ')
}

// ===== P3.2 (Task 10): 批量粘贴 =====
const activeTab = ref<'single' | 'batch'>('single')

const batchInput = ref('')
const batchLoading = ref(false)
const batchError = ref('')
const batchResults = ref<BatchOemResult[]>([])
const batchTotal = ref(0)
const batchHits = ref(0)
const batchMiss = ref(0)
const batchElapsedMs = ref(0)

// 解析预览 (实时显示解析出的 OEM 数 + 重复数, 给用户即时反馈)
const parsedPreview = computed(() => {
  const oems = batchInput.value
    .split(/[\t\n,;]+/)
    .map((s) => s.trim())
    .filter(Boolean)
  const unique = [...new Set(oems)]
  return {
    raw: oems.length,
    unique: unique.length,
    duplicates: oems.length - unique.length
  }
})

async function doBatchSearch() {
  batchError.value = ''
  // 解析: tab/换行/逗号/分号 分隔, trim + 去重 (上限 500 由后端校验)
  //   WHY split 用 [\t\n,;]: 兼容 Excel 粘贴 (tab 单元格) / CSV (逗号) / 行复制 (换行) / 分号
  //   WHY 不过滤中文/斜杠/引号: 这些都是合法 OEM 字符, 留作原样匹配
  const oems = [...new Set(
    batchInput.value
      .split(/[\t\n,;]+/)
      .map((s) => s.trim())
      .filter(Boolean)
  )]
  if (oems.length === 0) {
    ElMessage.warning(t('common.feedback.error_045'))
    return
  }
  if (oems.length > 500) {
    ElMessage.error(`最多 500 个 OEM, 当前 ${oems.length}`)
    return
  }
  batchLoading.value = true
  const t0 = performance.now()
  try {
    const resp = await searchApi.batchOem({ oems })
    batchResults.value = resp.results
    batchTotal.value = resp.total
    batchHits.value = resp.hits
    batchMiss.value = resp.miss
    batchElapsedMs.value = Math.round(performance.now() - t0)
  } catch (e: any) {
    batchError.value = e?.message || '批量查询失败'
    batchResults.value = []
    batchTotal.value = 0
    batchHits.value = 0
    batchMiss.value = 0
  } finally {
    batchLoading.value = false
  }
}

function clearBatch() {
  batchInput.value = ''
  batchResults.value = []
  batchError.value = ''
  batchTotal.value = 0
  batchHits.value = 0
  batchMiss.value = 0
  batchElapsedMs.value = 0
}

function viewProductById(row: BatchOemResult) {
  // V2 Task 4.4: 改用 SEO URL
  //   BatchOemResult 含 oemBrand + productName1 + oem2, 用 oem2 作为 oem3 段降级
  //   buildProductUrl 缺 productName2 → slug="untitled", 缺 oemNo3 → 用 oem2
  const url = buildProductUrl({
    productName1: row.productName1,
    oemBrand: row.oemBrand,
    oemNo3: row.oem2,  // oem2 作 oem3 降级 (BatchOemResult 无 oemNo3 字段)
    oemNoDisplay: row.oem2 ?? row.oem
  })
  window.location.href = url
}

onMounted(() => {
  // 默认显示空状态
})

// P2-8.1: 组件卸载时取消未完成的搜索请求, 防止内存泄漏与卸载后状态写入
onBeforeUnmount(() => {
  searchAbort?.abort()
})
</script>

<template>
  <div class="p-3 max-w-screen-2xl mx-auto">
    <!-- Day 14+ A11y: 页面主标题 (屏幕阅读器可达, 视觉上用 .sr-only 隐藏, 避免破坏极简风格) -->
    <h1 class="sr-only">{{ t('search.title', '产品搜索') }}</h1>
    <!-- P3.2 (Task 10): 单条 / 批量 切换 Tab -->
    <el-tabs v-model="activeTab" class="mb-3">
      <!-- ===== 单条 Tab (Day 9 + Task 9 容差) ===== -->
      <el-tab-pane label="单条搜索" name="single">
        <div class="flex items-center gap-2 mb-3 flex-wrap">
          <el-input
            v-model="q"
            :placeholder="t('search.placeholder')"
            clearable
            size="large"
            style="max-width: 480px"
            :aria-label="t('common.search')"
            role="searchbox"
            data-testid="search-input"
            @keyup.enter="doSearch"
          >
            <template #prefix>
              <el-icon aria-hidden="true"><Search /></el-icon>
            </template>
          </el-input>
          <!-- Task 9 (P3.1): 尺寸容差下拉, ±1/±5/±10, 用 el-popover 包裹加性能提示 -->
          <el-popover
            placement="bottom-start"
            :width="260"
            trigger="hover"
            popper-class="tol-popover"
          >
            <template #reference>
              <el-select
                v-model="tolerance"
                size="large"
                style="width: 168px"
                :aria-label="t('search.tolerance')"
              >
                <el-option :label="t('search.tolerance1')" :value="1" />
                <el-option :label="t('search.tolerance5')" :value="5" />
                <el-option :label="t('search.tolerance10')" :value="10" />
              </el-select>
            </template>
            <div class="text-xs leading-relaxed">
              <div class="font-medium mb-1">{{ t('search.tolerance') }}</div>
              <div class="text-muted">{{ t('search.toleranceDesc') }}</div>
            </div>
          </el-popover>
          <el-button
            type="primary"
            size="large"
            @click="doSearch"
            :loading="loading"
            :aria-label="t('common.search')"
          >{{ t('common.search') }}</el-button>
          <!-- 入口: 跳转公开搜索 (8 字段联想 + 8 列明细 + 加入对比)
               WHY: 两个页定位不同 — 本页是内部用户深度查询 (关键词 + 容差 + 批量粘贴),
                    公开搜索页是给客户/外网用的友好界面 (联想/对比/极简)
               客户被内部同事引导到 /search 后, 通过此按钮可一键到"客户友好"页面 -->
          <el-button
            size="large"
            plain
            @click="router.push('/public/search')"
            :aria-label="t('search.advancedSearch', '高级搜索 (公开版)')"
          >
            <el-icon class="mr-1" aria-hidden="true"><Search /></el-icon>
            {{ t('search.advancedSearch', '高级搜索') }}
          </el-button>
        </div>

        <!-- Task 9 (P3.1): 容差切换结果数变化提示, 仅在切换后短暂出现 -->
        <div
          v-if="toleranceHint && q"
          class="text-xs text-muted mb-2"
          role="status"
          aria-live="polite"
        >
          {{ toleranceHint }}
        </div>

        <div v-if="lastError" class="text-red-600 text-sm mb-2" role="alert" aria-live="assertive">{{ lastError }}</div>

        <div v-if="!q" class="py-12 text-center text-muted" role="status" :aria-label="t('search.startSearch')">
          <EmptyState
            type="empty"
            :title="t('search.startSearch')"
            :description="t('search.startSearchDesc')"
          />
        </div>

        <!-- 修复: 已输入未搜索的中间状态, 避免立即显示"暂无结果" -->
        <div v-else-if="q && !searched" class="py-12 text-center text-muted" role="status">
          <EmptyState
            type="empty"
            :title="t('search.clickToSearch')"
            :description="t('search.currentKeyword', { q })"
          />
        </div>

        <div
          v-else-if="searched && hits.length === 0 && !loading"
          class="py-12 text-center text-muted"
          role="status"
          aria-live="polite"
          :aria-label="t('common.noResult')"
        >
          <EmptyState
            type="no-result"
            :description="t('search.noResult', { q })"
            :action-text="t('search.clearRetry')"
            @action="q = ''; searched = false"
          />
        </div>

        <div v-else class="hairline" role="region" :aria-label="t('common.search')">
          <div class="hairline-b px-2 py-1 bg-[var(--color-bg-hover)] text-xs text-muted flex items-center">
            <span>{{ t('search.resultCount', { total, tol: tolerance }) }}</span>
            <span class="ml-2">{{ t('search.showingFirst', { n: hits.length }) }}</span>
          </div>
          <el-table
            :data="hits"
            stripe
            size="small"
            @row-click="viewDetail"
            :row-style="{ cursor: 'pointer' }"
            v-loading="loading"
            max-height="calc(100vh - 200px)"
          >
            <el-table-column prop="id" label="ID" width="60" />
            <el-table-column label="OEM" width="180">
              <template #default="{ row }">{{ row.oemNoDisplay ?? row.oem_no_display }}</template>
            </el-table-column>
            <el-table-column label="MR.1" width="120" show-overflow-tooltip>
              <template #default="{ row }">{{ row.mr1 ?? row.mr_1 }}</template>
            </el-table-column>
            <el-table-column prop="type" label="Type" width="80" />
            <el-table-column label="名称" min-width="200" show-overflow-tooltip>
              <template #default="{ row }">{{ row.productName1 ?? row.product_name_1 }}</template>
            </el-table-column>
            <el-table-column label="D1" width="60" align="right">
              <template #default="{ row }">{{ row.d1Mm ?? row.d1_mm }}</template>
            </el-table-column>
            <el-table-column label="D2" width="60" align="right">
              <template #default="{ row }">{{ row.d2Mm ?? row.d2_mm }}</template>
            </el-table-column>
            <el-table-column label="D3" width="60" align="right">
              <template #default="{ row }">{{ row.d3Mm ?? row.d3_mm }}</template>
            </el-table-column>
            <el-table-column label="H1" width="60" align="right">
              <template #default="{ row }">{{ row.h1Mm ?? row.h1_mm }}</template>
            </el-table-column>
            <el-table-column label="D7" width="80">
              <template #default="{ row }">{{ row.d7Thread ?? row.d7_thread }}</template>
            </el-table-column>
            <el-table-column label="D8" width="80">
              <template #default="{ row }">{{ row.d8Thread ?? row.d8_thread }}</template>
            </el-table-column>
            <el-table-column prop="media" label="Media" width="120" show-overflow-tooltip />
            <el-table-column label="MediaModel" width="120" show-overflow-tooltip>
              <template #default="{ row }">{{ row.mediaModel ?? row.media_model }}</template>
            </el-table-column>
            <el-table-column label="更新" width="120">
              <template #default="{ row }">{{ fmtDate(row.updatedAt ?? row.updated_at) }}</template>
            </el-table-column>
          </el-table>
        </div>
      </el-tab-pane>

      <!-- ===== P3.2 (Task 10): 批量粘贴 Tab ===== -->
      <el-tab-pane label="批量粘贴 (Excel)" name="batch">
        <div class="mb-3">
          <el-input
            v-model="batchInput"
            type="textarea"
            :rows="10"
            placeholder="粘贴 OEM 编号, 每行一个 (支持 tab/换行/逗号/分号分隔)&#10;例如:&#10;OEN-123&#10;AB/CD/456&#10;滤清器 1142"
            :disabled="batchLoading"
          />
          <!-- 解析预览: 实时显示已识别 OEM 数 + 重复数 -->
          <div class="mt-2 text-xs text-muted flex items-center gap-3">
            <span>已识别 {{ parsedPreview.unique }} 条</span>
            <span v-if="parsedPreview.duplicates > 0" class="text-orange-500">
              (含 {{ parsedPreview.duplicates }} 条重复, 将自动去重)
            </span>
          </div>
        </div>

        <div class="flex items-center gap-2 mb-3">
          <el-button type="primary" @click="doBatchSearch" :loading="batchLoading" :disabled="parsedPreview.unique === 0">
            查询
          </el-button>
          <el-button @click="clearBatch" :disabled="batchLoading">清空</el-button>
          <span v-if="batchTotal > 0" class="text-xs text-muted">
            共 {{ batchTotal }} 个 OEM, 命中 {{ batchHits }} / 未命中 {{ batchMiss }}, 耗时 {{ batchElapsedMs }} ms
          </span>
        </div>

        <div v-if="batchError" class="text-red-600 text-sm mb-2">{{ batchError }}</div>

        <!-- 进度条: 命中数 / 总数 (展示查询完成度) -->
        <div v-if="batchTotal > 0" class="mb-2">
          <el-progress
            :percentage="batchTotal > 0 ? Math.round((batchHits / batchTotal) * 100) : 0"
            :status="batchHits === batchTotal ? 'success' : (batchHits === 0 ? 'exception' : '')"
            :stroke-width="14"
            :text-inside="true"
          />
        </div>

        <!-- 结果表格: 100 行分页 + 列排序 -->
        <el-table
          v-if="batchResults.length > 0"
          :data="batchResults"
          stripe
          size="small"
          border
          :row-style="{ cursor: 'pointer' }"
          @row-click="viewProductById"
          v-loading="batchLoading"
          max-height="calc(100vh - 280px)"
          :default-sort="{ prop: 'hit', order: 'descending' }"
        >
          <el-table-column type="index" label="#" width="50" />
          <el-table-column prop="oem" label="OEM 编号" min-width="180" sortable show-overflow-tooltip />
          <el-table-column prop="hit" label="命中" width="80" sortable :sort-method="(a: any, b: any) => Number(b.hit) - Number(a.hit)">
            <template #default="{ row }">
              <span v-if="row.hit" class="text-green-600 font-semibold">✓</span>
              <span v-else class="text-red-500 font-semibold">✗</span>
            </template>
          </el-table-column>
          <el-table-column prop="productId" label="产品 ID" width="100" sortable>
            <template #default="{ row }">
              <span v-if="row.productId" class="text-blue-600">{{ row.productId }}</span>
              <span v-else class="text-muted">-</span>
            </template>
          </el-table-column>
          <el-table-column prop="oemBrand" label="OEM Brand" min-width="160" show-overflow-tooltip>
            <template #default="{ row }">
              <span v-if="row.oemBrand">{{ row.oemBrand }}</span>
              <span v-else class="text-muted">-</span>
            </template>
          </el-table-column>
          <el-table-column prop="productName1" label="Product Name 1" min-width="200" show-overflow-tooltip>
            <template #default="{ row }">
              <span v-if="row.productName1">{{ row.productName1 }}</span>
              <span v-else class="text-muted">-</span>
            </template>
          </el-table-column>
          <el-table-column prop="oem2" label="备用 OEM 2" min-width="180" show-overflow-tooltip>
            <template #default="{ row }">
              <span v-if="row.oem2">{{ row.oem2 }}</span>
              <span v-else class="text-muted">-</span>
            </template>
          </el-table-column>
        </el-table>

        <div v-else-if="!batchLoading && batchTotal === 0" class="py-12 text-center text-muted">
          <el-icon class="text-4xl mb-2"><DocumentCopy /></el-icon>
          <div>粘贴 OEM 编号后点击"查询"</div>
          <div class="text-xs mt-2">支持每行一个 / tab 分列 / 逗号 / 分号, 自动 trim + 去重</div>
        </div>
      </el-tab-pane>
    </el-tabs>
  </div>
</template>
