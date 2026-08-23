<script setup lang="ts">
// V2 Task 2.2.1: OEM 3 白名单管理页 (修复漏洞 13)
//   布局: 左侧 Brand 列表 + 右侧 vuedraggable 拖拽 OEM 3 排序
//   - 拖拽完成自动调 POST /api/admin/xrefs/reorder 保存 (Task 2.2.3)
//   - 含 rowVersion 透传 (xmin 乐观锁, 冲突返 409 XREF_CONFLICT)
//   - 409 时提示刷新重试 (Task 2.2.4)
//   - Musk 风格极简: 纯黑白 + 1px 细线 + 8px 网格
// V24-F86: 加分页 (pageSize=50) + oemNo3 搜索 + 单条 CRUD (新增/编辑/删除弹窗)
//   - 解决全量加载卡顿 + 无法维护 OEM 3 白名单
//   - 拖拽排序仅在当前页内生效, 翻页/搜索后仍可拖拽
//   - 删除为软删 (置 is_discontinued=true), 非物理删除
// 白名单改造:
//   - 列表只显示白名单内产品 (sort_order > 0), 不再展示该品牌下所有产品
//   - "添加到白名单": 新建 cross_reference, 后端自动 sort_order = max+1, 新增即入白名单
//   - "从白名单移除": 置 sort_order = 0 (产品本身不删, 仅不再优先展示)
//   - 添加弹窗按 selectedBrand 过滤产品搜索 (adminProductApi.search 加 oemBrand 参数)
import { ref, onMounted, reactive, onBeforeUnmount } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import draggable from 'vuedraggable'
import { adminXrefApi, adminProductApi } from '@/api'
import type {
  XrefBrandItem,
  XrefOem3Item,
  XrefOem3CreatePayload,
  XrefOem3UpdatePayload,
  ProductListItem
} from '@/api/types'

const { t } = useI18n()

// ===== Brand 列表状态 =====
const brands = ref<XrefBrandItem[]>([])
const selectedBrand = ref<string>('')
const loadingBrands = ref(false)

// ===== OEM 3 列表状态 (V24-F86: 加分页 + 搜索) =====
const oemList = ref<XrefOem3Item[]>([])
const loadingOem = ref(false)
const saving = ref(false)
// 拖拽过程中本地副本, 拖完才提交
const dragList = ref<XrefOem3Item[]>([])
// 分页状态
const page = ref(1)
const pageSize = ref(50)
const total = ref(0)
const totalPages = ref(0)
// 搜索状态 (oemNo3 模糊匹配, 防抖 300ms)
const searchQ = ref('')
let searchTimer: ReturnType<typeof setTimeout> | null = null

// ===== 弹窗状态 (V24-F86: 新增/编辑) =====
const dialogVisible = ref(false)
const dialogMode = ref<'create' | 'edit'>('create')
const dialogLoading = ref(false)
const editingId = ref<number | null>(null)
const form = reactive({
  productId: 0,
  oemBrand: '',
  oemNo3: '',
  oem2: '',
  machineType: 'others',
  isPublished: true,
  rowVersion: 0
})
// product 联想 (el-select remote)
const productOptions = ref<ProductListItem[]>([])
const productLoading = ref(false)

// ===== 加载 Brand 列表 =====
async function loadBrands() {
  loadingBrands.value = true
  try {
    const resp = await adminXrefApi.listBrands()
    brands.value = resp.items
    // 默认选第一个
    if (brands.value.length > 0 && !selectedBrand.value) {
      selectedBrand.value = brands.value[0].brand
      await loadOemList()
    }
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.detail || t('admin.xrefreorder.err_load_brands'))
  } finally {
    loadingBrands.value = false
  }
}

// ===== 加载某 Brand 下 OEM 3 列表 (V24-F86: 加分页 + 搜索参数) =====
async function loadOemList() {
  if (!selectedBrand.value) {
    oemList.value = []
    dragList.value = []
    total.value = 0
    totalPages.value = 0
    return
  }
  loadingOem.value = true
  try {
    const resp = await adminXrefApi.listByBrand(selectedBrand.value, {
      page: page.value,
      pageSize: pageSize.value,
      q: searchQ.value || undefined
    })
    oemList.value = resp.items
    // 复制一份用于拖拽 (避免直接改 oemList 触发响应式重渲染)
    dragList.value = [...resp.items]
    total.value = resp.total
    totalPages.value = resp.totalPages
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.detail || t('admin.xrefreorder.err_load_oem_list'))
    oemList.value = []
    dragList.value = []
    total.value = 0
    totalPages.value = 0
  } finally {
    loadingOem.value = false
  }
}

