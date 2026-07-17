<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
// P0 权限改造 (Day 14): 公开产品对比页
//   - 无需 token, 游客可直接访问 /compare?ids=1,2,3,4,5,6
//   - 复用 AdminCompareView 6 字段组布局 (基础/尺寸/性能/包装/外箱/CrossRef)
//   - 数据源: GET /api/public/compare?ids=1,2,3 (排除下架产品, 最多 6 个)
//   - 保留差异高亮 + 列调序 + 加入/移除 + 打印
//   - 不写 localStorage (游客会话性, 不持久化列顺序)
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { publicCompareApi } from '@/api'
import type { ProductDetail, XrefInfo, MachineAppInfo } from '@/api/types'
import { buildProductUrl } from '@/utils/build-product-url'

const route = useRoute()
const router = useRouter()

// ===== 常量 =====
const MAX_COMPARE = 6

// ===== 字段定义 (与 AdminCompareView 一致) =====
interface FieldDef {
  key: keyof ProductDetail | string
  label: string
  get: (p: ProductDetail) => string
  eq?: (a: string, b: string) => boolean
}

const xrefSummary = (list: XrefInfo[] | undefined) => {
  if (!list || list.length === 0) return ''
  const head = list.slice(0, 3).map((x) => `${x.oemBrand || ''} ${x.oemNo3 || ''}`.trim()).filter(Boolean)
  return head.length === 0 ? '' : head.join('; ') + (list.length > 3 ? ` (+${list.length - 3})` : '')
}

const machineSummary = (list: MachineAppInfo[] | undefined) => {
  if (!list || list.length === 0) return ''
  const head = list.slice(0, 2).map((m) => `${m.machineBrand || ''} ${m.machineModel || ''}`.trim()).filter(Boolean)
  return head.length === 0 ? '' : head.join('; ') + (list.length > 2 ? ` (+${list.length - 2})` : '')
}

interface FieldGroup {
  name: string
  fields: FieldDef[]
}

