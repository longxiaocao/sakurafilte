<script setup lang="ts">
// V2 Task 4.5.2: 产品详情页"加入对比"子组件 (Vue client mount)
//   - 点击按钮跳转到 /compare?ids=<productId> (整页跳转触发 SSR)
//   - 同时存 localStorage 最近的对比 ID 列表 (跨页面记忆, 最多 6 个)
//   - props.productId 来自 JSON 数据岛的 product.id
//   - 注意: props.oemNo3 实际传值为 product.oemNoDisplay (产品 OEM 编号)
import { ref, onMounted } from 'vue'

interface CompareProps {
  mr1: string | null
  oemNo3: string
  productId: number | null
}

const props = defineProps<CompareProps>()

const recentIds = ref<number[]>([])

const STORAGE_KEY = 'recentCompareIds'
const MAX_RECENT = 6

onMounted(() => {
  // 读取最近对比列表 (localStorage 不可用时静默降级)
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw) recentIds.value = JSON.parse(raw).slice(0, MAX_RECENT)
  } catch {
    // 忽略: localStorage 不可用或 JSON 解析失败
  }
})

function saveRecent(ids: number[]): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(ids))
  } catch {
    // 静默失败
  }
}

function addToCompare(): void {
  if (props.productId == null) return
  const id = props.productId
  // 去重 + 移到最前 + 截断
  const next = [id, ...recentIds.value.filter(x => x !== id)].slice(0, MAX_RECENT)
  recentIds.value = next
  saveRecent(next)
  // 跳转对比页 (整页跳转触发 SSR 渲染)
  window.location.href = `/compare?ids=${id}`
}

function viewCompare(): void {
  if (recentIds.value.length === 0) return
  window.location.href = `/compare?ids=${recentIds.value.join(',')}`
}
</script>

<template>
  <div class="compare-app">
    <button
      type="button"
      class="compare-btn primary"
      :disabled="props.productId == null"
      @click="addToCompare"
    >
      加入对比
    </button>
    <button
      v-if="recentIds.length > 0"
      type="button"
      class="compare-btn ghost"
      @click="viewCompare"
    >
      查看最近对比 ({{ recentIds.length }})
    </button>
  </div>
</template>

<style scoped>
.compare-app {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}
.compare-btn {
  padding: 8px 16px;
  font-size: 14px;
  border: 1px solid #000;
  cursor: pointer;
  border-radius: 4px;
  transition: background 0.15s, color 0.15s;
}
.compare-btn.primary {
  background: #000;
  color: #fff;
}
.compare-btn.primary:hover:not(:disabled) {
  background: #333;
}
.compare-btn.primary:disabled {
  background: #ccc;
  border-color: #ccc;
  cursor: not-allowed;
}
.compare-btn.ghost {
  background: transparent;
  color: #000;
}
.compare-btn.ghost:hover {
  background: #f5f5f5;
}
</style>
