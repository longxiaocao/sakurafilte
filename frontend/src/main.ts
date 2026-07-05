import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import * as ElementPlusIconsVue from '@element-plus/icons-vue'

import App from './App.vue'
import router from './router'
import { installPerfInterceptor } from './utils/perf'
import i18n from './i18n'
import './styles/index.css'

// P5.5: 启动前端性能埋点 (axios 拦截器, 批量上报到 /api/perf/ingest)
installPerfInterceptor()

const app = createApp(App)

// 全局注册 Element Plus 图标 (Musk 风格简洁, 不滥用)
for (const [key, component] of Object.entries(ElementPlusIconsVue)) {
  app.component(key, component as any)
}

app.use(createPinia())
app.use(router)
app.use(i18n)
// P2.6: Element Plus locale 通过 App.vue 的 ElConfigProvider 响应式切换, 无需在此固定
app.use(ElementPlus)

app.mount('#app')

// A11y: 给所有 el-table 可滚动区域加 tabindex=0, 满足 WCAG scrollable-region-focusable
//   axe-core 检查的是 .el-scrollbar__wrap (真正的滚动容器), 必须在该元素上设 tabindex
//   给 el-pagination 内部的 el-select (每页条数) 加 aria-label, 满足 WCAG label
//   给 el-switch 内部的 input 加 aria-label (默认无 label, 触发 axe 'label' violation)
//   监听动态渲染: 用 MutationObserver 兜底
function enhanceA11yOnDom() {
  // 1) el-table 可滚动区域: tabindex=0
  document.querySelectorAll<HTMLElement>('.el-table__body-wrapper .el-scrollbar__wrap').forEach((el) => {
    if (el.getAttribute('tabindex') !== '0') {
      el.setAttribute('tabindex', '0')
      el.setAttribute('aria-label', '可滚动表格区域, 使用方向键浏览')
      el.style.outline = 'none'
    }
  })

  // 2) el-pagination 内部 el-select: 选每页条数 (默认无 label)
  //   Element Plus 渲染为 .el-pagination .el-pagination__sizes .el-select input
  document.querySelectorAll<HTMLInputElement>('.el-pagination .el-pagination__sizes .el-select input').forEach((input) => {
    if (!input.getAttribute('aria-label')) {
      input.setAttribute('aria-label', '每页显示条数')
    }
  })

  // 3) el-switch 内部 input: 默认无 label
  //   aria-label 缺省时, 沿用外层 switch 的 active-text/inactive-text 拼接
  //   若外层也无文字, 兜底为 '开关'
  document.querySelectorAll<HTMLInputElement>('.el-switch__input').forEach((input) => {
    if (!input.getAttribute('aria-label')) {
      const wrap = input.closest('.el-switch') as HTMLElement | null
      const text = (wrap?.querySelector('.el-switch__label')?.textContent || '').trim()
      input.setAttribute('aria-label', text || '开关')
    }
  })
}
function makeTablesKeyboardAccessible() {
  const init = () => enhanceA11yOnDom()
  init()
  const obs = new MutationObserver(() => init())
  obs.observe(document.body, { childList: true, subtree: true })
}
if (typeof window !== 'undefined') {
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', makeTablesKeyboardAccessible)
  } else {
    makeTablesKeyboardAccessible()
  }
}
