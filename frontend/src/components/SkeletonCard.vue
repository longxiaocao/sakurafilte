<script setup lang="ts">
// 骨架屏组件 (P1.2)
//   - 用途: 数据加载期间替代简单 spinner, 提升感知性能
//   - 设计: 1px hairline + 脉冲动画 (Vercel/Linear 风格)
//   - 三种形态: 卡片骨架 / 详情页骨架 / 表格行骨架
//   - 颜色: 使用 --color-bg-hover, 跟随主题切换
import { computed } from 'vue'

interface Props {
  // 形态: card 单卡片 / detail 详情页 / table-row 表格行 / list 列表
  variant?: 'card' | 'detail' | 'table-row' | 'list'
  // 数量 (仅 list/table-row 生效)
  count?: number
  // 高度 (仅 card/list 单项生效)
  height?: string
}

const props = withDefaults(defineProps<Props>(), {
  variant: 'card',
  count: 3,
  height: '120px'
})

const items = computed(() => Array.from({ length: props.count }, (_, i) => i))
</script>

<template>
  <!-- 详情页骨架: 左图 5/12 + 右关键信息 7/12 -->
  <div v-if="variant === 'detail'" class="skeleton-detail" role="status" aria-label="详情加载中" aria-live="polite">
    <div class="grid grid-cols-1 lg:grid-cols-12 gap-8 lg:gap-12 mb-12">
      <!-- 左侧图片区 -->
      <div class="lg:col-span-5">
        <div class="skeleton-box aspect-square mb-3" />
        <div class="grid grid-cols-6 gap-2">
          <div v-for="i in 6" :key="i" class="skeleton-box aspect-square" />
        </div>
      </div>
      <!-- 右侧信息区 -->
      <div class="lg:col-span-7">
        <div class="skeleton-box h-8 w-3/4 mb-4" />
        <div class="skeleton-box h-4 w-1/3 mb-6" />
        <div class="grid grid-cols-2 gap-4 mb-6">
          <div v-for="i in 4" :key="i" class="skeleton-box h-20" />
        </div>
        <div class="skeleton-box h-4 w-full mb-2" />
        <div class="skeleton-box h-4 w-5/6 mb-2" />
        <div class="skeleton-box h-4 w-2/3" />
      </div>
    </div>
    <span class="sr-only">正在加载产品详情...</span>
  </div>

  <!-- 表格行骨架 -->
  <div
    v-else-if="variant === 'table-row'"
    role="status"
    aria-label="表格加载中"
    aria-live="polite"
  >
    <div v-for="i in items" :key="i" class="skeleton-row flex items-center gap-3 px-3 py-2">
      <div class="skeleton-box h-4 w-12" />
      <div class="skeleton-box h-4 w-32" />
      <div class="skeleton-box h-4 flex-1" />
      <div class="skeleton-box h-4 w-20" />
      <div class="skeleton-box h-4 w-16" />
    </div>
    <span class="sr-only">正在加载列表...</span>
  </div>

  <!-- 列表骨架 -->
  <div
    v-else-if="variant === 'list'"
    class="space-y-3"
    role="status"
    aria-label="列表加载中"
    aria-live="polite"
  >
    <div v-for="i in items" :key="i" class="skeleton-box" :style="{ height }" />
    <span class="sr-only">正在加载列表...</span>
  </div>

  <!-- 单卡片骨架 (默认) -->
  <div
    v-else
    class="skeleton-card p-4 hairline"
    role="status"
    aria-label="内容加载中"
    aria-live="polite"
  >
    <div class="skeleton-box mb-3" :style="{ height }" />
    <div class="skeleton-box h-4 w-2/3 mb-2" />
    <div class="skeleton-box h-3 w-1/2" />
    <span class="sr-only">正在加载内容...</span>
  </div>
</template>

<style scoped>
/* 骨架块基础样式: 使用 bg-hover 颜色 + 脉冲动画 */
.skeleton-box {
  background: var(--color-bg-hover);
  border-radius: 0;
  position: relative;
  overflow: hidden;
  animation: skeleton-pulse 1.6s cubic-bezier(0.4, 0, 0.6, 1) infinite;
}

.skeleton-row {
  border-bottom: 1px solid var(--color-border);
}

/* 脉冲动画: 亮度 0.6 → 1.0 → 0.6 */
@keyframes skeleton-pulse {
  0%, 100% {
    opacity: 0.6;
  }
  50% {
    opacity: 1;
  }
}

/* 屏幕阅读器专用 (视觉隐藏但 SR 可读) */
.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

/* 尊重 prefers-reduced-motion: 关闭动画 */
@media (prefers-reduced-motion: reduce) {
  .skeleton-box {
    animation: none;
    opacity: 0.8;
  }
}
</style>
