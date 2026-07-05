<script setup lang="ts">
// P3.5 (Task 12): 产品对比 UI 完整版
//   - 6 列 grid 布局 (200px 字段名 + 6 个产品列等分)
//   - 差异高亮: 全部相等灰底, 有差异黄底, 全部空灰字
//   - 字段行分组: 基础/尺寸/性能/包装/车型/CrossRef
//   - 拖拽列调序 (用上下按钮, 不引入 vuedraggable)
//   - URL 路由: /admin/compare?ids=1,2,3,4,5,6
//   - 6 产品上限, 每列右上角 × 移除
//   - 打印优化 CSS
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { adminProductApi } from '@/api'
import type { ProductDetail, XrefInfo, MachineAppInfo } from '@/api/types'

const { t } = useI18n()

const route = useRoute()
const router = useRouter()

// ===== 常量 =====
const MAX_COMPARE = 6
const ORDER_KEY = 'sakura_compare_order'

// ===== 字段定义 (6 大组) =====
interface FieldDef {
  key: keyof ProductDetail | string
  label: string
  // 取值函数 (从 ProductDetail 提取展示值, 数组/对象类需自定义)
  get: (p: ProductDetail) => string
  // 字符串比较 (默认严格相等)
  eq?: (a: string, b: string) => boolean
}

