<script setup lang="ts">
// V2 Task 1.3.2: 聚合搜索页 (需求 5)
//   URL: /search/aggregate?q=CAT 320D&page=1
//   - 调 POST /api/public/search/aggregate (Meili 主 + PG 兜底)
//   - 文档级聚合、OEM 3 对外展示 + 可展开 oemList (每个 OEM 3 一行)
//   - _formatted 高亮渲染 (sanitizeFormatted 双保险, 只允许 <mark> 标签)
//   - 500ms 防抖 + AbortController 取消前序请求 (复用 PublicSearchView 模式)
//   - Musk 风格极简: 纯黑白 + 1px 细线 + 8px 网格 + 无阴影
import { ref, reactive, computed, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
// V24-F38: 改用 searchWithFallback (封装聚合 API 404 降级逻辑)
//   保留 publicSearchApi 导入: clearSearch 等其他函数可能用到 (此处仅类型兼容)
// V24-F40: shouldShowLegacyFallbackWarn 5 秒去重, 避免连续搜索刷屏
import { publicSearchApi, searchApi, searchWithFallback, wasLastSearchLegacyFallback, shouldShowLegacyFallbackWarn, adminProductApi } from '@/api'
import type { AggregateSearchHit, AggregateSearchResponse, MachineCatalogResponse, BatchOemResult } from '@/api/types'
import { sanitizeFormatted } from '@/utils/html-sanitizer'
import { buildProductUrl } from '@/utils/build-product-url'
// W7 (2026-10-01 走查): 快捷添加复用后台 Operator 接口 → 需判断登录态 (未登录先跳登录, 不新增公开写接口)
import { useAdminAuth } from '@/composables/useAdminAuth'

const route = useRoute()
const router = useRouter()
const { t } = useI18n()
const { isAuthenticated } = useAdminAuth()

// ===== 搜索表单 =====
const q = ref<string>((route.query.q as string) || '')
const page = ref<number>(route.query.page ? Number(route.query.page) : 1)
// 🔧 fix(2026-08-23 走查): pageSize 20 → 6 (首屏仅 6 张图并发 ≈ 100ms 完, 避免 20 张图冷启动 ~500ms 卡顿)
//   配合页面底部"加载更多"按钮, 用户主动翻页, 每次 +6 条/图, 显著降低返回时感知卡顿
const pageSize = ref<number>(6)
// 高级筛选 (折叠展开, 默认收起)
const showAdvanced = ref(false)
const routeTolerance = Number(route.query.tolerance)

// W5 (2026-10-01 走查): 高级搜索与筛选合并 — 8 字段集中定义 (模板循环渲染 + URL 同步 + 请求组装共用)
//   WHY: 原"高级筛选"(分类/机型分类/容差) 与 8 字段搜索分处两页 (/search/aggregate 与 /public/search),
//     用户需在两个面板间切换; 合并为单一"高级搜索与筛选"入口, 一次提交。
//   语义保持各自独立: OEM Brand 与其他 7 字段互不替代, 空字段不参与, 多字段 AND 收窄。
const EIGHT_FIELDS: ReadonlyArray<{ key: EightFieldKey; label: string; placeholder: string }> = [
  { key: 'oemBrand',     label: 'OEM Brand',     placeholder: 'e.g. MANN, Bosch, CAT' },
  { key: 'oemNo2',       label: 'OEM 2 NO.',     placeholder: '产品自身 OEM 2 编号' },
  { key: 'oemNo3',       label: 'OEM 3 NO.',     placeholder: 'e.g. 207-60... (交叉引用)' },
  { key: 'machineBrand', label: 'Machine Brand', placeholder: 'e.g. Caterpillar, JCB' },
  { key: 'machineModel', label: 'Machine Model', placeholder: '机型' },
  { key: 'modelName',    label: 'Model Name',    placeholder: '型号名' },
  { key: 'engineBrand',  label: 'Engine Brand',  placeholder: '发动机品牌' },
  { key: 'engineType',   label: 'Engine Type',   placeholder: '发动机型号' }
]

// W5: 尺寸拆为 6 个子条件 (用户决策: D1/D2/D3 + H1/H2/H3 各自独立), 共用同一容差
const DIMENSIONS: ReadonlyArray<{ key: DimensionKey; label: string }> = [
  { key: 'd1', label: 'D1 (mm)' }, { key: 'd2', label: 'D2 (mm)' }, { key: 'd3', label: 'D3 (mm)' },
  { key: 'h1', label: 'H1 (mm)' }, { key: 'h2', label: 'H2 (mm)' }, { key: 'h3', label: 'H3 (mm)' }
]

// W5: OEM Brand 在面板中单独一行展示 (独立语义), 其余 7 字段走网格
const OTHER_EIGHT_FIELDS = EIGHT_FIELDS.slice(1)

// W5: 显式声明表单模型类型 — WHY: reactive 推断类型下 advancedForm[f.key] 会被扩宽为
//   string | number | undefined 的联合, 循环里调用 .trim() / 赋 undefined 均无法通过 vue-tsc;
//   显式接口让 8 字段组恒为 string、尺寸组恒为 number | undefined。
type EightFieldKey =
  | 'oemBrand' | 'oemNo2' | 'oemNo3' | 'machineBrand'
  | 'machineModel' | 'modelName' | 'engineBrand' | 'engineType'
type DimensionKey = 'd1' | 'd2' | 'd3' | 'h1' | 'h2' | 'h3'

interface AdvancedSearchFormModel {
  type: string
  machineCategory: string
  tolerance: number
  oemBrand: string
  oemNo2: string
  oemNo3: string
  machineBrand: string
  machineModel: string
  modelName: string
  engineBrand: string
  engineType: string
  // 用 undefined (而非 null) 表示"该尺寸子条件不参与" — 与 el-input-number 的 v-model 类型一致
  d1: number | undefined
  d2: number | undefined
  d3: number | undefined
  h1: number | undefined
  h2: number | undefined
  h3: number | undefined
}

function queryStr(key: string): string {
  return (route.query[key] as string) || ''
}
// 尺寸是数字: 空/非数字 → undefined (undefined 表示该子条件不参与过滤)
function queryNum(key: string): number | undefined {
  const raw = route.query[key]
  if (raw === undefined || raw === '') return undefined
  const n = Number(raw)
  return Number.isFinite(n) ? n : undefined
}

const advancedForm = reactive<AdvancedSearchFormModel>({
  type: queryStr('type'),
  machineCategory: queryStr('machineCategory'),
  tolerance: [1, 5, 10].includes(routeTolerance) ? routeTolerance : 5,
  // 8 字段 (与融合搜索框可叠加, 互不替代)
  oemBrand: queryStr('oemBrand'),
  oemNo2: queryStr('oemNo2'),
  oemNo3: queryStr('oemNo3'),
  machineBrand: queryStr('machineBrand'),
  machineModel: queryStr('machineModel'),
  modelName: queryStr('modelName'),
  engineBrand: queryStr('engineBrand'),
  engineType: queryStr('engineType'),
  // 尺寸 6 子条件
  d1: queryNum('d1'), d2: queryNum('d2'), d3: queryNum('d3'),
  h1: queryNum('h1'), h2: queryNum('h2'), h3: queryNum('h3')
})
// 🔧 fix(切库 2026-10-01): 原快捷分类直接把展示名 ('Air Filter') 当过滤值发给后端,
//   而 products.type 实际取值是短码 (air/oil/fuel/hydraulic/cabin/others, 由 catalog 派生分类写入),
//   Meili 过滤 type = "Air Filter" 恒为 0 结果。改为 value/label 分离, 请求只发短码。
const quickProductTypes = [
  { value: 'air', label: 'Air Filter' },
  { value: 'oil', label: 'Oil Filter' },
  { value: 'fuel', label: 'Fuel Filter' },
  { value: 'hydraulic', label: 'Hydraulic Filter' },
  { value: 'cabin', label: 'Cabin Filter' }
] as const

function toggleQuickProductType(type: string) {
  advancedForm.type = advancedForm.type === type ? '' : type
  // 🔧 fix(2026-10-01 走查): 快捷分类点击后同步 URL — 原实现只改 advancedForm,
  //   watch(advancedForm) 只触发 doSearch 不同步 URL, 导致刷新/分享后分类条件丢失。
  syncUrl()
}

// W5: 是否存在任一搜索条件 — 8 字段/尺寸任一有值即视为有条件
//   WHY: doSearch 原守卫只看 q/type/machineCategory, 合并面板新增 8 字段与尺寸后必须同步,
//     否则"只填 OEM Brand 点搜索"会被当作空条件直接清空结果。
const hasEightField = computed(() => EIGHT_FIELDS.some((f) => !!advancedForm[f.key].trim()))
const hasDimension = computed(() => DIMENSIONS.some((d) => advancedForm[d.key] !== undefined))
const hasCondition = computed(() =>
  !!q.value.trim() || !!advancedForm.type || !!advancedForm.machineCategory
  || hasEightField.value || hasDimension.value
)

// ===== 搜索结果状态 =====
const loading = ref(false)
// 🔧 fix(2026-08-23 走查): 区分新搜索与加载更多 (后者累积结果不替换)
const _loadMoreInFlight = ref(false)
const results = ref<AggregateSearchHit[]>([])
const total = ref(0)
const totalPages = ref(0)
const hasMore = computed(() => results.value.length < total.value)
const lastError = ref('')

// 🔧 fix(2026-08-23 走查): 目录默认收起品牌, 避免 1000 机型 DOM 首次渲染卡顿
//   WHY: 之前 v-for 默认展开全部 brand.models.slice(0,8) = ~1000 DOM 节点
//   进搜索页时主线程渲染卡顿明显。改为默认收起, 点品牌按钮再展开。
const expandedBrands = ref<Set<string>>(new Set())
function toggleBrand(brand: string) {
  if (expandedBrands.value.has(brand)) expandedBrands.value.delete(brand)
  else expandedBrands.value.add(brand)
  // 触发响应式 (Set 的 add/delete 不自动触发 ref 更新, 需重新赋值)
  expandedBrands.value = new Set(expandedBrands.value)
}

// 🔧 fix(2026-08-23 走查): 目录选中态 — 之前点击品牌/机型无任何视觉反馈
//   (el-button text 无 active 样式), 用户不知道当前选中了什么。
//   selectedCatalog 记录最近一次目录选择, 模板按钮按需高亮 (暗色底 + 主题色文字)。
const selectedCatalog = ref<{ category: string; brand?: string; model?: string } | null>(null)

// 🔧 fix(2026-08-23 走查): 批量粘贴对话框 — 旧 SearchView 含此功能 (P3.2 Task 10),
//   重构成 AggregateSearchView 时未迁移, /search 又重定向到 /search/aggregate → 用户报"批量查询界面没了"。
//   修复: 顶部搜索框旁加"批量粘贴"按钮 → 弹 el-dialog, 复用 SearchView 的批量粘贴逻辑 (textarea 解析 + 进度条 + 命中表)。
const batchDialogOpen = ref(false)
const batchInput = ref('')
const batchLoading = ref(false)
const batchError = ref('')
const batchResults = ref<BatchOemResult[]>([])
const batchTotal = ref(0)
const batchHits = ref(0)
const batchMiss = ref(0)
const batchElapsedMs = ref(0)
const batchParsedPreview = computed(() => {
  const oems = batchInput.value
    .split(/[\t\n,;]+/)
    .map((s) => s.trim())
    .filter(Boolean)
  const unique = [...new Set(oems)]
  return { raw: oems.length, unique: unique.length, duplicates: oems.length - unique.length }
})
function openBatchSearch() {
  batchDialogOpen.value = true
}
async function doBatchSearch() {
  batchError.value = ''
  const oems = [...new Set(
    batchInput.value.split(/[\t\n,;]+/).map((s) => s.trim()).filter(Boolean)
  )]
  if (oems.length === 0) { ElMessage.warning('请粘贴 OEM 编号'); return }
  if (oems.length > 500) { ElMessage.error(`最多 500 个 OEM, 当前 ${oems.length}`); return }
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
    batchTotal.value = 0; batchHits.value = 0; batchMiss.value = 0
  } finally { batchLoading.value = false }
}
function clearBatch() {
  batchInput.value = ''
  batchResults.value = []
  batchError.value = ''
  batchTotal.value = 0; batchHits.value = 0; batchMiss.value = 0; batchElapsedMs.value = 0
}
function viewBatchProduct(row: BatchOemResult) {
  // 🔧 fix(2026-08-23 走查): 必须用 row.oem (用户查询的 OEM) 作 oemNo3/oemNoDisplay,
  //   之前从旧 SearchView 复制用 row.oem2 是错的 — row.oem2 是 xrefs 表另一条记录的 oem_2 字段
  //   (不是用户查询的), 跳详情会落到错误 OEM (实测: U0000014 → /seo/FRA-53205 → 404)。
  const url = buildProductUrl({
    productName1: row.productName1,
    oemBrand: row.oemBrand,
    oemNo3: row.oem,
    oemNoDisplay: row.oem
  })
  router.push(url)
}

