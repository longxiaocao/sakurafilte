<script setup lang="ts">
// Day 9.8: 取消原因 reason_code 饼图 (纯 SVG 实现, 零依赖)
//   - 数据来自 /api/admin/etl/history/aggregate
//   - 5 枚举 + LEGACY (旧数据无 reason_code) 各分配颜色
//   - 鼠标悬停高亮 + tooltip
import { computed, ref } from 'vue'
import type { EtlReasonCodeAggregate } from '@/api/types'

// Day 9.8: 显式声明组件名 (Vue 3.3+), Vue DevTools + 错误堆栈能正确显示
defineOptions({ name: 'EtlReasonCodePie' })

const props = defineProps<{
  data: EtlReasonCodeAggregate | null
  size?: number  // 直径 px, 默认 180
}>()

const SIZE = computed(() => props.size ?? 180)
const RADIUS = computed(() => SIZE.value / 2)
const STROKE = computed(() => SIZE.value / 7)  // 环宽 ≈ 直径/7

// Day 9.8: 颜色映射 (Musk-style 极简, 灰阶 + 单一强调色)
//   USER_REQUEST (用户主动) — 蓝 #409eff (主色)
//   ADMIN_OVERRIDE (管理员) — 紫 #7c3aed
//   TIMEOUT (超时)         — 橙 #f59e0b
//   SYSTEM_SHUTDOWN        — 灰 #6b7280
//   OTHER                  — 浅灰 #d1d5db
//   LEGACY (旧数据)        — 暗灰 #9ca3af
const COLORS: Record<string, string> = {
  USER_REQUEST: '#409eff',
  ADMIN_OVERRIDE: '#7c3aed',
  TIMEOUT: '#f59e0b',
  SYSTEM_SHUTDOWN: '#6b7280',
  OTHER: '#d1d5db',
  LEGACY: '#9ca3af'
}

const LABEL: Record<string, string> = {
  USER_REQUEST: '用户主动',
  ADMIN_OVERRIDE: '管理员强制',
  TIMEOUT: '任务超时',
  SYSTEM_SHUTDOWN: '系统关闭',
  OTHER: '其他',
  LEGACY: '旧数据 (无码)'
}

// 悬停高亮 index
const hoverIdx = ref<number | null>(null)

// Day 9.8: 计算每段弧的 path d 属性 (圆环图, 中间镂空)
//   - 用 stroke-dasharray + stroke-dashoffset 在单圆上实现分段
//   - circumference = 2πr, 每段按比例切
//   - rotate 累加作为起始偏移
interface Segment {
  code: string
  count: number
  pct: number
  color: string
  label: string
  offset: number   // 起始位置 (相对圆周比例, 0~1)
  length: number   // 段长度比例, 0~1
}

const CIRCUMFERENCE = computed(() => 2 * Math.PI * RADIUS.value)

const segments = computed<Segment[]>(() => {
  if (!props.data || props.data.total === 0) return []
  let acc = 0
  return props.data.breakdown.map((b) => {
    const seg: Segment = {
      code: b.code,
      count: b.count,
      pct: b.pct,
      color: COLORS[b.code] || '#9ca3af',
      label: LABEL[b.code] || b.code,
      offset: acc,
      length: b.pct / 100
    }
    acc += seg.length
    return seg
  })
})

function dashStyle(seg: Segment) {
  // 留 1.5% 间隙让分段可见
  const visible = seg.length * CIRCUMFERENCE.value
  const gap = CIRCUMFERENCE.value * 0.015
  const segLen = Math.max(0, visible - gap)
  // stroke-dasharray: [段长, 间隙] — 用负 dashoffset 让段从 offset 处开始
  return {
    stroke: seg.color,
    'stroke-dasharray': `${segLen} ${CIRCUMFERENCE.value - segLen}`,
    'stroke-dashoffset': -seg.offset * CIRCUMFERENCE.value,
    opacity: hoverIdx.value === null || hoverIdx.value === segments.value.indexOf(seg) ? 1 : 0.35
  }
}