// ===== 切换 Brand (重置分页 + 搜索) =====
async function selectBrand(brand: string) {
  selectedBrand.value = brand
  page.value = 1
  searchQ.value = ''
  await loadOemList()
}

// ===== 搜索输入 (防抖 300ms) =====
function onSearchInput() {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(() => {
    page.value = 1
    loadOemList()
  }, 300)
}

// ===== 翻页 =====
async function onPageChange(p: number) {
  page.value = p
  await loadOemList()
}
async function onPageSizeChange(ps: number) {
  pageSize.value = ps
  page.value = 1
  await loadOemList()
}

// ===== 拖拽完成自动保存 (Task 2.2.3 + 改进 2.3: 409 自动重试 1 次) =====
//   WHY 自动重试: 后端 xmin 乐观锁在他人修改后立即返 409, 多数情况是"用户停留过久未刷新"导致
//     自动 reload 获取新 rowVersion 后用最新顺序重试一次, 可消除 90% 的非真冲突场景
//   边界: 仅重试 1 次, 避免死循环; 真正并发冲突时第二次仍 409 → 弹框提示用户手动决策
//   V24-F86: 拖拽仅在当前页内生效, items 只含当前页数据
async function onDragEnd() {
  if (dragList.value.length === 0) return
  // 检查顺序是否有变化 (避免无变化时多余的 API 调用)
  const hasChange = dragList.value.some((item, idx) => item.sortOrder !== idx + 1)
  if (!hasChange) return
  await saveReorder(false)
}

// 改进 2.3: 抽取保存逻辑, 支持 isRetry 参数避免重试递归
//   isRetry=true 表示当前是 409 后的自动重试, 再失败直接弹框, 不再重试
async function saveReorder(isRetry: boolean) {
  // 重新计算 sortOrder (1-based, 拖拽顺序即排序)
  // 改进 2.3: 保留用户拖拽后的 dragList 副本, 重试时仅刷新 rowVersion, 不丢失用户意图
  const userOrderedDragList = [...dragList.value]
  const items = userOrderedDragList.map((item, idx) => ({
    id: item.id,  // 🔧 fix: 联调发现 oemNo3 不唯一, 必须用 Id 主键定位 UPDATE
    oemNo3: item.oemNo3,
    sortOrder: idx + 1,
    rowVersion: item.rowVersion  // 透传 xmin 乐观锁令牌
  }))

  saving.value = true
  try {
    await adminXrefApi.reorder({
      oemBrand: selectedBrand.value,
      items
    })
    // 保存成功: 更新本地 sortOrder + rowVersion (后端 xmin 变了, 下次需要新值)
    // WHY 重新加载: 后端 UPDATE 后 xmin 自动变化, 前端持有的 rowVersion 已失效
    //   下次拖拽必须用最新 rowVersion, 否则会触发 409
    await loadOemList()
    ElMessage.success(t('admin.xrefreorder.success_saved_order', { count: items.length }))
  } catch (e: any) {
    // Task 2.2.4: 409 XREF_CONFLICT 处理
    const status = e?.response?.status
    const errorCode = e?.response?.data?.errorCode || e?.response?.data?.extensions?.errorCode
    if (status === 409 || errorCode === 'XREF_CONFLICT') {
      // 改进 2.3: 首次 409 时拉取最新 rowVersion + 用用户拖拽顺序重试 1 次
      if (!isRetry) {
        try {
          // WHY 临时拉取: 仅为了拿最新的 rowVersion, 不覆盖 dragList (保留用户拖拽意图)
          //   V24-F86: 带当前页分页参数, 否则拉到的可能是不同页数据, id 映射会缺失
          const fresh = await adminXrefApi.listByBrand(selectedBrand.value, {
            page: page.value,
            pageSize: pageSize.value,
            q: searchQ.value || undefined
          })
          // 构建 id → rowVersion 映射, 用于更新 userOrderedDragList
          //   🔧 fix: 用 id 而非 oemNo3 (联调发现 oemNo3 不唯一, 会拿错 rowVersion)
          const rvMap = new Map(fresh.items.map((it) => [it.id, it.rowVersion]))
          // 边界: 用户拖拽的某项已被他人删除 → rvMap 取不到 → 终止重试
          const missingItem = userOrderedDragList.find((it) => !rvMap.has(it.id))
          if (missingItem) {
            ElMessage.warning(t('admin.xrefreorder.warn_item_deleted', { oemNo3: missingItem.oemNo3 }))
            await loadOemList()
            return
          }
          // 用最新 rowVersion + 用户拖拽顺序覆盖 dragList, 然后重试
          dragList.value = userOrderedDragList.map((it) => ({
            ...it,
            rowVersion: rvMap.get(it.id) as number
          }))
          // 重试 (递归 1 次, isRetry=true, 再 409 会走下方弹框分支)
          await saveReorder(true)
          return
        } catch (reloadErr) {
          // 拉取最新列表本身失败 (网络/服务异常) → 降级为弹框
          console.error('重试时拉取最新 rowVersion 失败', reloadErr)
        }
      }
      // 第二次仍 409 或重试拉取失败: 真并发冲突, 弹框让用户决策
      ElMessageBox.confirm(
        t('admin.xrefreorder.conflict_message'),
        t('admin.xrefreorder.conflict_title'),
        { confirmButtonText: t('admin.xrefreorder.refresh'), cancelButtonText: t('admin.xrefreorder.cancel'), type: 'warning' }
      ).then(() => loadOemList()).catch(() => {})
    } else {
      ElMessage.error(e?.response?.data?.detail || t('admin.xrefreorder.err_save_order'))
    }
  } finally {
    saving.value = false
  }
}