// ===== W6 (2026-10-01 走查): 批量结果分区 (已匹配 / 未匹配) =====
//   WHY: 用户需求 5 — 未命中的 OEM 需要单独成区, 才能对其做"快捷添加"(补录缺失数据)。
const batchHitRows = computed(() => batchResults.value.filter((r) => r.hit))
const batchMissRows = computed(() => batchResults.value.filter((r) => !r.hit))

// ===== W7: 未匹配行快捷添加 (页内弹窗直接新建产品, 复用 POST /api/admin/products, 策略 Operator) =====
const quickAddVisible = ref(false)
const quickAddSubmitting = ref(false)
const quickAddSourceOem = ref('')
const quickAddForm = reactive({
  mr1: '',
  oem2: '',
  productName1: '',
  type: 'others',
  oemBrand: '',
  oemNo3: ''
})

// MR.1 规则 (见后端 Mr1Validator): 1-10 位字母数字; 未命中 OEM 常含 '/' '-' 等字符 → 预填时净化
const Mr1MaxLength = 10
function sanitizeToMr1(oem: string): string {
  return oem.replace(/[^A-Za-z0-9]/g, '').slice(0, Mr1MaxLength)
}

function openQuickAdd(row: BatchOemResult) {
  // 复用后台写接口 → 未登录先引导登录 (回跳当前页), 不新增公开写接口
  if (!isAuthenticated()) {
    ElMessage.warning('快捷添加需要管理员登录, 正在跳转登录页')
    router.push({ path: '/login', query: { redirect: route.fullPath } })
    return
  }
  quickAddSourceOem.value = row.oem
  quickAddForm.mr1 = sanitizeToMr1(row.oem)
  quickAddForm.oem2 = row.oem
  quickAddForm.productName1 = ''
  quickAddForm.type = 'others'
  quickAddForm.oemBrand = ''
  quickAddForm.oemNo3 = row.oem
  quickAddVisible.value = true
}

