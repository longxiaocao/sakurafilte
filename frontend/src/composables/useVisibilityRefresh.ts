// V24-F103 (P2-2): 跨标签页 stale 数据感知 composable
//   WHY: 字典页用户跨标签页编辑后, 回到本标签页看到的是旧数据, 误导
//   方案: 监听 document.visibilitychange 事件, 页面重新可见时调 refreshFn 刷新
//   替代方案: 30s 定时刷新 (成本更高, 字典页 stale 影响小, 不推荐)
//   复用: 8 个字典页 + AdminApiDocsView 等需刷新的页面均可使用
//   P1-1 铺路: 未来 DictManagerLayout 提取时, useDictManager 内部可直接调用此 composable
import { onMounted, onBeforeUnmount } from 'vue'

/**
 * 监听页面可见性变化, 页面重新可见时触发刷新
 * @param refreshFn 刷新函数 (通常为 load/fetchData)
 */
export function useVisibilityRefresh(refreshFn: () => void | Promise<void>): void {
  function onVisChange() {
    // 仅当页面从隐藏切回可见时触发, 初次加载由 onMounted(load) 处理
    if (document.visibilityState === 'visible') {
      refreshFn()
    }
  }

  onMounted(() => {
    document.addEventListener('visibilitychange', onVisChange)
  })

  onBeforeUnmount(() => {
    document.removeEventListener('visibilitychange', onVisChange)
  })
}
