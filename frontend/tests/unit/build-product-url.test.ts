import { describe, expect, it } from 'vitest'
import { buildProductUrl } from '@/utils/build-product-url'

describe('buildProductUrl', () => {
  it('统一生成 /seo/{oem} SPA 详情路径', () => {
    // 🔧 fix(审查): 统一跳转 SPA /seo/{oem} (原 /products/ 四段走 Razor SSR 无 main.css 样式丢失)
    expect(buildProductUrl({
      productName1: 'Air Filter', productName2: 'Premium', oemBrand: 'BOSCH', oemNo3: 'F0001'
    })).toBe('/seo/F0001')
  })

  it('即使调用方携带 MR1，公开 URL 也不得包含它', () => {
    const url = buildProductUrl({
      productName1: 'Air Filter', productName2: 'Premium', oemBrand: 'BOSCH', oemNo3: 'F0001', mr1: 'INTERNAL123'
    } as never)

    expect(url).toBe('/seo/F0001')
    expect(url).not.toContain('INTERNAL123')
  })

  it('保留 OEM3 大小写并编码特殊字符', () => {
    expect(buildProductUrl({
      productName1: 'Air_Filter', productName2: 'A/B', oemBrand: 'MANN Filter', oemNo3: 'W 700/1'
    })).toBe('/seo/W%20700%2F1')
  })

  it('只有 OEM 显示编号时同样走 /seo/{oem}', () => {
    expect(buildProductUrl({ oemNoDisplay: 'F0001' })).toBe('/seo/F0001')
  })

  it('缺少 OEM 时返回 /search', () => {
    expect(buildProductUrl({
      productName1: 'Air Filter', productName2: 'Premium', oemBrand: 'BOSCH'
    })).toBe('/search')
  })
})
