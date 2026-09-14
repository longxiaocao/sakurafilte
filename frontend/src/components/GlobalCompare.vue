<script setup lang="ts">
// 全局对比组件 — 悬浮按钮 + 对比抽屉 (所有公开页共享)
//   WHY: 详情页/搜索页共用同一对比集合 (useCompareStore), 本组件挂载于 App.vue,
//       任一路由页都可随时点开悬浮球查看对比, 不再依赖各页自建抽屉。
import { ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { useCompareStore, MAX_COMPARE } from '@/stores/useCompareStore'
import { publicCompareApi } from '@/api'
import type { PublicProductDetail } from '@/api/types'
import PublicComparePanel from './PublicComparePanel.vue'

const store = useCompareStore()

const open = ref(false)
const products = ref<PublicProductDetail[]>([])
const loading = ref(false)

async function openCompare() {
  if (store.count.value === 0) {
    ElMessage.warning('请先在结果中点击"加入对比"')
    return
  }
  open.value = true
  await loadProducts()
}

async function loadProducts() {
  if (store.count.value === 0) {
    products.value = []
    return
  }
  loading.value = true
  try {
    const ids = Array.from(store.ids.value).slice(0, MAX_COMPARE)
    const data = await publicCompareApi.compare(ids)
    const map = new Map(data.items.map((p) => [p.id, p]))
    // 保持与 store 中 id 顺序一致
    products.value = ids.map((id) => map.get(id)).filter((p): p is PublicProductDetail => !!p)
  } catch (e: any) {
    ElMessage.error(e?.problem?.detail || e?.response?.data?.error || e?.message || '对比加载失败')
  } finally {
    loading.value = false
  }
}

function removeFromCompare(idx: number) {
  const p = products.value[idx]
  if (p) {
    store.remove(p.id)
    products.value = products.value.filter((_item, i) => i !== idx)
  }
}

function moveCompare(idx: number, dir: -1 | 1) {
  const target = idx + dir
  if (target < 0 || target >= products.value.length) return
  const arr = [...products.value]
  ;[arr[idx], arr[target]] = [arr[target], arr[idx]]
  products.value = arr
  // 列调序持久化: 覆盖 store 顺序 (store.replace 会写回 sessionStorage)
  store.replace(arr.map((p) => p.id))
}

function clearCompare() {
  store.clear()
  products.value = []
  open.value = false
}

// 抽屉开着时若集合变化, 同步刷新 (如详情页在后台又加入了一个)
watch(() => store.ids.value, (ids) => {
  if (open.value && ids.size !== products.value.length) {
    loadProducts()
  }
})

// 响应各页的 requestOpen() 信号 (如搜索栏"查看对比"按钮) — 打开全局抽屉
watch(() => store.pendingOpen.value, (n) => {
  if (n > 0) {
    store.consumeOpen()
    openCompare()
  }
})
</script>

<template>
  <!-- 悬浮对比球: 集合非空时右下角常驻, 点击展开抽屉; 不打断当前页面浏览 -->
  <div v-if="store.count.value > 0" class="fixed bottom-6 right-6 z-50 flex flex-col items-end gap-2">
    <div class="text-xs text-muted pointer-events-none hidden sm:block">
      已加入 {{ store.count.value }} 个，点击查看对比
    </div>
    <el-badge :value="store.count.value" :max="MAX_COMPARE" class="compare-float-badge" data-testid="global-compare-float-btn">
      <el-button round type="primary" size="large" data-testid="global-compare-btn" @click="openCompare">
        对比
      </el-button>
    </el-badge>
  </div>

  <!-- 对比抽屉 -->
  <el-drawer v-model="open" title="产品对比" size="80%" direction="rtl" data-testid="global-compare-drawer">
    <div v-loading="loading" class="p-3">
      <div class="flex items-center justify-between mb-3">
        <span v-if="products.length > 0" class="text-xs text-muted">{{ products.length }} 个产品</span>
        <el-button v-if="products.length > 0" size="small" data-testid="global-clear-compare-btn" @click="clearCompare">清空对比</el-button>
      </div>
      <div v-if="products.length === 0 && !loading" class="text-sm text-muted py-8 text-center">
        对比列表为空
      </div>
      <PublicComparePanel
        v-if="products.length > 0"
        :products="products"
        @remove="removeFromCompare"
        @move-left="moveCompare($event, -1)"
        @move-right="moveCompare($event, 1)"
      />
    </div>
  </el-drawer>
</template>