// ===== 手动批量保存 (备用, 拖拽自动保存失败时使用) =====
async function manualSave() {
  await onDragEnd()
}

// ===== V24-F86: 新增弹窗 =====
function openCreateDialog() {
  dialogMode.value = 'create'
  editingId.value = null
  form.productId = 0
  form.oemBrand = selectedBrand.value  // 默认填充当前选中 brand
  form.oemNo3 = ''
  form.oem2 = ''
  form.machineType = 'others'
  form.isPublished = true
  form.rowVersion = 0
  productOptions.value = []
  dialogVisible.value = true
}

// ===== V24-F86: 编辑弹窗 (拉取详情回填) =====
async function openEditDialog(item: XrefOem3Item) {
  dialogMode.value = 'edit'
  editingId.value = item.id
  dialogVisible.value = true
  dialogLoading.value = true
  try {
    const detail = await adminXrefApi.getItem(item.id)
    form.productId = detail.productId
    form.oemBrand = detail.oemBrand ?? selectedBrand.value
    form.oemNo3 = detail.oemNo3 ?? ''
    form.oem2 = detail.oem2 ?? ''
    form.machineType = detail.machineType ?? 'others'
    form.isPublished = detail.isPublished
    form.rowVersion = detail.rowVersion
    // 预填 product 联想选项 (让 el-select 显示当前关联产品)
    productOptions.value = [
      { id: detail.productId, oemNoDisplay: '', mr1: detail.mr1 ?? undefined } as ProductListItem
    ]
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.detail || t('admin.xrefreorder.err_load_detail'))
    dialogVisible.value = false
  } finally {
    dialogLoading.value = false
  }
}

// ===== V24-F86: product 联想 (复用 adminProductApi.search, 多字段模糊查询) =====
//   🔧 P0 fix: 移除 oemBrand 过滤, 让用户能搜索所有产品
//   原因: cross_references.oem_brand 独立于 products.oem_brand, 一个产品可关联到多个品牌
//   (如产品 A 可在 Donaldson 下有 OEM 3 号 DON-001, 也可在 FLEETGUARD 下有 FLT-001)
//   之前按 oemBrand 过滤导致新品牌 (如 FLEETGUARD) 下搜索返回空, 用户无法添加白名单
//   修复: 不按品牌过滤, 让用户自由搜索所有产品; 选择后由后端创建 cross_reference 关联
//   多字段: mr1 + productName1 (mr1 优先, 无结果再按 productName1)
async function searchProducts(q: string) {
  if (!q || q.trim().length < 1) {
    productOptions.value = []
    return
  }
  productLoading.value = true
  try {
    // 优先按 mr1 搜索 (核心业务字段)
    const resp = await adminProductApi.search({
      mr1: q.trim(),
      page: 1,
      pageSize: 20,
      includeDiscontinued: false
    })
    // 如果 mr1 搜索无结果, 尝试按 productName1 搜索
    if (resp.items.length === 0) {
      const resp2 = await adminProductApi.search({
        productName1: q.trim(),
        page: 1,
        pageSize: 20,
        includeDiscontinued: false
      })
      productOptions.value = resp2.items
    } else {
      productOptions.value = resp.items
    }
  } catch {
    productOptions.value = []
  } finally {
    productLoading.value = false
  }
}

