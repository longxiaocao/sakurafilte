/**
 * EmptyState 组件单元测试
 *   - 验证三种语义 (empty/no-result/error) 的默认配置
 *   - 验证自定义 props 和 action 事件
 */
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import EmptyState from '@/components/EmptyState.vue'

// Element Plus 组件 stubs (jsdom 环境下简化渲染)
const elStubs = {
  'el-button': { template: '<button><slot /></button>' },
  'el-icon': { template: '<span><slot /></span>' }
}

describe('EmptyState', () => {
  it('type=empty 时显示默认空数据配置', () => {
    const wrapper = mount(EmptyState, { props: { type: 'empty' } })
    expect(wrapper.text()).toContain('暂无数据')
    expect(wrapper.text()).toContain('当前列表为空')
    expect(wrapper.find('[role="status"]').exists()).toBe(true)
    expect(wrapper.attributes('aria-live')).toBe('polite')
  })

  it('type=no-result 时显示默认无结果配置', () => {
    const wrapper = mount(EmptyState, { props: { type: 'no-result' } })
    expect(wrapper.text()).toContain('未找到匹配结果')
    expect(wrapper.text()).toContain('尝试调整搜索条件或更换关键词')
  })

  it('type=error 时显示默认错误配置且文字为红色', () => {
    const wrapper = mount(EmptyState, { props: { type: 'error' } })
    expect(wrapper.text()).toContain('加载失败')
    expect(wrapper.classes()).toContain('text-red-600')
  })

  it('自定义 title 和 description 优先于默认配置', () => {
    const wrapper = mount(EmptyState, {
      props: {
        type: 'empty',
        title: '自定义标题',
        description: '自定义描述'
      }
    })
    expect(wrapper.text()).toContain('自定义标题')
    expect(wrapper.text()).toContain('自定义描述')
  })

  it('actionText 为空时不显示操作按钮', () => {
    const wrapper = mount(EmptyState, { props: { type: 'empty' } })
    expect(wrapper.find('button').exists()).toBe(false)
  })

  it('点击操作按钮触发 action 事件', async () => {
    const wrapper = mount(EmptyState, {
      props: {
        type: 'no-result',
        actionText: '重试'
      },
      global: { stubs: elStubs }
    })
    const btn = wrapper.find('button')
    expect(btn.exists()).toBe(true)
    expect(btn.text()).toBe('重试')
    await btn.trigger('click')
    expect(wrapper.emitted('action')).toBeTruthy()
    expect(wrapper.emitted('action')?.length).toBe(1)
  })

  it('无障碍: 包含 role=status 和 aria-live=polite', () => {
    const wrapper = mount(EmptyState, { props: { type: 'empty' } })
    const statusEl = wrapper.find('[role="status"]')
    expect(statusEl.exists()).toBe(true)
    expect(statusEl.attributes('aria-live')).toBe('polite')
  })

  it('自定义 icon 覆盖默认图标', () => {
    const wrapper = mount(EmptyState, {
      props: { type: 'empty', icon: 'Star' }
    })
    // 图标通过动态组件渲染, 验证不报错即可
    expect(wrapper.find('.empty-state').exists()).toBe(true)
  })
})
