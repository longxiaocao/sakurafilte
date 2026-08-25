import { reactive, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { publicCompareApi } from '@/api'
import type { PublicProductDetail } from '@/api/types'

// V3(2026-08-25) 用户反馈: 加入对比后没有常驻浮动 N/6 控件, 客户没地方点"查看对比".
//   全局共享对比状态 (模块级单例) — 任何页面 (聚合搜索/高级搜索/详情页) 的
//   加入对比/移除/清空/查看 都走这里, 全局浮动栏 CompareFloatingBar 消费.
//   持久化: sessionStorage (key 与 PublicSearchView 历史一致, 兼容已存数据)
export const MAX_COMPARE = 6
export const COMPARE_STORAGE_KEY = 'sakurafilter_compare_ids'

interface CompareState {
  ids: number[]
  open: boolean
  products: PublicProductDetail[]
  loading: boolean
}

const state = reactive<CompareState>({
  ids: [],
  open: false,
  products: [],
  loading: false,
})

// 初始化: 从 sessionStorage 恢复 (兼容 PublicSearchView 旧实现写入的数据)
function restore() {
  try {
    const raw = sessionStorage.getItem(COMPARE_STORAGE_KEY)
    if (raw) {
      const list = (JSON.parse(raw) as number[]).filter((n) => Number.isInteger(n) && n > 0)
      state.ids = list.slice(0, MAX_COMPARE)
    }
  } catch { /* 忽略损坏数据 */ }
}
restore()

// 任何变化持久化 (统一入口, 替代各页面手写)
watch(
  () => state.ids,
  (ids) => {
    try {
      sessionStorage.setItem(COMPARE_STORAGE_KEY, JSON.stringify(ids))
    } catch { /* 隐私模式忽略 */ }
  },
  { deep: true }
)

function persist() {
  try {
    sessionStorage.setItem(COMPARE_STORAGE_KEY, JSON.stringify(state.ids))
  } catch { /* 忽略 */ }
}

async function fetchProducts() {
  if (state.ids.length === 0) return
  state.loading = true
  try {
    const data = await publicCompareApi.compare(state.ids)
    const map = new Map(data.items.map((p) => [p.id, p]))
    state.products = state.ids.map((id) => map.get(id)).filter((p): p is PublicProductDetail => !!p)
  } catch (e: any) {
    ElMessage.error(e?.problem?.detail || e?.response?.data?.error || e?.message || '对比加载失败')
  } finally {
    state.loading = false
  }
}

export function useCompareStore() {
  return {
    state,

    get count() {
      return state.ids.length
    },

    /** 加入对比 (任意页面调用). 返回是否成功 */
    add(id: number): boolean {
      if (state.ids.includes(id)) {
        ElMessage.info('已在对比列表中')
        return false
      }
      if (state.ids.length >= MAX_COMPARE) {
        ElMessage.warning({ message: `最多对比 ${MAX_COMPARE} 个产品, 可点击浮动栏"清空对比"后重新添加`, duration: 4000 })
        return false
      }
      state.ids.push(id)
      persist()
      // 预拉详情, 打开抽屉时无延迟 (失败静默, 打开时重试)
      if (state.products.length === 0) fetchProducts().catch(() => {})
      ElMessage.success(`已加入对比 (${state.ids.length}/${MAX_COMPARE})`)
      return true
    },

    remove(id: number) {
      state.ids = state.ids.filter((x) => x !== id)
      state.products = state.products.filter((p) => p.id !== id)
      persist()
      if (state.ids.length === 0) state.open = false
    },

    clear() {
      state.ids = []
      state.products = []
      state.open = false
      persist()
      ElMessage.success('已清空对比')
    },

    /** 打开对比抽屉 (首次打开拉详情) */
    async openCompare() {
      if (state.ids.length === 0) {
        ElMessage.warning('请先在结果中点击"加入对比"')
        return
      }
      state.open = true
      if (state.products.length === 0) await fetchProducts()
    },

    close() {
      state.open = false
    },

    /** URL ?compare= 落地: 只填入 ids 不开抽屉 (用户主动点浮动栏/摘要条才开) */
    adoptFromUrl(ids: number[]) {
      state.ids = ids.slice(0, MAX_COMPARE)
      persist()
      if (state.products.length === 0) fetchProducts().catch(() => {})
    },

    move(idx: number, dir: -1 | 1) {
      const target = idx + dir
      if (target < 0 || target >= state.products.length) return
      const arr = [...state.products]
      ;[arr[idx], arr[target]] = [arr[target], arr[idx]]
      state.products = arr
      state.ids = arr.map((p) => p.id)
      persist()
    },
  }
}
