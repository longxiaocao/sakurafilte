// V2 Task 4.1.4 / 4.1.9 / 4.1.11 / 4.1.12 / 4.1.19 / 4.5: 产品详情页 Vue client mount 入口
//
// 设计:
//   - 非 hydration 模式: Vue createApp 直接 mount (清空挂载点内 SSR 兜底内容)
//   - SSR 内容放在独立 #seo-content 容器 (Vue mount 不影响 SEO 抓取)
//   - 通过 JSON 数据岛 #product-data 读取产品数据 (修复 F2-1, 不用 window.__PRODUCT__)
//   - safeMount 包裹 ErrorBoundary + try-catch (修复 F2-8/F3-9)
//   - 模块加载失败由 Detail.cshtml 内 window.addEventListener('error', ...) 兜底 (修复 F3-2)
//
// 构建产物: /assets/product-detail-client.js (Vite 多入口, Task 4.5.5)
//   子组件实现在 Task 4.5 (GalleryApp.vue / CompareApp.vue / InquiryApp.vue)

import { createApp, type App, type Component } from 'vue'
import { captureException } from '@/utils/errorMonitor'

// ============================================================================
// 类型定义 (与 frontend/src/api/types.ts ProductDetailDto 对齐)
// ============================================================================

interface ProductImageInfo {
  id: number
  productId: number
  slot: number
  imageKey: string
  imageUrl: string
  sizeBytes: number | null
  contentType: string | null
  width: number | null
  height: number | null
  isPrimary: boolean
  uploadedAt: string
  uploadedBy: string | null
  oemNo3: string | null
  imageRole: string | null
}

interface ProductData {
  // V2 Task 4.5: 加 id 字段, CompareApp 跳对比页需要 productId
  id: number
  mr1: string | null
  oemNoDisplay: string
  oem2: string | null
  productName1: string | null
  productName2: string | null
  type: string | null
  images: ProductImageInfo[] | null
  crossReferences: Array<{
    oemBrand: string | null
    oemNo3: string | null
    oem2: string | null
    sortOrder: number
    machineType: string | null
  }> | null
}

// ============================================================================
// V2 Task 4.1.12: safeMount — ErrorBoundary + try-catch 降级 UI
//   修复 F2-8: Vue 应用初始化失败时降级 UI, 不破坏页面其他部分
//   修复 F3-9: catch 内调 errorMonitor.captureException 上报
// ============================================================================

function safeMount(elementId: string, component: Component, props: Record<string, unknown>): void {
  const el = document.getElementById(elementId)
  if (!el) {
    console.warn(`[Detail] mount point #${elementId} not found`)
    return
  }

  try {
    // 标记已挂载, 防止 Detail.cshtml 内 window.addEventListener('error', ...) 误覆盖 (修复 F3-2 衍生)
    el.dataset.vueMounted = 'true'

    // 清空 SSR 兜底内容 (例如 gallery-app 内的 <img>)
    el.innerHTML = ''

    const app: App = createApp(component, props)
    app.mount(el)
  } catch (err) {
    // F2-8/F3-9: 降级 UI + 上报 errorMonitor
    el.innerHTML = '<div class="mount-fallback">交互模块加载失败, <button onclick="location.reload()">刷新重试</button></div>'
    captureException(err as Error, { tags: { mount: elementId } })
  }
}

// ============================================================================
// V2 Task 4.1.11: 从 JSON 数据岛读取产品数据
//   修复 F2-1: 不用 window.__PRODUCT__ (XSS 风险), 用 textContent + JSON.parse
// ============================================================================

function loadProductData(): ProductData {
  const el = document.getElementById('product-data')
  if (!el || !el.textContent) {
    throw new Error('[Detail] product-data JSON island missing')
  }
  return JSON.parse(el.textContent) as ProductData
}

// ============================================================================
// V2 Task 4.1.4 / 4.5: 主入口 — 动态导入 Vue 子组件 (Code Splitting)
//   WHY 动态 import: Task 4.5.5 vite manualChunks 把 vue 拆 chunk, 主 bundle 更小
//   完整子组件实现在 Task 4.5 (GalleryApp.vue / CompareApp.vue / InquiryApp.vue)
//   WHY try-catch 包裹每个 import: 单个组件加载失败不阻塞其他组件
//        失败时 safeMount 不会被调用, 挂载点保留 SSR 兜底内容 (gallery-app 内 <img>)
// ============================================================================

async function bootstrap(): Promise<void> {
  let product: ProductData
  try {
    product = loadProductData()
  } catch (err) {
    // 产品数据缺失是致命错误, 直接上报 (不影响 SSR 内容展示)
    captureException(err as Error, { tags: { stage: 'product-data' } })
    return
  }

  // GalleryApp: 图片画廊 (主图 + 缩略图切换)
  try {
    const GalleryApp = (await import('@/components/GalleryApp.vue')).default
    safeMount('gallery-app', GalleryApp, {
      mr1: product.mr1,
      oemNo3: product.oemNoDisplay,
      images: product.images ?? []
    })
  } catch (err) {
    // GalleryApp 加载失败, 保留 SSR 兜底主图 (gallery-app 内 <img> 未被清空)
    console.warn('[Detail] GalleryApp load failed, fallback to SSR image:', err)
  }

  // CompareApp: 加入对比按钮 (跳 /compare?ids=<productId>)
  try {
    const CompareApp = (await import('@/components/CompareApp.vue')).default
    safeMount('compare-app', CompareApp, {
      mr1: product.mr1,
      oemNo3: product.oemNoDisplay,
      productId: product.id
    })
  } catch (err) {
    console.warn('[Detail] CompareApp load failed:', err)
  }

  // InquiryApp: 询盘表单 (mailto: 兜底, 后端 API 待 Phase 5)
  try {
    const InquiryApp = (await import('@/components/InquiryApp.vue')).default
    safeMount('inquiry-app', InquiryApp, {
      mr1: product.mr1,
      oemNo3: product.oemNoDisplay,
      brand: product.crossReferences?.[0]?.oemBrand ?? null,
      productName1: product.productName1
    })
  } catch (err) {
    console.warn('[Detail] InquiryApp load failed:', err)
  }
}

// ============================================================================
// 启动: 容错 + DOMContentLoaded 兜底
// ============================================================================

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => {
    bootstrap().catch(err => {
      captureException(err as Error, { tags: { stage: 'bootstrap' } })
    })
  })
} else {
  bootstrap().catch(err => {
    captureException(err as Error, { tags: { stage: 'bootstrap' } })
  })
}
