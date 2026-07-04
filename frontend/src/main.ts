import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import en from 'element-plus/es/locale/lang/en'
import * as ElementPlusIconsVue from '@element-plus/icons-vue'

import App from './App.vue'
import router from './router'
import { installPerfInterceptor } from './utils/perf'
import i18n, { setLocale } from './i18n'
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

// P2.6: Element Plus locale 跟随 i18n 语言切换
//   - 中文: zhCn, 英文: en
//   - 通过 watch locale 动态切换 (Element Plus 不支持响应式, 需重挂载)
const currentLocale = i18n.global.locale.value
app.use(ElementPlus, { locale: currentLocale === 'en-US' ? en : zhCn })

// 暴露 setLocale 给全局 (便于切换语言时同步 Element Plus locale)
;(window as any).__setSakuraLocale = (locale: string) => {
  setLocale(locale)
  // 提示用户刷新以应用 Element Plus locale 变更 (简化实现)
  console.info(`[i18n] locale changed to ${locale}, refresh to apply Element Plus locale`)
}

app.mount('#app')
