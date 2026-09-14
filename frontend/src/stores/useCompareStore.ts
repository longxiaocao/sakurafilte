// 全局共享对比 store (模块级单例)
//   WHY: 产品详情页 / 8 字段高级搜索页 / 聚合搜索页都能无缝"加入对比",
//       不因路由跳转而丢失已选集合。此前详情页加入对比被迫 router.push 到
//       高级搜索页 (用户反馈体验断裂), 现统一走此 store, 各页共用同一响应式集合。
//   持久化: sessionStorage key = 'sakurafilter_compare_ids' (与旧版一致, 无缝兼容)
//   抽屉/悬浮按钮由全局组件 GlobalCompare.vue 统一渲染 (挂载于 App.vue)
import { computed, readonly, ref } from 'vue'

/** 最大对比数 (与 后台 PublicCompareView/PublicSearchView.MAX_COMPARE 一致) */
export const MAX_COMPARE = 6

const STORAGE_KEY = 'sakurafilter_compare_ids'

function loadPersisted(): Set<number> {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    if (raw) {
      const ids = JSON.parse(raw).filter((n: unknown) => Number.isInteger(n) && (n as number) > 0)
      return new Set<number>(ids.slice(0, MAX_COMPARE))
    }
  } catch { /* 隐私模式等场景忽略损坏数据 */ }
  return new Set<number>()
}

function persist(ids: Set<number>) {
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(Array.from(ids)))
  } catch { /* 隐私模式等场景忽略 */ }
}

const compareIds = ref<Set<number>>(loadPersisted())

// "打开对比抽屉" 请求信号: 各页(搜索栏/详情页等)想要主动打开全局抽屉时调用 requestOpen()
//   GlobalCompare.vue 会 watch pendingOpen, 打开抽屉后 consumeOpen() 清零
const pendingOpen = ref(0)
function requestOpen() { pendingOpen.value += 1 }
function consumeOpen() { pendingOpen.value = 0 }

const count = computed(() => compareIds.value.size)
const has = (id: number) => compareIds.value.has(id)

function add(id: number) {
  if (compareIds.value.has(id)) return { ok: true, reason: 'existing' }
  if (compareIds.value.size >= MAX_COMPARE) return { ok: false, reason: 'full' }
  const next = new Set(compareIds.value)
  next.add(id)
  compareIds.value = next
  persist(next)
  return { ok: true, reason: 'added' }
}

function remove(id: number) {
  if (!compareIds.value.has(id)) return
  const next = new Set(compareIds.value)
  next.delete(id)
  compareIds.value = next
  persist(next)
}

function clear() {
  compareIds.value = new Set()
  try { sessionStorage.removeItem(STORAGE_KEY) } catch { /* ignore */ }
}

/** 用一组 id 覆盖 (应用于 URL ?compare= 恢复等场景) */
function replace(ids: number[]) {
  const valid = ids.filter((n) => Number.isInteger(n) && n > 0).slice(0, MAX_COMPARE)
  compareIds.value = new Set(valid)
  if (valid.length > 0) persist(compareIds.value)
}

export function useCompareStore() {
  return {
    ids: readonly(compareIds),
    count,
    has,
    add,
    remove,
    clear,
    replace,
    pendingOpen: readonly(pendingOpen),
    requestOpen,
    consumeOpen,
  }
}