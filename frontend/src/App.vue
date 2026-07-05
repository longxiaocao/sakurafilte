<script setup lang="ts">
// 根组件 (P2.8 已集成 ErrorBoundary + P2.6 i18n 响应式)
//   - ErrorBoundary 包裹 RouterView 捕获子组件渲染错误, 避免白屏
//   - 错误自动持久化到 localStorage (最近 20 条)
//   - P2.6: ElConfigProvider 响应式跟随 i18n locale 切换 Element Plus 语言 (无需刷新)
//   - Day 14+: 顶部"跳到主内容"链接 (A11y 键盘用户必备)
import { computed } from 'vue'
import { RouterView } from 'vue-router'
import AppHeader from './components/AppHeader.vue'
import ErrorBoundary from './components/ErrorBoundary.vue'
import DragDropOverlay from './components/DragDropOverlay.vue'
import { useI18n } from 'vue-i18n'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import en from 'element-plus/es/locale/lang/en'

const { locale, t } = useI18n()
// 响应式映射: i18n locale → Element Plus locale
const elLocale = computed(() => (locale.value === 'en-US' ? en : zhCn))
</script>

<template>
  <el-config-provider :locale="elLocale">
    <!-- A11y 跳到主内容: 视觉隐藏, 键盘 Tab 第一焦点 -->
    <a href="#main-content" class="skip-to-content">{{ t('a11y.skipToContent') }}</a>
    <div class="h-full flex flex-col">
      <AppHeader />
      <main id="main-content" tabindex="-1" class="flex-1 overflow-auto" role="main">
        <ErrorBoundary>
          <RouterView />
        </ErrorBoundary>
      </main>
    </div>
    <!-- 全局拖拽反馈遮罩 (UX 偏好: 全窗口拖拽上传) -->
    <DragDropOverlay />
  </el-config-provider>
</template>

<style scoped>
/* A11y: 视觉隐藏但屏幕阅读器可读, 获得焦点时显形 */
.skip-to-content {
  position: absolute;
  top: 0;
  left: 0;
  padding: 8px 16px;
  background: var(--color-accent);
  color: #ffffff;
  text-decoration: none;
  font-size: 14px;
  font-weight: 500;
  z-index: 9999;
  transform: translateY(-100%);
  transition: transform 0.15s ease;
}
.skip-to-content:focus {
  transform: translateY(0);
  outline: 2px solid var(--color-bg);
  outline-offset: 2px;
}
</style>