const xrefSummary = (list: XrefInfo[] | undefined) => {
  if (!list || list.length === 0) return ''
  // 取前 3 个 oemBrand + oemNo3, 其余 ...
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
    name: t('admin.compareview.string.l53_'),
    fields: [
      { key: 'oemNoDisplay', label: t('admin.compareview.string.l55_oem'), get: (p) => p.oemNoDisplay ?? '' },
      { key: 'oem2', label: 'OEM 2', get: (p) => p.oem2 ?? '' },
      { key: 'mr1', label: 'MR.1', get: (p) => p.mr1 ?? '' },
      { key: 'productName1', label: t('admin.compareview.string.l58_1'), get: (p) => p.productName1 ?? '' },
      { key: 'productName2', label: t('admin.compareview.string.l59_2'), get: (p) => p.productName2 ?? '' },
      { key: 'type', label: t('admin.compareview.string.l60_'), get: (p) => p.type ?? '' }
    ]
  },
  {
    name: t('admin.compareview.string.l64_mm'),
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
    name: t('admin.compareview.string.l77_'),
    fields: [
      { key: 'd7Thread', label: t('admin.compareview.string.l79_d7'), get: (p) => p.d7Thread ?? '' },
      { key: 'd8Thread', label: t('admin.compareview.string.l80_d8'), get: (p) => p.d8Thread ?? '' },
      { key: 'noCheckValves', label: t('admin.compareview.string.l81_'), get: (p) => (p.noCheckValves !== undefined && p.noCheckValves !== null ? String(p.noCheckValves) : '') },
      { key: 'noBypassValves', label: t('admin.compareview.string.l82_'), get: (p) => (p.noBypassValves !== undefined && p.noBypassValves !== null ? String(p.noBypassValves) : '') },
      { key: 'bypassValveLr', label: t('admin.compareview.string.l83_lr'), get: (p) => (p.bypassValveLr !== undefined && p.bypassValveLr !== null ? String(p.bypassValveLr) : '') },
      { key: 'bypassValveHr', label: t('admin.compareview.string.l84_hr'), get: (p) => (p.bypassValveHr !== undefined && p.bypassValveHr !== null ? String(p.bypassValveHr) : '') },
      { key: 'efficiency1', label: t('admin.compareview.string.l85_1'), get: (p) => p.efficiency1 ?? '' },
      { key: 'efficiency2', label: t('admin.compareview.string.l86_2'), get: (p) => p.efficiency2 ?? '' },
      { key: 'bypassPressure', label: t('admin.compareview.string.l87_'), get: (p) => (p.bypassPressure !== undefined && p.bypassPressure !== null ? String(p.bypassPressure) : '') },
      { key: 'collapsePressureBar', label: t('admin.compareview.string.l88_bar'), get: (p) => (p.collapsePressureBar !== undefined && p.collapsePressureBar !== null ? String(p.collapsePressureBar) : '') },
      { key: 'sealingMaterial', label: t('admin.compareview.string.l89_'), get: (p) => p.sealingMaterial ?? '' },
      { key: 'tempRange', label: t('admin.compareview.string.l90_'), get: (p) => p.tempRange ?? '' }
    ]
  },
  {
    name: t('admin.compareview.string.l94_'),
    fields: [
      { key: 'media', label: t('admin.compareview.string.l96_'), get: (p) => p.media ?? '' },
      { key: 'mediaModel', label: t('admin.compareview.string.l97_'), get: (p) => p.mediaModel ?? '' },
      { key: 'qtyPerCarton', label: t('admin.compareview.string.l98_'), get: (p) => (p.qtyPerCarton !== undefined && p.qtyPerCarton !== null ? String(p.qtyPerCarton) : '') },
      { key: 'weightKgs', label: t('admin.compareview.string.l99_kg'), get: (p) => (p.weightKgs !== undefined && p.weightKgs !== null ? String(p.weightKgs) : '') },
      { key: 'cartonLengthMm', label: t('admin.compareview.string.l100_mm'), get: (p) => (p.cartonLengthMm !== undefined && p.cartonLengthMm !== null ? String(p.cartonLengthMm) : '') },
      { key: 'cartonWidthMm', label: t('admin.compareview.string.l101_mm'), get: (p) => (p.cartonWidthMm !== undefined && p.cartonWidthMm !== null ? String(p.cartonWidthMm) : '') },
      { key: 'cartonHeightMm', label: t('admin.compareview.string.l102_mm'), get: (p) => (p.cartonHeightMm !== undefined && p.cartonHeightMm !== null ? String(p.cartonHeightMm) : '') },
      { key: 'volumePerCartonM3', label: t('admin.compareview.string.l103_m'), get: (p) => (p.volumePerCartonM3 !== undefined && p.volumePerCartonM3 !== null ? String(p.volumePerCartonM3) : '') }
    ]
  },
  {
    name: t('admin.compareview.string.l107_'),
    fields: [
      { key: 'masterBoxQty', label: t('admin.compareview.string.l109_'), get: (p) => (p.masterBoxQty !== undefined && p.masterBoxQty !== null ? String(p.masterBoxQty) : '') },
      { key: 'masterBoxWeightKgs', label: t('admin.compareview.string.l110_kg'), get: (p) => (p.masterBoxWeightKgs !== undefined && p.masterBoxWeightKgs !== null ? String(p.masterBoxWeightKgs) : '') },
      { key: 'masterBoxLengthMm', label: t('admin.compareview.string.l111_mm'), get: (p) => (p.masterBoxLengthMm !== undefined && p.masterBoxLengthMm !== null ? String(p.masterBoxLengthMm) : '') },
      { key: 'masterBoxWidthMm', label: t('admin.compareview.string.l112_mm'), get: (p) => (p.masterBoxWidthMm !== undefined && p.masterBoxWidthMm !== null ? String(p.masterBoxWidthMm) : '') },
      { key: 'masterBoxHeightMm', label: t('admin.compareview.string.l113_mm'), get: (p) => (p.masterBoxHeightMm !== undefined && p.masterBoxHeightMm !== null ? String(p.masterBoxHeightMm) : '') }
    ]
  },
  {
    name: t('admin.compareview.string.l117_crossref'),
    fields: [
      { key: 'crossReferences', label: t('admin.compareview.string.l119_oem'), get: (p) => xrefSummary(p.crossReferences) },
      { key: 'machineApplications', label: t('admin.compareview.string.l120_'), get: (p) => machineSummary(p.machineApplications) }
    ]
  }
]

// ===== 状态 =====
const loading = ref(false)
const error = ref('')
const products = ref<ProductDetail[]>([])
// addById 输入框
const newIdInput = ref('')

// ===== 工具: 列顺序持久化 =====
//   products 内部按 reorderApplied 后的顺序展示, 但原始 selectedIds 不变
//   这里直接对 products 数组重排
function loadSavedOrder(ids: number[]): number[] {
  try {
    const raw = localStorage.getItem(ORDER_KEY)
    if (!raw) return ids
    const saved = JSON.parse(raw) as { id: number; order: number }[]
    const orderMap = new Map(saved.map((s) => [s.id, s.order]))
    // 按保存的 order 升序排, 未保存的放最后
    return [...ids].sort((a, b) => {
      const oa = orderMap.get(a)
      const ob = orderMap.get(b)
      if (oa === undefined && ob === undefined) return 0
      if (oa === undefined) return 1
      if (ob === undefined) return -1
      return oa - ob
    })
  } catch {
    return ids
  }
}

