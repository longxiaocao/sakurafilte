<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
// P3.4 (Task 11.5): 公开搜索页 8 字段多框模糊搜索
//   URL 格式: /public/search?oemBrand=...&oemNo2=...&oemNo3=...&machineBrand=...&machineModel=...&modelName=...&engineBrand=...&engineType=...
//   规格 (新思路.xlsx R2): 8 字段同时支持模糊搜索,任一字段命中即返回
//   - 8 字段全部 optional, 全部空 → 提示 "至少输入 1 个搜索字段"
//   - 多字段 = AND 关系 (收窄范围)
//   - 全部走 P0.1 ILIKE ESCAPE (后端负责转义)
//   - URL 同步: 改字段 → 自动更新 URL, 浏览器后退/前进 → 还原字段
//   - SEO: <title> + meta description 反映当前搜索条件
//   - 设计: Musk 风格极简专业 (纯黑白, 1px 细线, 无阴影, 8px 网格)
import { ref, computed, onMounted, watch, onUnmounted, reactive } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { publicSearchApi } from '@/api'
import type { PublicSearchHit, PublicEightResponse } from '@/api/types'

const route = useRoute()
const router = useRouter()

// ===== 8 字段状态 (与 URL query 双向同步) =====
interface SearchForm {
  oemBrand: string
  oemNo2: string
  oemNo3: string
  machineBrand: string
  machineModel: string
  modelName: string
  engineBrand: string
  engineType: string
}

function emptyForm(): SearchForm {
  return {
    oemBrand: '',
    oemNo2: '',
    oemNo3: '',
    machineBrand: '',
    machineModel: '',
    modelName: '',
    engineBrand: '',
    engineType: ''
  }
}

const form = reactive<SearchForm>(emptyForm())
const page = ref(1)
const pageSize = ref(20)

// 字段定义 — 集中维护 label / placeholder / key / typeaheadField, 模板循环用
//   typeaheadField: 后端 /api/public/typeahead/{field} 的 field 名 (kebab-case)
const fields = [
  { key: 'oemBrand',     label: 'OEM Brand',      placeholder: 'e.g. MANN, Bosch, CAT',  typeaheadField: 'oem-brand' },
  { key: 'oemNo2',       label: 'OEM 2 NO.',      placeholder: '产品自身 OEM 2 编号',     typeaheadField: 'oem-no2' },
  { key: 'oemNo3',       label: 'OEM 3 NO.',      placeholder: 'e.g. 207-60... (交叉引用)', typeaheadField: 'oem-no3' },
  { key: 'machineBrand', label: 'Machine Brand',  placeholder: 'e.g. Caterpillar, JCB',  typeaheadField: 'machine-brand' },
  { key: 'machineModel', label: 'Machine Model',  placeholder: '机型',                   typeaheadField: 'machine-model' },
  { key: 'modelName',    label: 'Model Name',     placeholder: '型号名',                 typeaheadField: 'model-name' },
  { key: 'engineBrand',  label: 'Engine Brand',   placeholder: '发动机品牌',             typeaheadField: 'engine-brand' },
  { key: 'engineType',   label: 'Engine Type',    placeholder: '发动机型号',             typeaheadField: 'engine-type' }
] as const

// ===== typeahead 候选项 (每字段独立 AbortController, 快速输入只保留最后一次请求) =====
//   WHY: 用户快速输入 "CATER" 时, "C"/"CA"/"CAT"/"CATE"/"CATER" 5 次请求,
//        只保留最后一次, 前 4 次用 AbortController 取消, 避免后端无效查询
const typeaheadControllers: Record<string, AbortController | null> = {}

async function fetchSuggestions(fieldKey: string, typeaheadField: string, query: string, cb: (items: string[]) => void) {
  // 输入 < 2 字符不查 (与后端一致, 避免全表扫描)
  if (!query || query.trim().length < 2) {
    cb([])
    return
  }
  // 取消上一次同字段的请求
  const prev = typeaheadControllers[fieldKey]
  if (prev) prev.abort()
  const ctrl = new AbortController()
  typeaheadControllers[fieldKey] = ctrl
  try {
    const resp = await publicSearchApi.typeahead(typeaheadField, query.trim(), 20, ctrl.signal)
    cb(resp.items || [])
  } catch {
    cb([])
  } finally {
    if (typeaheadControllers[fieldKey] === ctrl) typeaheadControllers[fieldKey] = null
  }
}

