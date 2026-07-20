// P1-1 DictManagerLayout 提取: 字典管理页通用 composable
//   WHY: 8 个字典页 (AdminEnginesView/AdminTypesView/.../AdminProductName2sView) ~80% 代码逐字重复
//        提取 state + CRUD + 拖拽 + 辅助为可复用 composable, 消除 1477 行重复
//   设计文档: .trae/specs/v2-architecture-migration/design-dict-manager-layout.md
//   关联 ADR: #13 (DictManagerLayout 提取决策)
import { ref, reactive, computed, onMounted, type Ref, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useVisibilityRefresh } from '@/composables/useVisibilityRefresh'

// ========== 类型定义 ==========

/** 字典项通用接口 (所有 *Item 类型都满足) */
export interface DictItem {
  id: number
  sortOrder: number
  xrefCount: number
  updatedAt?: string
  deletedAt?: string | null
}

/** Reorder 项通用接口 (所有 *ReorderItem 类型都满足) */
export interface DictReorderItem {
  id: number
  sortOrder: number
}

/** useDictManager 配置 */
export interface DictManagerConfig<
  T extends DictItem,
  R extends DictReorderItem
> {
  /** 字典 API 命名空间, 如 dictApi.engines */
  api: {
    list: (
      search?: string,
      includeDeleted?: boolean,
      limit?: number
    ) => Promise<{ items: T[] }>
    create: (...args: any[]) => Promise<unknown>
    update: (id: number, payload: any) => Promise<unknown>
    delete: (id: number) => Promise<unknown>
    restore: (id: number) => Promise<unknown>
    reorder: (items: R[]) => Promise<unknown>
  }
  /** 默认空表单 (用于 openCreate 时 reset) */
  emptyForm: () => Record<string, any>
  /** 从 row 提取 dialogForm (用于 openEdit) */
  rowToForm: (row: T) => Record<string, any>
  /** 校验 dialogForm, 返回 { ok, errMsg? } */
  validate: (form: Record<string, any>) => { ok: boolean; errMsg?: string }
  /** 从 dialogForm 转换为 create API 入参 (数组, 对应 api.create(...args)) */
  formToCreatePayload: (form: Record<string, any>) => any[]
  /** 从 dialogForm 转换为 update API 入参 */
  formToUpdatePayload: (form: Record<string, any>) => any
  /** softDelete 确认文案 (可基于 row 自定义, 如 AdminTypesView 固定 5 值警告) */
  softDeleteMessage?: (row: T) => string
  /** reorder 成功 i18n key, 默认 common.action.sort_order_saved */
  reorderSuccessKey?: string
}

/** useDictManager 返回值 */
export interface DictManagerReturn<T extends DictItem> {
  // 状态
  items: Ref<T[]>
  loading: Ref<boolean>
  loadError: Ref<string | null>
  includeDeleted: Ref<boolean>
  searchKw: Ref<string>
  dialogOpen: Ref<boolean>
  dialogMode: Ref<'create' | 'edit'>
  dialogForm: Record<string, any>
  draggingId: Ref<number | null>
  dragOverId: Ref<number | null>
  // computed
  total: ComputedRef<number>
  activeCount: ComputedRef<number>
  // 方法
  load: () => Promise<void>
  onSearch: () => void
  openCreate: () => void
  openEdit: (row: T) => void
  saveDialog: () => Promise<void>
  softDelete: (row: T) => Promise<void>
  restore: (row: T) => Promise<void>
  onDragStart: (e: DragEvent, id: number) => void
  onDragOver: (e: DragEvent, id: number) => void
  onDragLeave: (e: DragEvent, id: number) => void
  onDrop: (e: DragEvent, targetId: number) => Promise<void>
  onDragEnd: () => void
  fmtDate: (iso?: string) => string
  isDraggable: (row: T) => boolean
  rowClass: (row: T) => string
}

// ========== composable 实现 ==========

export function useDictManager<
  T extends DictItem,
  R extends DictReorderItem
