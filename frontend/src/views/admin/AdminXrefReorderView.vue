<script setup lang="ts">
// V2 Task 2.2.1: OEM 3 排序管理页 (修复漏洞 13)
//   布局: 左侧 Brand 列表 + 右侧 vuedraggable 拖拽 OEM 3 排序
//   - 拖拽完成自动调 POST /api/admin/xrefs/reorder 保存 (Task 2.2.3)
//   - 含 rowVersion 透传 (xmin 乐观锁, 冲突返 409 XREF_CONFLICT)
//   - 409 时提示刷新重试 (Task 2.2.4)
//   - Musk 风格极简: 纯黑白 + 1px 细线 + 8px 网格
import { ref, onMounted, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import draggable from 'vuedraggable'
import { adminXrefApi } from '@/api'
import type { XrefBrandItem, XrefOem3Item } from '@/api/types'

// ===== Brand 列表状态 =====
const brands = ref<XrefBrandItem[]>([])
const selectedBrand = ref<string>('')
const loadingBrands = ref(false)

// ===== OEM 3 列表状态 =====
const oemList = ref<XrefOem3Item[]>([])
const loadingOem = ref(false)
const saving = ref(false)
// 拖拽过程中本地副本, 拖完才提交
const dragList = ref<XrefOem3Item[]>([])

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
    ElMessage.error(e?.response?.data?.detail || '加载品牌列表失败')
  } finally {
    loadingBrands.value = false
  }
}

// ===== 加载某 Brand 下 OEM 3 列表 =====
async function loadOemList() {
  if (!selectedBrand.value) {
    oemList.value = []
    dragList.value = []
    return
  }
  loadingOem.value = true
  try {
    const resp = await adminXrefApi.listByBrand(selectedBrand.value)
    oemList.value = resp.items
    // 复制一份用于拖拽 (避免直接改 oemList 触发响应式重渲染)
    dragList.value = [...resp.items]
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.detail || '加载 OEM 3 列表失败')
    oemList.value = []
    dragList.value = []
  } finally {
    loadingOem.value = false
  }
}

// ===== 切换 Brand =====
async function selectBrand(brand: string) {
  selectedBrand.value = brand
  await loadOemList()
}

// ===== 拖拽完成自动保存 (Task 2.2.3) =====
async function onDragEnd() {
  if (dragList.value.length === 0) return
  // 检查顺序是否有变化 (避免无变化时多余的 API 调用)
  const hasChange = dragList.value.some((item, idx) => item.sortOrder !== idx + 1)
  if (!hasChange) return

  // 重新计算 sortOrder (1-based, 拖拽顺序即排序)
  const items = dragList.value.map((item, idx) => ({
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
    ElMessage.success(`已保存 ${items.length} 条 OEM 3 排序`)
  } catch (e: any) {
    // Task 2.2.4: 409 XREF_CONFLICT 时提示刷新重试
    const status = e?.response?.status
    const errorCode = e?.response?.data?.errorCode || e?.response?.data?.extensions?.errorCode
    if (status === 409 || errorCode === 'XREF_CONFLICT') {
      ElMessageBox.confirm(
        'OEM 3 排序已被其他用户修改, 请刷新后重试。是否立即刷新?',
        '排序冲突',
        { confirmButtonText: '刷新', cancelButtonText: '取消', type: 'warning' }
      ).then(() => loadOemList()).catch(() => {})
    } else {
      ElMessage.error(e?.response?.data?.detail || '保存排序失败')
    }
  } finally {
    saving.value = false
  }
}

// ===== 手动批量保存 (备用, 拖拽自动保存失败时使用) =====
async function manualSave() {
  await onDragEnd()
}

onMounted(loadBrands)
</script>

<template>
  <div class="p-4 max-w-7xl mx-auto">
    <!-- 标题 -->
    <div class="border-b border-gray-200 pb-3 mb-4">
      <h1 class="text-xl font-medium">OEM 排序管理</h1>
      <p class="text-xs text-gray-500 mt-1">
        拖拽 OEM 3 调整排序 (数值越小越靠前, 类竞价排名) · 自动保存 · 冲突时刷新重试
      </p>
    </div>

    <div class="flex gap-4" style="min-height: 600px">
      <!-- 左侧: Brand 列表 -->
      <div class="w-64 border border-gray-200 rounded">
        <div class="px-3 py-2 border-b border-gray-200 bg-gray-50 text-sm font-medium">
          品牌 ({{ brands.length }})
        </div>
        <div v-loading="loadingBrands" class="overflow-auto" style="max-height: 540px">
          <div
            v-for="b in brands"
            :key="b.brand"
            class="px-3 py-2 border-b border-gray-100 cursor-pointer hover:bg-gray-50 flex items-center justify-between"
            :class="{ 'bg-blue-50 border-l-2 border-l-blue-500': b.brand === selectedBrand }"
            @click="selectBrand(b.brand)"
          >
            <div class="flex-1 min-w-0">
              <div class="text-sm truncate">{{ b.brand }}</div>
              <div class="text-xs text-gray-500">sort: {{ b.sortOrder }} · {{ b.oem3Count }} OEM 3</div>
            </div>
          </div>
          <div v-if="!loadingBrands && brands.length === 0" class="p-4 text-center text-gray-400 text-sm">
            无品牌数据
          </div>
        </div>
      </div>

      <!-- 右侧: OEM 3 拖拽列表 -->
      <div class="flex-1 border border-gray-200 rounded">
        <div class="px-3 py-2 border-b border-gray-200 bg-gray-50 flex items-center justify-between">
          <div class="text-sm font-medium">
            {{ selectedBrand || '请选择品牌' }}
            <span v-if="dragList.length > 0" class="text-xs text-gray-500 ml-2">
              ({{ dragList.length }} OEM 3)
            </span>
          </div>
          <el-button
            v-if="dragList.length > 0"
            size="small"
            type="primary"
            :loading="saving"
            @click="manualSave"
          >
            保存排序
          </el-button>
        </div>

        <div v-loading="loadingOem || saving" class="p-3" style="min-height: 540px">
          <draggable
            v-if="dragList.length > 0"
            v-model="dragList"
            item-key="oemNo3"
            handle=".drag-handle"
            animation="200"
            ghost-class="opacity-50"
            @end="onDragEnd"
          >
            <template #item="{ element, index }">
              <div class="flex items-center gap-3 px-3 py-2 border border-gray-200 rounded mb-1 hover:border-gray-400 bg-white">
                <span class="drag-handle cursor-move text-gray-400 hover:text-gray-700">⋮⋮</span>
                <span class="text-xs font-mono text-gray-500 w-8">{{ index + 1 }}</span>
                <div class="flex-1 min-w-0">
                  <div class="font-mono text-sm truncate">{{ element.oemNo3 }}</div>
                  <div class="text-xs text-gray-500">
                    MR.1: {{ element.mr1 || '-' }}
                    <el-tag v-if="!element.isPublished" size="small" type="info" class="ml-1">未上架</el-tag>
                  </div>
                </div>
                <span class="text-xs text-gray-400">sort: {{ element.sortOrder }}</span>
              </div>
            </template>
          </draggable>

          <div v-else-if="!loadingOem" class="py-12 text-center text-gray-400 text-sm">
            <p v-if="selectedBrand">该品牌下无 OEM 3</p>
            <p v-else>请从左侧选择品牌</p>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
