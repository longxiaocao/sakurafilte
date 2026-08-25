<script setup lang="ts">
// V3(2026-08-25) 用户反馈: 加入对比后没有常驻浮动 N/6 控件, 客户没地方点"查看对比".
//   全局浮动对比栏: 右下角悬浮, 显示 "对比 N/6", 点击展开对比抽屉 (PublicComparePanel),
//   带清空按钮. 挂载于 App.vue, 所有页面可见 (聚合搜索/高级搜索/详情页/首页等).
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Collection, Delete } from '@element-plus/icons-vue'
import { useCompareStore, MAX_COMPARE } from '@/composables/useCompareStore'
import PublicComparePanel from '@/components/PublicComparePanel.vue'

const { t } = useI18n()
const store = useCompareStore()

const count = computed(() => store.state.ids.length)
const visible = computed(() => count.value > 0)

function onRemove(idx: number) {
  const p = store.state.products[idx]
  if (p) store.remove(p.id)
}
</script>

<template>
  <div v-if="visible" class="fixed bottom-6 right-6 z-50 flex flex-col items-end gap-2">
    <!-- 浮动徽标: 点击打开/关闭对比抽屉 -->
    <button
      class="flex items-center gap-2 px-4 py-2.5 rounded-full shadow-lg border border-[var(--color-border)] bg-[var(--color-bg-elevated)] hover:shadow-xl transition-shadow"
      @click="store.state.open ? store.close() : store.openCompare()"
      :aria-label="t('compare.floating.toggle')"
    >
      <el-icon class="text-[var(--color-accent)]"><Collection /></el-icon>
      <span class="text-sm font-medium">{{ t('compare.floating.label', { n: count, max: MAX_COMPARE }) }}</span>
      <span v-if="store.state.loading" class="text-xs text-[var(--color-text-muted)]">…</span>
    </button>
    <!-- 清空 -->
    <button
      class="flex items-center gap-1 px-3 py-1 rounded-full text-xs text-[var(--color-text-muted)] hover:text-[var(--color-danger)] transition-colors"
      @click="store.clear()"
      :aria-label="t('compare.floating.clear')"
    >
      <el-icon><Delete /></el-icon>
      {{ t('compare.floating.clear') }}
    </button>
  </div>

  <!-- 全局对比抽屉 (唯一入口: 浮动栏 / 搜索页摘要条 / 详情页 toast 均可触发 store.openCompare) -->
  <el-drawer
    :model-value="store.state.open"
    :title="t('compare.floating.drawer_title', { n: count })"
    size="min(96vw, 1280px)"
    destroy-on-close
    @update:model-value="store.close()"
  >
    <PublicComparePanel
      :products="store.state.products"
      @move-left="(i: number) => store.move(i, -1)"
      @move-right="(i: number) => store.move(i, 1)"
      @remove="onRemove"
    />
    <div v-if="store.state.ids.length === 0" class="py-12 text-center text-sm text-[var(--color-text-muted)]">
      {{ t('compare.floating.empty') }}
    </div>
  </el-drawer>
</template>