async function submitQuickAdd() {
  if (!quickAddForm.mr1.trim() || !quickAddForm.oem2.trim()) {
    ElMessage.warning('MR.1 与 OEM 2 为必填项')
    return
  }
  quickAddSubmitting.value = true
  try {
    // 交叉引用与主号同源: OEM 3 记录用户查询的那个未命中号, 便于下次批量查询直接命中
    await adminProductApi.create(
      {
        mr1: quickAddForm.mr1.trim(),
        oem2: quickAddForm.oem2.trim(),
        productName1: quickAddForm.productName1.trim() || null,
        productName2: null,
        type: quickAddForm.type || 'others',
        isPublished: true,
        crossReferences: [
          {
            productName1: quickAddForm.productName1.trim() || null,
            oemBrand: quickAddForm.oemBrand.trim() || null,
            oemNo3: quickAddForm.oemNo3.trim() || null,
            oem2: quickAddForm.oem2.trim(),
            sortOrder: 0,
            machineType: null,
            isPublished: true
          }
        ],
        machineApplications: []
      },
      'admin'
    )
    ElMessage.success(`已新建产品: ${quickAddForm.oem2.trim()}`)
    quickAddVisible.value = false
    // 刷新批量查询结果 (新建后原未命中行应变为已匹配), 并同步刷新聚合搜索结果
    await doBatchSearch()
    doSearch()
  } catch (e: any) {
    const detail = e?.response?.data?.detail || e?.response?.data?.error || e?.message || '新建产品失败'
    ElMessage.error(detail)
  } finally {
    quickAddSubmitting.value = false
  }
}

// 🔧 fix(2026-08-23 走查): 加载更多 — page++ 后调 doSearch, 因 _loadMoreInFlight 累积结果
async function loadMore() {
  if (!hasMore.value || loading.value) return
  _loadMoreInFlight.value = true
  page.value++
  try { await doSearch() } finally { _loadMoreInFlight.value = false }
}
// 展开的聚合卡片使用服务端提供的公开键。
const expandedKeys = ref<Set<string>>(new Set())
// V24-F38 (spec 改进建议): 标记本次搜索是否降级到旧 API
//   - true: 聚合 API 404, 降级到 searchApi.search, 无 oemList/machineList 嵌套
//   - false: 聚合 API 正常, 完整渲染
//   - 渲染时检查: 降级时隐藏 "展开 OEM" 按钮 + 机型列表区域
const isLegacyFallback = ref(false)
const machineCatalog = ref<MachineCatalogResponse>({ categories: [] })
// 🔧 fix(审查): 目录为空时 grid 回退单列 — 原两列模板 (220px+1fr) 在 aside 不渲染时
//   只剩 1 个子项 → 内容被压缩在 220px 列 (用户实测 "页面居左 + 搜索框宽度异常")
const hasMachineCatalog = computed(() => machineCatalog.value.categories.some((c) => c.brands.length > 0))
// 🔧 fix(审查): 程序性批量更新标志 (selectMachine 合并触发, 防重复请求)
let programmaticUpdate = false

// ===== 防抖 + AbortController (Task 1.3.5) =====
let debounceTimer: number | null = null
let abortCtrl: AbortController | null = null