const fieldGroups: FieldGroup[] = [
  {
    name: '基础',
    fields: [
      { key: 'oemNoDisplay', label: 'OEM 编号', get: (p) => p.oemNoDisplay ?? '' },
      { key: 'oem2', label: 'OEM 2', get: (p) => p.oem2 ?? '' },
      { key: 'mr1', label: 'MR.1', get: (p) => p.mr1 ?? '' },
      { key: 'productName1', label: '产品名 1', get: (p) => p.productName1 ?? '' },
      { key: 'productName2', label: '产品名 2', get: (p) => p.productName2 ?? '' },
      { key: 'type', label: '类型', get: (p) => p.type ?? '' }
    ]
  },
  {
    name: '尺寸 (mm)',
    fields: [
      { key: 'd1Mm', label: 'D1', get: (p) => (p.d1Mm !== undefined && p.d1Mm !== null ? String(p.d1Mm) : '') },
      { key: 'd2Mm', label: 'D2', get: (p) => (p.d2Mm !== undefined && p.d2Mm !== null ? String(p.d2Mm) : '') },
      { key: 'd3Mm', label: 'D3', get: (p) => (p.d3Mm !== undefined && p.d3Mm !== null ? String(p.d3Mm) : '') },
      { key: 'd4Mm', label: 'D4', get: (p) => (p.d4Mm !== undefined && p.d4Mm !== null ? String(p.d4Mm) : '') },
      { key: 'h1Mm', label: 'H1', get: (p) => (p.h1Mm !== undefined && p.h1Mm !== null ? String(p.h1Mm) : '') },
      { key: 'h2Mm', label: 'H2', get: (p) => (p.h2Mm !== undefined && p.h2Mm !== null ? String(p.h2Mm) : '') },
      { key: 'h3Mm', label: 'H3', get: (p) => (p.h3Mm !== undefined && p.h3Mm !== null ? String(p.h3Mm) : '') },
      { key: 'h4Mm', label: 'H4', get: (p) => (p.h4Mm !== undefined && p.h4Mm !== null ? String(p.h4Mm) : '') }
    ]
  },
  {
    name: '性能',
    fields: [
      { key: 'd7Thread', label: 'D7 螺纹', get: (p) => p.d7Thread ?? '' },
      { key: 'd8Thread', label: 'D8 螺纹', get: (p) => p.d8Thread ?? '' },
      { key: 'noCheckValves', label: '单向阀数', get: (p) => (p.noCheckValves !== undefined && p.noCheckValves !== null ? String(p.noCheckValves) : '') },
      { key: 'noBypassValves', label: '旁通阀数', get: (p) => (p.noBypassValves !== undefined && p.noBypassValves !== null ? String(p.noBypassValves) : '') },
      { key: 'bypassValveLr', label: '旁通 LR', get: (p) => (p.bypassValveLr !== undefined && p.bypassValveLr !== null ? String(p.bypassValveLr) : '') },
      { key: 'bypassValveHr', label: '旁通 HR', get: (p) => (p.bypassValveHr !== undefined && p.bypassValveHr !== null ? String(p.bypassValveHr) : '') },
      { key: 'efficiency1', label: '效率 1', get: (p) => p.efficiency1 ?? '' },
      { key: 'efficiency2', label: '效率 2', get: (p) => p.efficiency2 ?? '' },
      { key: 'bypassPressure', label: '旁通压力', get: (p) => (p.bypassPressure !== undefined && p.bypassPressure !== null ? String(p.bypassPressure) : '') },
      { key: 'collapsePressureBar', label: '耐压 (bar)', get: (p) => (p.collapsePressureBar !== undefined && p.collapsePressureBar !== null ? String(p.collapsePressureBar) : '') },
      { key: 'sealingMaterial', label: '密封材料', get: (p) => p.sealingMaterial ?? '' },
      { key: 'tempRange', label: '温度范围', get: (p) => p.tempRange ?? '' }
    ]
  },
  {
    name: '包装',
    fields: [
      { key: 'media', label: '介质', get: (p) => p.media ?? '' },
      { key: 'mediaModel', label: '介质型号', get: (p) => p.mediaModel ?? '' },
      { key: 'qtyPerCarton', label: '箱/件', get: (p) => (p.qtyPerCarton !== undefined && p.qtyPerCarton !== null ? String(p.qtyPerCarton) : '') },
      { key: 'weightKgs', label: '重量 (kg)', get: (p) => (p.weightKgs !== undefined && p.weightKgs !== null ? String(p.weightKgs) : '') },
      { key: 'cartonLengthMm', label: '箱长 (mm)', get: (p) => (p.cartonLengthMm !== undefined && p.cartonLengthMm !== null ? String(p.cartonLengthMm) : '') },
      { key: 'cartonWidthMm', label: '箱宽 (mm)', get: (p) => (p.cartonWidthMm !== undefined && p.cartonWidthMm !== null ? String(p.cartonWidthMm) : '') },
      { key: 'cartonHeightMm', label: '箱高 (mm)', get: (p) => (p.cartonHeightMm !== undefined && p.cartonHeightMm !== null ? String(p.cartonHeightMm) : '') },
      { key: 'volumePerCartonM3', label: '箱体积 (m³)', get: (p) => (p.volumePerCartonM3 !== undefined && p.volumePerCartonM3 !== null ? String(p.volumePerCartonM3) : '') }
    ]
  },
  {
    name: 'CrossRef / 车型',
    fields: [
      { key: 'crossReferences', label: 'OEM 交叉引用', get: (p) => xrefSummary(p.crossReferences) },
      { key: 'machineApplications', label: '适配车型', get: (p) => machineSummary(p.machineApplications) }
    ]
  }
]

// ===== 状态 =====
const loading = ref(false)
const error = ref('')
const products = ref<ProductDetail[]>([])
const newIdInput = ref('')

// ===== 加载产品 =====
async function loadByIds(ids: number[]) {
  if (ids.length === 0) {
    products.value = []
    return
  }
  loading.value = true
  error.value = ''
  try {
    const capped = ids.slice(0, MAX_COMPARE)
    const data = await publicCompareApi.compare(capped)
    // 按 capped 顺序对齐 (后端可能按 id 排序)
    const map = new Map(data.items.map((p) => [p.id, p]))
    products.value = capped.map((id) => map.get(id)).filter((p): p is ProductDetail => !!p)
  } catch (e: any) {
    error.value = e?.problem?.detail || e?.response?.data?.error || e?.message || '加载失败'
    ElMessage.error(error.value)
  } finally {
    loading.value = false
  }
}

