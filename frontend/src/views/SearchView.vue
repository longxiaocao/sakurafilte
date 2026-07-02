<script setup lang="ts">
// Day 9: 产品搜索页
//   - 顶部搜索框 (全文/按字段)
//   - 高级筛选抽屉 (17 字段 + 尺寸范围 + 批量 OEM)
//   - 高密度表格 (27 列, 紧凑)
//   - 分页 (offset / cursor)
//   - 排序白名单
//   - 点击行 → 详情页
// Task 9 (P3.1): 尺寸容差 UI — ±1 / ±5 / ±10mm 下拉, 默认 5,
//   切换容差自动重新搜索, 显示结果数变化提示
import { ref, computed, onMounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { searchApi } from '@/api'
import type { SearchResult, SearchHit } from '@/api/types'

const router = useRouter()

const q = ref('')
// Task 9 (P3.1): 容差默认 5mm (后端 SearchRequest.Tolerance 默认值, 与 AdminProductsView 对齐)
const tolerance = ref<1 | 5 | 10>(5)
const loading = ref(false)
const hits = ref<SearchHit[]>([])
const total = ref(0)
const provider = ref('')
const lastError = ref('')
// Task 9 (P3.1): 上一次容差 + 上一次总数, 用于"切换后命中 X 条, 较 ±N 多 Y 条"提示
const prevTolerance = ref<1 | 5 | 10 | null>(null)
const prevTotal = ref<number | null>(null)
// Task 9 (P3.1): 容差切换提示文案, 例如 "切换后命中 50+ 条, 较 ±1mm 多 35 条"
const toleranceHint = computed(() => {
  if (prevTolerance.value === null || prevTotal.value === null) return ''
  const cur = total.value
  const prev = prevTotal.value
  const diff = cur - prev
  if (diff > 0) return `切换后命中 ${cur} 条, 较 ±${prevTolerance.value}mm 多 ${diff} 条`
  if (diff < 0) return `切换后命中 ${cur} 条, 较 ±${prevTolerance.value}mm 少 ${-diff} 条`
  return `切换后命中 ${cur} 条, 与 ±${prevTolerance.value}mm 相同`
})

async function doSearch() {
  if (!q.value.trim()) {
    hits.value = []
    total.value = 0
    return
  }
  loading.value = true
  lastError.value = ''
  try {
    const { provider: p, result } = await searchApi.search({
      q: q.value.trim(),
      // Task 9 (P3.1): 把容差传到后端 (后端 SearchRequest.Tolerance, PostgresSearchProvider 走 ±t 区间)
      tolerance: tolerance.value,
      pageSize: 50
    })
    provider.value = p
    // Day 9.2: 修复 - 后端字段是 items (PascalCase), 不是 hits
    //   兼容 fallback: 万一后端返回 hits 也能用
    const items = (result?.items ?? result?.hits ?? []) as SearchHit[]
    hits.value = items
    total.value = result?.total ?? 0
  } catch (e: any) {
    lastError.value = e?.message || '搜索失败'
    // Day 9.2: 出错时清空, 防止 undefined.length 报错
    hits.value = []
    total.value = 0
  } finally {
    loading.value = false
  }
}

// Task 9 (P3.1): 切换容差后自动重新搜索 (仅在已有关键词时触发, 空状态不打扰)
//   策略: 缓存切换前的 (tolerance, total), 搜索完成后用 toleranceHint 提示差异
watch(tolerance, async (_newVal, oldVal) => {
  if (typeof oldVal !== 'number') return
  if (!q.value.trim()) return
  prevTolerance.value = oldVal
  prevTotal.value = total.value
  await doSearch()
})

function viewDetail(row: SearchHit) {
  // Day 9.2: 兼容 snake_case 和 PascalCase 字段
  const oem = row.oemNoDisplay ?? row.oem_no_display ?? ''
  router.push(`/product/${encodeURIComponent(oem)}`)
}

function fmtDate(iso?: string) {
  if (!iso) return ''
  return iso.substring(0, 16).replace('T', ' ')
}

onMounted(() => {
  // 默认显示空状态
})
</script>

<template>
  <div class="p-3 max-w-screen-2xl mx-auto">
    <div class="flex items-center gap-2 mb-3 flex-wrap">
      <el-input
        v-model="q"
        placeholder="搜索 OEM / 名称 / 车型..."
        clearable
        size="large"
        style="max-width: 480px"
        @keyup.enter="doSearch"
      >
        <template #prefix>
          <el-icon><Search /></el-icon>
        </template>
      </el-input>
      <!-- Task 9 (P3.1): 尺寸容差下拉, ±1/±5/±10, 用 el-popover 包裹加性能提示 -->
      <el-popover
        placement="bottom-start"
        :width="260"
        trigger="hover"
        popper-class="tol-popover"
      >
        <template #reference>
          <el-select
            v-model="tolerance"
            size="large"
            style="width: 168px"
          >
            <el-option label="±1mm (精确)" :value="1" />
            <el-option label="±5mm (推荐)" :value="5" />
            <el-option label="±10mm (宽松)" :value="10" />
          </el-select>
        </template>
        <div class="text-xs leading-relaxed">
          <div class="font-medium mb-1">尺寸容差</div>
          <div class="text-muted">
            切换容差会显著影响搜索速度 (10mm 比 1mm 慢 5-10 倍),
            默认 ±5mm 是大多数场景的平衡点。
          </div>
        </div>
      </el-popover>
      <el-button type="primary" size="large" @click="doSearch" :loading="loading">搜索</el-button>
      <span v-if="provider" class="text-xs text-muted">provider: {{ provider }}</span>
    </div>

    <!-- Task 9 (P3.1): 容差切换结果数变化提示, 仅在切换后短暂出现 (computed 始终计算, 但 q 为空时不显示) -->
    <div
      v-if="toleranceHint && q"
      class="text-xs text-muted mb-2"
    >
      {{ toleranceHint }}
    </div>

    <div v-if="lastError" class="text-red-600 text-sm mb-2">{{ lastError }}</div>

    <div v-if="!q" class="py-12 text-center text-muted">
      <el-icon class="text-4xl mb-2"><Search /></el-icon>
      <div>输入关键词开始搜索</div>
      <div class="text-xs mt-2">支持 OEM 编号、产品名、车型等</div>
    </div>

    <div v-else-if="hits.length === 0 && !loading" class="py-12 text-center text-muted">
      暂无结果
    </div>

    <div v-else class="hairline">
      <div class="hairline-b px-2 py-1 bg-neutral-50 text-xs text-muted flex items-center">
        <span>共 {{ total }} 条结果 (容差 ±{{ tolerance }}mm)</span>
        <span class="ml-2">(显示前 {{ hits.length }} 条)</span>
      </div>
      <el-table
        :data="hits"
        stripe
        size="small"
        @row-click="viewDetail"
        :row-style="{ cursor: 'pointer' }"
        v-loading="loading"
        max-height="calc(100vh - 200px)"
      >
        <el-table-column prop="id" label="ID" width="60" />
        <!-- Day 9.2: 列 prop 改 PascalCase 匹配后端, 加 fallback 兼容 snake_case -->
        <el-table-column label="OEM" width="180">
          <template #default="{ row }">{{ row.oemNoDisplay ?? row.oem_no_display }}</template>
        </el-table-column>
        <el-table-column label="MR.1" width="120" show-overflow-tooltip>
          <template #default="{ row }">{{ row.mr1 ?? row.mr_1 }}</template>
        </el-table-column>
        <el-table-column prop="type" label="Type" width="80" />
        <el-table-column label="名称" min-width="200" show-overflow-tooltip>
          <template #default="{ row }">{{ row.productName1 ?? row.product_name_1 }}</template>
        </el-table-column>
        <el-table-column label="D1" width="60" align="right">
          <template #default="{ row }">{{ row.d1Mm ?? row.d1_mm }}</template>
        </el-table-column>
        <el-table-column label="D2" width="60" align="right">
          <template #default="{ row }">{{ row.d2Mm ?? row.d2_mm }}</template>
        </el-table-column>
        <el-table-column label="D3" width="60" align="right">
          <template #default="{ row }">{{ row.d3Mm ?? row.d3_mm }}</template>
        </el-table-column>
        <el-table-column label="H1" width="60" align="right">
          <template #default="{ row }">{{ row.h1Mm ?? row.h1_mm }}</template>
        </el-table-column>
        <el-table-column label="D7" width="80">
          <template #default="{ row }">{{ row.d7Thread ?? row.d7_thread }}</template>
        </el-table-column>
        <el-table-column label="D8" width="80">
          <template #default="{ row }">{{ row.d8Thread ?? row.d8_thread }}</template>
        </el-table-column>
        <el-table-column prop="media" label="Media" width="120" show-overflow-tooltip />
        <el-table-column label="MediaModel" width="120" show-overflow-tooltip>
          <template #default="{ row }">{{ row.mediaModel ?? row.media_model }}</template>
        </el-table-column>
        <el-table-column label="更新" width="120">
          <template #default="{ row }">{{ fmtDate(row.updatedAt ?? row.updated_at) }}</template>
        </el-table-column>
      </el-table>
    </div>
  </div>
</template>
