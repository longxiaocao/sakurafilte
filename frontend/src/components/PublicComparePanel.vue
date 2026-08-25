<script setup lang="ts">
// 🔧 fix(审查): 产品对比面板组件 (从 PublicCompareView 抽取, 内嵌到高级搜索页)
//   用户反馈: 独立对比页与高级搜索页重复且无引导 — 移除独立页, 对比内嵌搜索页
//   对比表格复用 AdminCompareView 6 字段组布局 (Musk 极简风, CSS 变量适配主题)
import { computed } from 'vue'
import { buildProductUrl } from '@/utils/build-product-url'
import type { PublicProductDetail, PublicXrefInfo, MachineAppInfo } from '@/api/types'

const props = defineProps<{ products: PublicProductDetail[] }>()
const emit = defineEmits<{
  (e: 'moveLeft', idx: number): void
  (e: 'moveRight', idx: number): void
  (e: 'remove', idx: number): void
}>()

const xrefSummary = (list: PublicXrefInfo[] | undefined) => {
  if (!list || list.length === 0) return ''
  const head = list.slice(0, 3).map((x) => `${x.oemBrand || ''} ${x.oemNo3 || ''}`.trim()).filter(Boolean)
  return head.length === 0 ? '' : head.join('; ') + (list.length > 3 ? ` (+${list.length - 3})` : '')
}

const machineSummary = (list: MachineAppInfo[] | undefined) => {
  if (!list || list.length === 0) return ''
  const head = list.slice(0, 2).map((m) => `${m.machineBrand || ''} ${m.machineModel || ''}`.trim()).filter(Boolean)
  return head.length === 0 ? '' : head.join('; ') + (list.length > 2 ? ` (+${list.length - 2})` : '')
}

interface FieldDef {
  key: string
  label: string
  get: (p: PublicProductDetail) => string
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

function valueOf(p: PublicProductDetail, field: FieldDef): string {
  return field.get(p)
}

// 差异高亮: 该行所有产品值相同时不高亮, 有差异时高亮
function cellClass(values: string[]) {
  const distinct = new Set(values.map((v) => v || '—'))
  return distinct.size > 1 ? 'diff' : ''
}

const visibleGroups = computed(() => fieldGroups)
</script>

<template>
  <div class="compare-grid-wrap hairline">
    <div
      class="compare-grid"
      :style="{ gridTemplateColumns: `140px repeat(${products.length}, minmax(150px, 1fr))` }"
    >
      <div class="compare-header-cell field-name-cell sticky-left">字段</div>
      <div v-for="(p, idx) in products" :key="p.id" class="compare-header-cell product-cell">
        <div class="flex items-start justify-between gap-1">
          <div class="flex-1 min-w-0">
            <div class="font-medium text-sm truncate" :title="p.oemNoDisplay">
              <a
                :href="buildProductUrl({
                  productName1: p.productName1,
                  productName2: p.productName2,
                  oemBrand: p.crossReferences?.[0]?.oemBrand,
                  oemNo3: p.crossReferences?.[0]?.oemNo3,
                  oemNoDisplay: p.oemNoDisplay
                })"
                class="hover:underline"
              >{{ p.oemNoDisplay }}</a>
            </div>
            <div class="text-xs text-muted truncate" :title="p.oem2 || ''">{{ p.oem2 || '—' }}</div>
          </div>
          <div class="flex flex-col gap-0.5 no-print">
            <el-button size="small" text :disabled="idx === 0" @click="emit('moveLeft', idx)" title="左移" style="padding: 0 2px; height: 16px" aria-label="左移">‹</el-button>
            <el-button size="small" text :disabled="idx === products.length - 1" @click="emit('moveRight', idx)" title="右移" style="padding: 0 2px; height: 16px" aria-label="右移">›</el-button>
          </div>
          <el-button size="small" text class="no-print" @click="emit('remove', idx)" title="移除该列" aria-label="移除该列" style="padding: 0 4px; height: 18px; color: #d00">×</el-button>
        </div>
      </div>

      <template v-for="group in visibleGroups" :key="group.name">
        <div class="group-name-cell hairline-t" :style="{ gridColumn: `1 / span ${products.length + 1}` }">
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
</template>

<style scoped>
/* 复用 AdminCompareView 样式 — Musk 极简风, 无阴影, 1px 边框, 颜色走 CSS 变量 (适配浅/深色) */
.compare-grid-wrap { overflow-x: auto; }
.compare-grid { display: grid; min-width: 100%; }
.compare-header-cell { padding: 8px; font-size: 12px; font-weight: 500; border-bottom: 1px solid var(--color-border); }
.field-name-cell { color: var(--color-text-muted); background: var(--color-bg-soft); position: sticky; left: 0; z-index: 1; }
.product-cell { background: var(--color-bg-elevated, var(--color-bg)); }
.group-name-cell { grid-column: 1 / -1; padding: 6px 8px; font-size: 12px; font-weight: 500; color: var(--color-text-muted); background: var(--color-bg-hover); }
.data-cell { padding: 6px 8px; font-size: 12px; border-bottom: 1px solid var(--color-border); word-break: break-word; }
.data-cell.diff { background: rgba(64, 158, 255, 0.08); color: var(--color-accent); font-weight: 500; }
.sticky-left { position: sticky; left: 0; }
</style>
