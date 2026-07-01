<script setup lang="ts">
// Day 9: 产品详情页
//   - 顶部: 关键信息 (OEM/MR/Type)
//   - 中部: 7 分区规格 (基础信息/尺寸/螺纹/性能/包装/媒体/备注)
//   - 底部: 交叉引用 + 适用车型
import { ref, onMounted, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { productApi } from '@/api'
import type { ProductDetail } from '@/api/types'

const route = useRoute()
const router = useRouter()

const oem = computed(() => String(route.params.oem || ''))
const data = ref<ProductDetail | null>(null)
const loading = ref(false)
const err = ref('')

async function load() {
  loading.value = true
  err.value = ''
  try {
    data.value = await productApi.getByOem(oem.value)
  } catch (e: any) {
    err.value = e?.problem?.detail || e?.message || '加载失败'
  } finally {
    loading.value = false
  }
}

onMounted(load)
watch(() => oem.value, load)

function goBack() {
  router.back()
}

function numOrDash(v?: number | string) {
  if (v === null || v === undefined || v === '') return '—'
  return v
}
</script>

<template>
  <div class="p-3 max-w-screen-2xl mx-auto" v-loading="loading">
    <div class="flex items-center gap-2 mb-3">
      <el-button @click="goBack" size="small">
        <el-icon><ArrowLeft /></el-icon> 返回
      </el-button>
      <h1 class="text-lg font-medium" v-if="data">{{ data.oemNoDisplay }}</h1>
      <el-tag v-if="data?.type" size="small">{{ data.type }}</el-tag>
      <el-tag v-if="data?.isPublished" type="success" size="small">已发布</el-tag>
      <el-tag v-if="data?.isDiscontinued" type="info" size="small">已停售</el-tag>
    </div>

    <div v-if="err" class="text-red-600 text-sm mb-2">{{ err }}</div>

    <div v-if="data" class="grid grid-cols-2 gap-3">
      <!-- 分区 1: 基础信息 -->
      <div class="hairline p-3">
        <div class="text-sm font-medium mb-2 text-muted">基础信息</div>
        <div class="grid grid-cols-2 gap-y-1 text-sm">
          <div class="text-muted">MR.1</div><div>{{ data.mr1 || '—' }}</div>
          <div class="text-muted">OEM 2</div><div>{{ data.oem2 || '—' }}</div>
          <div class="text-muted">产品名 1</div><div>{{ data.productName1 || '—' }}</div>
          <div class="text-muted">产品名 2</div><div>{{ data.productName2 || '—' }}</div>
          <div class="text-muted">备注</div><div>{{ data.remark || '—' }}</div>
        </div>
      </div>

      <!-- 分区 3: 尺寸 -->
      <div class="hairline p-3">
        <div class="text-sm font-medium mb-2 text-muted">尺寸 (mm)</div>
        <div class="grid grid-cols-4 gap-y-1 text-sm">
          <div class="text-muted">D1</div><div>{{ numOrDash(data.d1Mm) }}</div>
          <div class="text-muted">D2</div><div>{{ numOrDash(data.d2Mm) }}</div>
          <div class="text-muted">D3</div><div>{{ numOrDash(data.d3Mm) }}</div>
          <div class="text-muted">D4</div><div>{{ numOrDash(data.d4Mm) }}</div>
          <div class="text-muted">H1</div><div>{{ numOrDash(data.h1Mm) }}</div>
          <div class="text-muted">H2</div><div>{{ numOrDash(data.h2Mm) }}</div>
          <div class="text-muted">H3</div><div>{{ numOrDash(data.h3Mm) }}</div>
          <div class="text-muted">H4</div><div>{{ numOrDash(data.h4Mm) }}</div>
          <div class="text-muted">D7 螺纹</div><div>{{ data.d7Thread || '—' }}</div>
          <div class="text-muted">D8 螺纹</div><div>{{ data.d8Thread || '—' }}</div>
          <div class="text-muted">单向阀</div><div>{{ numOrDash(data.noCheckValves) }}</div>
          <div class="text-muted">旁通阀</div><div>{{ numOrDash(data.noBypassValves) }}</div>
        </div>
      </div>

      <!-- 分区 5: 性能 -->
      <div class="hairline p-3">
        <div class="text-sm font-medium mb-2 text-muted">性能</div>
        <div class="grid grid-cols-2 gap-y-1 text-sm">
          <div class="text-muted">Media</div><div>{{ data.media || '—' }}</div>
          <div class="text-muted">MediaModel</div><div>{{ data.mediaModel || '—' }}</div>
          <div class="text-muted">效率</div><div>{{ data.efficiency1 || '—' }} / {{ data.efficiency2 || '—' }}</div>
          <div class="text-muted">旁通压力</div><div>{{ numOrDash(data.bypassPressure) }}</div>
          <div class="text-muted">破裂压力 (bar)</div><div>{{ numOrDash(data.collapsePressureBar) }}</div>
          <div class="text-muted">密封材料</div><div>{{ data.sealingMaterial || '—' }}</div>
          <div class="text-muted">温度范围</div><div>{{ data.tempRange || '—' }}</div>
        </div>
      </div>

      <!-- 分区 6: 包装 -->
      <div class="hairline p-3">
        <div class="text-sm font-medium mb-2 text-muted">包装</div>
        <div class="grid grid-cols-2 gap-y-1 text-sm">
          <div class="text-muted">箱/件</div><div>{{ numOrDash(data.qtyPerCarton) }}</div>
          <div class="text-muted">重量 (kg)</div><div>{{ numOrDash(data.weightKgs) }}</div>
          <div class="text-muted">箱尺寸 (mm)</div><div>{{ numOrDash(data.cartonLengthMm) }} × {{ numOrDash(data.cartonWidthMm) }} × {{ numOrDash(data.cartonHeightMm) }}</div>
          <div class="text-muted">体积 (m³)</div><div>{{ numOrDash(data.volumePerCartonM3) }}</div>
        </div>
      </div>
    </div>

    <!-- 交叉引用 -->
    <div v-if="data && data.crossReferences.length > 0" class="hairline mt-3">
      <div class="hairline-b px-2 py-1 bg-neutral-50 text-sm font-medium">交叉引用 ({{ data.crossReferences.length }})</div>
      <el-table :data="data.crossReferences" size="small" max-height="240">
        <el-table-column prop="oemBrand" label="品牌" width="160" />
        <el-table-column prop="oemNo3" label="OEM" width="220" />
        <el-table-column prop="productName1" label="产品名" />
      </el-table>
    </div>

    <!-- 适用车型 -->
    <div v-if="data && data.machineApplications.length > 0" class="hairline mt-3">
      <div class="hairline-b px-2 py-1 bg-neutral-50 text-sm font-medium">适用车型 ({{ data.machineApplications.length }})</div>
      <el-table :data="data.machineApplications" size="small" max-height="320">
        <el-table-column prop="machineBrand" label="品牌" width="120" />
        <el-table-column prop="machineModel" label="型号" width="160" />
        <el-table-column prop="modelName" label="名称" width="160" />
        <el-table-column prop="engineBrand" label="发动机品牌" width="100" />
        <el-table-column prop="engineType" label="发动机型号" width="120" />
        <el-table-column prop="productionDateStart" label="生产起始" width="100" />
        <el-table-column prop="productionDateEnd" label="生产结束" width="100" />
      </el-table>
    </div>
  </div>
</template>