async function doSearch() {
  // 取消前序请求 (快速连续搜索时只保留最后一次)
  if (abortCtrl) abortCtrl.abort()
  abortCtrl = new AbortController()

  if (!hasCondition.value) {
    // W5: 合并面板后条件来源变多, 空条件提示不能只提关键词 (见 t('common.feedback.warn_empty_form'))
    ElMessage.warning(t('common.feedback.warn_empty_form'))
    results.value = []
    total.value = 0
    totalPages.value = 0
    return
  }

  const isLoadMore = _loadMoreInFlight.value
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
        type: advancedForm.type || undefined,
        machineCategory: advancedForm.machineCategory || undefined,
        // W4/W5: 8 字段多框条件 (后端任一非空 → 走 PG 精确过滤; 与 q 可叠加)
        oemBrand: advancedForm.oemBrand.trim() || undefined,
        oemNo2: advancedForm.oemNo2.trim() || undefined,
        oemNo3: advancedForm.oemNo3.trim() || undefined,
        machineBrand: advancedForm.machineBrand.trim() || undefined,
        machineModel: advancedForm.machineModel.trim() || undefined,
        modelName: advancedForm.modelName.trim() || undefined,
        engineBrand: advancedForm.engineBrand.trim() || undefined,
        engineType: advancedForm.engineType.trim() || undefined,
        // W5: 尺寸 6 子条件 (null → 该子条件不参与)
        d1: advancedForm.d1 ?? undefined,
        d2: advancedForm.d2 ?? undefined,
        d3: advancedForm.d3 ?? undefined,
        h1: advancedForm.h1 ?? undefined,
        h2: advancedForm.h2 ?? undefined,
        h3: advancedForm.h3 ?? undefined
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
    // 🔧 fix(2026-08-23 走查): 加载更多累积结果, 新搜索/筛选仍重置
    if (isLoadMore) {
      results.value = results.value.concat(resp.hits || [])
    } else {
      results.value = resp.hits || []
    }
    total.value = resp.total
    totalPages.value = resp.totalPages
  } catch (e: any) {
    // AbortError 静默 (用户快速输入时正常取消)
    if (e?.name === 'CanceledError' || e?.code === 'ERR_CANCELED') return
    lastError.value = e?.problem?.detail || e?.response?.data?.detail || e?.response?.data?.error || e?.message || '搜索失败'
    results.value = []
    total.value = 0
    totalPages.value = 0
  } finally {
    loading.value = false
  }
}

// 🔧 fix(2026-08-22 走查 P2-1): 已在聚合搜索页时, 用顶部全局搜索框输入回车
//   → 路由 query.q 变化但组件复用不重挂载 → onMounted 不触发, q ref 不更新 → 结果区空。
//   watch route.query.q: 同步到 q ref 并立即搜索 (programmaticUpdate 跳过 watch(q) 防抖防重复)。
watch(() => route.query.q as string | undefined, (newQ, oldQ) => {
  if (newQ === oldQ) return
  programmaticUpdate = true
  setTimeout(() => { programmaticUpdate = false }, 0)
  q.value = (newQ as string) || ''
  page.value = 1
  syncUrl()
  doSearch()
})

// q 输入 → 500ms 防抖搜索
watch(q, () => {
  if (programmaticUpdate) return
  if (debounceTimer) window.clearTimeout(debounceTimer)
  // 🔧 fix(2026-10-01 走查): 清空关键词/点「清空」后已无任何条件时, 自动触发路径不得发起搜索 —
  //   否则误弹「请在融合搜索框或 8 字段中输入至少一项」。与 clearSearch() 空态一致: 静默重置结果。
  if (!hasCondition.value) {
    results.value = []
    total.value = 0
    totalPages.value = 0
    return
  }
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
  if (programmaticUpdate) return
  // 🔧 fix(2026-10-01 走查): 8 字段/尺寸被清空且无其它条件时, 不应自动搜索 (同 watch(q) 守卫理由)
  if (!hasCondition.value) {
    results.value = []
    total.value = 0
    totalPages.value = 0
    return
  }
  page.value = 1
  doSearch()
}, { deep: true })

// URL 同步 (刷新页面可还原状态)
function syncUrl() {
  const query: Record<string, string> = {}
  if (q.value.trim()) query.q = q.value.trim()
  if (page.value > 1) query.page = String(page.value)
  if (advancedForm.type) query.type = advancedForm.type
  if (advancedForm.machineCategory) query.machineCategory = advancedForm.machineCategory
  if (advancedForm.tolerance !== 5) query.tolerance = String(advancedForm.tolerance)
  // W5 (2026-10-01 走查): 合并面板新增的 8 字段 + 6 尺寸同样需要同步,
  //   否则刷新/分享链接后 OEM Brand、尺寸等条件全部丢失 (与 advancedForm 初始 queryStr/queryNum 读取对称)。
  for (const f of EIGHT_FIELDS) {
    const value = advancedForm[f.key].trim()
    if (value) query[f.key] = value
  }
  for (const d of DIMENSIONS) {
    const value = advancedForm[d.key]
    if (value !== undefined) query[d.key] = String(value)
  }
  router.replace({ path: '/search/aggregate', query })
}

// 展开/收起聚合卡片的 oemList
function toggleExpand(key: string) {
  const next = new Set(expandedKeys.value)
  if (next.has(key)) next.delete(key)
  else next.add(key)
  expandedKeys.value = next
}

function getPrimaryOem(hit: AggregateSearchHit) {
  return hit.oemList?.find((item) => item.oemNo3)
}

function getPublicOemLabel(hit: AggregateSearchHit): string {
  return getPrimaryOem(hit)?.oemNo3 || hit.oem2 || 'OEM -'
}

function stripSearchHighlight(value: string | null | undefined): string | undefined {
  return value?.replace(/<\/?mark>/gi, '')
}

const placeholderImage = '/images/product-placeholder.svg'

function getPrimaryImageUrl(hit: AggregateSearchHit): string {
  const oemNo3 = getPrimaryOem(hit)?.oemNo3
  return oemNo3 ? `/oem2/${encodeURIComponent(oemNo3)}.jpg` : placeholderImage
}

function usePlaceholder(event: Event): void {
  const image = event.currentTarget as HTMLImageElement | null
  if (image && image.src !== new URL(placeholderImage, window.location.origin).href) {
    image.src = placeholderImage
  }
}

// V2 Task 4.4: 跳转产品详情 SEO URL
//   AggregateSearchHit 含产品名和 OEM3，可拼完整 SEO URL。
// 🔧 fix(2026-08-23 走查): window.location.href 整页刷新 → router.push 无刷新跳转。
//   /seo/{oem} 是 SPA 路由 (ProductDetailView), 整页刷新会白屏重载 (用户感知"半秒多");
//   router.push 同一 URL 无刷新, 详情进入感知瞬间 (URL 不变, SEO/分享不受影响)。
function viewDetail(hit: AggregateSearchHit) {
  const firstOem = getPrimaryOem(hit)
  const url = buildProductUrl({
    productName1: stripSearchHighlight(hit.productName1),
    productName2: stripSearchHighlight(hit.productName2),
    oemBrand: firstOem?.oemBrand,
    oemNo3: firstOem?.oemNo3,
    oemNoDisplay: firstOem?.oemNo3 || hit.oem2
  })
  router.push(url)
}

// 清空搜索
function clearSearch() {
  q.value = ''
  advancedForm.type = ''
  advancedForm.machineCategory = ''
  advancedForm.tolerance = 5
  // W5: 合并面板新增条件必须一并清空, 否则"清空"后仍被 8 字段/尺寸过滤 (用户会以为清空无效)
  for (const f of EIGHT_FIELDS) advancedForm[f.key] = ''
  for (const d of DIMENSIONS) advancedForm[d.key] = undefined
  page.value = 1
  results.value = []
  total.value = 0
  syncUrl()
}

