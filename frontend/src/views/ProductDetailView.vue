<script setup lang="ts">
// Day 9: 产品详情页
//   - 顶部: 关键信息 (OEM/MR/Type)
//   - 中部: 7 分区规格 (基础信息/尺寸/螺纹/性能/包装/媒体/备注)
//   - 底部: 交叉引用 + 适用车型
// P3.3 (Task 11): 公开产品页 + SEO/OG meta + imageKey 命名验证 (R5 规格)
//   - 调 productApi.getByOem(slug) → /public/product/{slug} (公开)
//   - URL 格式 (R1): {name1}-{name2}-{oemBrand}-{oemNo}, 后端解析末段为 oem
//   - SEO: document.title = "name1 name2 OEM_BRAND OEM_NO - SakuraFilter"
//   - OG: og:title / og:image / og:description / og:type=product
//   - imageKey: 主图 oem2/{OEM}.jpg, 副图 oem2/{OEM}_{slot}.jpg
import { ref, onMounted, computed, watch, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { productApi } from '@/api'
import type { ProductDetail } from '@/api/types'

const route = useRoute()
const router = useRouter()

const slug = computed(() => String(route.params.oem || ''))  // param name still 'oem' (backward compat)
const data = ref<ProductDetail | null>(null)
const loading = ref(false)
const err = ref('')

async function load() {
  loading.value = true
  err.value = ''
  try {
    data.value = await productApi.getByOem(slug.value)
    applySeo()
  } catch (e: any) {
    err.value = e?.problem?.detail || e?.message || '加载失败'
    data.value = null
  } finally {
    loading.value = false
  }
}

// ===== P3.3 (Task 11): SEO + OG meta =====
let ogTags: HTMLMetaElement[] = []
function applySeo() {
  const d = data.value
  if (!d) return
  // title
  const brand = (d.crossReferences[0]?.oemBrand) ?? d.oem2 ?? ''
  const title = `${d.productName1 ?? ''} ${d.productName2 ?? ''} ${brand} ${d.oemNoDisplay} - SakuraFilter`.replace(/\s+/g, ' ').trim()
  document.title = title
  // OG meta (动态创建, 组件卸载时移除)
  ensureOgTag('og:title', `${d.productName1 ?? ''} ${d.productName2 ?? ''}`)
  ensureOgTag('og:description', `${d.productName1 ?? ''} ${d.productName2 ?? ''} ${brand} ${d.oemNoDisplay}`)
  ensureOgTag('og:type', 'product')
  if (d.images && d.images.length > 0) {
    // 后端 ProductDetailDto 通过 images[].url 暴露预签名 URL (P3.3 改造)
    // 前端 TS 类型用 imageUrl 字段名 (与后端 url 映射)
    // 命名约定: 主图 slot 1 = oem2/{OEM}.jpg, 副图 slot 2-6 = oem2/{OEM}_{slot}.jpg
    ensureOgTag('og:image', d.images[0].imageUrl)
  }
}
function ensureOgTag(property: string, content: string) {
  let el = document.head.querySelector<HTMLMetaElement>(`meta[property="${property}"]`)
  if (!el) {
    el = document.createElement('meta')
    el.setAttribute('property', property)
    document.head.appendChild(el)
    ogTags.push(el)
  }
  el.setAttribute('content', content)
}

// ===== P3.3 (Task 11): imageKey 命名 (R5 规格) =====
// 主图: oem2/{OEM}.jpg
// 副图: oem2/{OEM}_{slot}.jpg (slot 2-6)
function buildImageUrl(key: string, oem: string, slot: number): string {
  // 实际项目用 OSS 预签名; MVP 阶段回退到 /static/images/
  if (key.startsWith('http')) return key
  // 命名: oem2/{OEM}.jpg (主图) / oem2/{OEM}_{slot}.jpg (副图)
  const slotSuffix = slot === 1 ? '' : `_${slot}`
  return `/oem2/${oem}${slotSuffix}.jpg`
}

// 收集所有可用图片 URL (主图 + 副图 slot 1-6, R5 规格命名)
const imageUrls = computed(() => {
  const d = data.value
  if (!d) return []
  return (d.images ?? []).map(img => ({
    slot: img.slot,
    url: img.imageUrl || buildImageUrl(img.imageKey, d.oemNoDisplay, img.slot)
  }))
})

// 清理 OG meta (避免页面切换残留)
onUnmounted(() => {
  for (const el of ogTags) el.remove()
  ogTags = []
  document.title = 'SakuraFilter'
})

onMounted(load)
watch(() => slug.value, load)

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

    <!-- P3.3 (Task 11): 产品图片 (主图 + 副图 slot 1-6, R5 规格命名) -->
    <div v-if="imageUrls.length > 0" class="hairline mb-3 p-3">
      <div class="text-sm font-medium mb-2 text-muted">产品图片</div>
      <div class="flex gap-2 flex-wrap">
        <div v-for="img in imageUrls" :key="img.slot" class="text-center">
          <img :src="img.url" :alt="`Slot ${img.slot}`"
               class="w-32 h-32 object-cover hairline-b"
               @error="(e) => ((e.target as HTMLImageElement).src = '/logo.png')" />
          <div class="text-xs text-muted mt-1">图 {{ img.slot }}</div>
        </div>
      </div>
    </div>

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
