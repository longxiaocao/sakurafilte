<script setup lang="ts">
// DragDropOverlay — 全屏拖拽反馈遮罩
//   仅在 useGlobalDragDrop 的 isDragging 状态为 true 时显示
//   居中提示: "松开导入文件" + 文件类型
//   跟随 Musk 极简风: hairline 边框 + 单色强调
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useGlobalDragDrop, DEFAULT_ADMIN_ACCEPT } from '@/composables/useGlobalDragDrop'

const { isDragging, hint } = useGlobalDragDrop()
const route = useRoute()
// 🔧 fix(审查): 遮罩仅在白名单路由 (admin) 显示 — 原实现全局显示,
//   非 admin 页面 (如公开搜索) 拖文件时遮罩闪现但无回调可执行 (无意义打扰)
const visible = computed(() => isDragging.value && DEFAULT_ADMIN_ACCEPT(route.path))
</script>

<template>
  <Transition name="dragdrop">
    <div
      v-if="visible"
      class="fixed inset-0 z-[9998] flex items-center justify-center pointer-events-none"
      role="status"
      aria-live="polite"
      aria-label="拖拽文件中"
    >
      <div
        class="absolute inset-0 bg-[var(--color-bg)] opacity-70"
      />
      <div
        class="relative hairline bg-[var(--color-bg-elevated)] px-10 py-8 text-center max-w-md mx-4"
      >
        <el-icon class="text-5xl mb-3 text-[var(--color-accent)]" aria-hidden="true">
          <UploadFilled />
        </el-icon>
        <div class="text-lg font-medium mb-1">{{ hint }}</div>
        <div class="text-xs text-muted">
          支持 .xlsx / .xls / .jsonl / .json / .csv
        </div>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.dragdrop-enter-active,
.dragdrop-leave-active {
  transition: opacity 0.15s ease;
}
.dragdrop-enter-from,
.dragdrop-leave-to {
  opacity: 0;
}
</style>
