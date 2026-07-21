<script setup lang="ts">
// V2 Task 1.3.2: 聚合搜索页 (需求 5)
//   URL: /search/aggregate?q=CAT 320D&page=1
//   - 调 POST /api/public/search/aggregate (Meili 主 + PG 兜底)
//   - 文档级展示: MR.1 卡片 + 可展开 oemList (每个 OEM 3 一行)
//   - _formatted 高亮渲染 (sanitizeFormatted 双保险, 只允许 <mark> 标签)
//   - 500ms 防抖 + AbortController 取消前序请求 (复用 PublicSearchView 模式)
//   - Musk 风格极简: 纯黑白 + 1px 细线 + 8px 网格 + 无阴影
import { ref, reactive, computed, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
// V24-F38: 改用 searchWithFallback (封装聚合 API 404 降级逻辑)
//   保留 publicSearchApi 导入: clearSearch 等其他函数可能用到 (此处仅类型兼容)
// V24-F40: shouldShowLegacyFallbackWarn 5 秒去重, 避免连续搜索刷屏
import { searchWithFallback, wasLastSearchLegacyFallback, shouldShowLegacyFallbackWarn } from '@/api'
import type { AggregateSearchHit, AggregateSearchResponse } from '@/api/types'
import { sanitizeFormatted } from '@/utils/html-sanitizer'
import { buildProductUrl } from '@/utils/build-product-url'

const route = useRoute()
const router = useRouter()

// ===== 搜索表单 =====
const q = ref<string>((route.query.q as string) || '')
const page = ref<number>(route.query.page ? Number(route.query.page) : 1)
const pageSize = ref<number>(20)
// 高级筛选 (折叠展开, 默认收起)
const showAdvanced = ref(false)
const advancedForm = reactive({
  type: '',
  machineCategory: '',
  tolerance: 5,
  includeDiscontinued: false
})

// ===== 搜索结果状态 =====
const loading = ref(false)
const results = ref<AggregateSearchHit[]>([])
const total = ref(0)
const totalPages = ref(0)
const processingTimeMs = ref(0)
const provider = ref<string>('')
const lastError = ref('')
// 展开的 MR.1 卡片 (展示完整 oemList)
const expandedMr1 = ref<Set<string>>(new Set())
// V24-F38 (spec 改进建议): 标记本次搜索是否降级到旧 API
//   - true: 聚合 API 404, 降级到 searchApi.search, 无 oemList/machineList 嵌套
//   - false: 聚合 API 正常, 完整渲染
//   - 渲染时检查: 降级时隐藏 "展开 OEM" 按钮 + 机型列表区域
const isLegacyFallback = ref(false)

// ===== 防抖 + AbortController (Task 1.3.5) =====
let debounceTimer: number | null = null
let abortCtrl: AbortController | null = null

async function doSearch() {
  // 取消前序请求 (快速连续搜索时只保留最后一次)
  if (abortCtrl) abortCtrl.abort()
  abortCtrl = new AbortController()

  if (!q.value.trim() && !advancedForm.type && !advancedForm.machineCategory) {
    results.value = []
    total.value = 0
    totalPages.value = 0
    return
  }

  loading.value = true
  lastError.value = ''
  try {
    // V24-F38: 改用 searchWithFallback, 支持聚合 API 404 时降级到旧 API
    //   WHY 不直接用 publicSearchApi.aggregate: 降级逻辑封装在 searchWithFallback 中
    //   降级时 wasLastSearchLegacyFallback() 返回 true, 设置 isLegacyFallback 标志
    const resp: AggregateSearchResponse = await searchWithFallback(
      {
        q: q.value.trim() || undefined,
        page: page.value,
        pageSize: pageSize.value,
        tolerance: advancedForm.tolerance,
        includeDiscontinued: advancedForm.includeDiscontinued,
        type: advancedForm.type || undefined,
        machineCategory: advancedForm.machineCategory || undefined
      },
      abortCtrl.signal
    )
    // V24-F38: 检查是否降级, 降级时隐藏 oemList/machineList 展开按钮
    isLegacyFallback.value = wasLastSearchLegacyFallback()
    if (isLegacyFallback.value) {
      // V24-F40: 5 秒去重, 避免连续搜索时 ElMessage.warning 刷屏
      //   WHY: 用户输入关键词时 500ms 防抖触发搜索, 连续输入会多次降级
      //        5 秒窗口内只提示一次, 类似后端 ETL 告警抑制窗口
      if (shouldShowLegacyFallbackWarn()) {
        ElMessage.warning('聚合搜索 API 暂不可用,已降级到基础搜索 (不展示 OEM 交叉引用详情)')
      }
    }
    results.value = resp.hits || []
    total.value = resp.total
    totalPages.value = resp.totalPages
    processingTimeMs.value = resp.processingTimeMs
    provider.value = resp.provider
  } catch (e: any) {
    // AbortError 静默 (用户快速输入时正常取消)
    if (e?.name === 'CanceledError' || e?.code === 'ERR_CANCELED') return
    lastError.value = e?.problem?.detail || e?.response?.data?.detail || e?.message || '搜索失败'
    results.value = []
    total.value = 0
    totalPages.value = 0
  } finally {
    loading.value = false
  }
}

// q 输入 → 500ms 防抖搜索
watch(q, () => {
  if (debounceTimer) window.clearTimeout(debounceTimer)
  debounceTimer = window.setTimeout(() => {
    page.value = 1
    syncUrl()
    doSearch()
  }, 500)
})

// 翻页
watch(page, () => {
  syncUrl()
  doSearch()
})

// 高级筛选变化 → 立即搜索 (用户主动改条件, 无需防抖)
watch(advancedForm, () => {
  page.value = 1
  doSearch()
}, { deep: true })

// URL 同步 (刷新页面可还原状态)
function syncUrl() {
  const query: Record<string, string> = {}
  if (q.value.trim()) query.q = q.value.trim()
  if (page.value > 1) query.page = String(page.value)
  router.replace({ path: '/search/aggregate', query })
}

// 展开/收起 MR.1 卡片的 oemList
function toggleExpand(mr1: string) {
  const next = new Set(expandedMr1.value)
  if (next.has(mr1)) next.delete(mr1)
  else next.add(mr1)
  expandedMr1.value = next
}

// V2 Task 4.4: 跳转产品详情 SEO URL
//   AggregateSearchHit 含完整字段 (mr1/pn1/pn2/oemList[0].brand&oemNo3), 可拼完整 SEO URL
function viewDetail(hit: AggregateSearchHit) {
  const firstOem = hit.oemList?.[0]
  const url = buildProductUrl({
    productName1: hit.productName1,
    productName2: hit.productName2,
    oemBrand: firstOem?.oemBrand,
    oemNo3: firstOem?.oemNo3,
    oemNoDisplay: hit.oem2 || hit.mr1,
    mr1: hit.mr1
  })
  window.location.href = url
}

// 清空搜索
function clearSearch() {
  q.value = ''
  advancedForm.type = ''
  advancedForm.machineCategory = ''
  advancedForm.tolerance = 5
  advancedForm.includeDiscontinued = false
  page.value = 1
  results.value = []
  total.value = 0
  syncUrl()
}

// 取 _formatted 字段值 (后端高亮版本, 前端 sanitizeFormatted 双保险)
function getHighlighted(hit: AggregateSearchHit, field: string): string {
  const formatted = hit.formatted as Record<string, unknown> | null
  const raw = formatted?.[field]
  if (typeof raw === 'string') return sanitizeFormatted(raw)
  // 降级: 用原始字段 (无高亮)
  const fallback = (hit as unknown as Record<string, unknown>)[field]
  return typeof fallback === 'string' ? fallback : ''
}

onMounted(() => {
  if (q.value.trim()) doSearch()
})

onBeforeUnmount(() => {
  if (debounceTimer) window.clearTimeout(debounceTimer)
  if (abortCtrl) abortCtrl.abort()
})
</script>

<template>
  <div class="p-4 max-w-7xl mx-auto">
    <!-- 标题 + 搜索框 -->
    <div class="border-b border-gray-200 pb-3 mb-4">
      <h1 class="text-xl font-medium mb-3">聚合搜索</h1>
      <div class="flex gap-2 items-center">
        <el-input
          v-model="q"
          placeholder="输入关键词 (产品名 / OEM / 机型 / 品牌)"
          clearable
          size="large"
          class="flex-1"
          @keyup.enter="page = 1; syncUrl(); doSearch()"
        />
        <el-button type="primary" size="large" @click="page = 1; syncUrl(); doSearch()" :loading="loading">
          搜索
        </el-button>
        <el-button size="large" @click="clearSearch">清空</el-button>
      </div>
      <!-- 高级筛选 (折叠展开) -->
      <div class="mt-2">
        <el-button text size="small" @click="showAdvanced = !showAdvanced">
          {{ showAdvanced ? '收起高级筛选' : '展开高级筛选' }}
        </el-button>
        <div v-if="showAdvanced" class="flex flex-wrap gap-3 mt-2 p-3 border border-gray-200 rounded">
          <el-form-item label="分类" class="!mb-0">
            <el-select v-model="advancedForm.type" placeholder="全部" clearable size="small" style="width: 120px">
              <el-option label="机油滤" value="oil" />
              <el-option label="燃油滤" value="fuel" />
              <el-option label="空气滤" value="air" />
              <el-option label="空调滤" value="cabin" />
              <el-option label="其他" value="others" />
            </el-select>
          </el-form-item>
          <el-form-item label="机型分类" class="!mb-0">
            <el-select v-model="advancedForm.machineCategory" placeholder="全部" clearable size="small" style="width: 140px">
              <el-option label="农业" value="agriculture" />
              <el-option label="商用" value="commercial" />
              <el-option label="工程机械" value="construction" />
              <el-option label="工业" value="industrial" />
              <el-option label="其他" value="others" />
            </el-select>
          </el-form-item>
          <el-form-item label="尺寸容差" class="!mb-0">
            <el-select v-model="advancedForm.tolerance" size="small" style="width: 100px">
              <el-option label="±1mm" :value="1" />
              <el-option label="±5mm" :value="5" />
              <el-option label="±10mm" :value="10" />
            </el-select>
          </el-form-item>
          <el-form-item label="含下架" class="!mb-0">
            <el-switch v-model="advancedForm.includeDiscontinued" />
          </el-form-item>
        </div>
      </div>
    </div>

    <!-- 错误提示 -->
    <div v-if="lastError" class="p-3 mb-3 border border-red-300 bg-red-50 text-red-700 text-sm">
      {{ lastError }}
    </div>

    <!-- 元信息 (总数 / 耗时 / provider) -->
    <div v-if="total > 0" class="text-sm text-gray-600 mb-3 flex gap-4">
      <span>共 {{ total }} 条</span>
      <span>耗时 {{ processingTimeMs }}ms</span>
      <span>来源: {{ provider === 'meilisearch' ? 'Meilisearch' : 'PostgreSQL (兜底)' }}</span>
    </div>

    <!-- 加载中 -->
    <div v-if="loading" class="py-12 text-center text-gray-500">
      <el-icon class="is-loading text-2xl"><Loading /></el-icon>
      <p class="mt-2">搜索中...</p>
    </div>

    <!-- 空结果 -->
    <div v-else-if="!loading && results.length === 0 && q.trim()" class="py-12 text-center text-gray-500">
      <p>未找到匹配结果</p>
      <p class="text-xs mt-1">尝试更换关键词或调整筛选条件</p>
    </div>

    <!-- 搜索结果列表 (MR.1 文档级卡片) -->
    <div v-else class="space-y-2">
      <div
        v-for="hit in results"
        :key="hit.mr1"
        class="border border-gray-200 rounded p-3 hover:border-gray-400 transition-colors cursor-pointer"
        @click="viewDetail(hit)"
      >
        <!-- MR.1 主信息行 -->
        <div class="flex items-start gap-3">
          <div class="flex-1 min-w-0">
            <div class="flex items-baseline gap-2 flex-wrap">
              <span class="font-mono text-sm text-gray-900 font-medium">{{ hit.mr1 }}</span>
              <!-- V2 Task 1.3.3: v-html 渲染 _formatted 高亮 (sanitizeFormatted 双保险) -->
              <span
                v-if="getHighlighted(hit, 'product_name_1')"
                class="text-sm text-gray-700"
                v-html="getHighlighted(hit, 'product_name_1')"
              ></span>
              <span v-if="hit.productName2" class="text-xs text-gray-500">{{ hit.productName2 }}</span>
              <el-tag size="small" type="info">{{ hit.type }}</el-tag>
              <el-tag v-if="!hit.isPublished" size="small" type="warning">未上架</el-tag>
              <el-tag v-if="hit.isDiscontinued" size="small" type="danger">已下架</el-tag>
            </div>
            <div v-if="hit.oem2" class="text-xs text-gray-500 mt-1">OEM 2: {{ hit.oem2 }}</div>
          </div>
          <div class="flex items-center gap-2">
            <span v-if="hit.rankingScore != null" class="text-xs text-gray-400">
              相关度 {{ (hit.rankingScore * 100).toFixed(0) }}%
            </span>
            <!-- V24-F38: 降级模式 (isLegacyFallback=true) 隐藏 "展开 OEM" 按钮 -->
            <!--   WHY: 旧 API 返回空 oemList, 展开后无内容, 按钮点击无意义 -->
            <el-button
              v-if="!isLegacyFallback"
              text
              size="small"
              @click.stop="toggleExpand(hit.mr1)"
            >
              {{ expandedMr1.has(hit.mr1) ? '收起' : `展开 OEM (${hit.oemList.length})` }}
            </el-button>
            <!-- V24-F38: 降级模式显示 "基础模式" 标记, 告知用户无 OEM 嵌套详情 -->
            <el-tag v-if="isLegacyFallback" size="small" type="info">基础模式</el-tag>
          </div>
        </div>

        <!-- OEM 3 列表 (展开时显示) -->
        <!-- V24-F38: 降级模式 (isLegacyFallback=true) 不渲染 oemList 区域 -->
        <!--   WHY: 旧 API 返回空 oemList, 渲染空表格无意义且误导用户 -->
        <div v-if="!isLegacyFallback && expandedMr1.has(hit.mr1)" class="mt-3 pt-3 border-t border-gray-100">
          <div class="text-xs text-gray-500 mb-2">交叉引用 (OEM 3 列表,按品牌优先级排序)</div>
          <table class="w-full text-xs">
            <thead class="text-gray-500 border-b border-gray-200">
              <tr>
                <th class="text-left py-1 px-2 font-normal">OEM Brand</th>
                <th class="text-left py-1 px-2 font-normal">OEM 3</th>
                <th class="text-left py-1 px-2 font-normal">OEM 2</th>
                <th class="text-left py-1 px-2 font-normal">Sort</th>
                <th class="text-left py-1 px-2 font-normal">机型类型</th>
                <th class="text-left py-1 px-2 font-normal">上架</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(oem, idx) in hit.oemList"
                :key="`${oem.oemBrand}-${oem.oemNo3}-${idx}`"
                class="border-b border-gray-100 hover:bg-gray-50"
              >
                <td class="py-1 px-2">{{ oem.oemBrand || '-' }}</td>
                <td class="py-1 px-2 font-mono">{{ oem.oemNo3 || '-' }}</td>
                <td class="py-1 px-2 font-mono">{{ oem.oem2 || '-' }}</td>
                <td class="py-1 px-2">{{ oem.sortOrder }}</td>
                <td class="py-1 px-2">{{ oem.machineType || '-' }}</td>
                <td class="py-1 px-2">
                  <el-tag v-if="oem.isPublished" size="small" type="success">上架</el-tag>
                  <el-tag v-else size="small" type="info">下架</el-tag>
                </td>
              </tr>
            </tbody>
          </table>

          <!-- 机型列表 (展开时显示) -->
          <div v-if="hit.machineList.length > 0" class="mt-3">
            <div class="text-xs text-gray-500 mb-2">适配机型 ({{ hit.machineList.length }})</div>
            <div class="flex flex-wrap gap-1">
              <el-tag
                v-for="(m, idx) in hit.machineList.slice(0, 20)"
                :key="`${m.machineBrand}-${m.machineModel}-${idx}`"
                size="small"
                type="info"
              >
                {{ [m.machineBrand, m.machineModel].filter(Boolean).join(' ') }}
              </el-tag>
              <span v-if="hit.machineList.length > 20" class="text-xs text-gray-400 self-center">
                + {{ hit.machineList.length - 20 }} 更多
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 分页 -->
    <div v-if="totalPages > 1" class="mt-6 flex justify-center">
      <el-pagination
        v-model:current-page="page"
        :page-size="pageSize"
        :total="total"
        layout="prev, pager, next, total"
        background
      />
    </div>
  </div>
</template>

<script lang="ts">
import { Loading } from '@element-plus/icons-vue'
export default { components: { Loading } }
</script>