>(config: DictManagerConfig<T, R>): DictManagerReturn<T> {
  const { t } = useI18n()
  const {
    api,
    emptyForm,
    rowToForm,
    validate,
    formToCreatePayload,
    formToUpdatePayload,
    softDeleteMessage,
    reorderSuccessKey,
  } = config

  // ========== state ==========
  const items = ref<T[]>([]) as Ref<T[]>
  const loading = ref(false)
  // V24-F102 (P2-2, 规则 8): 加 loadError, 加载失败时显示持久 el-alert + 重试按钮
  const loadError = ref<string | null>(null)
  const includeDeleted = ref(false)
  const searchKw = ref('')

  const dialogOpen = ref(false)
  const dialogMode = ref<'create' | 'edit'>('create')
  const dialogForm = reactive<Record<string, any>>(emptyForm())

  const draggingId = ref<number | null>(null)
  const dragOverId = ref<number | null>(null)

  // ========== load ==========
  async function load() {
    loading.value = true
    // V24-F102 (P2-2, 规则 8): 进入 load 时清空 loadError, 避免上次失败提示残留
    loadError.value = null
    try {
      const { items: list } = await api.list(
        searchKw.value || undefined,
        includeDeleted.value,
        500
      )
      items.value = list
    } catch (e: any) {
      ElMessage.error(t('common.action.load_failed') + (e?.message || ''))
      // V24-F102 (P2-2, 规则 8): 持久 error UI, 让用户能看到错误并重试
      loadError.value =
        e?.response?.data?.detail || e?.message || '字典加载失败'
    } finally {
      loading.value = false
    }
  }
  function onSearch() {
    load()
  }

  // ========== CRUD ==========
  function openCreate() {
    dialogMode.value = 'create'
    Object.assign(dialogForm, emptyForm())
    // 新增时 sortOrder 默认为当前最大值 + 10
    const maxSort = items.value
      .filter((x) => !x.deletedAt)
      .reduce((m, x) => Math.max(m, x.sortOrder), 0)
    dialogForm.sortOrder = maxSort + 10
    dialogOpen.value = true
  }
  function openEdit(row: T) {
    dialogMode.value = 'edit'
    Object.assign(dialogForm, rowToForm(row))
    dialogOpen.value = true
  }
  async function saveDialog() {
    const { ok, errMsg } = validate(dialogForm)
    if (!ok) {
      if (errMsg) ElMessage.warning(errMsg)
      return
    }
    try {
      if (dialogMode.value === 'create') {
        await api.create(...formToCreatePayload(dialogForm))
        ElMessage.success(t('common.action.created'))
      } else if (dialogForm.id != null) {
        await api.update(dialogForm.id as number, formToUpdatePayload(dialogForm))
        ElMessage.success(t('common.action.updated'))
      }
      dialogOpen.value = false
      await load()
    } catch (e: any) {
      ElMessage.error(
        e?.response?.data?.detail || e?.message || t('common.action.operation_failed')
      )
    }
  }
  async function softDelete(row: T) {
    // softDeleteMessage 可基于 row 自定义 (如 AdminTypesView 固定 5 值警告)
    const msg = softDeleteMessage
      ? softDeleteMessage(row)
      : `确定删除 "${(row as any).name ?? row.id}" 吗? (软删除)`
    try {
      await ElMessageBox.confirm(msg, t('common.action.confirm'), {
        type: 'warning',
      })
    } catch {
      return
    }
    try {
      await api.delete(row.id)
      ElMessage.success(t('common.action.deleted'))
      await load()
    } catch (e: any) {
      ElMessage.error(
        e?.response?.data?.detail || e?.message || t('common.action.delete_failed')
      )
    }
  }
  async function restore(row: T) {
    try {
      await api.restore(row.id)
      ElMessage.success(t('common.action.restored'))
      await load()
    } catch (e: any) {
      ElMessage.error(
        e?.response?.data?.detail || e?.message || t('common.action.restore_failed')
      )
    }
  }

  // ========== 拖拽 (与 8 页现有实现逐字一致, 不做任何修改) ==========
  function onDragStart(e: DragEvent, id: number) {
    draggingId.value = id
    if (e.dataTransfer) {
      e.dataTransfer.effectAllowed = 'move'
      e.dataTransfer.setData('text/plain', String(id))
    }
  }
  function onDragOver(e: DragEvent, id: number) {
    e.preventDefault()
    if (e.dataTransfer) e.dataTransfer.dropEffect = 'move'
    if (draggingId.value !== id) dragOverId.value = id
  }
  function onDragLeave(_e: DragEvent, id: number) {
    if (dragOverId.value === id) dragOverId.value = null
  }
  async function onDrop(e: DragEvent, targetId: number) {
    e.preventDefault()
    const sourceId = draggingId.value
    dragOverId.value = null
    draggingId.value = null
    if (sourceId == null || sourceId === targetId) return
    const sourceIdx = items.value.findIndex((x) => x.id === sourceId)
    const targetIdx = items.value.findIndex((x) => x.id === targetId)
    if (sourceIdx < 0 || targetIdx < 0) return
    const moved = items.value.splice(sourceIdx, 1)[0]
    items.value.splice(targetIdx, 0, moved)
    const updates: R[] = items.value.map((it, idx) => ({
      id: it.id,
      sortOrder: (idx + 1) * 10,
    })) as R[]
    items.value.forEach((it, idx) => {
      it.sortOrder = (idx + 1) * 10
    })
    try {
      await api.reorder(updates)
      ElMessage.success(t(reorderSuccessKey ?? 'common.action.sort_order_saved'))
    } catch (e: any) {
      ElMessage.error(
        e?.response?.data?.detail || e?.message || t('common.action.sort_failed')
      )
      // 失败回滚: 重新加载
      await load()
    }
  }
  function onDragEnd() {
    draggingId.value = null
    dragOverId.value = null
  }

  // ========== 辅助 ==========
  function fmtDate(iso?: string) {
    return iso ? iso.substring(0, 16).replace('T', ' ') : ''
  }
  function isDraggable(row: T) {
    // 拖拽禁用已删项 (避免打乱含已删集合的视觉顺序)
    return !row.deletedAt
  }
  function rowClass(row: T): string {
    const c: string[] = []
    if (row.deletedAt) c.push('dict-row--deleted')
    if (draggingId.value === row.id) c.push('dict-row--dragging')
    if (dragOverId.value === row.id) c.push('dict-row--dragover')
    return c.join(' ')
  }

  // ========== computed ==========
  const total = computed(() => items.value.length)
  const activeCount = computed(() =>
    items.value.filter((x) => !x.deletedAt).length
  )

  // ========== 生命周期 ==========
  // V24-F103 (P2-2): 跨标签页 stale 数据感知, 页面重新可见时自动刷新
  useVisibilityRefresh(load)
  onMounted(load)

  return {
    items,
    loading,
    loadError,
    includeDeleted,
    searchKw,
    dialogOpen,
    dialogMode,
    dialogForm,
    draggingId,
    dragOverId,
    total,
    activeCount,
    load,
    onSearch,
    openCreate,
    openEdit,
    saveDialog,
    softDelete,
    restore,
    onDragStart,
    onDragOver,
    onDragLeave,
    onDrop,
    onDragEnd,
    fmtDate,
    isDraggable,
    rowClass,
  }
}
