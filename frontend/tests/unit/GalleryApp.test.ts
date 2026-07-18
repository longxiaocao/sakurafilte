/**
 * V2 Task 4.5.1 / V24-F35 (spec Task 4.9.1): GalleryApp 组件单测
 *   覆盖产品详情页图片画廊的核心交互
 *
 * 测试目标:
 *   - 主图初始渲染 (优先 isPrimary, 否则第一张)
 *   - 缩略图点击切换主图
 *   - 主图加载失败兜底 (placeholder)
 *   - 仅 1 张图时显示 "仅 1 张图片" 提示
 *   - 0 张图时显示 "暂无图片" 兜底
 *
 * WHY 组件测试: GalleryApp 是详情页核心交互组件
 *   - 主图加载失败不兜底会导致白屏, 影响转化
 *   - 缩略图切换是用户主动行为, 必须有反馈
 */
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import GalleryApp from '@/components/GalleryApp.vue'

interface GalleryImage {
  imageKey: string
  imageUrl: string
  oemNo3?: string | null
  imageRole?: string | null
  isPrimary?: boolean
  slot?: number
}

function makeImages(count: number, primaryIdx: number = 0): GalleryImage[] {
  return Array.from({ length: count }, (_, i): GalleryImage => ({
    imageKey: `key-${i}`,
    imageUrl: `https://example.com/img-${i}.jpg`,
    oemNo3: 'OEM001',
    imageRole: i === primaryIdx ? 'primary' : 'detail',
    isPrimary: i === primaryIdx,
    slot: i + 1
  }))
}

describe('GalleryApp', () => {
  // ===== 主图初始渲染 =====
  it('多图时主图初始取 isPrimary 的图片', () => {
    const images = makeImages(3, 1)  // 第 2 张是 primary
    const wrapper = mount(GalleryApp, {
      props: { images, oemNo3: 'OEM001', mr1: 'MR001' }
    })
    const mainImg = wrapper.find('.gallery-main img')
    expect(mainImg.attributes('src')).toBe('https://example.com/img-1.jpg')
  })

  it('无 isPrimary 时主图初始取第一张', () => {
    const images: GalleryImage[] = [
      { imageKey: 'k1', imageUrl: 'url-1.jpg', oemNo3: 'OEM001', slot: 1 },
      { imageKey: 'k2', imageUrl: 'url-2.jpg', oemNo3: 'OEM001', slot: 2 }
    ]
    const wrapper = mount(GalleryApp, {
      props: { images, oemNo3: 'OEM001', mr1: 'MR001' }
    })
    const mainImg = wrapper.find('.gallery-main img')
    expect(mainImg.attributes('src')).toBe('url-1.jpg')
  })

  // ===== 缩略图点击切换 =====
  it('点击缩略图切换主图', async () => {
    const images = makeImages(3, 0)
    const wrapper = mount(GalleryApp, {
      props: { images, oemNo3: 'OEM001', mr1: 'MR001' }
    })
    // 初始主图是第 1 张
    expect(wrapper.find('.gallery-main img').attributes('src')).toBe('https://example.com/img-0.jpg')

    // 点击第 3 张缩略图
    const thumbs = wrapper.findAll('.gallery-thumb')
    expect(thumbs.length).toBe(3)
    await thumbs[2].trigger('click')

    // 主图应切换为第 3 张
    expect(wrapper.find('.gallery-main img').attributes('src')).toBe('https://example.com/img-2.jpg')
  })

  it('点击的缩略图高亮 (active class)', async () => {
    const images = makeImages(3, 0)
    const wrapper = mount(GalleryApp, {
      props: { images, oemNo3: 'OEM001', mr1: 'MR001' }
    })
    const thumbs = wrapper.findAll('.gallery-thumb')
    // 初始第 1 张高亮
    expect(thumbs[0].classes()).toContain('active')

    // 点击第 2 张
    await thumbs[1].trigger('click')
    // 第 2 张应高亮, 第 1 张不高亮
    expect(wrapper.findAll('.gallery-thumb')[1].classes()).toContain('active')
    expect(wrapper.findAll('.gallery-thumb')[0].classes()).not.toContain('active')
  })

  // ===== 兜底场景 =====
  it('仅 1 张图时显示 "仅 1 张图片" 提示 (不渲染缩略图列表)', () => {
    const images = makeImages(1, 0)
    const wrapper = mount(GalleryApp, {
      props: { images, oemNo3: 'OEM001', mr1: 'MR001' }
    })
    expect(wrapper.find('.gallery-thumbs').exists()).toBe(false)
    expect(wrapper.text()).toContain('仅 1 张图片')
  })

  it('0 张图时显示 "暂无图片" 兜底', () => {
    const wrapper = mount(GalleryApp, {
      props: { images: [], oemNo3: 'OEM001', mr1: 'MR001' }
    })
    expect(wrapper.find('.gallery-thumbs').exists()).toBe(false)
    expect(wrapper.text()).toContain('暂无图片')
    // 主图 src 应为 placeholder
    expect(wrapper.find('.gallery-main img').attributes('src')).toBe('/static/placeholder.png')
  })

  // ===== 主图加载失败兜底 =====
  it('主图加载失败时切换到 placeholder', async () => {
    const images = makeImages(1, 0)
    const wrapper = mount(GalleryApp, {
      props: { images, oemNo3: 'OEM001', mr1: 'MR001' }
    })
    const mainImg = wrapper.find('.gallery-main img')
    // 初始为真实 URL
    expect(mainImg.attributes('src')).toBe('https://example.com/img-0.jpg')

    // 触发 @error 事件
    await mainImg.trigger('error')
    // 应切换为 placeholder
    expect(wrapper.find('.gallery-main img').attributes('src')).toBe('/static/placeholder.png')
  })

  // ===== alt 文本 =====
  it('主图 alt 默认使用 oemNo3', () => {
    const images = makeImages(1, 0)
    const wrapper = mount(GalleryApp, {
      props: { images, oemNo3: 'ABC123', mr1: 'MR001' }
    })
    const mainImg = wrapper.find('.gallery-main img')
    expect(mainImg.attributes('alt')).toBe('ABC123')
  })

  it('切换缩略图后 alt 包含 slot 信息', async () => {
    const images = makeImages(2, 0)
    const wrapper = mount(GalleryApp, {
      props: { images, oemNo3: 'ABC123', mr1: 'MR001' }
    })
    const thumbs = wrapper.findAll('.gallery-thumb')
    await thumbs[1].trigger('click')
    const mainImg = wrapper.find('.gallery-main img')
    expect(mainImg.attributes('alt')).toContain('ABC123')
    expect(mainImg.attributes('alt')).toContain('slot 2')
  })
})