// ===== 搜索结果状态 =====
const loading = ref(false)
const results = ref<PublicSearchHit[]>([])
const total = ref(0)
const totalPages = ref(0)
const elapsedMs = ref(0)
const countMode = ref<string>('exact')
const lastError = ref('')

// 8 字段是否全部空 — 用于禁用搜索按钮 + 提示文案
const allEmpty = computed(() =>
  !form.oemBrand && !form.oemNo2 && !form.oemNo3
  && !form.machineBrand && !form.machineModel
  && !form.modelName && !form.engineBrand && !form.engineType
)

// 当前填了几个字段 — 显示在结果区顶部 "8 字段中 N 项有值"
const filledCount = computed(() =>
  fields.reduce((acc, f) => acc + (form[f.key] ? 1 : 0), 0)
)

// ===== URL 双向同步 =====
//   WHY: 分享链接 (R8 规格 "/search?oemBrand=CAT" 可直接发给客户), 浏览器后退能回到上次的搜索
//   策略: form → router.replace 同步 (不污染 history); route.query → form 还原 (用户分享/后退)
function syncFormFromUrl() {
  const q = route.query
  form.oemBrand = String(q.oemBrand ?? '')
  form.oemNo2 = String(q.oemNo2 ?? '')
  form.oemNo3 = String(q.oemNo3 ?? '')
  form.machineBrand = String(q.machineBrand ?? '')
  form.machineModel = String(q.machineModel ?? '')
  form.modelName = String(q.modelName ?? '')
  form.engineBrand = String(q.engineBrand ?? '')
  form.engineType = String(q.engineType ?? '')
  page.value = Math.max(1, Number(q.page ?? 1) || 1)
  pageSize.value = Math.max(1, Math.min(100, Number(q.pageSize ?? 20) || 20))
}

function syncUrlFromForm() {
  const q: Record<string, string> = {}
  for (const f of fields) {
    if (form[f.key]) q[f.key] = form[f.key]
  }
  if (page.value > 1) q.page = String(page.value)
  if (pageSize.value !== 20) q.pageSize = String(pageSize.value)
  // 用 replace 不入栈, 避免每个按键都新增 history entry
  router.replace({ path: '/public/search', query: q })
}

// 监听 form 变化 → 同步到 URL (用 nextTick 避免重入)
let syncing = false
watch(form, () => {
  if (syncing) return
  syncUrlFromForm()
}, { deep: true })

watch(page, () => {
  if (syncing) return
  syncUrlFromForm()
})

watch(pageSize, () => {
  if (syncing) return
  syncUrlFromForm()
})

// 监听 route 变化 (浏览器后退/前进/分享链接打开) → 还原 form
watch(() => route.query, () => {
  if (syncing) return
  syncing = true
  syncFormFromUrl()
  syncing = false
  // URL 变化时也跑一次搜索 (若是分享链接)
  if (filledCount.value > 0) doSearch()
})

// ===== 搜索执行 =====
async function doSearch() {
  if (allEmpty.value) {
    ElMessage.warning(t('common.feedback.warn_040'))
    return
  }
  loading.value = true
  lastError.value = ''
  try {
    const resp: PublicEightResponse = await publicSearchApi.eightField({
      oemBrand: form.oemBrand || undefined,
      oemNo2: form.oemNo2 || undefined,
      oemNo3: form.oemNo3 || undefined,
      machineBrand: form.machineBrand || undefined,
      machineModel: form.machineModel || undefined,
      modelName: form.modelName || undefined,
      engineBrand: form.engineBrand || undefined,
      engineType: form.engineType || undefined,
      page: page.value,
      pageSize: pageSize.value
    })
    results.value = resp.items
    total.value = resp.total
    totalPages.value = resp.totalPages
    elapsedMs.value = resp.elapsedMs
    countMode.value = resp.countMode
    applySeo()
  } catch (e: any) {
    lastError.value = e?.problem?.detail || e?.response?.data?.detail || e?.message || '搜索失败'
    results.value = []
    total.value = 0
    totalPages.value = 0
  } finally {
    loading.value = false
  }
}