function parseIdsFromQuery(): number[] {
  const raw = (route.query.ids as string) || ''
  if (!raw) return []
  return raw
    .split(',')
    .map((s) => parseInt(s.trim(), 10))
    .filter((n) => !isNaN(n) && n > 0)
    .slice(0, MAX_COMPARE)
}

onMounted(async () => {
  const ids = parseIdsFromQuery()
  if (ids.length > 0) {
    await loadByIds(ids)
  }
})

// 路由变化时 (如刷新/分享链接) 重新加载
watch(
  () => route.query.ids,
  async (newIds) => {
    if (typeof newIds === 'string' && newIds) {
      const ids = parseIdsFromQuery()
      if (ids.length > 0 && ids.join(',') !== products.value.map((p) => p.id).join(',')) {
        await loadByIds(ids)
      }
    }
  }
)

// ===== 列调序 (按钮方式) =====
function moveLeft(idx: number) {
  if (idx <= 0) return
  const arr = [...products.value]
  ;[arr[idx - 1], arr[idx]] = [arr[idx], arr[idx - 1]]
  products.value = arr
  persistUrlOrder()
}

function moveRight(idx: number) {
  if (idx >= products.value.length - 1) return
  const arr = [...products.value]
  ;[arr[idx], arr[idx + 1]] = [arr[idx + 1], arr[idx]]
  products.value = arr
  persistUrlOrder()
}

function removeProduct(idx: number) {
  products.value = products.value.filter((_, i) => i !== idx)
  persistUrlOrder()
  ElMessage.success(t('common.feedback.info_017'))
}

function persistUrlOrder() {
  const ids = products.value.map((p) => p.id).join(',')
  router.replace({ path: '/compare', query: ids ? { ids } : {} })
}

// ===== 加入产品 (公开版本, 通过公开 compare API 拉取单个) =====
async function addProductById() {
  const id = parseInt(newIdInput.value.trim(), 10)
  if (!id || isNaN(id)) {
    ElMessage.warning(t('common.feedback.error_048'))
    return
  }
  if (products.value.some((p) => p.id === id)) {
    ElMessage.warning(t('common.feedback.info_041'))
    return
  }
  if (products.value.length >= MAX_COMPARE) {
    ElMessage.warning(`最多对比 ${MAX_COMPARE} 个产品`)
    return
  }
  loading.value = true
  try {
    // 复用 compare API 拉单条: GET /api/public/compare?ids={id}
    const data = await publicCompareApi.compare([id])
    if (data.items.length === 0) {
      ElMessage.warning(t('common.feedback.error_003'))
      return
    }
    products.value = [...products.value, data.items[0]]
    newIdInput.value = ''
    persistUrlOrder()
    ElMessage.success(`已加入: ${data.items[0].oemNoDisplay}`)
  } catch (e: any) {
    ElMessage.error(e?.problem?.detail || e?.message || '加载产品失败')
  } finally {
    loading.value = false
  }
}

function clearAll() {
  products.value = []
  persistUrlOrder()
}

// ===== 差异高亮算法 =====
function cellClass(values: string[]): string {
  const allEmpty = values.every((v) => !v)
  if (allEmpty) return 'empty'
  const allEqual = values.every((v) => v === values[0])
  return allEqual ? 'same' : 'diff'
}

function valueOf(p: ProductDetail | undefined, def: FieldDef): string {
  if (!p) return ''
  return def.get(p)
}

// ===== 仅看差异 开关 =====
const onlyDiff = ref(false)
function groupHasDiff(group: FieldGroup): boolean {
  if (products.value.length < 2) return false
  return group.fields.some((f) => {
    const values = products.value.map((p) => valueOf(p, f))
    const allEmpty = values.every((v) => !v)
    if (allEmpty) return false
    return !values.every((v) => v === values[0])
  })
}

const visibleGroups = computed(() => {
  if (!onlyDiff.value) return fieldGroups
  return fieldGroups.filter((g) => groupHasDiff(g))
})

