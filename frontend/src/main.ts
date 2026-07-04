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
