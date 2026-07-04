<script setup lang="ts">
// Day 9: 产品详情页 (后台/前台共用)
// P3.3 (Task 11): 公开产品页 + SEO/OG meta + imageKey 命名 (R5 规格) + 7 分区折叠
//   - 调 productApi.getByOem(slug) → /public/product/{slug} (公开, [AllowAnonymous])
//   - URL 格式 (R1): {name1}-{name2}-{oemBrand}-{oemNo}, 后端解析末段为 oem
//   - SEO: document.title = "name1 name2 OEM_BRAND OEM_NO - SakuraFilter"
//   - OG: og:title / og:image / og:description / og:type=product
//   - imageKey: 主图 oem2/{OEM}.jpg, 副图 oem2/{OEM}_{slot}.jpg (R5 命名)
//   - 7 分区: 基础/替代/尺寸/图片/性能/包装/适配, <el-collapse> 全展开
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

// P3.3 (Task 11): 7 分区默认全展开
const activeNames = ref<string[]>(['p1', 'p2', 'p3', 'p4', 'p5', 'p6', 'p7'])

async function load() {
  loading.value = true
  err.value = ''
  try {
    data.value = await productApi.getByOem(slug.value)
    applySeo()
  } catch (e: any) {
    err.value = e?.problem?.detail || e?.response?.data?.detail || e?.message || '加载失败'
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
  const brand = (d.crossReferences?.[0]?.oemBrand) ?? d.oem2 ?? ''
  const title = `${d.productName1 ?? ''} ${d.productName2 ?? ''} ${brand} ${d.oemNoDisplay} - SakuraFilter`.replace(/\s+/g, ' ').trim()
  document.title = title
  // OG meta (动态创建, 组件卸载时移除)
  ensureOgTag('og:title', `${d.productName1 ?? ''} ${d.productName2 ?? ''}`.trim())
  ensureOgTag('og:description', `${d.productName1 ?? ''} ${d.productName2 ?? ''} ${brand} ${d.oemNoDisplay}`.replace(/\s+/g, ' ').trim())
  ensureOgTag('og:type', 'product')
  if (d.images && d.images.length > 0 && d.images[0].imageUrl) {
    ensureOgTag('og:image', d.images[0].imageUrl)
  }
  // canonical link
  ensureLinkTag('canonical', `${location.origin}/product/${encodeURIComponent(slug.value)}`)
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
function ensureLinkTag(rel: string, href: string) {
  let el = document.head.querySelector<HTMLLinkElement>(`link[rel="${rel}"]`)
  if (!el) {
    el = document.createElement('link')
    el.setAttribute('rel', rel)
    document.head.appendChild(el)
    ogTags.push(el as any)
  }
  el.setAttribute('href', href)
}

// ===== P3.3 (Task 11): imageKey 命名 (R5 规格) =====
// 主图: oem2/{OEM}.jpg
// 副图: oem2/{OEM}_{slot}.jpg (slot 2-6)
function buildImageUrl(key: string, oem: string, slot: number): string {
  if (key && key.startsWith('http')) return key
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
  <!-- 需求 2: 卡片式布局 + 1px hairline 边框 + 7 分区宽度统一 (max-w-6xl) -->
  <div class="p-3 max-w-6xl mx-auto" v-loading="loading">
    <!-- 顶部导航 + 关键标识 -->
    <div class="flex items-center gap-2 mb-4 pb-3 hairline-b">
      <el-button @click="goBack" size="small">
        <el-icon><ArrowLeft /></el-icon> 返回
      </el-button>
      <h1 class="text-lg font-medium" v-if="data">{{ data.oemNoDisplay }}</h1>
      <el-tag v-if="data?.type" size="small">{{ data.type }}</el-tag>
      <el-tag v-if="data?.isPublished" type="success" size="small">已发布</el-tag>
      <el-tag v-if="data?.isDiscontinued" type="info" size="small">已停售</el-tag>
    </div>

    <div v-if="err" class="text-red-600 text-sm mb-2">{{ err }}</div>

    <!-- 7 分区卡片网格: 2 列布局 (大屏) / 1 列 (小屏); 表格/图片类分区跨 2 列 -->
    <div v-if="data" class="grid grid-cols-1 lg:grid-cols-2 gap-4">
      <!-- 分区 1: 基础信息 -->
      <section class="hairline p-4 bg-[var(--color-bg-elevated)]">
        <header class="flex items-center justify-between mb-3 pb-2 hairline-b">
          <h2 class="text-sm font-medium">基础信息</h2>
          <span class="text-xs text-muted">7 项</span>
        </header>
        <div class="grid grid-cols-2 gap-y-2 text-sm">
          <div class="text-muted">Product Name 1</div><div>{{ data.productName1 || '—' }}</div>
          <div class="text-muted">Product Name 2</div><div>{{ data.productName2 || '—' }}</div>
          <div class="text-muted">Type</div><div>{{ data.type || '—' }}</div>
          <div class="text-muted">MR.1</div><div>{{ data.mr1 || '—' }}</div>
          <div class="text-muted">OEM 2</div><div>{{ data.oem2 || '—' }}</div>
          <div class="text-muted">OEM 1 (Display)</div><div>{{ data.oemNoDisplay || '—' }}</div>
          <div class="text-muted">上架</div>
          <div>
            <el-tag v-if="data.isPublished" type="success" size="small">是</el-tag>
            <el-tag v-else type="info" size="small">否</el-tag>
          </div>
        </div>
      </section>

      <!-- 分区 2: 替代 OEM (跨整行, 表格宽) -->
      <section class="hairline p-4 bg-[var(--color-bg-elevated)] lg:col-span-2">
        <header class="flex items-center justify-between mb-3 pb-2 hairline-b">
          <h2 class="text-sm font-medium">替代 OEM</h2>
          <span class="text-xs text-muted">{{ data.crossReferences?.length ?? 0 }} 条</span>
        </header>
        <el-table v-if="data.crossReferences && data.crossReferences.length > 0"
          :data="data.crossReferences" size="small" max-height="320">
          <el-table-column prop="oemBrand" label="OEM Brand" width="160" />
          <el-table-column prop="oemNo3" label="OEM 3 NO." width="220" />
          <el-table-column prop="productName1" label="Product Name 1" />
        </el-table>
        <div v-else class="text-muted text-sm">暂无替代 OEM</div>
      </section>

      <!-- 分区 3: 尺寸 -->
      <section class="hairline p-4 bg-[var(--color-bg-elevated)]">
        <header class="flex items-center justify-between mb-3 pb-2 hairline-b">
          <h2 class="text-sm font-medium">尺寸 (mm)</h2>
        </header>
        <div class="grid grid-cols-4 gap-y-2 text-sm">
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
          <div class="text-muted">单向阀数</div><div>{{ numOrDash(data.noCheckValves) }}</div>
          <div class="text-muted">旁通阀数</div><div>{{ numOrDash(data.noBypassValves) }}</div>
        </div>
      </section>

      <!-- 分区 4: 图片 (跨整行, 主图 + 5 副图, R5 命名 oem2/{OEM}_{slot}.jpg) -->
      <section class="hairline p-4 bg-[var(--color-bg-elevated)] lg:col-span-2">
        <header class="flex items-center justify-between mb-3 pb-2 hairline-b">
          <h2 class="text-sm font-medium">图片</h2>
          <span class="text-xs text-muted">{{ imageUrls.length }} 张</span>
        </header>
        <div v-if="imageUrls.length > 0" class="flex gap-3 flex-wrap">
          <div v-for="img in imageUrls" :key="img.slot" class="text-center">
            <img :src="img.url" :alt="`Slot ${img.slot}`"
                 class="w-32 h-32 object-cover hairline"
                 @error="(e) => ((e.target as HTMLImageElement).src = '/logo.png')" />
            <div class="text-xs text-muted mt-1">图 {{ img.slot }}</div>
          </div>
        </div>
        <div v-else class="text-muted text-sm">暂无图片 (按 R5 命名: oem2/{OEM}.jpg)</div>
      </section>

      <!-- 分区 5: 性能 -->
      <section class="hairline p-4 bg-[var(--color-bg-elevated)]">
        <header class="flex items-center justify-between mb-3 pb-2 hairline-b">
          <h2 class="text-sm font-medium">性能</h2>
        </header>
        <div class="grid grid-cols-2 gap-y-2 text-sm">
          <div class="text-muted">Media Name</div><div>{{ data.media || '—' }}</div>
          <div class="text-muted">Media Model</div><div>{{ data.mediaModel || '—' }}</div>
          <div class="text-muted">Bypass Valve LR</div><div>{{ numOrDash(data.bypassValveLr) }}</div>
          <div class="text-muted">Bypass Valve HR</div><div>{{ numOrDash(data.bypassValveHr) }}</div>
          <div class="text-muted">Efficiency 1 / 2</div><div>{{ data.efficiency1 || '—' }} / {{ data.efficiency2 || '—' }}</div>
          <div class="text-muted">Bypass Pressure</div><div>{{ numOrDash(data.bypassPressure) }}</div>
          <div class="text-muted">Δ Collapse Pressure (bar)</div><div>{{ numOrDash(data.collapsePressureBar) }}</div>
          <div class="text-muted">Seal Material</div><div>{{ data.sealingMaterial || '—' }}</div>
          <div class="text-muted">Temperature Range</div><div>{{ data.tempRange || '—' }}</div>
        </div>
      </section>

      <!-- 分区 6: 包装 -->
      <section class="hairline p-4 bg-[var(--color-bg-elevated)]">
        <header class="flex items-center justify-between mb-3 pb-2 hairline-b">
          <h2 class="text-sm font-medium">包装</h2>
        </header>
        <div class="grid grid-cols-2 gap-y-2 text-sm">
          <div class="text-muted">Each Carton QTY</div><div>{{ numOrDash(data.qtyPerCarton) }}</div>
          <div class="text-muted">Each Carton Weight (KGS)</div><div>{{ numOrDash(data.weightKgs) }}</div>
          <div class="text-muted">Carton Length (mm)</div><div>{{ numOrDash(data.cartonLengthMm) }}</div>
          <div class="text-muted">Carton Width (mm)</div><div>{{ numOrDash(data.cartonWidthMm) }}</div>
          <div class="text-muted">Carton Height (mm)</div><div>{{ numOrDash(data.cartonHeightMm) }}</div>
          <div class="text-muted">Volume / CTN (m³)</div>
          <div><strong>{{ numOrDash(data.volumePerCartonM3) }}</strong> <span class="text-xs text-muted">(自动)</span></div>
        </div>
      </section>

      <!-- 分区 7: 适配车型 (跨整行) -->
      <section class="hairline p-4 bg-[var(--color-bg-elevated)] lg:col-span-2">
        <header class="flex items-center justify-between mb-3 pb-2 hairline-b">
          <h2 class="text-sm font-medium">适配车型</h2>
          <span class="text-xs text-muted">{{ data.machineApplications?.length ?? 0 }} 条</span>
        </header>
        <el-table v-if="data.machineApplications && data.machineApplications.length > 0"
          :data="data.machineApplications" size="small" max-height="360">
          <el-table-column prop="machineBrand" label="品牌" width="120" />
          <el-table-column prop="machineModel" label="型号" width="140" />
          <el-table-column prop="modelName" label="名称" width="140" />
          <el-table-column prop="engineBrand" label="发动机品牌" width="100" />
          <el-table-column prop="engineType" label="发动机型号" width="120" />
          <el-table-column prop="productionDateStart" label="生产起" width="100" />
          <el-table-column prop="productionDateEnd" label="生产止" width="100" />
          <el-table-column prop="power" label="功率" width="80" />
        </el-table>
        <div v-else class="text-muted text-sm">暂无适配车型</div>
      </section>
    </div>

    <!-- 备注 (分区 2 派生) -->
    <div v-if="data?.remark" class="hairline mt-4 p-4 bg-[var(--color-bg-elevated)]">
      <div class="text-sm font-medium mb-1 text-muted">备注</div>
      <div class="text-sm">{{ data.remark }}</div>
    </div>
  </div>
</template>