// ===== 选择产品后自动填充 oemNo3 (从选中产品的 oemNoDisplay 取) =====
function onProductSelected(productId: number) {
  const product = productOptions.value.find((p) => p.id === productId)
  if (product) {
    // 自动填充 oemNo3 (如果产品有 oemNoDisplay, 用它作为默认 OEM 3 号)
    if (!form.oemNo3 && product.oemNoDisplay) {
      form.oemNo3 = product.oemNoDisplay
    }
    // 自动填充 oem2 (如果产品有 oem2)
    if (!form.oem2 && (product as any).oem2) {
      form.oem2 = (product as any).oem2
    }
  }
}

// ===== V24-F86: 提交表单 (新增/编辑) =====
async function submitForm() {
  if (form.productId <= 0) {
    ElMessage.error(t('admin.xrefreorder.err_select_product'))
    return
  }
  if (!form.oemBrand) {
    ElMessage.error(t('admin.xrefreorder.err_oem_brand_required'))
    return
  }
  if (!form.oemNo3) {
    ElMessage.error(t('admin.xrefreorder.err_oem_no3_required'))
    return
  }
  dialogLoading.value = true
  try {
    if (dialogMode.value === 'create') {
      const payload: XrefOem3CreatePayload = {
        productId: form.productId,
        oemBrand: form.oemBrand,
        oemNo3: form.oemNo3,
        oem2: form.oem2 || null,
        machineType: form.machineType || null,
        isPublished: form.isPublished
      }
      await adminXrefApi.addItem(payload)
      ElMessage.success(t('admin.xrefreorder.success_added'))
    } else {
      if (editingId.value == null) return
      const payload: XrefOem3UpdatePayload = {
        oemNo3: form.oemNo3,
        machineType: form.machineType || null,
        isPublished: form.isPublished,
        rowVersion: form.rowVersion
      }
      const result = await adminXrefApi.updateItem(editingId.value, payload)
      // 更新本地 rowVersion (xmin 已变, 下次编辑需用新值)
      form.rowVersion = result.rowVersion
      ElMessage.success(t('admin.xrefreorder.success_edited'))
    }
    dialogVisible.value = false
    await loadOemList()
    // 同步刷新 brand 列表 (oem3Count 可能变化)
    await loadBrandsSilently()
  } catch (e: any) {
    const status = e?.response?.status
    if (status === 409) {
      ElMessage.error(e?.response?.data?.detail || t('admin.xrefreorder.err_conflict'))
    } else {
      ElMessage.error(e?.response?.data?.detail || t('admin.xrefreorder.err_save_failed'))
    }
  } finally {
    dialogLoading.value = false
  }
}

// ===== V24-F86: 从白名单移除 (置 sort_order=0, 产品本身不删) =====
//   白名单改造: 原"软删 is_discontinued=true" 改为"从白名单移除 sort_order=0"
async function deleteItem(item: XrefOem3Item) {
  try {
    await ElMessageBox.confirm(
      t('admin.xrefreorder.confirm_remove_message', { oemNo3: item.oemNo3 }),
      t('admin.xrefreorder.remove_from_whitelist'),
      { confirmButtonText: t('admin.xrefreorder.remove'), cancelButtonText: t('admin.xrefreorder.cancel'), type: 'warning' }
    )
  } catch {
    return  // 用户取消
  }
  try {
    await adminXrefApi.deleteItem(item.id, item.rowVersion)
    ElMessage.success(t('admin.xrefreorder.success_removed'))
    await loadOemList()
    await loadBrandsSilently()
  } catch (e: any) {
    const status = e?.response?.status
    if (status === 409) {
      ElMessage.error(e?.response?.data?.detail || t('admin.xrefreorder.err_conflict'))
    } else {
      ElMessage.error(e?.response?.data?.detail || t('admin.xrefreorder.err_remove_failed'))
    }
  }
}

