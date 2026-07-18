/// <reference types="vite/client" />

// V24-F44 (spec F3-13 修复方案 4): build flag 标记 API 版本
//   vite.config.ts define 注入, 构建时替换为 package.json version 字符串
//   http.ts 请求拦截器读取后写入 X-Client-Version 头
declare const __API_VERSION__: string

interface ImportMetaEnv {
  readonly VITE_ERROR_REPORT_URL?: string
  readonly VITE_HOOK_CONSOLE_ERROR?: string
  // V2 Task V17-3.4: 开放重定向白名单主机 (逗号分隔,如 "localhost,127.0.0.1")
  readonly VITE_SAFE_REDIRECT_HOSTS?: string
  // V24-F34 (spec Task 4.5.27): 聚合搜索 API 404 降级开关
  //   "true" = 允许降级到旧 API (dev 默认)
  //   "false" / 未设置 = 404 时直接抛错 (prod 默认)
  readonly VITE_ENABLE_LEGACY_FALLBACK?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<{}, {}, any>
  export default component
}
