/**
 * AdminEtlView 全量重建 (V17-3.1) 危险操作流程测试
 *
 * 测试矩阵:
 *   1. 用户取消确认 → etlApi.reindexAll 不被调用, reindexing 保持 false
 *   2. 用户确认 + API 返回成功 (无 error) → ElMessage.success 调用, lastReindex 显示
 *   3. 用户确认 + API 返回 error='CANCELLED' → ElMessage.warning 调用
 *   4. 用户确认 + API 返回 error 非 null → ElMessage.error 调用
 *   5. 用户确认 + API 抛 409 → ElMessage.warning('已有 ETL 任务在运行') 调用
 *   6. 用户确认 + API 抛其他错误 → lastReindex 设置错误兜底
 *
 * WHY 不 mount 完整组件: AdminEtlView 依赖 useEtlProgress (SSE 连接) + useGlobalDragDrop (DOM 监听)
 *   等会触发 onMounted 副作用, 测试聚焦 doReindexAll 逻辑分支, mock 所有外部依赖
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { nextTick } from 'vue'

// ===== Mock 依赖 =====
// 必须在 import 组件前 mock, 避免 hoist 问题
vi.mock('@/api', () => ({
  etlApi: {
    reindexAll: vi.fn(),
    // useEtlProgress 在 onMounted 会调用的方法 (mock 为空实现避免副作用)
    getActiveTask: vi.fn().mockResolvedValue({ inProgress: false }),
    getHistory: vi.fn().mockResolvedValue({ items: [], total: 0 }),
    getReasonCodeAggregate: vi.fn().mockResolvedValue(null),
  }
}))

vi.mock('element-plus', () => ({
  ElMessage: {
    success: vi.fn(),
    warning: vi.fn(),
    error: vi.fn(),
    info: vi.fn(),
  },
  ElMessageBox: {
    confirm: vi.fn(),
  },
}))

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (key: string, params?: any) => JSON.stringify({ key, params }) })
}))

vi.mock('@/composables/useEtlProgress', () => ({
  useEtlProgress: () => ({
    task: { value: { inProgress: false } },
    lastFinished: { value: null },
    reasonCodeAgg: { value: null },
    historyItems: { value: [] },
    historyLoading: { value: false },
    hasPausedTask: { value: false },
    legacyStatus: { value: null },
    status: { value: 'idle' },
    connect: vi.fn(),
    disconnect: vi.fn(),
    refreshHistory: vi.fn(),
  })
}))

vi.mock('@/composables/useGlobalDragDrop', () => ({
  useGlobalDragDrop: () => ({
    register: vi.fn(),
    unregister: vi.fn(),
  }),
  DEFAULT_ADMIN_ACCEPT: '.jsonl,.json'
}))

// 子组件 stub (避免渲染复杂子组件)
//   el-button: emits 声明 'click' 防止 Vue 3 attrs 透传导致 click 双触发
//   (Vue 3 默认把父组件 @click 透传到子组件根元素作为原生监听器,
//    若 stub 内部也 @click="$emit('click')", 会触发 2 次)
const childStubs = {
  'el-button': {
    template: '<button :disabled="disabled" :data-loading="loading" @click="$emit(\'click\', $event)"><slot /></button>',
    props: ['disabled', 'loading', 'type'],
    emits: ['click']
  },
  'el-card': { template: '<div class="el-card"><slot name="header" /><slot /></div>' },
  'el-alert': { template: '<div class="el-alert" :data-title="title"><slot /></div>', props: ['title', 'type', 'closable', 'description'] },
  'el-tag': { template: '<span class="el-tag"><slot /></span>', props: ['size', 'type'] },
  'el-tooltip': { template: '<span class="el-tooltip"><slot /></span>', props: ['content', 'placement'] },
  'el-icon': { template: '<span class="el-icon"><slot /></span>' },
  'el-descriptions': { template: '<div class="el-descriptions"><slot /></div>', props: ['column', 'size', 'border'] },
  'el-descriptions-item': { template: '<div class="el-descriptions-item"><slot /></div>', props: ['label'] },
  'el-input': { template: '<input />', props: ['modelValue'] },
  'el-select': { template: '<select><slot /></select>', props: ['modelValue'] },
  'el-option': { template: '<option />', props: ['label', 'value'] },
  'el-switch': { template: '<input type="checkbox" />', props: ['modelValue'] },
  'el-table': { template: '<div class="el-table" />' },
  'el-table-column': { template: '<div class="el-table-column" />' },
  'el-radio-group': { template: '<div class="el-radio-group"><slot /></div>', props: ['modelValue'] },
  'el-radio-button': { template: '<label><slot /></label>', props: ['label'] },
  'el-form': { template: '<form><slot /></form>' },
  'el-form-item': { template: '<div class="el-form-item"><slot /></div>', props: ['label'] },
  'el-checkbox': { template: '<label><input type="checkbox" /><slot /></label>', props: ['modelValue'] },
  'InfoFilled': { template: '<span />' },
  EtlPipeline: { template: '<div class="stub-etl-pipeline" />' },
  EtlKpiCards: { template: '<div class="stub-etl-kpi" />' },
  EtlAlertStatus: { template: '<div class="stub-etl-alert" />' },
  EtlReasonCodePie: { template: '<div class="stub-etl-pie" />' },
}

import AdminEtlView from '@/views/admin/AdminEtlView.vue'
import { etlApi } from '@/api'
import { ElMessage, ElMessageBox } from 'element-plus'

describe('AdminEtlView 全量重建 (V17-3.1)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    // 默认 ElMessageBox.confirm reject 模拟用户取消
    ;(ElMessageBox.confirm as any).mockRejectedValue(new Error('cancel'))
  })

  it('点击按钮触发二次确认对话框', async () => {
    const wrapper = mount(AdminEtlView, { global: { stubs: childStubs } })
    // 找到"执行全量重建"按钮 (danger type)
    const buttons = wrapper.findAll('button')
    const reindexBtns = buttons.filter(b => b.text().includes('执行全量重建'))
    expect(reindexBtns.length).toBe(1)  // 确保只匹配一个按钮
    const reindexBtn = reindexBtns[0]
    await reindexBtn.trigger('click')
    await flushPromises()
    expect(ElMessageBox.confirm).toHaveBeenCalledTimes(1)
    // 确认对话框文案含"全量重建"和"危险操作"
    const callArgs = (ElMessageBox.confirm as any).mock.calls[0]
    expect(callArgs[0]).toContain('全量重建')
    expect(callArgs[1]).toContain('危险操作')
  })

  it('用户取消确认 → etlApi.reindexAll 不被调用', async () => {
    ;(ElMessageBox.confirm as any).mockRejectedValue(new Error('cancel'))
    const wrapper = mount(AdminEtlView, { global: { stubs: childStubs } })
    const reindexBtn = wrapper.findAll('button').find(b => b.text().includes('执行全量重建'))
    await reindexBtn!.trigger('click')
    await flushPromises()
    expect(etlApi.reindexAll).not.toHaveBeenCalled()
    // ElMessage 不应被调用 (取消是无声退出)
    expect(ElMessage.success).not.toHaveBeenCalled()
    expect(ElMessage.warning).not.toHaveBeenCalled()
    expect(ElMessage.error).not.toHaveBeenCalled()
  })

  it('用户确认 + API 成功 → ElMessage.success 调用', async () => {
    ;(ElMessageBox.confirm as any).mockResolvedValue('confirm')
    ;(etlApi.reindexAll as any).mockResolvedValue({
      message: '全量重建完成: 直接=100, 入队=0',
      direct: 100,
      queued: 0,
      elapsedMs: 5000,
      error: null,
    })
    const wrapper = mount(AdminEtlView, { global: { stubs: childStubs } })
    const reindexBtn = wrapper.findAll('button').find(b => b.text().includes('执行全量重建'))
    await reindexBtn!.trigger('click')
    await flushPromises()
    expect(etlApi.reindexAll).toHaveBeenCalledTimes(1)
    expect(ElMessage.success).toHaveBeenCalledTimes(1)
    const msg = (ElMessage.success as any).mock.calls[0][0]
    expect(msg).toContain('全量重建完成')
    // 错误消息不应被调用
    expect(ElMessage.error).not.toHaveBeenCalled()
    expect(ElMessage.warning).not.toHaveBeenCalled()
    // lastReindex 应被设置 (显示 descriptions)
    expect(wrapper.find('.el-descriptions').exists()).toBe(true)
  })

  it('用户确认 + API 返回 error=CANCELLED → ElMessage.warning 调用', async () => {
    ;(ElMessageBox.confirm as any).mockResolvedValue('confirm')
    ;(etlApi.reindexAll as any).mockResolvedValue({
      message: '全量重建被取消',
      direct: 0,
      queued: 0,
      elapsedMs: 100,
      error: 'CANCELLED',
    })
    const wrapper = mount(AdminEtlView, { global: { stubs: childStubs } })
    const reindexBtn = wrapper.findAll('button').find(b => b.text().includes('执行全量重建'))
    await reindexBtn!.trigger('click')
    await flushPromises()
    expect(ElMessage.warning).toHaveBeenCalledTimes(1)
    expect((ElMessage.warning as any).mock.calls[0][0]).toContain('已被取消')
    // 成功/错误消息不应被调用
    expect(ElMessage.success).not.toHaveBeenCalled()
    expect(ElMessage.error).not.toHaveBeenCalled()
  })

  it('用户确认 + API 返回 error 非 null → ElMessage.error 调用', async () => {
    ;(ElMessageBox.confirm as any).mockResolvedValue('confirm')
    ;(etlApi.reindexAll as any).mockResolvedValue({
      message: '全量重建失败',
      direct: 0,
      queued: 0,
      elapsedMs: 50,
      error: 'MeiliSearch 连接失败',
    })
    const wrapper = mount(AdminEtlView, { global: { stubs: childStubs } })
    const reindexBtn = wrapper.findAll('button').find(b => b.text().includes('执行全量重建'))
    await reindexBtn!.trigger('click')
    await flushPromises()
    expect(ElMessage.error).toHaveBeenCalledTimes(1)
    expect((ElMessage.error as any).mock.calls[0][0]).toContain('MeiliSearch 连接失败')
    // 成功/警告消息不应被调用
    expect(ElMessage.success).not.toHaveBeenCalled()
    expect(ElMessage.warning).not.toHaveBeenCalled()
  })

  it('用户确认 + API 抛 409 → ElMessage.warning("已有 ETL 任务在运行") 调用', async () => {
    ;(ElMessageBox.confirm as any).mockResolvedValue('confirm')
    const err409: any = new Error('Request failed with status code 409')
    err409.response = { status: 409, data: { error: 'ETL 任务在运行' } }
    ;(etlApi.reindexAll as any).mockRejectedValue(err409)
    const wrapper = mount(AdminEtlView, { global: { stubs: childStubs } })
    const reindexBtn = wrapper.findAll('button').find(b => b.text().includes('执行全量重建'))
    await reindexBtn!.trigger('click')
    await flushPromises()
    expect(ElMessage.warning).toHaveBeenCalledTimes(1)
    expect((ElMessage.warning as any).mock.calls[0][0]).toContain('已有 ETL 任务在运行')
    // 成功/错误消息不应被调用 (409 是业务语义, 不是失败)
    expect(ElMessage.success).not.toHaveBeenCalled()
    expect(ElMessage.error).not.toHaveBeenCalled()
  })

  it('用户确认 + API 抛 500 → lastReindex 设置错误兜底', async () => {
    ;(ElMessageBox.confirm as any).mockResolvedValue('confirm')
    const err500: any = new Error('Internal Server Error')
    err500.response = { status: 500, data: { error: '数据库连接失败' } }
    ;(etlApi.reindexAll as any).mockRejectedValue(err500)
    const wrapper = mount(AdminEtlView, { global: { stubs: childStubs } })
    const reindexBtn = wrapper.findAll('button').find(b => b.text().includes('执行全量重建'))
    await reindexBtn!.trigger('click')
    await flushPromises()
    // 500 不是 409, 走 else 分支: lastReindex 设置错误兜底
    expect(wrapper.find('.el-descriptions').exists()).toBe(true)
    // el-alert 显示错误信息
    //   模板顶部常驻一个 warning el-alert (type=warning, 固定 title), 错误兜底 el-alert 是 type=error
    //   el-alert stub 渲染 :data-title="title", 用 attributes 定位错误兜底 alert
    const errorAlert = wrapper.findAll('.el-alert').find(a => {
      const t = a.attributes('data-title')
      return typeof t === 'string' && t.includes('数据库连接失败')
    })
    expect(errorAlert).toBeDefined()
    expect(errorAlert!.attributes('data-title')).toContain('数据库连接失败')
  })

  it('reindexing 状态在请求期间为 true, 请求完成后为 false', async () => {
    ;(ElMessageBox.confirm as any).mockResolvedValue('confirm')
    let resolveApi: (v: any) => void = () => {}
    ;(etlApi.reindexAll as any).mockReturnValue(new Promise(r => { resolveApi = r }))
    const wrapper = mount(AdminEtlView, { global: { stubs: childStubs } })
    const reindexBtn = wrapper.findAll('button').find(b => b.text().includes('执行全量重建'))!
    await reindexBtn.trigger('click')
    await flushPromises()
    // 请求进行中: loading = true
    expect(reindexBtn.attributes('data-loading')).toBe('true')
    // 请求完成
    resolveApi({ message: '完成', direct: 1, queued: 0, elapsedMs: 10, error: null })
    await flushPromises()
    // 请求完成: loading = false
    expect(reindexBtn.attributes('data-loading')).toBe('false')
  })
})
