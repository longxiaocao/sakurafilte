/**
 * V24-F49 (spec Task 5.1.25): useFormDraft composable 单元测试
 *
 * 测试目标:
 *   - save/load/clear 基本读写流程
 *   - 7 天 TTL 过期自动清理
 *   - debounce 500ms 自动保存 (用 vi.useFakeTimers)
 *   - startAutoSave/stopAutoSave 生命周期
 *   - Safari 隐私模式 localStorage 抛异常时静默降级
 *   - JSON 解析失败 (草稿格式损坏) 时清理并返回 null
 *
 * WHY 单元测试: useFormDraft 是表单数据持久化的核心逻辑
 *   - TTL/debounce/异常降级任一环节失败都会导致用户数据丢失
 *   - 集成测试难以模拟 7 天过期 + 隐私模式场景
 */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { reactive, nextTick } from 'vue'
import { useFormDraft } from '@/composables/useFormDraft'

describe('useFormDraft', () => {
  beforeEach(() => {
    // 每个测试前清空 localStorage
    localStorage.clear()
    // 使用真实 timers (部分测试需要手动切换 fake/real)
  })

  afterEach(() => {
    vi.useRealTimers()
    localStorage.clear()
  })

  // ===== 基本读写流程 =====
  it('save + load: 保存后能读取到草稿数据', () => {
    const form = reactive({ mr1: 'MR001', name: 'Test Product' })
    const draft = useFormDraft('test', () => ({ ...form }))

    draft.save()
    const loaded = draft.load()

    expect(loaded).not.toBeNull()
    expect(loaded?.mr1).toBe('MR001')
    expect(loaded?.name).toBe('Test Product')
  })

  it('load: 无草稿时返回 null', () => {
    const draft = useFormDraft('test', () => ({ foo: 'bar' }))
    expect(draft.load()).toBeNull()
  })

  it('clear: 清除后 load 返回 null', () => {
    const form = reactive({ mr1: 'MR001' })
    const draft = useFormDraft('test', () => ({ ...form }))

    draft.save()
    expect(draft.hasDraft()).toBe(true)

    draft.clear()
    expect(draft.hasDraft()).toBe(false)
    expect(draft.load()).toBeNull()
  })

  it('hasDraft: 有未过期草稿时返回 true, 无时返回 false', () => {
    const draft = useFormDraft('test', () => ({ foo: 'bar' }))
    expect(draft.hasDraft()).toBe(false)

    draft.save()
    expect(draft.hasDraft()).toBe(true)
  })

  // ===== namespace + key 隔离 =====
  it('不同 namespace 的草稿互不干扰', () => {
    const draft1 = useFormDraft('product', () => ({ a: 1 }))
    const draft2 = useFormDraft('xref', () => ({ b: 2 }))

    draft1.save()
    draft2.save()

    expect(draft1.load()?.a).toBe(1)
    expect(draft1.load()?.b).toBeUndefined()
    expect(draft2.load()?.b).toBe(2)
    expect(draft2.load()?.a).toBeUndefined()
  })

  it('不同 key 的草稿互不干扰', () => {
    const draft1 = useFormDraft('product', () => ({ mr1: 'MR001' }), 'MR001')
    const draft2 = useFormDraft('product', () => ({ mr1: 'MR002' }), 'MR002')

    draft1.save()
    draft2.save()

    expect(draft1.load()?.mr1).toBe('MR001')
    expect(draft2.load()?.mr1).toBe('MR002')
  })

  // ===== 7 天 TTL 过期 =====
  it('TTL: 超过 7 天的草稿自动清理, load 返回 null', () => {
    vi.useFakeTimers()
    const form = reactive({ mr1: 'MR001' })
    const draft = useFormDraft('test', () => ({ ...form }))

    draft.save()
    expect(draft.hasDraft()).toBe(true)

    // 快进 7 天 + 1 秒
    vi.advanceTimersByTime(7 * 24 * 60 * 60 * 1000 + 1000)

    expect(draft.load()).toBeNull()
    expect(draft.hasDraft()).toBe(false)
  })

  it('TTL: 6 天 23 小时的草稿仍未过期, load 返回数据', () => {
    vi.useFakeTimers()
    const form = reactive({ mr1: 'MR001' })
    const draft = useFormDraft('test', () => ({ ...form }))

    draft.save()
    // 快进 6 天 23 小时 (未过期)
    vi.advanceTimersByTime(6 * 24 * 60 * 60 * 1000 + 23 * 60 * 60 * 1000)

    expect(draft.load()).not.toBeNull()
    expect(draft.load()?.mr1).toBe('MR001')
  })

  // ===== debounce 500ms 自动保存 =====
  it('startAutoSave: watch 触发后 500ms 内不保存, 500ms 后保存', async () => {
    vi.useFakeTimers()
    const form = reactive({ mr1: '' })
    const draft = useFormDraft('test', () => ({ ...form }))

    draft.startAutoSave()

    // 修改 form 触发 watch
    form.mr1 = 'MR001'
    await nextTick()

    // 500ms 内: 草稿未保存
    vi.advanceTimersByTime(499)
    expect(draft.hasDraft()).toBe(false)

    // 500ms 后: 草稿已保存
    vi.advanceTimersByTime(2)
    expect(draft.hasDraft()).toBe(true)
    expect(draft.load()?.mr1).toBe('MR001')
  })

  it('startAutoSave: 连续修改只保存最后一次 (debounce)', async () => {
    vi.useFakeTimers()
    const form = reactive({ count: 0 })
    const draft = useFormDraft('test', () => ({ ...form }))

    draft.startAutoSave()

    // 连续修改 3 次
    form.count = 1
    await nextTick()
    vi.advanceTimersByTime(200)

    form.count = 2
    await nextTick()
    vi.advanceTimersByTime(200)

    form.count = 3
    await nextTick()
    vi.advanceTimersByTime(200)

    // 600ms 内: 草稿未保存 (每次修改重置 debounce)
    expect(draft.hasDraft()).toBe(false)

    // 再等 500ms: 草稿保存, 只保留最后一次值
    vi.advanceTimersByTime(500)
    expect(draft.hasDraft()).toBe(true)
    expect(draft.load()?.count).toBe(3)
  })

  it('stopAutoSave: 停止后 watch 不再触发保存', async () => {
    vi.useFakeTimers()
    const form = reactive({ mr1: '' })
    const draft = useFormDraft('test', () => ({ ...form }))

    draft.startAutoSave()
    form.mr1 = 'MR001'
    await nextTick()
    vi.advanceTimersByTime(500)
    expect(draft.hasDraft()).toBe(true)

    // 清理后停止自动保存
    draft.clear()
    draft.stopAutoSave()

    // 修改 form: 不应触发保存
    form.mr1 = 'MR002'
    await nextTick()
    vi.advanceTimersByTime(1000)
    expect(draft.hasDraft()).toBe(false)
  })

  it('stopAutoSave: 清理 debounce timer, 避免卸载后触发保存', async () => {
    vi.useFakeTimers()
    const form = reactive({ mr1: '' })
    const draft = useFormDraft('test', () => ({ ...form }))

    draft.startAutoSave()
    form.mr1 = 'MR001'
    await nextTick()
    // 500ms 未到, debounce timer 仍在等待
    vi.advanceTimersByTime(300)
    expect(draft.hasDraft()).toBe(false)

    // 卸载组件: stopAutoSave 应清理 debounce timer
    draft.stopAutoSave()
    // 快进超过 500ms: 不应保存 (debounce timer 已清理)
    vi.advanceTimersByTime(1000)
    expect(draft.hasDraft()).toBe(false)
  })

  it('startAutoSave: 重复调用不会启动多个 watch', async () => {
    vi.useFakeTimers()
    const form = reactive({ mr1: '' })
    const draft = useFormDraft('test', () => ({ ...form }))

    // 重复调用 startAutoSave
    draft.startAutoSave()
    draft.startAutoSave()
    draft.startAutoSave()

    form.mr1 = 'MR001'
    await nextTick()
    vi.advanceTimersByTime(500)

    // 只保存一次 (不会因多个 watch 重复保存)
    expect(draft.hasDraft()).toBe(true)
    expect(draft.load()?.mr1).toBe('MR001')
  })

  // ===== 异常降级 =====
  it('Safari 隐私模式: localStorage.setItem 抛异常时静默降级, 不抛错', () => {
    const form = reactive({ mr1: 'MR001' })
    const draft = useFormDraft('test', () => ({ ...form }))

    // mock localStorage.setItem 抛 QuotaExceededError
    //   用 Object.defineProperty 覆盖 (jsdom 的 localStorage 是 Storage 实例, 直接赋值/ spy 可能不生效)
    const originalSetItem = localStorage.setItem
    Object.defineProperty(localStorage, 'setItem', {
      value: () => { throw new DOMException('QuotaExceededError') },
      configurable: true,
      writable: true
    })

    // 核心断言: safeSetItem 捕获异常, save() 不传播
    expect(() => draft.save()).not.toThrow()

    // 恢复
    Object.defineProperty(localStorage, 'setItem', {
      value: originalSetItem,
      configurable: true,
      writable: true
    })
    localStorage.clear()
  })

  it('Safari 隐私模式: localStorage.getItem 抛异常时 load 返回 null', () => {
    const draft = useFormDraft('test', () => ({ foo: 'bar' }))

    const originalGetItem = localStorage.getItem
    Object.defineProperty(localStorage, 'getItem', {
      value: () => { throw new DOMException('QuotaExceededError') },
      configurable: true,
      writable: true
    })

    // 核心断言: safeGetItem 捕获异常, load() 返回 null
    expect(draft.load()).toBeNull()
    expect(draft.hasDraft()).toBe(false)

    Object.defineProperty(localStorage, 'getItem', {
      value: originalGetItem,
      configurable: true,
      writable: true
    })
  })

  it('JSON 解析失败 (草稿格式损坏): load 清理并返回 null', () => {
    // 手动写入损坏的 JSON
    localStorage.setItem('sakurafilter_draft_test_default', '{invalid json')

    const draft = useFormDraft('test', () => ({ foo: 'bar' }))
    expect(draft.load()).toBeNull()

    // 损坏的草稿应被清理
    expect(localStorage.getItem('sakurafilter_draft_test_default')).toBeNull()
  })

  it('JSON.stringify 失败 (循环引用): save 静默降级, 不抛错', () => {
    // 构造循环引用对象 (JSON.stringify 会抛错)
    const circular: any = { name: 'test' }
    circular.self = circular

    const draft = useFormDraft('test', () => circular)

    expect(() => draft.save()).not.toThrow()
    expect(draft.hasDraft()).toBe(false)
  })

  // ===== 完整生命周期测试 =====
  it('完整生命周期: load → startAutoSave → 修改 → 自动保存 → 409 恢复 → clear', async () => {
    vi.useFakeTimers()
    const form = reactive({ mr1: 'MR001', name: 'Original' })
    const draft = useFormDraft('product_form', () => ({ ...form }), 'MR001')

    // 1. 用户进入表单, 无草稿
    expect(draft.hasDraft()).toBe(false)

    // 2. 启动自动保存
    draft.startAutoSave()

    // 3. 用户修改表单
    form.name = 'Modified'
    await nextTick()
    vi.advanceTimersByTime(500)

    // 4. 草稿已自动保存
    expect(draft.hasDraft()).toBe(true)
    expect(draft.load()?.name).toBe('Modified')

    // 5. 模拟 409 冲突: 用户选择恢复草稿
    const restored = draft.load()
    expect(restored?.name).toBe('Modified')

    // 6. 保存成功后清理草稿
    draft.clear()
    expect(draft.hasDraft()).toBe(false)

    // 7. 卸载组件
    draft.stopAutoSave()
  })
})