async function loadMachineCatalog() {
  try {
    // 🔧 fix(2026-08-22 走查 P3-3): 目录 15.5MB JSON (gzip 4.2MB) 每次进页都拉取 → sessionStorage 缓存 30 分钟,
    //   配合后端 MemoryCache (30 分钟 TTL) 双重减少传输与构建开销。
    const CACHE_KEY = 'machine-catalog-v1'
    const cachedRaw = sessionStorage.getItem(CACHE_KEY)
    if (cachedRaw) {
      machineCatalog.value = JSON.parse(cachedRaw)
      return
    }
    machineCatalog.value = await publicSearchApi.machineCatalog()
    try { sessionStorage.setItem(CACHE_KEY, JSON.stringify(machineCatalog.value)) } catch { /* 超限忽略 */ }
    // 30 分钟后清除, 由下一次加载刷新
    setTimeout(() => { try { sessionStorage.removeItem(CACHE_KEY) } catch { /* ignore */ } }, 30 * 60 * 1000)
  } catch (error) {
    // 目录加载失败不阻断公开搜索，避免辅助导航影响主流程。
    console.warn('[AggregateSearchView] 机型目录加载失败', error)
  }
}

function selectMachine(category: string, brand?: string, model?: string) {
  // 🔧 fix(审查): 程序性批量更新标志 — selectMachine 同时改 machineCategory + q,
  //   watch(advancedForm)(立即) + watch(q)(500ms 防抖) 会触发两次 doSearch (用户实测 "快速显示两遍")
  //   setTimeout 0 是宏任务: watch 回调(微任务)执行时标志仍为 true → 跳过; 随后清除 → 后续手动输入不受影响
  programmaticUpdate = true
  setTimeout(() => { programmaticUpdate = false }, 0)
  const categoryMap: Record<string, string> = {
    Agriculture: 'agriculture', Commercial: 'commercial', Construction: 'construction',
    Industrial: 'industrial', others: 'others'
  }
  advancedForm.machineCategory = categoryMap[category] || 'others'
  q.value = [brand, model].filter(Boolean).join(' ')
  // 🔧 fix(2026-08-23 走查): 记录目录选中态供按钮高亮
  selectedCatalog.value = { category, brand, model }
  page.value = 1
  syncUrl()
  doSearch()
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
  loadMachineCatalog()
  // W5: 首屏条件判断改用 hasCondition — 合并面板后 URL 可能只带 oemBrand/尺寸等条件 (原判断会漏掉不搜索)
  if (hasCondition.value) doSearch()
})

onBeforeUnmount(() => {
  if (debounceTimer) window.clearTimeout(debounceTimer)
  if (abortCtrl) abortCtrl.abort()
})
</script>