// ===== 打印 =====
function doPrint() {
  window.print()
}
</script>

<template>
  <div class="p-3 max-w-screen-2xl mx-auto compare-root">
    <!-- Day 14+ A11y: 页面主标题 (视觉隐藏, 屏幕阅读器可达) -->
    <h1 class="sr-only">产品对比</h1>
    <!-- 工具条 -->
    <div class="compare-toolbar flex items-center gap-2 mb-3 flex-wrap">
      <span class="text-sm font-medium">产品对比</span>
      <span class="text-xs text-muted">最多 {{ MAX_COMPARE }} 个 · 公开访问</span>
      <div class="flex-1" />
      <el-input
        v-model="newIdInput"
        placeholder="输入产品 ID 加入"
        size="small"
        style="width: 180px"
        @keyup.enter="addProductById"
      />
      <el-button size="small" :loading="loading" @click="addProductById">加入</el-button>
      <el-checkbox v-model="onlyDiff" size="small">仅看差异</el-checkbox>
      <el-button size="small" @click="clearAll" :disabled="products.length === 0">清空</el-button>
      <el-button size="small" @click="doPrint" :disabled="products.length === 0">打印</el-button>
    </div>

    <!-- 错误状态 -->
    <div v-if="error && products.length === 0" class="py-12 text-center text-red-600 hairline" role="alert">
      {{ error }}
    </div>

    <!-- 空状态 -->
    <div v-if="products.length === 0 && !loading && !error" class="py-12 text-center text-muted hairline">
      <div>暂无对比产品</div>
      <div class="text-xs mt-2">
        在产品详情页点击"加入对比"自动携带 ID, 或在 URL 加 <code>?ids=1,2,3</code>
      </div>
    </div>

    <!-- 加载中 — 骨架屏 (Day 14 感知性能优化) -->
    <div v-if="loading && products.length === 0" class="py-4" role="status" aria-live="polite" aria-label="正在加载产品对比数据">
      <div v-for="g in 6" :key="g" class="mb-4">
        <div class="skel-block h-5 w-20 mb-2" />
        <div v-for="r in 4" :key="r" class="flex gap-3 mb-2">
          <div class="skel-block h-4 w-32" />
          <div class="skel-block h-4 flex-1" />
          <div class="skel-block h-4 flex-1" />
        </div>
      </div>
      <span class="sr-only">正在加载产品对比数据...</span>
    </div>

    <!-- 对比表格 (复用 AdminCompareView 6 字段组布局) -->
    <div v-if="products.length > 0" class="compare-grid-wrap hairline">
      <div
        class="compare-grid"
        :style="{ gridTemplateColumns: `200px repeat(${products.length}, minmax(0, 1fr))` }"
      >
        <div class="compare-header-cell field-name-cell sticky-left">字段</div>
        <div
          v-for="(p, idx) in products"
          :key="p.id"
          class="compare-header-cell product-cell"
        >
          <div class="flex items-start justify-between gap-1">
            <div class="flex-1 min-w-0">
              <div class="font-medium text-sm truncate" :title="p.oemNoDisplay">
                <!-- V2 Task 4.4: SEO URL (ProductDetail 含完整字段, a 标签触发整页 SSR 渲染) -->
                <a
                  :href="buildProductUrl({
                    productName1: p.productName1,
                    productName2: p.productName2,
                    oemBrand: p.crossReferences?.[0]?.oemBrand,
                    oemNo3: p.crossReferences?.[0]?.oemNo3,
                    oemNoDisplay: p.oemNoDisplay,
                    mr1: p.mr1
                  })"
                  class="hover:underline"
                >{{ p.oemNoDisplay }}</a>
              </div>
              <div class="text-xs text-muted truncate" :title="p.oem2 || ''">{{ p.oem2 || '—' }}</div>
            </div>
            <div class="flex flex-col gap-0.5 no-print">
              <el-button
                size="small"
                text
                :disabled="idx === 0"
                @click="moveLeft(idx)"
                title="左移"
                style="padding: 0 2px; height: 16px; line-height: 16px"
                aria-label="左移"
              >‹</el-button>
              <el-button
                size="small"
                text
                :disabled="idx === products.length - 1"
                @click="moveRight(idx)"
                title="右移"
                style="padding: 0 2px; height: 16px; line-height: 16px"
                aria-label="右移"
              >›</el-button>
            </div>
            <el-button
              size="small"
              text
              class="no-print"
              @click="removeProduct(idx)"
              title="移除该列"
              aria-label="移除该列"
              style="padding: 0 4px; height: 18px; line-height: 18px; color: #d00"
            >×</el-button>
          </div>
        </div>

        <template v-for="group in visibleGroups" :key="group.name">
          <div
            class="group-name-cell hairline-t"
            :style="{ gridColumn: `1 / span ${products.length + 1}` }"
          >
            {{ group.name }}
          </div>

          <template v-for="field in group.fields" :key="(group.name + '.' + field.key)">
            <div class="field-name-cell sticky-left">{{ field.label }}</div>
            <div
              v-for="(p, idx) in products"
              :key="p.id + '.' + field.key"
              :class="['data-cell', cellClass(products.map((pp) => valueOf(pp, field)))]"
            >
              {{ valueOf(p, field) || '—' }}
            </div>
          </template>
        </template>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* 复用 AdminCompareView 样式 — Musk 极简风, 无阴影, 1px 边框, 纯黑/白 + 单一强调色 */
