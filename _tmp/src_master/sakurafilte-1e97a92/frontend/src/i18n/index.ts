/**
 * 国际化配置 (P2.6)
 *   - vue-i18n 9.x Composition API 模式
 *   - 默认语言: zh-CN (跟随浏览器语言自动检测)
 *   - 持久化: localStorage key 'sakura_locale'
 *   - 切换: 通过 useI18n() 的 locale.value 切换
 *
 * 用法:
 *   <script setup>
 *   import { useI18n } from 'vue-i18n'
 *   const { t } = useI18n()
 *   </script>
 *   <template>{{ t('common.search') }}</template>
 */
import { createI18n } from 'vue-i18n'
import zhCN from './locales/zh-CN'
import enUS from './locales/en-US'

// 检测用户偏好语言: localStorage > 浏览器语言 > 默认中文
function detectLocale(): string {
  const saved = localStorage.getItem('sakura_locale')
  if (saved && ['zh-CN', 'en-US'].includes(saved)) return saved

  const browser = navigator.language
  if (browser.startsWith('en')) return 'en-US'
  return 'zh-CN'  // 默认中文
}

const i18n = createI18n({
  legacy: false,                    // Composition API 模式
  locale: detectLocale(),           // 当前语言
  fallbackLocale: 'zh-CN',          // 回退语言
  messages: {
    'zh-CN': zhCN,
    'en-US': enUS
  }
})

// 切换语言并持久化
export function setLocale(locale: string) {
  if (!['zh-CN', 'en-US'].includes(locale)) return
  ;(i18n.global.locale as any).value = locale as 'zh-CN' | 'en-US'
  localStorage.setItem('sakura_locale', locale)
  // 同步 HTML lang 属性 (无障碍 + SEO)
  document.documentElement.lang = locale
}

// 初始化 HTML lang
document.documentElement.lang = i18n.global.locale.value

export default i18n
