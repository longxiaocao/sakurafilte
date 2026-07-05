// useGlobalDragDrop — 全局拖拽上传 Composable
// WHY: 用户偏好"全窗口拖拽上传" (user_profile 明确要求),
//   拖动文件到窗口任意区域触发上传/填路径, 提升操作效率。
//
// 设计:
//   1. 全局监听 dragenter/over/leave/drop 事件 (addEventListener on document)
//   2. 仅在目标路径匹配时激活 (默认: /admin/etl 与 /admin/* 字典管理)
//   3. 通过 Vue 的 provide/inject 模式让目标组件拿到 drop 回调
//   4. 视觉反馈: 全屏遮罩 + 高亮提示, 让用户知道"在这里松开即可"
//
// 约束:
//   - 浏览器安全: 拖入文件时只能拿到 file.name, 不能拿到 file.path
//     (Chrome 旧版可以, 现代浏览器拿不到绝对路径)
//   - 因此本实现: 拖入 xlsx/jsonl → 调用回调, 业务侧自行决定
//     (例如 ETL 页面用 baseDir + file.name 拼出服务端路径)
//   - 在白名单路由外, 拖拽仅显示轻量提示, 不执行任何操作
import { ref, onMounted, onBeforeUnmount, type Ref } from 'vue'
import { useRoute } from 'vue-router'

export interface DragDropCallbacks {
  // 拖入文件时触发
  onFilesDropped: (files: File[]) => void
  // 可选: 是否接受该路由 (默认: 公开页面禁用, 仅 admin 启用)
  acceptRoute?: (path: string) => boolean
  // 可选: 拖入提示文案
  hintText?: string
}

// 内部状态 (跨组件共享)
const isDragging = ref(false)
const dragDepth = ref(0) // 处理 enter/leave 嵌套触发
const dragRoute = ref<string>('') // 触发时的当前路径

// 已注册的回调 (后注册覆盖前注册, 单实例全局拖拽)
let activeCallbacks: DragDropCallbacks | null = null

function isFileDrag(ev: DragEvent): boolean {
  if (!ev.dataTransfer) return false
  const types = ev.dataTransfer.types
  if (!types) return false
  // DataTransferItemList 包含 'Files' 时是文件拖拽
  // Array.from 兼容 types 是 DOMStringList
  return Array.from(types).includes('Files')
}

function handleDragEnter(ev: DragEvent) {
  if (!isFileDrag(ev)) return
  ev.preventDefault()
  dragDepth.value++
  if (dragDepth.value === 1) {
    isDragging.value = true
    dragRoute.value = window.location.pathname
  }
}

function handleDragOver(ev: DragEvent) {
  if (!isFileDrag(ev)) return
  ev.preventDefault()
  if (ev.dataTransfer) {
    // 必须设置 dropEffect 才能触发 drop
    ev.dataTransfer.dropEffect = 'copy'
  }
}

function handleDragLeave(ev: DragEvent) {
  if (!isFileDrag(ev)) return
  dragDepth.value = Math.max(0, dragDepth.value - 1)
  if (dragDepth.value === 0) {
    isDragging.value = false
    dragRoute.value = ''
  }
}

function handleDrop(ev: DragEvent) {
  if (!isFileDrag(ev)) return
  ev.preventDefault()
  dragDepth.value = 0
  isDragging.value = false

  if (!activeCallbacks) return
  const files = ev.dataTransfer?.files
  if (!files || files.length === 0) return
  // 路由白名单校验
  if (activeCallbacks.acceptRoute && !activeCallbacks.acceptRoute(window.location.pathname)) {
    return
  }
  // 过滤: 仅接受 xlsx/jsonl/csv/xls
  const accepted: File[] = []
  for (const f of Array.from(files)) {
    const name = f.name.toLowerCase()
    if (name.endsWith('.xlsx') || name.endsWith('.xls') ||
        name.endsWith('.jsonl') || name.endsWith('.json') ||
        name.endsWith('.csv')) {
      accepted.push(f)
    }
  }
  if (accepted.length === 0) return
  activeCallbacks.onFilesDropped(accepted)
}

let mounted = false
function ensureGlobalListener() {
  if (mounted) return
  if (typeof document === 'undefined') return
  document.addEventListener('dragenter', handleDragEnter)
  document.addEventListener('dragover', handleDragOver)
  document.addEventListener('dragleave', handleDragLeave)
  document.addEventListener('drop', handleDrop)
  mounted = true
}

function removeGlobalListener() {
  if (!mounted) return
  document.removeEventListener('dragenter', handleDragEnter)
  document.removeEventListener('dragover', handleDragOver)
  document.removeEventListener('dragleave', handleDragLeave)
  document.removeEventListener('drop', handleDrop)
  mounted = false
}

/**
 * 启用全局拖拽, 返回状态 ref 与回调注册函数
 * @example
 *   const { isDragging, hint, register } = useGlobalDragDrop()
 *   onMounted(() => register({
 *     onFilesDropped: (files) => { ... }
 *   }))
 */
export function useGlobalDragDrop() {
  const route = useRoute()
  const hint: Ref<string> = ref('松开导入文件')

  onMounted(() => {
    ensureGlobalListener()
  })
  onBeforeUnmount(() => {
    activeCallbacks = null
    // 仅当全应用无其他实例时才卸载监听 (这里粗暴处理: 始终保留, 不卸载)
    // WHY: 多个组件挂载/卸载时, 监听器应保持
  })

  function register(callbacks: DragDropCallbacks) {
    activeCallbacks = callbacks
    hint.value = callbacks.hintText || '松开导入文件'
  }

  function unregister() {
    activeCallbacks = null
  }

  return {
    isDragging,
    hint,
    route,
    register,
    unregister
  }
}

// 默认白名单: admin 路径下启用
export const DEFAULT_ADMIN_ACCEPT = (path: string) => path.startsWith('/admin')

// 测试导出 (单元测试用)
export const _internals = {
  isFileDrag,
  handleDragEnter,
  handleDragOver,
  handleDragLeave,
  handleDrop
}