// 任意字段输入 → 自动搜索 (debounce 500ms, 与 Day 9 SearchView 体验一致)
let debounceTimer: number | null = null
watch(form, () => {
  if (allEmpty.value) {
    // 全部清空 → 重置结果
    results.value = []
    total.value = 0
    return
  }
  if (debounceTimer) window.clearTimeout(debounceTimer)
  debounceTimer = window.setTimeout(() => {
    page.value = 1  // 改条件回到第 1 页
    doSearch()
  }, 500)
}, { deep: true })

// 翻页
watch(page, () => {
  if (allEmpty.value) return
  doSearch()
})

function clearAll() {
  for (const f of fields) form[f.key] = ''
  results.value = []
  total.value = 0
  page.value = 1
  ElMessage.info(t('common.feedback.success_016'))
}

// ===== 详情页跳转 =====
function viewDetail(row: PublicSearchHit) {
  const oem = row.oemNoDisplay || row.oem2
  if (oem) router.push(`/product/${encodeURIComponent(oem)}`)
}

// ===== SEO meta =====
let ogTags: HTMLMetaElement[] = []
function applySeo() {
  const filled = fields.filter(f => form[f.key]).map(f => `${f.label}=${form[f.key]}`).join(', ')
  const title = filled
    ? `搜索: ${filled.slice(0, 60)} - SakuraFilter`
    : '产品搜索 - SakuraFilter'
  document.title = title
  ensureMeta('description', filled
    ? `共 ${total.value} 条结果 (${elapsedMs.value}ms). ${filled}`
    : '8 字段多框模糊搜索 1M+ 滤芯产品')
}
function ensureMeta(name: string, content: string) {
  let el = document.head.querySelector<HTMLMetaElement>(`meta[name="${name}"]`)
  if (!el) {
    el = document.createElement('meta')
    el.setAttribute('name', name)
    document.head.appendChild(el)
    ogTags.push(el)
  }
  el.setAttribute('content', content)
}
onUnmounted(() => {
  for (const el of ogTags) el.remove()
  ogTags = []
  document.title = 'SakuraFilter'
})

onMounted(() => {
  syncFormFromUrl()
  if (filledCount.value > 0) doSearch()
})

// P1-4 修复: 组件卸载时清理 debounceTimer, 防止内存泄漏 (规则 5.2 副作用清理)
//   WHY: watch 内 setTimeout 若未清理, 组件卸载后仍会触发 doSearch, 访问已销毁的响应式状态
onUnmounted(() => {
  if (debounceTimer) {
    window.clearTimeout(debounceTimer)
    debounceTimer = null
  }
})
</script>