<template>
  <!-- P-Admin-UX: 改 max-w-screen-2xl mx-auto → w-full, 撑满容器 (同 AdminProductsView 先例: 原 1536px 限制下内容只占左侧, 右侧留白) -->
  <div class="p-4 w-full">
    <div :class="hasMachineCatalog ? 'lg:grid lg:grid-cols-[220px_minmax(0,1fr)] lg:gap-6' : ''">
      <aside
        v-if="hasMachineCatalog"
        class="hidden lg:block self-start sticky top-4 max-h-[calc(100vh-5rem)] overflow-y-auto border border-gray-200 p-3 dark:border-[var(--color-border)]"
        aria-label="机型分类目录"
      >
        <div class="text-sm font-medium pb-2 mb-2 border-b border-gray-200 dark:border-[var(--color-border)]">机型目录</div>
        <section v-for="category in machineCatalog.categories" :key="category.category" class="py-2 border-b border-gray-100 last:border-b-0 dark:border-[var(--color-border-subtle)]">
          <!-- 🔧 fix(2026-08-23 走查): 选中态高亮 — 选中分类/品牌/机型时按钮暗色底 + 主题色文字 -->
          <el-button
            text
            size="small"
            class="!px-0 !font-medium catalog-node"
            :class="{ 'is-selected': selectedCatalog?.category === category.category && !selectedCatalog?.brand }"
            @click="selectMachine(category.category)"
          >
            {{ category.category }}
          </el-button>
          <div v-for="brand in category.brands" :key="brand.brand" class="mt-1 text-xs">
            <el-button
              text
              size="small"
              class="!h-auto !px-0 catalog-node"
              :class="{ 'is-selected': selectedCatalog?.brand === brand.brand }"
              @click="toggleBrand(brand.brand); selectMachine(category.category, brand.brand)"
            >
              {{ brand.brand }}
            </el-button>
            <div v-if="brand.models.length && expandedBrands.has(brand.brand)" class="ml-2 mt-1 space-y-1">
              <el-button
                v-for="model in brand.models.slice(0, 8)"
                :key="model"
                text
                size="small"
                class="!h-auto !px-0 block text-left text-gray-500 dark:text-[var(--color-text-muted)] catalog-node"
                :class="{ 'is-selected': selectedCatalog?.model === model }"
                @click="selectMachine(category.category, brand.brand, model)"
              >
                {{ model }}
              </el-button>
            </div>
          </div>
        </section>
      </aside>

      <div class="min-w-0">
    <!-- 标题 + 搜索框 -->
    <div class="border-b border-gray-200 pb-3 mb-4 dark:border-[var(--color-border)]">
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
        <!-- 🔧 fix(2026-08-23 走查): 恢复批量粘贴入口 — 见 openBatchSearch/batchDialogOpen -->
        <el-button size="large" @click="openBatchSearch">批量粘贴</el-button>
      </div>
      <div class="flex flex-wrap gap-2 mt-3" aria-label="产品类型快捷筛选">
        <el-button
          v-for="type in quickProductTypes"
          :key="type.value"
          size="small"
          :type="advancedForm.type === type.value ? 'primary' : 'default'"
          @click="toggleQuickProductType(type.value)"
        >
          {{ type.label }}
        </el-button>
      </div>
      <!-- W5 (2026-10-01 走查): 原「高级筛选」与 8 字段搜索合并为统一入口「高级搜索与筛选」 — 见用户需求 6/8 -->
      <div class="mt-2">
        <el-button text size="small" @click="showAdvanced = !showAdvanced">
          {{ showAdvanced ? '收起高级搜索与筛选' : '展开高级搜索与筛选' }}
        </el-button>
        <div
          v-if="showAdvanced"
          class="mt-2 p-3 border border-gray-200 rounded dark:border-[var(--color-border)]"
        >
          <!-- OEM Brand: 独立一行, 与其他 7 字段互不替代 (用户需求 4) -->
          <el-form-item label="OEM Brand" class="!mb-3">
            <el-input
              v-model="advancedForm.oemBrand"
              placeholder="e.g. MANN, Bosch, CAT"
              clearable
              size="small"
              style="max-width: 320px"
            />
          </el-form-item>

          <!-- 其他 7 字段: 各自独立输入, 多字段 AND 收窄, 空字段不参与 -->
          <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
            <el-form-item
              v-for="field in OTHER_EIGHT_FIELDS"
              :key="field.key"
              :label="field.label"
              class="!mb-0"
            >
              <el-input
                v-model="advancedForm[field.key]"
                :placeholder="field.placeholder"
                clearable
                size="small"
              />
            </el-form-item>
          </div>

          <!-- 分类 / 机型分类 / 容差 -->
          <div class="flex flex-wrap gap-3 mt-3 pt-3 border-t border-gray-100 dark:border-[var(--color-border-subtle)]">
            <el-form-item label="分类" class="!mb-0">
              <el-select v-model="advancedForm.type" placeholder="全部" clearable size="small" style="width: 120px">
                <el-option v-for="type in quickProductTypes" :key="type.value" :label="type.label" :value="type.value" />
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
          </div>

          <!-- 尺寸 6 子条件 (用户决策: D1/D2/D3 与 H1/H2/H3 各自独立, 共用上方容差) -->
          <div class="mt-3 pt-3 border-t border-gray-100 dark:border-[var(--color-border-subtle)]">
            <div class="text-xs text-gray-500 mb-2 dark:text-[var(--color-text-muted)]">
              尺寸 (mm) — 填写则按容差匹配, 留空不参与
            </div>
            <div class="grid gap-3 grid-cols-2 sm:grid-cols-3 lg:grid-cols-6">
              <el-form-item v-for="dim in DIMENSIONS" :key="dim.key" :label="dim.label" class="!mb-0">
                <el-input-number
                  v-model="advancedForm[dim.key]"
                  :controls="false"
                  :precision="1"
                  :min="0"
                  placeholder="—"
                  size="small"
                  class="!w-full"
                />
              </el-form-item>
            </div>
          </div>
        </div>
      </div>
    </div>

    <el-collapse v-if="hasMachineCatalog" class="mb-4 lg:hidden">
      <el-collapse-item title="机型目录" name="machine-catalog">
        <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-5">
          <section v-for="category in machineCatalog.categories" :key="category.category" class="min-w-0">
            <el-button text size="small" class="!px-0 !font-medium" @click="selectMachine(category.category)">
              {{ category.category }}
            </el-button>
            <div v-for="brand in category.brands" :key="brand.brand" class="mt-1 text-xs">
              <el-button text size="small" class="!h-auto !px-0" @click="selectMachine(category.category, brand.brand)">
                {{ brand.brand }}
              </el-button>
              <div v-if="brand.models.length" class="ml-2 flex flex-wrap gap-x-2">
                <el-button
                  v-for="model in brand.models.slice(0, 8)"
                  :key="model"
                  text
                  size="small"
                  class="!h-auto !px-0 text-gray-500 dark:text-[var(--color-text-muted)]"
                  @click="selectMachine(category.category, brand.brand, model)"
                >
                  {{ model }}
                </el-button>
              </div>
            </div>
          </section>
        </div>
      </el-collapse-item>
    </el-collapse>

    <!-- 错误提示 -->
    <div v-if="lastError" class="p-3 mb-3 border border-red-300 bg-red-50 text-red-700 text-sm">
      {{ lastError }}
    </div>

    <!-- 元信息 -->
    <div v-if="total > 0" class="text-sm text-gray-600 mb-3 dark:text-[var(--color-text-muted)]">
      <span>共 {{ total }} 条</span>
    </div>

    <!-- 加载中 -->
    <div v-if="loading" class="py-12 text-center text-gray-500 dark:text-[var(--color-text-muted)]">
      <el-icon class="is-loading text-2xl"><Loading /></el-icon>
      <p class="mt-2">搜索中...</p>
    </div>

    <!-- 空结果 -->
    <div v-else-if="!loading && results.length === 0 && q.trim()" class="py-12 text-center text-gray-500 dark:text-[var(--color-text-muted)]">
      <p>未找到匹配结果</p>
      <p class="text-xs mt-1">尝试更换关键词或调整筛选条件</p>
    </div>

    <!-- 搜索结果列表 (内部按 MR.1 聚合，对外展示 OEM 3) -->
    <div v-else class="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
      <div
        v-for="hit in results"
        :key="hit.key"
        class="border border-gray-200 rounded p-3 hover:border-gray-400 transition-colors cursor-pointer dark:border-[var(--color-border)] dark:hover:border-[var(--color-border-strong)]"
        role="link"
        tabindex="0"
        :aria-label="`查看产品 ${getPublicOemLabel(hit)} 详情`"
        @click="viewDetail(hit)"
        @keyup.enter="viewDetail(hit)"
      >
        <div class="flex items-start gap-3">
          <img
            :src="getPrimaryImageUrl(hit)"
            :alt="`${getPublicOemLabel(hit)} 产品主图`"
            class="h-20 w-20 shrink-0 border border-gray-100 object-contain bg-white"
            loading="lazy"
            @error="usePlaceholder"
          />
          <!-- 右侧内容区: 垂直分层 (主信息行 + 操作行), 避免三块混排 -->
          <div class="flex-1 min-w-0 flex flex-col gap-2">
            <!-- 第一行: 物品型号主信息 -->
            <div class="flex items-baseline gap-2 flex-wrap">
              <span class="font-mono text-sm text-gray-900 font-medium dark:text-[var(--color-text)]">{{ getPublicOemLabel(hit) }}</span>
              <!-- V2 Task 1.3.3: v-html 渲染 _formatted 高亮 (sanitizeFormatted 双保险) -->
              <span
                v-if="getHighlighted(hit, 'product_name_1')"
                class="text-sm text-gray-700 dark:text-[var(--color-text)]"
                v-html="getHighlighted(hit, 'product_name_1')"
              ></span>
              <span v-if="hit.productName2" class="text-xs text-gray-500 dark:text-[var(--color-text-muted)]">{{ hit.productName2 }}</span>
              <el-tag size="small" type="info">{{ stripSearchHighlight(hit.type) }}</el-tag>
            </div>
            <!-- 第二行: OEM 2 (可选) -->
            <div v-if="hit.oem2" class="text-xs text-gray-500 dark:text-[var(--color-text-muted)]">OEM 2: {{ hit.oem2 }}</div>
            <!-- 第三行: 操作区 (相关度 + 展开 OEM 按钮), justify-between 分隔 -->
            <div class="flex items-center justify-between gap-2">
              <span v-if="hit.rankingScore != null" class="text-xs text-gray-400 dark:text-[var(--color-text-muted)]">
                相关度 {{ (hit.rankingScore * 100).toFixed(0) }}%
              </span>
              <span v-else></span>
              <!-- V24-F38: 降级模式 (isLegacyFallback=true) 隐藏 "展开 OEM" 按钮 -->
              <!--   WHY: 旧 API 返回空 oemList, 展开后无内容, 按钮点击无意义 -->
              <el-button
                v-if="!isLegacyFallback"
                text
                size="small"
                @click.stop="toggleExpand(hit.key)"
              >
                {{ expandedKeys.has(hit.key) ? '收起' : `展开 OEM (${hit.oemList.length})` }}
              </el-button>
              <!-- V24-F38: 降级模式显示 "基础模式" 标记, 告知用户无 OEM 嵌套详情 -->
              <el-tag v-if="isLegacyFallback" size="small" type="info">基础模式</el-tag>
            </div>
          </div>
        </div>

        <!-- OEM 3 列表 (展开时显示) -->
        <!-- V24-F38: 降级模式 (isLegacyFallback=true) 不渲染 oemList 区域 -->
        <!--   WHY: 旧 API 返回空 oemList, 渲染空表格无意义且误导用户 -->
        <div v-if="!isLegacyFallback && expandedKeys.has(hit.key)" class="mt-3 pt-3 border-t border-gray-100 dark:border-[var(--color-border-subtle)]">
          <div class="text-xs text-gray-500 mb-2 dark:text-[var(--color-text-muted)]">交叉引用 (OEM 3 列表,按品牌优先级排序)</div>
          <table class="w-full text-xs">
            <thead class="text-gray-500 border-b border-gray-200 dark:text-[var(--color-text-muted)] dark:border-[var(--color-border)]">
              <tr>
                <th class="text-left py-1 px-2 font-normal">OEM Brand</th>
                <th class="text-left py-1 px-2 font-normal">OEM 3</th>
                <th class="text-left py-1 px-2 font-normal">OEM 2</th>
                <th class="text-left py-1 px-2 font-normal">机型类型</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(oem, idx) in hit.oemList"
                :key="`${oem.oemBrand}-${oem.oemNo3}-${idx}`"
                class="border-b border-gray-100 hover:bg-gray-50 dark:border-[var(--color-border-subtle)] dark:hover:bg-[var(--color-bg-hover)]"
              >
                <td class="py-1 px-2">{{ oem.oemBrand || '-' }}</td>
                <td class="py-1 px-2 font-mono">{{ oem.oemNo3 || '-' }}</td>
                <td class="py-1 px-2 font-mono">{{ oem.oem2 || '-' }}</td>
                <td class="py-1 px-2">{{ oem.machineType || '-' }}</td>
              </tr>
            </tbody>
          </table>

          <!-- 机型列表 (展开时显示) -->
          <div v-if="hit.machineList.length > 0" class="mt-3">
            <div class="text-xs text-gray-500 mb-2 dark:text-[var(--color-text-muted)]">适配机型 ({{ hit.machineList.length }})</div>
            <div class="flex flex-wrap gap-1">
              <el-tag
                v-for="(m, idx) in hit.machineList.slice(0, 20)"
                :key="`${m.machineBrand}-${m.machineModel}-${idx}`"
                size="small"
                type="info"
              >
                {{ [m.machineBrand, m.machineModel].filter(Boolean).join(' ') }}
              </el-tag>
              <span v-if="hit.machineList.length > 20" class="text-xs text-gray-400 self-center dark:text-[var(--color-text-muted)]">
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
    <!-- 🔧 fix(2026-08-23 走查): 加载更多按钮 — 累积结果不替换 (避免再次全量重置图片加载) -->
    <div v-if="hasMore" class="mt-3 flex justify-center">
      <el-button :loading="loading" @click="loadMore">加载更多 (已显示 {{ results.length }} / {{ total }})</el-button>
    </div>
      </div>
    </div>
  </div>

  <!-- 🔧 fix(2026-08-23 走查): 批量粘贴对话框 — 完整 UI (textarea + 解析预览 + 进度条 + 命中表) -->
  <el-dialog
    v-model="batchDialogOpen"
    title="批量粘贴 OEM 查询 (Excel 多行)"
    width="900px"
    :close-on-click-modal="false"
    destroy-on-close
  >
    <div class="mb-3">
      <el-input
        v-model="batchInput"
        type="textarea"
        :rows="10"
        placeholder="粘贴 OEM 编号, 每行一个 (支持 tab/换行/逗号/分号分隔)&#10;例如:&#10;OEN-123&#10;AB/CD/456&#10;滤清器 1142"
        :disabled="batchLoading"
      />
      <div class="mt-2 text-xs text-muted flex items-center gap-3">
        <span>已识别 {{ batchParsedPreview.unique }} 条</span>
        <span v-if="batchParsedPreview.duplicates > 0" class="text-orange-500">
          (含 {{ batchParsedPreview.duplicates }} 条重复, 将自动去重)
        </span>
      </div>
    </div>

    <div class="flex items-center gap-2 mb-3">
      <el-button type="primary" :loading="batchLoading" :disabled="batchParsedPreview.unique === 0" @click="doBatchSearch">
        查询
      </el-button>
      <el-button :disabled="batchLoading" @click="clearBatch">清空</el-button>
      <span v-if="batchTotal > 0" class="text-xs text-muted">
        共 {{ batchTotal }} 个 OEM, 命中 {{ batchHits }} / 未命中 {{ batchMiss }}, 耗时 {{ batchElapsedMs }} ms
      </span>
    </div>

    <div v-if="batchError" class="text-red-600 text-sm mb-2">{{ batchError }}</div>

    <div v-if="batchTotal > 0" class="mb-2">
      <el-progress
        :percentage="batchTotal > 0 ? Math.round((batchHits / batchTotal) * 100) : 0"
        :status="batchHits === batchTotal ? 'success' : (batchHits === 0 ? 'exception' : '')"
        :stroke-width="14"
            :text-inside="true"
      />
    </div>

    <!-- W8: 批量查询加载态 (首次查询尚无结果时也需明确反馈, 见用户需求 9) -->
    <div v-if="batchLoading && batchResults.length === 0" class="py-8 text-center text-gray-500 dark:text-[var(--color-text-muted)]">
      <el-icon class="is-loading text-2xl"><Loading /></el-icon>
      <p class="mt-2">查询中...</p>
    </div>

    <!-- W6: 已匹配区 (用户需求 5/8 — 上方展示已搜索到的内容) -->
    <div v-if="batchHitRows.length > 0" class="mb-3" data-testid="batch-hit-section">
      <div class="text-sm font-medium mb-2">
        已匹配 ({{ batchHitRows.length }})
      </div>
      <el-table
        :data="batchHitRows"
        stripe
        size="small"
        border
        :row-style="{ cursor: 'pointer' }"
        @row-click="viewBatchProduct"
        v-loading="batchLoading"
        max-height="calc(100vh - 420px)"
      >
        <el-table-column type="index" label="#" width="50" />
        <el-table-column prop="oem" label="OEM 编号" min-width="180" show-overflow-tooltip />
        <el-table-column label="状态" width="80">
          <template #default>
            <span class="text-green-600 font-semibold">✓</span>
          </template>
        </el-table-column>
        <el-table-column prop="productId" label="产品 ID" width="100">
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
    </div>

    <!-- W6/W7: 未匹配区 (下部单独区域 + 悬停快捷添加) -->
    <div v-if="batchTotal > 0" class="mb-3" data-testid="batch-miss-section">
      <div class="text-sm font-medium mb-2 flex items-center gap-2">
        <span>未匹配 ({{ batchMissRows.length }})</span>
        <span v-if="batchMissRows.length > 0" class="text-xs text-gray-500 dark:text-[var(--color-text-muted)]">
          未找到匹配结果 — 悬停行可快捷添加
        </span>
      </div>
      <el-table
        v-if="batchMissRows.length > 0"
        :data="batchMissRows"
        size="small"
        border
        :row-class-name="() => 'batch-miss-row'"
        v-loading="batchLoading"
        max-height="calc(100vh - 420px)"
      >
        <el-table-column type="index" label="#" width="50" />
        <el-table-column prop="oem" label="OEM 编号" min-width="220" show-overflow-tooltip />
        <el-table-column label="状态" width="110">
          <template #default>
            <span class="text-red-500 font-semibold">✗ 未找到匹配结果</span>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="140">
          <template #default="{ row }">
            <el-button
              class="quick-add-btn"
              type="primary"
              size="small"
              data-testid="quick-add-btn"
              @click.stop="openQuickAdd(row)"
            >
              + 快捷添加
            </el-button>
          </template>
        </el-table-column>
      </el-table>
      <div v-else class="text-xs text-gray-500 py-2 dark:text-[var(--color-text-muted)]">
        全部命中, 无未匹配项
      </div>
    </div>

    <div v-if="batchResults.length === 0 && !batchLoading && batchTotal === 0" class="py-12 text-center text-muted">
      <div class="text-4xl mb-2">📋</div>
      <div>粘贴 OEM 编号后点击"查询"</div>
      <div class="text-xs mt-2">支持每行一个 / tab 分列 / 逗号 / 分号, 自动 trim + 去重</div>
    </div>
  </el-dialog>

  <!-- W7: 快捷添加产品弹窗 (未匹配 OEM 补录, 提交后自动重跑批量查询) -->
  <el-dialog
    v-model="quickAddVisible"
    title="快捷添加产品"
    width="560px"
    :close-on-click-modal="false"
    destroy-on-close
  >
    <div class="text-xs text-gray-500 mb-3 dark:text-[var(--color-text-muted)]">
      未匹配 OEM: <span class="font-mono">{{ quickAddSourceOem }}</span>
    </div>
    <el-form label-width="110px" size="small">
      <el-form-item label="MR.1" required>
        <el-input v-model="quickAddForm.mr1" maxlength="10" placeholder="1-10 位字母数字" data-testid="quick-add-mr1" />
      </el-form-item>
      <el-form-item label="OEM 2" required>
        <el-input v-model="quickAddForm.oem2" maxlength="50" data-testid="quick-add-oem2" />
      </el-form-item>
      <el-form-item label="Product Name 1">
        <el-input v-model="quickAddForm.productName1" maxlength="100" placeholder="可选" />
      </el-form-item>
      <el-form-item label="Type">
        <el-select v-model="quickAddForm.type" style="width: 160px">
          <el-option v-for="type in quickProductTypes" :key="type.value" :label="type.label" :value="type.value" />
          <el-option label="其他" value="others" />
        </el-select>
      </el-form-item>
      <el-form-item label="OEM Brand">
        <el-input v-model="quickAddForm.oemBrand" maxlength="100" placeholder="可选" />
      </el-form-item>
      <el-form-item label="OEM 3">
        <el-input v-model="quickAddForm.oemNo3" maxlength="100" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button :disabled="quickAddSubmitting" @click="quickAddVisible = false">取消</el-button>
      <el-button
        type="primary"
        :loading="quickAddSubmitting"
        data-testid="quick-add-submit"
        @click="submitQuickAdd"
      >
        保存
      </el-button>
    </template>
  </el-dialog>
</template>

<script lang="ts">
import { Loading } from '@element-plus/icons-vue'
export default { components: { Loading } }
</script>

<style scoped>
/* 🔧 fix(2026-08-23 走查): 目录选中态 — 暗色底 + 主题色文字, 用户明确看到当前选中项 */
.catalog-node.is-selected {
  background-color: var(--el-color-primary-light-9);
  color: var(--el-color-primary);
  font-weight: 600;
  border-radius: 4px;
}
html.dark .catalog-node.is-selected {
  background-color: rgba(64, 158, 255, 0.15);
  color: var(--el-color-primary-light-3);
}
/* W7 (2026-10-01 走查): 未匹配行 — 默认隐藏「+ 快捷添加」按钮, 悬停/键盘聚焦时显示 (用户需求 5)
   若 :deep 选择器未命中 (例如表格被 teleport 到 body), 按钮保持常显, 属可接受的降级。 */
:deep(.batch-miss-row) .quick-add-btn {
  opacity: 0;
  transition: opacity 0.15s ease;
}
:deep(.batch-miss-row:hover) .quick-add-btn,
:deep(.batch-miss-row:focus-within) .quick-add-btn {
  opacity: 1;
}
</style>
