/**
 * V24-F47 (spec Task 5.1.25): 表单数据 localStorage 持久化 composable
 *   - debounce 500ms 自动保存 (避免每次 keystroke 都写 localStorage)
 *   - 7 天 TTL 自动过期清理 (避免无限积累)
 *   - 409 冲突时调用 restore() 恢复本地草稿
 *   - 卸载时清理 debounce timer
 *
 * WHY localStorage 而非 sessionStorage:
 *   - 用户可能误关标签页 (sessionStorage 丢失), 但后台表单填写耗时较长
 *   - localStorage 跨标签页/会话保留, 7 天 TTL 防止无限积累
 *
 * 设计要点:
 *   - 仅在表单"脏" (form 发生变化) 时保存, 避免无变化重复写
 *   - 保存时附带 timestamp, restore 时检查 TTL
 *   - save/clear 不抛异常 (localStorage 满或隐私模式时静默降级)
 *   - 支持任意 reactive 对象 (内部 JSON.stringify 快照)
 *
 * @example
 * ```ts
 * const draft = useFormDraft('product', () => form)
 * draft.startAutoSave()  // 启动 watch + debounce
 * // 409 冲突时:
 * draft.restoreOrPrompt()  // 弹窗询问是否恢复
 * onBeforeUnmount(() => draft.stopAutoSave())
 * ```
 */
import { watch, type WatchSource } from 'vue'

const DRAFT_TTL_MS = 7 * 24 * 60 * 60 * 1000  // 7 天 (毫秒)
const DEBOUNCE_MS = 500

interface DraftRecord<T> {
  data: T
  timestamp: number
  version: string
}

export interface UseFormDraftReturn<T> {
  /** 启动自动保存 (watch + debounce 500ms) */
  startAutoSave: () => void
  /** 停止自动保存 (清理 watch + debounce timer) */
  stopAutoSave: () => void
  /** 手动保存当前表单数据 */
  save: () => void
  /** 清除草稿 (保存成功后调用) */
  clear: () => void
  /** 读取草稿 (含 TTL 检查, 过期返回 null) */
  load: () => T | null
  /** 检查是否存在未过期的草稿 */
  hasDraft: () => boolean
}

/**
 * 创建表单草稿管理器
 * @param namespace 草稿命名空间 (如 'product', 'xref_reorder')
 * @param getSource 获取表单数据的函数 (返回 reactive 对象的快照)
 * @param key 草稿 key (如 mr1 编号, 用于区分不同记录的草稿)
 * @returns 草稿管理器
 */
export function useFormDraft<T>(
  namespace: string,
  getSource: () => T,
  key: string = 'default'
): UseFormDraftReturn<T> {
  const storageKey = `sakurafilter_draft_${namespace}_${key}`

  // 安全读写 localStorage (Safari 隐私模式 / 配额超限时降级)
  function safeGetItem(k: string): string | null {
    try {
      return localStorage.getItem(k)
    } catch {
      return null
    }
  }

  function safeSetItem(k: string, v: string): void {
    try {
      localStorage.setItem(k, v)
    } catch {
      // 隐私模式或配额超限: 静默降级, 不影响主流程
    }
  }

  function safeRemoveItem(k: string): void {
    try {
      localStorage.removeItem(k)
    } catch {
      // 静默降级
    }
  }

  // 原生 setTimeout 实现 debounce (避免引入 @vueuse/core 依赖)
  //   WHY 不用 @vueuse/core: 项目未引入, 按规则 4.3 不增加新依赖
  let debounceTimer: ReturnType<typeof setTimeout> | null = null

  function debouncedSave(): void {
    if (debounceTimer !== null) {
      clearTimeout(debounceTimer)
    }
    debounceTimer = setTimeout(() => {
      debounceTimer = null
      save()
    }, DEBOUNCE_MS)
  }

  // watch 停止函数 (startAutoSave 时设置, stopAutoSave 时调用)
  let stopWatch: (() => void) | null = null

  function save(): void {
    try {
      const data = getSource()
      const record: DraftRecord<T> = {
        data,
        timestamp: Date.now(),
        version: '1'  // 草稿格式版本, 后续格式变更时升级
      }
      safeSetItem(storageKey, JSON.stringify(record))
    } catch {
      // JSON.stringify 失败 (循环引用等) 或其他异常: 静默降级
    }
  }

  function load(): T | null {
    const raw = safeGetItem(storageKey)
    if (!raw) return null

    try {
      const record = JSON.parse(raw) as DraftRecord<T>
      // TTL 检查: 超过 7 天自动清理
      const age = Date.now() - record.timestamp
      if (age > DRAFT_TTL_MS) {
        safeRemoveItem(storageKey)
        return null
      }
      return record.data
    } catch {
      // JSON 解析失败 (草稿格式损坏): 清理并返回 null
      safeRemoveItem(storageKey)
      return null
    }
  }

  function clear(): void {
    safeRemoveItem(storageKey)
  }

  function hasDraft(): boolean {
    return load() !== null
  }

  function startAutoSave(): void {
    // 避免重复启动
    if (stopWatch !== null) return

    // deep watch: 表单嵌套字段变化也触发
    //   WHY getSource 返回 reactive 快照: watch source 函数, Vue 会自动追踪内部 reactive
    stopWatch = watch(
      getSource as WatchSource,
      () => {
        debouncedSave()
      },
      { deep: true }
    )
  }

  function stopAutoSave(): void {
    if (stopWatch !== null) {
      stopWatch()
      stopWatch = null
    }
    // 清理 debounce timer (避免组件卸载后仍触发保存)
    if (debounceTimer !== null) {
      clearTimeout(debounceTimer)
      debounceTimer = null
    }
  }

  return {
    startAutoSave,
    stopAutoSave,
    save,
    clear,
    load,
    hasDraft
  }
}
