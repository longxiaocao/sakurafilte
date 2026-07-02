// Day 10+ P5.3 主题切换 (Pinia store)
//   - light / dark 双主题
//   - localStorage 持久化 (key: sakura_theme)
//   - 切换时同步给 <html> 加 .dark class
//   - 系统主题检测 (prefers-color-scheme) 作为初始 fallback
//
// WHY Pinia: 跨组件共享 (AppHeader 切换, App.vue 监听, 后台所有页面 CSS 变量联动)
import { ref, watch } from 'vue'
import { defineStore } from 'pinia'

const STORAGE_KEY = 'sakura_theme'
export type ThemeMode = 'light' | 'dark'

function detectInitial(): ThemeMode {
  try {
    const saved = localStorage.getItem(STORAGE_KEY) as ThemeMode | null
    if (saved === 'light' || saved === 'dark') return saved
  } catch {
    // localStorage 不可用 (隐私模式) — 走系统检测
  }
  if (typeof window !== 'undefined' && window.matchMedia) {
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
  }
  return 'light'
}

export const useThemeStore = defineStore('theme', () => {
  const mode = ref<ThemeMode>(detectInitial())

  function applyToDocument(m: ThemeMode) {
    if (typeof document === 'undefined') return
    const html = document.documentElement
    if (m === 'dark') html.classList.add('dark')
    else html.classList.remove('dark')
  }

  // 初始化时立即应用
  applyToDocument(mode.value)

  watch(mode, (v) => {
    applyToDocument(v)
    try {
      localStorage.setItem(STORAGE_KEY, v)
    } catch {
      // ignore
    }
  })

  function toggle() {
    mode.value = mode.value === 'light' ? 'dark' : 'light'
  }

  function set(m: ThemeMode) {
    mode.value = m
  }

  return { mode, toggle, set }
})
