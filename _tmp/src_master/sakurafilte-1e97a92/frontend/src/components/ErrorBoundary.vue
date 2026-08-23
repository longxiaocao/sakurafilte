<script setup lang="ts">
// 错误边界组件 (P2.8)
//   - 使用 onErrorCaptured 钩子捕获子组件树渲染错误
//   - 提供友好降级 UI (重试 / 复制错误 / 联系管理员)
//   - 错误日志持久化到 localStorage 供后续排查
//   - 全局注册: main.ts 中 <ErrorBoundary> 包裹 <RouterView>
import { ref, onErrorCaptured, onUnmounted } from 'vue'

interface ErrorInfo {
  message: string
  stack: string
  timestamp: string
  url: string
}

const error = ref<ErrorInfo | null>(null)
const copied = ref(false)
// P2-7 修复 v2: 保存 copyError 的 setTimeout 引用, 组件卸载时清理, 避免对已销毁 ref 写入
let copyResetTimer: ReturnType<typeof setTimeout> | null = null

onErrorCaptured((err: any) => {
  const info: ErrorInfo = {
    message: err?.message || String(err),
    stack: err?.stack || '',
    timestamp: new Date().toISOString(),
    url: window.location.href
  }
  error.value = info

  // 持久化到 localStorage (最多保留最近 20 条)
  try {
    const key = 'sakura_error_log'
    const list = JSON.parse(localStorage.getItem(key) || '[]')
    list.unshift(info)
    localStorage.setItem(key, JSON.stringify(list.slice(0, 20)))
  } catch {
    // localStorage 不可用时静默
  }

  // 阻止错误继续向上冒泡 (避免整个应用白屏)
  return false
})

function retry() {
  error.value = null
  copied.value = false
  // 触发视图刷新: 通过 v-if 重新挂载子组件树
  reloadKey.value++
}

const reloadKey = ref(0)

function copyError() {
  if (!error.value) return
  const text = `[SakuraFilter Error]\n时间: ${error.value.timestamp}\nURL: ${error.value.url}\n消息: ${error.value.message}\n堆栈:\n${error.value.stack}`
  navigator.clipboard?.writeText(text).then(() => {
    copied.value = true
    // P2-7 修复 v2: 保存 timer 引用, 卸载时清理
    if (copyResetTimer !== null) clearTimeout(copyResetTimer)
    copyResetTimer = setTimeout(() => (copied.value = false), 2000)
  })
}

function fullReload() {
  window.location.reload()
}

// P2-7 修复 v2: 组件卸载时清理 copyResetTimer, 避免对已销毁 ref 写入
onUnmounted(() => {
  if (copyResetTimer !== null) {
    clearTimeout(copyResetTimer)
    copyResetTimer = null
  }
})
</script>

<template>
  <div v-if="error" class="error-boundary min-h-screen flex items-center justify-center p-4">
    <div
      class="error-card w-full max-w-md hairline p-8 bg-[var(--color-bg-elevated)]"
      role="alert"
      aria-live="assertive"
    >
      <div class="text-center mb-6">
        <div class="inline-flex items-center justify-center w-12 h-12 hairline mb-3">
          <el-icon class="text-2xl text-red-600" aria-hidden="true"><WarningFilled /></el-icon>
        </div>
        <h1 class="text-xl font-medium tracking-tight mb-1">页面加载失败</h1>
        <p class="text-sm text-muted">系统遇到了意外错误, 可尝试以下操作</p>
      </div>

      <el-alert
        type="error"
        :closable="false"
        class="mb-4"
        :title="error.message"
      />

      <details class="mb-4 text-xs">
        <summary class="cursor-pointer text-muted hover:text-[var(--color-text)]">查看技术详情</summary>
        <pre class="mt-2 p-2 hairline bg-[var(--color-bg-hover)] overflow-auto max-h-40 whitespace-pre-wrap">{{ error.stack }}</pre>
      </details>

      <div class="flex gap-2">
        <el-button type="primary" class="flex-1" @click="retry">重试</el-button>
        <el-button class="flex-1" @click="copyError">
          {{ copied ? '已复制' : '复制错误' }}
        </el-button>
        <el-button class="flex-1" @click="fullReload">刷新页面</el-button>
      </div>

      <div class="mt-4 text-center text-xs text-muted">
        时间: {{ error.timestamp }} · 如持续出现请联系管理员
      </div>
    </div>
  </div>
  <slot v-else :key="reloadKey" />
</template>

<style scoped>
.error-card {
  border-radius: 0;
}
</style>