/* 颜色全部走 CSS 变量, 响应浅/深色主题切换 */
.compare-grid-wrap {
  overflow-x: auto;
  background: var(--color-bg);
}

.compare-grid {
  display: grid;
  min-width: 100%;
}

.compare-header-cell {
  padding: 8px 10px;
  background: var(--color-bg-table-stripe);
  border-bottom: 1px solid var(--color-border);
  font-size: 13px;
  min-height: 56px;
  position: sticky;
  top: 0;
  z-index: 2;
  color: var(--color-text);
}

.field-name-cell {
  padding: 6px 10px;
  background: var(--color-bg-table-stripe);
  border-bottom: 1px solid var(--color-border);
  border-right: 1px solid var(--color-border);
  font-size: 13px;
  color: var(--color-text);
  position: sticky;
  left: 0;
  z-index: 1;
  display: flex;
  align-items: center;
}

.compare-header-cell.field-name-cell {
  z-index: 3;
  font-weight: 500;
}

.group-name-cell {
  padding: 6px 10px;
  background: var(--color-text);
  color: var(--color-bg);
  font-size: 12px;
  font-weight: 500;
  letter-spacing: 0.5px;
  border-bottom: 1px solid var(--color-border);
  border-top: 1px solid var(--color-border);
}

.data-cell {
  padding: 6px 10px;
  background: var(--color-bg-elevated);
  border-bottom: 1px solid var(--color-border);
  border-right: 1px solid var(--color-border);
  font-size: 13px;
  word-break: break-word;
  min-height: 32px;
  display: flex;
  align-items: center;
  color: var(--color-text);
}

.data-cell:last-child {
  border-right: none;
}

.data-cell.same {
  background: var(--color-bg-same);
}

.data-cell.diff {
  background: var(--color-bg-diff);
}

.data-cell.empty {
  color: var(--color-text-muted);
}

@media print {
  /* 打印: 强制浅色, 隐藏导航/工具栏/拖拽层 */
  :global(.dark),
  :global(html.dark) {
    --color-bg: #ffffff;
    --color-bg-elevated: #ffffff;
    --color-bg-table-stripe: #fafafa;
    --color-bg-same: #f5f5f5;
    --color-bg-diff: #fffbe6;
    --color-text: #111827;
    --color-text-muted: #6b7280;
    --color-border: #e5e7eb;
  }
  .no-print {
    display: none !important;
  }
  .compare-toolbar {
    display: none !important;
  }
  .compare-grid-wrap {
    overflow: visible;
    background: #ffffff !important;
  }
  .field-name-cell,
  .compare-header-cell {
    position: static;
  }
  /* 避免跨页: 字段组强制不换行 */
  .group-name-cell,
  .data-cell,
  .field-name-cell {
    page-break-inside: avoid;
  }
}
</style>