function colorFor(code: string): string {
  return COLORS[code] || '#9ca3af'
}
</script>

<template>
  <div class="etl-pie">
    <div class="pie-canvas">
      <svg :width="SIZE" :height="SIZE" :viewBox="`0 0 ${SIZE} ${SIZE}`">
        <!-- 背景圆 (灰色占位) -->
        <circle
          :cx="RADIUS"
          :cy="RADIUS"
          :r="RADIUS - STROKE / 2"
          fill="none"
          stroke="#f3f4f6"
          :stroke-width="STROKE"
        />
        <!-- 分段 -->
        <g v-for="(seg, i) in segments" :key="seg.code">
          <circle
            :cx="RADIUS"
            :cy="RADIUS"
            :r="RADIUS - STROKE / 2"
            fill="none"
            :stroke-width="STROKE"
            :stroke-dasharray="dashStyle(seg)['stroke-dasharray']"
            :stroke-dashoffset="dashStyle(seg)['stroke-dashoffset']"
            :stroke="dashStyle(seg)['stroke']"
            :opacity="dashStyle(seg).opacity"
            :transform="`rotate(-90 ${RADIUS} ${RADIUS})`"
            style="transition: opacity 0.15s"
            @mouseenter="hoverIdx = i"
            @mouseleave="hoverIdx = null"
          />
        </g>
        <!-- 中心数字 (总数) -->
        <text
          :x="RADIUS"
          :y="RADIUS - 6"
          text-anchor="middle"
          font-size="22"
          font-weight="600"
          fill="#111827"
        >
          {{ data?.total ?? 0 }}
        </text>
        <text
          :x="RADIUS"
          :y="RADIUS + 14"
          text-anchor="middle"
          font-size="11"
          fill="#6b7280"
        >
          总取消数
        </text>
      </svg>
    </div>
    <div class="pie-legend">
      <div
        v-for="(seg, i) in segments"
        :key="seg.code"
        class="legend-item"
        :class="{ 'is-hover': hoverIdx === i, 'is-dim': hoverIdx !== null && hoverIdx !== i }"
        @mouseenter="hoverIdx = i"
        @mouseleave="hoverIdx = null"
      >
        <span class="legend-dot" :style="{ background: colorFor(seg.code) }"></span>
        <span class="legend-label">{{ seg.label }}</span>
        <span class="legend-code">{{ seg.code }}</span>
        <span class="legend-count">{{ seg.count }} ({{ seg.pct }}%)</span>
      </div>
      <div v-if="!data || data.total === 0" class="legend-empty">
        暂无取消记录
      </div>
    </div>
  </div>
</template>

<style scoped>
.etl-pie {
  display: flex;
  align-items: center;
  gap: 24px;
  flex-wrap: wrap;
}
.pie-canvas {
  flex: 0 0 auto;
  line-height: 0;
}
.pie-legend {
  flex: 1 1 240px;
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 240px;
}
.legend-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 6px;
  font-size: 12px;
  border-radius: 3px;
  cursor: default;
  transition: background 0.15s, opacity 0.15s;
}
.legend-item:hover,
.legend-item.is-hover {
  background: #f9fafb;
}
.legend-item.is-dim {
  opacity: 0.5;
}
.legend-dot {
  width: 10px;
  height: 10px;
  border-radius: 2px;
  flex: 0 0 auto;
}
.legend-label {
  color: #111827;
  font-weight: 500;
  flex: 0 0 auto;
}
.legend-code {
  color: #4b5563;
  font-family: 'SF Mono', Consolas, monospace;
  font-size: 10px;
  flex: 0 0 auto;
}
.legend-count {
  margin-left: auto;
  color: #374151;
  font-variant-numeric: tabular-nums;
}
.legend-empty {
  color: #9ca3af;
  font-size: 12px;
  padding: 8px 0;
}
</style>
