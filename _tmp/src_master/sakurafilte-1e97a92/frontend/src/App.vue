<script setup lang="ts">
// 根组件 (P2.8 已集成 ErrorBoundary + P2.6 i18n 响应式)
//   - ErrorBoundary 包裹 RouterView 捕获子组件渲染错误, 避免白屏
//   - 错误自动持久化到 localStorage (最近 20 条)
//   - P2.6: ElConfigProvider 响应式跟随 i18n locale 切换 Element Plus 语言 (无需刷新)
//   - Day 14+: 顶部"跳到主内容"链接 (A11y 键盘用户必备)
//   - V24-F37 (spec F3-5/Task 4.5.14): mounted 检查 sessionStorage 'cursor-reset-toast'
//     显示一次性 ElMessage.warning (整页刷新后 ElMessage 提示消失的兜底方案)
import { computed, onMounted } from 'vue'
import { RouterView } from 'vue-router'
import { ElMessage } from 'element-plus'
import AppHeader from './components/AppHeader.vue'
import ErrorBoundary from './components/ErrorBoundary.vue'
import DragDropOverlay from './components/DragDropOverlay.vue'
import { useI18n } from 'vue-i18n'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import en from 'element-plus/es/locale/lang/en'

const { locale, t } = useI18n()
// 响应式映射: i18n locale → Element Plus locale
const elLocale = computed(() => (locale.value === 'en-US' ? en : zhCn))

// V24-F37 (spec F3-5/Task 4.5.14): App.vue mounted 检查 sessionStorage 显示一次性 cursor 重置 toast
//   WHY App.vue mounted: 整页刷新后 http.ts 的 ElMessage.warning 会消失
//   WHY 一次性: 读取后立即 removeItem, 避免重复提示
//   WHY sessionStorage 不用 safeStorage: cursor 重置是低频事件, 隐私模式降级丢失 toast 不影响功能
onMounted(() => {
  try {
    const cursorFlag = sessionStorage.getItem('cursor-reset-toast')
    if (cursorFlag) {
      sessionStorage.removeItem('cursor-reset-toast')
      const msg = cursorFlag === 'CURSOR_EXPIRED'
        ? '分页游标已过期,已重置到第 1 页'
        : '分页游标无效,已重置到第 1 页'
      // nextTick 确保 ElMessage 实例已挂载 (App.vue mounted 时子组件可能未完全就绪)
      //   注: ElMessage 是命令式 API, 不依赖 DOM 挂载, 可直接调用
      ElMessage.warning(msg)
    }
  } catch {
    // Safari 隐私模式 sessionStorage.getItem 可能抛错, 静默忽略
  }
})
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

<style>
/* 全局打印规则: 隐藏导航/工具栏/拖拽层, 保留主内容
   覆盖所有页面, 防止 .compare-toolbar / header 出现在打印输出中 */
@media print {
  /* 隐藏 AppHeader (页面顶部导航) */
  header.app-header {
    display: none !important;
  }
  /* 拖拽遮罩绝不允许出现在打印中 */
  .drag-drop-overlay {
    display: none !important;
  }
  /* 任何 .no-print 标记的元素 (按钮/工具栏/缩放图标等) */
  .no-print {
    display: none !important;
  }
}
</style>

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
