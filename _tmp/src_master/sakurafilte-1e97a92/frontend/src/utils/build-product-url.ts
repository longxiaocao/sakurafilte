// V2 Task 4.5.3 / Task 4.4: SEO URL 公共构建工具
//
// 设计:
//   - 与后端 IProductDetailService.BuildProductUrl 逻辑对齐 (避免前后端 URL 不一致)
//   - 兼容输入: 支持完整 product 对象 (pn1/pn2/brand/oem3) 或仅 oem (单字段)
//   - 单字段输入时降级为 /product/{oem} (走后端 301 重定向到 SEO URL)
//
// 用法:
//   1. 完整数据: window.location.href = buildProductUrl({ productName1, productName2, oemBrand, oemNo3 })
//   2. 仅 OEM:   window.location.href = buildProductUrl({ oemNoDisplay: 'F000000001' })
//                  → /product/F000000001 (走后端 301)

interface ProductUrlInput {
  /** 产品名称 1 (pn1 段) */
  productName1?: string | null
  /** 产品名称 2 (pn2 段) */
  productName2?: string | null
  /** OEM 品牌 (brand 段) */
  oemBrand?: string | null
  /** OEM 3 编号 (oem3 段, 优先) */
  oemNo3?: string | null
  /** OEM 显示编号 (oem3 段, oemNo3 缺失时用) */
  oemNoDisplay?: string | null
}

/**
 * V2 Task 4.5.3: slug 化字符串 (与后端 IProductDetailService.BuildSlug 对齐)
 *   - 小写化 + 空白/下划线/连续- → 单个 -
 *   - 非 ASCII (含中文) 用 encodeURIComponent 转 %XX
 *   - 首尾 - 截断
 *   - 空输入返回 "untitled"
 */
function buildSlug(input: string | null | undefined): string {
  if (!input || !input.trim()) return 'untitled'
  const lower = input.trim().toLowerCase()
  // 空白/下划线/连续- → 单-
  const collapsed = lower.replace(/[\s_-]+/g, '-')
  // 非 ASCII 转 %XX 编码 (encodeURIComponent 默认输出大写)
  const encoded = encodeURIComponent(collapsed)
  // 首尾 - 截断
  return encoded.replace(/^-+|-+$/g, '')
}

/**
 * V2 Task 4.4 / 4.5.3: 拼 SEO URL
 *   格式: /products/{pn1Slug}/{pn2Slug}/{brandSlug}/{oem3Slug}
 *
 * 降级策略 (与后端 BuildProductUrl 一致):
 *   - oem3 优先取 oemNo3, 缺失时用 oemNoDisplay
 *   - brand 缺失 → "untitled" (与后端一致)
 *
 * V24-F42 (spec F5-1): oem3 段保留大小写, 不走 buildSlug
 *   - 后端 GetByOemAsync 用 === 大小写敏感查询, BuildSlug 小写化会导致 OEM 含大写字母时反查失败
 *   - oem3 段仅 encodeURIComponent (保留原值大小写), 后端 Uri.UnescapeDataString 解码后精确匹配
 *   - 其他段 (pn1/pn2/brand/mr1Suffix) 仍小写化 (SEO 友好, 不参与 DB 反查)
 *
 * @param product 产品字段对象
 * @returns SEO URL (oem3 段保留大小写, 其他段小写)
 */
export function buildProductUrl(product: ProductUrlInput): string {
  // 🔧 fix(审查): 统一跳转 SPA 详情页 /seo/{oem} — 原 /products/{pn1}/{pn2}/{brand}/{oem3} 被
  //   nginx 反代到 Razor SSR 页 (无 main.css), 用户实测 "样式丢失, 图标文字不正常"
  //   /seo/ 是 SPA 路由 (ProductDetailView, 完整 Tailwind + Element Plus 样式)
  //   /products/ SSR 路径保留给爬虫 (sitemap/canonical 由后端生成, 不受影响)
  const oem = product.oemNo3 || product.oemNoDisplay
  if (!oem) return '/search'
  return `/seo/${encodeURIComponent(oem)}`
}
