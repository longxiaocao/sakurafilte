// V2 Task 4.5.3 / Task 4.4: SEO URL 公共构建工具
//
// 设计:
//   - 与后端 IProductDetailService.BuildProductUrl 逻辑对齐 (避免前后端 URL 不一致)
//   - 兼容输入: 支持完整 product 对象 (pn1/pn2/brand/oem3) 或仅 oem (单字段)
//   - 单字段输入时降级为 /product/{oem} (走后端 301 重定向到 SEO URL)
//
// 用法:
//   1. 完整数据: window.location.href = buildProductUrl({ productName1, productName2, oemBrand, oemNo3, mr1 })
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
  /** MR.1 (用于末 6 位防 slug 冲突) */
  mr1?: string | null
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
 *   格式: /products/{pn1Slug}-{mr1Suffix6}/{pn2Slug}/{brandSlug}/{oem3Slug}
 *
 * 降级策略 (与后端 BuildProductUrl 一致):
 *   - oem3 优先取 oemNo3, 缺失时用 oemNoDisplay, 都缺失时用 mr1
 *   - brand 缺失 → "untitled" (与后端一致)
 *   - mr1Suffix: mr1 长度 > 6 取末 6 位, 否则取原值, 都缺失 → "nomr1"
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
  // V2 Task 4.4: 若仅有 oemNoDisplay (无 pn1/pn2/brand), 降级走 /product/{oem} 触发后端 301
  //   WHY 降级: SearchView/PublicSearchView 等场景搜索结果只有 oemNoDisplay, 无完整 SEO 字段
  //   单次 301 重定向可接受, 避免强制改后端搜索 API 返回字段
  if (!product.productName1 && !product.productName2 && !product.oemBrand
      && !product.oemNo3 && product.oemNoDisplay) {
    return `/product/${encodeURIComponent(product.oemNoDisplay)}`
  }

  const pn1Slug = buildSlug(product.productName1)
  const pn2Slug = buildSlug(product.productName2)
  const brandSlug = buildSlug(product.oemBrand)
  const oem3 = product.oemNo3 ?? product.oemNoDisplay ?? product.mr1 ?? ''
  // V24-F42 (spec F5-1): oem3 段保留大小写, 仅 URL 编码 (不走 buildSlug)
  //   WHY: 后端 GetByOemAsync 用 === 大小写敏感查询, buildSlug 会 toLowerCase 导致反查失败
  const oem3Slug = oem3 ? encodeURIComponent(oem3) : 'untitled'

  // mr1 末 6 位 (与后端 BuildProductUrl 一致, 仅用于 URL 唯一性, 可小写化)
  const mr1Val = product.mr1 ?? ''
  const mr1Suffix = mr1Val.length > 6 ? mr1Val.slice(-6).toLowerCase() : (mr1Val.toLowerCase() || 'nomr1')

  // V24-F42: 不再整体 toLowerCase (oem3Slug 已保留大小写)
  return `/products/${pn1Slug}-${mr1Suffix}/${pn2Slug}/${brandSlug}/${oem3Slug}`
}
