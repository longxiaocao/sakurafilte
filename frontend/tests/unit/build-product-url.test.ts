/**
 * V2 Task 4.4 / 4.5.3: SEO URL 构建器单测
 *   覆盖 buildProductUrl 纯函数的所有分支
 *
 * 测试目标:
 *   - slug 化规则 (空白/下划线/中划线折叠, 非 ASCII 编码, 首尾 - 截断)
 *   - 完整 URL 拼装 (pn1-mr1Suffix6/pn2/brand/oem3)
 *   - mr1 末 6 位防 slug 冲突
 *   - oemNoDisplay 降级路径 (走 301 重定向)
 *   - oem3 缺失时降级到 mr1
 *   - 空字段降级 ("untitled" / "nomr1")
 *
 * WHY 纯函数测试: buildProductUrl 是 V2 SEO URL 的核心逻辑
 *   - 前后端必须一致 (后端 IProductDetailService.BuildProductUrl 同口径)
 *   - 任何不一致会导致 SEO 404 或 301 循环
 */
import { describe, it, expect } from 'vitest'
import { buildProductUrl } from '@/utils/build-product-url'

describe('buildProductUrl', () => {
  // ===== 完整 URL 构建 (happy path) =====
  it('完整字段拼装为 /products/{pn1-mr1Suffix6}/{pn2}/{brand}/{oem3}', () => {
    const url = buildProductUrl({
      productName1: 'Air Filter',
      productName2: 'Premium',
      oemBrand: 'BOSCH',
      oemNo3: 'F0001',
      mr1: 'MR000001'
    })
    // mr1 8 位 → 末 6 位 = "MR000001" 的 slice(-6) = "000001"
    expect(url).toBe('/products/air-filter-000001/premium/bosch/f0001')
  })

  it('mr1 超过 6 位时取末 6 位防 slug 冲突', () => {
    // WHY 末 6 位: 不同 pn1 同名时, mr1 末 6 位作为唯一性区分
    const url = buildProductUrl({
      productName1: 'Air Filter',
      productName2: 'Premium',
      oemBrand: 'BOSCH',
      oemNo3: 'F0001',
      mr1: 'ABCDEFGHIJ' // 10 位 → 末 6 位 = "EFGHIJ"
    })
    expect(url).toBe('/products/air-filter-efghij/premium/bosch/f0001')
  })

  it('mr1 正好 6 位时直接使用不截断', () => {
    const url = buildProductUrl({
      productName1: 'Oil Filter',
      productName2: 'Standard',
      oemBrand: 'MANN',
      oemNo3: 'W7001',
      mr1: 'ABCDEF' // 6 位 → 直接用
    })
    expect(url).toBe('/products/oil-filter-abcdef/standard/mann/w7001')
  })

  // ===== slug 化规则 =====
  it('slug 折叠空白/下划线/连续中划线为单个 -', () => {
    const url = buildProductUrl({
      productName1: 'Air  Filter__Pro--X',  // 多空白 + 下划线 + 连续 -
      productName2: 'Cabin_Type - Filter',
      oemBrand: 'WIX Filters',
      oemNo3: 'F0001',
      mr1: 'MR000001'
    })
    expect(url).toBe('/products/air-filter-pro-x-000001/cabin-type-filter/wix-filters/f0001')
  })

  it('slug 首尾 - 截断', () => {
    const url = buildProductUrl({
      productName1: '  Air Filter  ',  // 前后空白
      productName2: '--Premium--',     // 前后中划线
      oemBrand: '__BOSCH__',
      oemNo3: 'F0001',
      mr1: 'MR000001'
    })
    expect(url).toBe('/products/air-filter-000001/premium/bosch/f0001')
  })

  it('非 ASCII 字符 (含中文) 用 encodeURIComponent 编码', () => {
    const url = buildProductUrl({
      productName1: '机油滤清器',
      productName2: 'Premium',
      oemBrand: 'BOSCH',
      oemNo3: 'F0001',
      mr1: 'MR000001'
    })
    // encodeURIComponent('机油滤清器') = %E6%9C%BA%E6%B2%B9%E6%BB%A4%E6%B8%85%E5%99%A8
    expect(url).toBe('/products/%e6%9c%ba%e6%b2%b9%e6%bb%a4%e6%b8%85%e5%99%a8-000001/premium/bosch/f0001')
    // URL 必须小写 (与后端 BuildProductUrl 一致)
    expect(url).toBe(url.toLowerCase())
  })

  // ===== 空字段降级 =====
  it('pn1 缺失时降级为 "untitled"', () => {
    const url = buildProductUrl({
      productName1: null,
      productName2: 'Premium',
      oemBrand: 'BOSCH',
      oemNo3: 'F0001',
      mr1: 'MR000001'
    })
    expect(url).toBe('/products/untitled-000001/premium/bosch/f0001')
  })

  it('pn2 缺失时降级为 "untitled"', () => {
    const url = buildProductUrl({
      productName1: 'Air Filter',
      productName2: '',
      oemBrand: 'BOSCH',
      oemNo3: 'F0001',
      mr1: 'MR000001'
    })
    expect(url).toBe('/products/air-filter-000001/untitled/bosch/f0001')
  })

  it('brand 缺失时降级为 "untitled"', () => {
    const url = buildProductUrl({
      productName1: 'Air Filter',
      productName2: 'Premium',
      oemBrand: undefined,
      oemNo3: 'F0001',
      mr1: 'MR000001'
    })
    expect(url).toBe('/products/air-filter-000001/premium/untitled/f0001')
  })

  it('mr1 缺失时 mr1Suffix 降级为 "nomr1"', () => {
    const url = buildProductUrl({
      productName1: 'Air Filter',
      productName2: 'Premium',
      oemBrand: 'BOSCH',
      oemNo3: 'F0001',
      mr1: null
    })
    expect(url).toBe('/products/air-filter-nomr1/premium/bosch/f0001')
  })

  it('oemNo3 缺失时降级用 oemNoDisplay', () => {
    const url = buildProductUrl({
      productName1: 'Air Filter',
      productName2: 'Premium',
      oemBrand: 'BOSCH',
      oemNo3: null,
      oemNoDisplay: 'DISPLAY-001',
      mr1: 'MR000001'
    })
    expect(url).toBe('/products/air-filter-000001/premium/bosch/display-001')
  })

  it('oemNo3 和 oemNoDisplay 都缺失时降级用 mr1', () => {
    const url = buildProductUrl({
      productName1: 'Air Filter',
      productName2: 'Premium',
      oemBrand: 'BOSCH',
      oemNo3: null,
      oemNoDisplay: null,
      mr1: 'MR000001'
    })
    // oem3 段降级为 mr1
    expect(url).toBe('/products/air-filter-000001/premium/bosch/mr000001')
  })

  // ===== oemNoDisplay 降级路径 (走 301) =====
  it('仅有 oemNoDisplay 时降级为 /product/{oem} (走后端 301)', () => {
    // WHY 降级: SearchView 搜索结果只有 oemNoDisplay, 无完整 SEO 字段
    //   单次 301 可接受, 避免强制改搜索 API 返回字段
    const url = buildProductUrl({ oemNoDisplay: 'F000000001' })
    expect(url).toBe('/product/F000000001')
  })

  it('oemNoDisplay 含特殊字符时 encodeURIComponent 编码', () => {
    const url = buildProductUrl({ oemNoDisplay: 'F/000 001' })
    // encodeURIComponent('F/000 001') = 'F%2F000%20001'
    expect(url).toBe('/product/F%2F000%20001')
  })

  // ===== 边界场景 =====
  it('全部字段为空时仍返回合法 URL (不抛异常)', () => {
    const url = buildProductUrl({})
    // pn1/pn2/brand/oem3 都空 → buildSlug 返回 'untitled'
    // mr1Suffix 空 → 'nomr1' (mr1Suffix 的降级与 buildSlug 不同)
    expect(url).toBe('/products/untitled-nomr1/untitled/untitled/untitled')
  })

  it('URL 全小写 (与后端 BuildProductUrl 一致)', () => {
    const url = buildProductUrl({
      productName1: 'AIR FILTER',
      productName2: 'PREMIUM',
      oemBrand: 'Bosch',
      oemNo3: 'F0001',
      mr1: 'MR000001'
    })
    // 注意: encodeURIComponent 后的 %XX 不被 toLowerCase 影响 (已是大写)
    //   但 slug 化本身已转小写, 所以最终 URL 全小写
    expect(url).toBe(url.toLowerCase())
    expect(url).toBe('/products/air-filter-000001/premium/bosch/f0001')
  })
})
