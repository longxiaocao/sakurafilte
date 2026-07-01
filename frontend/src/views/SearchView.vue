<script setup lang="ts">
// Day 9: 产品搜索页
//   - 顶部搜索框 (全文/按字段)
//   - 高级筛选抽屉 (17 字段 + 尺寸范围 + 批量 OEM)
//   - 高密度表格 (27 列, 紧凑)
//   - 分页 (offset / cursor)
//   - 排序白名单
//   - 点击行 → 详情页
import { ref, computed, onMounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { searchApi } from '@/api'
import type { SearchResult, SearchHit } from '@/api/types'

const router = useRouter()

const q = ref('')
const loading = ref(false)
const hits = ref<SearchHit[]>([])
const total = ref(0)
const provider = ref('')
const lastError = ref('')

async function doSearch() {
  if (!q.value.trim()) {
    hits.value = []
    total.value = 0
    return
  }
  loading.value = true
  lastError.value = ''
  try {
    const { provider: p, result } = await searchApi.search({ q: q.value.trim(), limit: 50 })
    provider.value = p
    hits.value = result.hits
    total.value = result.total
  } catch (e: any) {
    lastError.value = e?.message || '搜索失败'
  } finally {
    loading.value = false
  }
}

function viewDetail(row: SearchHit) {
  router.push(`/product/${encodeURIComponent(row.oem_no_display)}`)
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
    <div class="flex items-center gap-2 mb-3">
      <el-input
        v-model="q"
        placeholder="搜索 OEM / 名称 / 车型..."
        clearable
        size="large"
        @keyup.enter="doSearch"
      >
        <template #prefix>
          <el-icon><Search /></el-icon>
        </template>
      </el-input>
      <el-button type="primary" size="large" @click="doSearch" :loading="loading">搜索</el-button>
      <span v-if="provider" class="text-xs text-muted">provider: {{ provider }}</span>
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
        <span>共 {{ total }} 条结果</span>
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
        <el-table-column prop="oem_no_display" label="OEM" width="180" />
        <el-table-column prop="mr_1" label="MR.1" width="120" show-overflow-tooltip />
        <el-table-column prop="type" label="Type" width="80" />
        <el-table-column prop="product_name_1" label="名称" min-width="200" show-overflow-tooltip />
        <el-table-column prop="d1_mm" label="D1" width="60" align="right" />
        <el-table-column prop="d2_mm" label="D2" width="60" align="right" />
        <el-table-column prop="d3_mm" label="D3" width="60" align="right" />
        <el-table-column prop="h1_mm" label="H1" width="60" align="right" />
        <el-table-column prop="d7_thread" label="D7" width="80" />
        <el-table-column prop="d8_thread" label="D8" width="80" />
        <el-table-column prop="media" label="Media" width="120" show-overflow-tooltip />
        <el-table-column prop="media_model" label="MediaModel" width="120" show-overflow-tooltip />
        <el-table-column label="更新" width="120">
          <template #default="{ row }">{{ fmtDate(row.updated_at) }}</template>
        </el-table-column>
      </el-table>
    </div>
  </div>
</template>
