/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_ERROR_REPORT_URL?: string
  readonly VITE_HOOK_CONSOLE_ERROR?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<{}, {}, any>
  export default component
}