<template>
  <div class="p-3 max-w-screen-2xl mx-auto">
    <!-- 标题 + 清空按钮 -->
    <div class="flex items-center justify-between mb-3">
      <div>
        <h1 class="text-lg font-medium">产品搜索 (8 字段多框)</h1>
        <p class="text-xs text-muted mt-1">
          规格: OEM Brand / OEM 2 / OEM 3 / Machine Brand / Machine Model / Model Name / Engine Brand / Engine Type
        </p>
      </div>
      <el-button @click="clearAll" size="small" :disabled="allEmpty">清空</el-button>
    </div>

    <!-- 8 字段 2 行 4 列 grid 布局 (响应式) -->
    <div class="hairline p-3 mb-3">
      <div class="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-4 gap-3">
        <div v-for="f in fields" :key="f.key">
          <label class="block text-xs text-muted mb-1">{{ f.label }}</label>
          <el-autocomplete
            v-model="form[f.key]"
            :placeholder="f.placeholder"
            :fetch-suggestions="(q: any, cb: any) => fetchSuggestions(f.key, f.typeaheadField, String(q ?? ''), cb as any)"
            :trigger-on-focus="false"
            clearable
            size="default"
            class="w-full"
            @select="() => doSearch()"
            @keyup.enter="doSearch"
          />
        </div>
      </div>
      <div class="mt-2 text-xs text-muted flex items-center gap-3">
        <span>已填 {{ filledCount }} / 8 字段</span>
        <span v-if="filledCount > 0" class="text-blue-600">500ms 防抖自动搜索</span>
        <span v-else>输入任意字段开始搜索</span>
      </div>
    </div>

    <!-- 错误提示 -->
    <div v-if="lastError" class="text-red-600 text-sm mb-2">{{ lastError }}</div>

    <!-- 全部空 → 提示输入 -->
    <div v-if="allEmpty" class="py-12 text-center text-muted">
      <el-icon class="text-4xl mb-2"><Search /></el-icon>
      <div>请输入至少 1 个搜索字段</div>
      <div class="text-xs mt-2">多字段为 AND 关系, 全部走 P0.1 ILIKE ESCAPE 模糊匹配</div>
    </div>

    <!-- 结果区 -->
    <div v-else>
      <!-- 结果头: 数量 + 耗时 + countMode + 翻页 -->
      <div class="hairline-b px-2 py-1 bg-[var(--color-bg-hover)] text-xs text-muted flex items-center justify-between">
        <div class="flex items-center gap-3">
          <span>
            共 <strong class="text-[var(--color-text)]">{{ total.toLocaleString() }}</strong> 条结果
            <span v-if="countMode === 'estimated'" class="text-orange-500 ml-1">(估计值, 实际可能更少)</span>
          </span>
          <span>耗时 {{ elapsedMs }} ms</span>
          <span>显示第 {{ (page - 1) * pageSize + 1 }}-{{ Math.min(page * pageSize, total) }} 条</span>
        </div>
        <div class="flex items-center gap-2">
          <el-pagination
            v-model:current-page="page"
            v-model:page-size="pageSize"
            :total="total"
            :page-sizes="[10, 20, 50, 100]"
            layout="sizes, prev, pager, next"
            small
            background
          />
        </div>
      </div>

      <!-- 结果表格 -->
      <el-table
        v-loading="loading"
        :data="results"
        stripe
        size="small"
        :row-style="{ cursor: 'pointer' }"
        @row-click="viewDetail"
        max-height="calc(100vh - 320px)"
      >
        <el-table-column prop="id" label="ID" width="70" />
        <el-table-column label="OEM" min-width="180" show-overflow-tooltip>
          <template #default="{ row }">
            <span class="text-blue-600">{{ row.oemNoDisplay || row.oem2 || '—' }}</span>
          </template>
        </el-table-column>
        <el-table-column label="OEM 2" min-width="160" show-overflow-tooltip>
          <template #default="{ row }">{{ row.oem2 || '—' }}</template>
        </el-table-column>
        <el-table-column label="Product Name 1" min-width="200" show-overflow-tooltip>
          <template #default="{ row }">{{ row.productName1 || '—' }}</template>
        </el-table-column>
        <el-table-column prop="type" label="Type" width="100" />
        <el-table-column label="D1 (mm)" width="100" align="right">
          <template #default="{ row }">{{ row.d1Mm || '—' }}</template>
        </el-table-column>
        <el-table-column label="H1 (mm)" width="100" align="right">
          <template #default="{ row }">{{ row.h1Mm || '—' }}</template>
        </el-table-column>
      </el-table>

      <!-- 底部翻页 -->
      <div class="mt-3 flex justify-end">
        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :total="total"
          :page-sizes="[10, 20, 50, 100]"
          layout="sizes, prev, pager, next, total"
          background
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
/* 8 字段 grid 在窄屏 (mobile) 折叠为 1 列, 桌面 4 列 */
@media (max-width: 640px) {
  .grid-cols-1 { grid-template-columns: repeat(1, minmax(0, 1fr)) !important; }
}
</style>
