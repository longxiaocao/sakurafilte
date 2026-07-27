import { describe, expect, it } from 'vitest'
import { buildProductUrl } from '@/utils/build-product-url'

describe('buildProductUrl', () => {
  it('使用公开字段生成四段 SEO URL', () => {
    expect(buildProductUrl({
      productName1: 'Air Filter', productName2: 'Premium', oemBrand: 'BOSCH', oemNo3: 'F0001'
    })).toBe('/products/air-filter/premium/bosch/F0001')
  })

  it('即使调用方携带 MR1，公开 URL 也不得包含它', () => {
    const url = buildProductUrl({
      productName1: 'Air Filter', productName2: 'Premium', oemBrand: 'BOSCH', oemNo3: 'F0001', mr1: 'INTERNAL123'
    } as never)

    expect(url).toBe('/products/air-filter/premium/bosch/F0001')
    expect(url).not.toContain('INTERNAL123')
  })

  it('保留 OEM3 大小写并编码特殊字符', () => {
    expect(buildProductUrl({
      productName1: 'Air_Filter', productName2: 'A/B', oemBrand: 'MANN Filter', oemNo3: 'W 700/1'
    })).toBe('/products/air-filter/a%2Fb/mann-filter/W%20700%2F1')
  })

  it('只有 OEM 显示编号时走旧入口重定向', () => {
    expect(buildProductUrl({ oemNoDisplay: 'F0001' })).toBe('/product/F0001')
  })

  it('缺少公开 OEM3 时不以 MR1 作为降级值', () => {
    expect(buildProductUrl({
      productName1: 'Air Filter', productName2: 'Premium', oemBrand: 'BOSCH'
    })).toBe('/products/air-filter/premium/bosch/untitled')
  })
})
