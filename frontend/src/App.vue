<script setup lang="ts">
// 根组件 (P2.8 已集成 ErrorBoundary + P2.6 i18n 响应式)
//   - ErrorBoundary 包裹 RouterView 捕获子组件渲染错误, 避免白屏
//   - 错误自动持久化到 localStorage (最近 20 条)
//   - P2.6: ElConfigProvider 响应式跟随 i18n locale 切换 Element Plus 语言 (无需刷新)
import { computed } from 'vue'
import { RouterView } from 'vue-router'
import AppHeader from './components/AppHeader.vue'
import ErrorBoundary from './components/ErrorBoundary.vue'
import { useI18n } from 'vue-i18n'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import en from 'element-plus/es/locale/lang/en'

const { locale } = useI18n()
// 响应式映射: i18n locale → Element Plus locale
const elLocale = computed(() => (locale.value === 'en-US' ? en : zhCn))
</script>

<template>
  <el-config-provider :locale="elLocale">
    <div class="h-full flex flex-col">
      <AppHeader />
      <main class="flex-1 overflow-auto" role="main">
        <ErrorBoundary>
          <RouterView />
        </ErrorBoundary>
      </main>
    </div>
  </el-config-provider>
</template>