// ===== 静默刷新 brand 列表 (oem3Count 变化, 不改 selectedBrand) =====
async function loadBrandsSilently() {
  try {
    const resp = await adminXrefApi.listBrands()
    brands.value = resp.items
  } catch {
    // 静默失败, 不打断主流程
  }
}

// ===== 新增品牌弹窗 (用户独立新增品牌到字典, 新增后即可在该品牌下添加白名单) =====
const brandDialogVisible = ref(false)
const newBrandName = ref('')
const brandSubmitting = ref(false)

function openCreateBrandDialog() {
  newBrandName.value = ''
  brandDialogVisible.value = true
}

async function submitBrand() {
  const name = newBrandName.value.trim()
  if (!name) {
    ElMessage.error(t('admin.xrefreorder.err_brand_name_required'))
    return
  }
  brandSubmitting.value = true
  try {
    const result = await adminXrefApi.addBrand(name)
    ElMessage.success(result.restored ? t('admin.xrefreorder.success_brand_restored', { brand: result.brand }) : t('admin.xrefreorder.success_brand_added', { brand: result.brand }))
    brandDialogVisible.value = false
    // 刷新品牌列表并自动选中新品牌
    await loadBrands()
    selectedBrand.value = result.brand
    page.value = 1
    searchQ.value = ''
    await loadOemList()
  } catch (e: any) {
    const status = e?.response?.status
    const errorCode = e?.response?.data?.errorCode
    if (status === 409 || errorCode === 'BRAND_EXISTS') {
      ElMessage.error(e?.response?.data?.detail || t('admin.xrefreorder.err_brand_exists'))
    } else {
      ElMessage.error(e?.response?.data?.detail || t('admin.xrefreorder.err_add_brand_failed'))
    }
  } finally {
    brandSubmitting.value = false
  }
}

onMounted(loadBrands)

// 🔧 fix(审查): 卸载时清理防抖 timer — 之前缺失, 卸载后 timer 仍会触发 loadOemList 访问已卸载状态
onBeforeUnmount(() => {
  if (searchTimer) {
    clearTimeout(searchTimer)
    searchTimer = null
  }
})
</script>

