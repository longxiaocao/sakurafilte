<script setup lang="ts">
// 404 路由兜底页 (catch-all)
//   - 用户访问不存在的路径时显示
//   - 提供返回首页/搜索的引导按钮
//   - 风格: Musk 极简 hairline + 居中卡片
import { useRouter } from 'vue-router'

const router = useRouter()

function goHome() {
  router.push('/search')
}

function goBack() {
  // 无历史记录时回退到首页
  if (window.history.length > 1) {
    router.back()
  } else {
    router.push('/search')
  }
}
</script>

<template>
  <div class="min-h-screen flex items-center justify-center p-4 bg-[var(--color-bg)]">
    <div
      class="w-full max-w-md hairline p-8 bg-[var(--color-bg-elevated)] text-center"
      role="alert"
      aria-live="assertive"
    >
      <div class="text-6xl font-bold tracking-tight mb-2 text-[var(--color-text)]">404</div>
      <h1 class="text-lg font-medium mb-2 text-[var(--color-text)]">页面不存在</h1>
      <p class="text-sm text-muted mb-6">
        您访问的页面可能已被删除、重命名, 或从未存在。
      </p>
      <div class="flex gap-2 justify-center">
        <el-button type="primary" @click="goHome">返回搜索</el-button>
        <el-button @click="goBack">返回上一页</el-button>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* 复用全局 hairline 与 CSS 变量, 无需额外样式 */
</style>
