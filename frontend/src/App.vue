<script setup lang="ts">
// 根组件 (P2.8 已集成 ErrorBoundary)
//   - ErrorBoundary 包裹 RouterView 捕获子组件渲染错误, 避免白屏
//   - 错误自动持久化到 localStorage (最近 20 条)
import { RouterView } from 'vue-router'
import AppHeader from './components/AppHeader.vue'
import ErrorBoundary from './components/ErrorBoundary.vue'
</script>

<template>
  <div class="h-full flex flex-col">
    <AppHeader />
    <main class="flex-1 overflow-auto" role="main">
      <ErrorBoundary>
        <RouterView />
      </ErrorBoundary>
    </main>
  </div>
</template>