<template>
  <div class="p-4 max-w-7xl mx-auto">
    <!-- 标题 -->
    <div class="border-b border-gray-200 pb-3 mb-4 dark:border-[var(--color-border)]">
      <h1 class="text-xl font-medium">{{ t('admin.xrefreorder.page_title') }}</h1>
      <p class="text-xs text-gray-500 mt-1 dark:text-[var(--color-text-muted)]">
        {{ t('admin.xrefreorder.page_subtitle') }}
      </p>
    </div>

    <div class="flex gap-4" style="min-height: 600px">
      <!-- 左侧: Brand 列表 -->
      <div class="w-64 border border-gray-200 rounded dark:border-[var(--color-border)]">
        <div class="px-3 py-2 border-b border-gray-200 bg-gray-50 text-sm font-medium flex items-center justify-between dark:border-[var(--color-border)] dark:bg-[var(--color-bg-elevated)]">
          <span>{{ t('admin.xrefreorder.brand_label', { count: brands.length }) }}</span>
          <el-button size="small" text type="primary" @click="openCreateBrandDialog">{{ t('admin.xrefreorder.add_brand') }}</el-button>
        </div>
        <div v-loading="loadingBrands" class="overflow-auto" style="max-height: 540px">
          <div
            v-for="b in brands"
            :key="b.brand"
            class="px-3 py-2 border-b border-gray-100 cursor-pointer hover:bg-gray-50 flex items-center justify-between dark:border-[var(--color-border-subtle)] dark:hover:bg-[var(--color-bg-hover)]"
            :class="{ 'bg-blue-50 border-l-2 border-l-blue-500': b.brand === selectedBrand, 'dark:bg-[var(--color-bg-hover)] dark:border-l-[var(--color-accent)]': b.brand === selectedBrand }"
            @click="selectBrand(b.brand)"
          >
            <div class="flex-1 min-w-0">
              <div class="text-sm truncate">{{ b.brand }}</div>
              <div class="text-xs text-gray-500 dark:text-[var(--color-text-muted)]">{{ t('admin.xrefreorder.brand_whitelist_count', { count: b.oem3Count, sortOrder: b.sortOrder }) }}</div>
            </div>
          </div>
          <div v-if="!loadingBrands && brands.length === 0" class="p-4 text-center text-gray-400 text-sm dark:text-[var(--color-text-muted)]">
            {{ t('admin.xrefreorder.no_brand_data') }}
          </div>
        </div>
      </div>

      <!-- 右侧: OEM 3 拖拽列表 + 分页 + 搜索 + CRUD -->
      <div class="flex-1 border border-gray-200 rounded dark:border-[var(--color-border)]">
        <div class="px-3 py-2 border-b border-gray-200 bg-gray-50 flex items-center justify-between flex-wrap gap-2 dark:border-[var(--color-border)] dark:bg-[var(--color-bg-elevated)]">
          <div class="text-sm font-medium flex items-center gap-2">
            {{ selectedBrand || t('admin.xrefreorder.select_brand_placeholder') }}
            <span v-if="total > 0" class="text-xs text-gray-500 dark:text-[var(--color-text-muted)]">
              {{ t('admin.xrefreorder.total_info', { total, page, totalPages: totalPages || 1 }) }}
            </span>
          </div>
          <div class="flex items-center gap-2">
            <!-- V24-F86: 搜索框 (oemNo3 模糊匹配, 防抖 300ms) -->
            <el-input
              v-model="searchQ"
              size="small"
              clearable
              :placeholder="t('admin.xrefreorder.search_placeholder')"
              :aria-label="t('admin.xrefreorder.search_placeholder')"
              style="width: 180px"
              @input="onSearchInput"
              @clear="onSearchInput"
            />
            <!-- 白名单改造: 添加到白名单按钮 (后端 sort_order=max+1 自动入白名单) -->
            <el-button size="small" type="success" @click="openCreateDialog" :disabled="!selectedBrand">
              {{ t('admin.xrefreorder.add_to_whitelist') }}
            </el-button>
            <el-button
              v-if="dragList.length > 0"
              size="small"
              type="primary"
              :loading="saving"
              @click="manualSave"
            >
              {{ t('admin.xrefreorder.save_order') }}
            </el-button>
          </div>
        </div>

        <div v-loading="loadingOem || saving" class="p-3" style="min-height: 480px">
          <draggable
            v-if="dragList.length > 0"
            v-model="dragList"
            item-key="id"
            handle=".drag-handle"
            animation="200"
            ghost-class="opacity-50"
            @end="onDragEnd"
          >
            <template #item="{ element, index }">
              <div class="flex items-center gap-3 px-3 py-2 border border-gray-200 rounded mb-1 hover:border-gray-400 bg-white dark:border-[var(--color-border)] dark:hover:border-[var(--color-border-strong)] dark:bg-[var(--color-bg-elevated)]">
                <span class="drag-handle cursor-move text-gray-400 hover:text-gray-700 dark:text-[var(--color-text-muted)]">⋮⋮</span>
                <span class="text-xs font-mono text-gray-500 w-8 dark:text-[var(--color-text-muted)]">{{ (page - 1) * pageSize + index + 1 }}</span>
                <div class="flex-1 min-w-0">
                  <div class="font-mono text-sm truncate">{{ element.oemNo3 }}</div>
                  <div class="text-xs text-gray-500 dark:text-[var(--color-text-muted)]">
                    {{ t('admin.xrefreorder.mr1_label', { mr1: element.mr1 || '-' }) }}
                    <el-tag v-if="!element.isPublished" size="small" type="info" class="ml-1">{{ t('admin.xrefreorder.unpublished') }}</el-tag>
                  </div>
                </div>
                <span class="text-xs text-gray-400 dark:text-[var(--color-text-muted)]">{{ t('admin.xrefreorder.sort_label', { sortOrder: element.sortOrder }) }}</span>
                <!-- V24-F86: 编辑/从白名单移除按钮 -->
                <div class="flex items-center gap-1">
                  <el-button size="small" text @click="openEditDialog(element)">{{ t('admin.xrefreorder.edit') }}</el-button>
                  <el-button size="small" text type="danger" @click="deleteItem(element)">{{ t('admin.xrefreorder.remove_from_whitelist') }}</el-button>
                </div>
              </div>
            </template>
          </draggable>

          <div v-else-if="!loadingOem" class="py-12 text-center text-gray-400 text-sm dark:text-[var(--color-text-muted)]">
            <p v-if="selectedBrand">{{ t('admin.xrefreorder.empty_no_whitelist') }}</p>
            <p v-else>{{ t('admin.xrefreorder.empty_select_brand') }}</p>
          </div>
        </div>

        <!-- V24-F86: 分页组件 (pageSize=50) -->
        <div v-if="total > 0" class="px-3 py-2 border-t border-gray-200 flex justify-end dark:border-[var(--color-border)]">
          <el-pagination
            v-model:current-page="page"
            v-model:page-size="pageSize"
            :total="total"
            :page-sizes="[20, 50, 100, 200]"
            layout="total, sizes, prev, pager, next, jumper"
            background
            @current-change="onPageChange"
            @size-change="onPageSizeChange"
          />
        </div>
      </div>
    </div>

    <!-- V24-F86: 添加到白名单 / 编辑弹窗 -->
    <el-dialog
      v-model="dialogVisible"
      :title="dialogMode === 'create' ? t('admin.xrefreorder.dialog_add_title') : t('admin.xrefreorder.dialog_edit_title')"
      width="520px"
      :close-on-click-modal="false"
    >
      <el-form v-loading="dialogLoading" :model="form" label-width="100px" label-position="right">
        <!-- 当前品牌 (只读显示, 不可编辑, 自动取 selectedBrand) -->
        <el-form-item :label="t('admin.xrefreorder.field_current_brand')">
          <span class="text-sm font-medium">{{ form.oemBrand || selectedBrand || '-' }}</span>
        </el-form-item>
        <el-form-item :label="t('admin.xrefreorder.field_product')">
          <!-- productId 联想 (el-select remote, 多字段搜索, 自动按当前 brand 过滤) -->
          <el-select
            v-model="form.productId"
            filterable
            remote
            :remote-method="searchProducts"
            :loading="productLoading"
            :disabled="dialogMode === 'edit'"
            :placeholder="t('admin.xrefreorder.product_placeholder')"
            style="width: 100%"
            @change="onProductSelected"
          >
            <el-option
              v-for="p in productOptions"
              :key="p.id"
              :label="p.mr1 ? `${p.mr1} · ${p.oemNoDisplay || ''} (#${p.id})` : `#${p.id}`"
              :value="p.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('admin.xrefreorder.field_oem_no3')">
          <el-input v-model="form.oemNo3" :placeholder="t('admin.xrefreorder.field_oem_no3')" />
        </el-form-item>
        <el-form-item :label="t('admin.xrefreorder.field_oem2')">
          <el-input v-model="form.oem2" :placeholder="t('admin.xrefreorder.field_oem2')" />
        </el-form-item>
        <el-form-item :label="t('admin.xrefreorder.field_machine_type')">
          <el-input v-model="form.machineType" :placeholder="t('admin.xrefreorder.field_machine_type')" />
        </el-form-item>
        <el-form-item :label="t('admin.xrefreorder.field_is_published')">
          <el-switch v-model="form.isPublished" />
        </el-form-item>
        <div v-if="dialogMode === 'create'" class="text-xs text-gray-500 pl-[100px] dark:text-[var(--color-text-muted)]">
          {{ t('admin.xrefreorder.dialog_add_hint') }}
        </div>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">{{ t('admin.xrefreorder.cancel') }}</el-button>
        <el-button type="primary" :loading="dialogLoading" @click="submitForm">
          {{ dialogMode === 'create' ? t('admin.xrefreorder.dialog_add_title') : t('admin.xrefreorder.save') }}
        </el-button>
      </template>
    </el-dialog>

    <!-- 新增品牌弹窗 (用户独立新增品牌到字典, 新增后即可在该品牌下添加白名单) -->
    <el-dialog
      v-model="brandDialogVisible"
      :title="t('admin.xrefreorder.add_brand_dialog_title')"
      width="420px"
      :close-on-click-modal="false"
    >
      <el-form :model="{ name: newBrandName }" label-width="80px" label-position="right">
        <el-form-item :label="t('admin.xrefreorder.field_brand_name')">
          <el-input
            v-model="newBrandName"
            :placeholder="t('admin.xrefreorder.brand_name_placeholder')"
            maxlength="100"
            show-word-limit
            @keyup.enter="submitBrand"
          />
        </el-form-item>
        <div class="text-xs text-gray-500 pl-[80px] dark:text-[var(--color-text-muted)]">
          {{ t('admin.xrefreorder.add_brand_hint') }}
        </div>
      </el-form>
      <template #footer>
        <el-button @click="brandDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="brandSubmitting" @click="submitBrand">
          {{ t('admin.xrefreorder.add_brand_dialog_title') }}
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>