function saveOrder() {
  try {
    const data = products.value.map((p, i) => ({ id: p.id, order: i }))
    localStorage.setItem(ORDER_KEY, JSON.stringify(data))
  } catch {}
}

function persistUrlOrder() {
  // 同步到 URL (供分享/刷新)
  const ids = products.value.map((p) => p.id).join(',')
  router.replace({ path: '/admin/compare', query: { ids } })
}

// ===== 加载产品 =====
async function loadByIds(ids: number[]) {
  if (ids.length === 0) {
    products.value = []
    return
  }
  loading.value = true
  error.value = ''
  try {
    // 用批量 API, 限 6 个
    const capped = ids.slice(0, MAX_COMPARE)
    const data = await adminProductApi.compare(capped)
    // 按 capped 顺序对齐 (后端可能按 id 排序)
    const map = new Map(data.items.map((p) => [p.id, p]))
    products.value = capped.map((id) => map.get(id)).filter((p): p is ProductDetail => !!p)
    saveOrder()
  } catch (e: any) {
    error.value = e?.message || t('admin.compareview.string.l185_')
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
  let ids = parseIdsFromQuery()
  if (ids.length > 0) {
    ids = loadSavedOrder(ids)
    await loadByIds(ids)
  }
})

// 路由变化时 (如刷新/分享链接) 重新加载
watch(
  () => route.query.ids,
  async (newIds) => {
    if (typeof newIds === 'string' && newIds) {
      const ids = loadSavedOrder(parseIdsFromQuery())
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
  saveOrder()
  persistUrlOrder()
}

function moveRight(idx: number) {
  if (idx >= products.value.length - 1) return
  const arr = [...products.value]
  ;[arr[idx], arr[idx + 1]] = [arr[idx + 1], arr[idx]]
  products.value = arr
  saveOrder()
  persistUrlOrder()
}

function removeProduct(idx: number) {
  products.value = products.value.filter((_, i) => i !== idx)
  saveOrder()
  persistUrlOrder()
  ElMessage.success(t('admin.compareview.success.l246_'))
}

// ===== 加入产品 =====
async function addProductById() {
  const id = parseInt(newIdInput.value.trim(), 10)
  if (!id || isNaN(id)) {
    ElMessage.warning(t('admin.compareview.warning.l253_id'))
    return
  }
  if (products.value.some((p) => p.id === id)) {
    ElMessage.warning(t('admin.compareview.warning.l257_'))
    return
  }
  if (products.value.length >= MAX_COMPARE) {
    ElMessage.warning(t('admin.compareview.warning.l262_max', { max: MAX_COMPARE }))
    return
  }
  loading.value = true
  try {
    const p = await adminProductApi.get(id)
    products.value = [...products.value, p]
    newIdInput.value = ''
    saveOrder()
    persistUrlOrder()
    ElMessage.success(t('admin.compareview.success.l272_added', { oem: p.oemNoDisplay }))
  } catch (e: any) {
    ElMessage.error(e?.message || t('admin.compareview.error.l273_'))
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
    <!-- 工具条 -->
    <div class="compare-toolbar flex items-center gap-2 mb-3 flex-wrap">
      <span class="text-sm font-medium">产品对比</span>
      <span class="text-xs text-muted">最多 {{ MAX_COMPARE }} 个</span>
      <div class="flex-1" />
      <el-input
        v-model="newIdInput"
        :placeholder="t('admin.compareview.placeholder.l329_id')"
        size="small"
        style="width: 180px"
        @keyup.enter="addProductById"
      />
      <el-button size="small" :loading="loading" @click="addProductById">加入</el-button>
      <el-checkbox v-model="onlyDiff" size="small">仅看差异</el-checkbox>
      <el-button size="small" @click="clearAll" :disabled="products.length === 0">清空</el-button>
      <el-button size="small" @click="doPrint" :disabled="products.length === 0">打印</el-button>
    </div>

    <!-- 空状态 -->
    <div v-if="products.length === 0 && !loading" class="py-12 text-center text-muted hairline">
      <div>暂无对比产品</div>
      <div class="text-xs mt-2">从产品列表勾选 2-6 个 → 批量对比, 或在 URL 加 <code>?ids=1,2,3</code></div>
    </div>

    <!-- 加载中 -->
    <div v-if="loading && products.length === 0" class="py-12 text-center text-muted">加载中...</div>

    <!-- 对比表格 -->
    <div v-if="products.length > 0" class="compare-grid-wrap hairline">
      <div
        class="compare-grid"
        :style="{ gridTemplateColumns: `200px repeat(${products.length}, minmax(0, 1fr))` }"
      >
        <!-- 表头: 字段名 + 产品列 -->
        <div class="compare-header-cell field-name-cell sticky-left">字段</div>
        <div
          v-for="(p, idx) in products"
          :key="p.id"
          class="compare-header-cell product-cell"
        >
          <div class="flex items-start justify-between gap-1">
            <div class="flex-1 min-w-0">
              <div class="font-medium text-sm truncate" :title="p.oemNoDisplay">{{ p.oemNoDisplay }}</div>
              <div class="text-xs text-muted truncate" :title="p.oem2 || ''">{{ p.oem2 || '—' }}</div>
            </div>
            <div class="flex flex-col gap-0.5 no-print">
              <el-button
                size="small"
                text
                :disabled="idx === 0"
                @click="moveLeft(idx)"
                :title="t('admin.compareview.title.l373_')"
                style="padding: 0 2px; height: 16px; line-height: 16px"
              >‹</el-button>
              <el-button
                size="small"
                text
                :disabled="idx === products.length - 1"
                @click="moveRight(idx)"
                :title="t('admin.compareview.title.l381_')"
                style="padding: 0 2px; height: 16px; line-height: 16px"
              >›</el-button>
            </div>
            <el-button
              size="small"
              text
              class="no-print"
              @click="removeProduct(idx)"
              :title="t('admin.compareview.title.l390_')"
              style="padding: 0 4px; height: 18px; line-height: 18px; color: #d00"
            >×</el-button>
          </div>
        </div>

        <!-- 字段行: 按组渲染 -->
        <template v-for="group in visibleGroups" :key="group.name">
          <!-- 组名行 (跨所有列) -->
          <div
            class="group-name-cell hairline-t"
            :style="{ gridColumn: `1 / span ${products.length + 1}` }"
          >
            {{ group.name }}
          </div>

          <!-- 字段行 -->
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
/* P3.5 (Task 12): 6 列 grid 对比布局 — Musk 极简风, 无阴影, 1px 边框, 纯黑/白 + 单一强调色 */
.compare-grid-wrap {
  overflow-x: auto;
  background: #fff;
}

.compare-grid {
  display: grid;
  /* 200px 字段名列 + N 个产品列等分 */
  min-width: 100%;
}

.compare-header-cell {
  padding: 8px 10px;
  background: #fafafa;
  border-bottom: 1px solid var(--color-border);
  font-size: 13px;
  min-height: 56px;
  position: sticky;
  top: 0;
  z-index: 2;
}

.field-name-cell {
  padding: 6px 10px;
  background: #fafafa;
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
  background: #111;
  color: #fff;
  font-size: 12px;
  font-weight: 500;
  letter-spacing: 0.5px;
  border-bottom: 1px solid var(--color-border);
  border-top: 1px solid var(--color-border);
}

.data-cell {
  padding: 6px 10px;
  border-bottom: 1px solid var(--color-border);
  border-right: 1px solid var(--color-border);
  font-size: 13px;
  word-break: break-word;
  min-height: 32px;
  display: flex;
  align-items: center;
}

.data-cell:last-child {
  border-right: none;
}

/* 差异高亮: 全部相等灰底, 有差异黄底 */
.data-cell.same {
  background: #f5f5f5;
}

.data-cell.diff {
  background: #fffbe6;
}

.data-cell.empty {
  color: #999;
}

.product-cell {
  border-right: 1px solid var(--color-border);
}
.product-cell:last-child {
  border-right: none;
}

/* 打印优化: 隐藏工具条, 保留对比表格, 强制背景色 */
@media print {
  .compare-toolbar,
  .el-pagination,
  .el-button {
    display: none !important;
  }
  .no-print {
    display: none !important;
  }
  .compare-grid {
    /* 打印时保持 grid 等分 */
    grid-template-columns: 200px repeat(6, 1fr) !important;
  }
  .field-name-cell,
  .compare-header-cell,
  .group-name-cell {
    position: static;
  }
  .data-cell.same {
    background: #f5f5f5 !important;
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
  }
  .data-cell.diff {
    background: #fffbe6 !important;
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
  }
  .compare-root {
    max-width: 100% !important;
  }
  body {
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
  }
}
</style>
