<script setup lang="ts">
// Day 9: 产品详情页 (后台/前台共用)
// 重构 (2026-07): 工业极简融合风 — 大字号对比 + 大留白 + 1px hairline
//   设计语言: Linear/Vercel/Stripe 工业极简 + Musk 极简主义
//   - 顶部细条: 返回 + OEM + 状态徽章
//   - Hero 区: 左图(5/12) + 右关键信息(7/12), 大字号产品名 + 4 关键规格卡片 + 统计行
//   - 详细规格: 4 卡片网格 2x2 (基础/尺寸/性能/包装)
//   - 全宽表格: 替代 OEM + 适配车型
//   - 图片画廊: 全宽 grid-cols-6, el-image 支持灯箱
//   - 保留: SEO/OG meta, imageKey 命名 (R5), numOrDash
import { ref, onMounted, computed, watch, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { productApi } from '@/api'
import type { ProductDetail } from '@/api/types'
import SkeletonCard from '@/components/SkeletonCard.vue'

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

// 工业极简融合风: 主图 + 灯箱预览列表
const mainImage = computed(() => imageUrls.value[0]?.url ?? '/logo.png')
const previewSrcList = computed(() => imageUrls.value.map(i => i.url))
const activeImageIdx = ref(0)
const activeImage = computed(() => imageUrls.value[activeImageIdx.value]?.url ?? mainImage.value)

// Hero 区关键规格 4 卡片 (D1 / H1 / Media / Type)
const heroSpecs = computed(() => {
  const d = data.value
  if (!d) return []
  return [
    { label: 'D1', value: d.d1Mm ? `${d.d1Mm}mm` : '—' },
    { label: 'H1', value: d.h1Mm ? `${d.h1Mm}mm` : '—' },
    { label: 'Media', value: d.media ?? '—' },
    { label: 'Type', value: d.type ?? '—' }
  ]
})

// 统计行 (替代 OEM 数 + 适配车型数 + 图片数)
const stats = computed(() => {
  const d = data.value
  if (!d) return []
  return [
    { label: '替代 OEM', value: d.crossReferences?.length ?? 0 },
    { label: '适配车型', value: d.machineApplications?.length ?? 0 },
    { label: '图片', value: imageUrls.value.length }
  ]
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

// ===== P0 UX 修复 (Day 14): 详情页操作按钮带上下文 =====
//   - 查询替代: 滚动到页面下方"替代 OEM" 表格, 而非裸跳 /search
//     (用户报告 P0: 跳到 /search 不带参数, 需要手输, 体验断链)
//   - 加入对比: 携带当前产品 ID 跳 /compare?ids=<id> (公开对比页, 游客可用)
//     (P0: 原实现跳 /admin/compare 需登录, 体验断链)
//     公开页未登录用户可直接访问 /compare, 无需 redirect/login
function goToAlternatives() {
  const el = document.getElementById('section-alternatives')
  if (el) {
    el.scrollIntoView({ behavior: 'smooth', block: 'start' })
  } else {
    ElMessage.warning('替代 OEM 表格未就绪')
  }
}

function addToCompare() {
  if (!data.value?.id) {
    ElMessage.warning('产品数据未加载')
    return
  }
  // P0 (Day 14): 跳公开对比页 /compare (无 requireAuth), 游客可直接使用
  router.push(`/compare?ids=${data.value.id}`)
}

function numOrDash(v?: number | string) {
  if (v === null || v === undefined || v === '') return '—'
  return v
}
</script>

<template>
  <!-- 工业极简融合风: 大字号 + 大留白 + 1px hairline + 数字驱动 -->
  <div class="p-4 md:p-8 max-w-7xl mx-auto">
    <!-- 顶部细条: 返回 + OEM + 状态徽章 -->
    <div class="flex items-center gap-3 mb-6 text-xs text-muted">
      <button
        @click="goBack"
        class="hover:text-[var(--color-accent)] flex items-center gap-1"
        aria-label="返回上一页"
      >
        <span aria-hidden="true">←</span> 返回
      </button>
      <span class="text-[var(--color-border)]" aria-hidden="true">/</span>
      <span v-if="data" class="font-mono">{{ data.oemNoDisplay }}</span>
      <template v-if="data">
        <span class="text-[var(--color-border)]" aria-hidden="true">·</span>
        <span>{{ data.type }}</span>
        <span v-if="data.isPublished" class="hairline px-2 py-0.5 text-[10px] uppercase tracking-wider">已发布</span>
        <span v-if="data.isDiscontinued" class="hairline px-2 py-0.5 text-[10px] uppercase tracking-wider text-muted">已停售</span>
      </template>
    </div>

    <div
      v-if="err"
      class="text-red-600 text-sm mb-6 hairline-l border-l-red-600 pl-3"
      role="alert"
      aria-live="assertive"
    >{{ err }}</div>

    <!-- P1.2 骨架屏: 加载期间展示详情骨架, 提升感知性能 -->
    <SkeletonCard v-if="loading && !data" variant="detail" />

    <!-- ===== Hero 区: 左图 5/12 + 右关键信息 7/12 ===== -->
    <section v-if="data" class="grid grid-cols-1 lg:grid-cols-12 gap-8 lg:gap-12 mb-12" role="region" aria-label="产品关键信息">
      <!-- 左: 主图 + 缩略图列表 -->
      <div class="lg:col-span-5">
        <div class="hairline bg-[var(--color-bg-elevated)] aspect-square flex items-center justify-center overflow-hidden">
          <el-image
            :src="activeImage"
            :preview-src-list="previewSrcList"
            :initial-index="activeImageIdx"
            fit="contain"
            :preview-teleported="true"
            class="w-full h-full"
            hide-on-click-modal
          >
            <template #error>
              <div class="w-full h-full flex items-center justify-center text-muted text-sm">
                <div class="text-center">
                  <div class="text-4xl mb-2">⊘</div>
                  <div>暂无图片</div>
                  <div class="text-xs mt-1">R5 命名: oem2/{{ data.oemNoDisplay }}.jpg</div>
                </div>
              </div>
            </template>
            <template #placeholder>
              <div class="w-full h-full flex items-center justify-center text-muted text-xs">加载中...</div>
            </template>
          </el-image>
        </div>
        <!-- 缩略图列表 -->
        <div v-if="imageUrls.length > 1" class="flex gap-2 mt-3 overflow-x-auto">
          <button
            v-for="(img, idx) in imageUrls"
            :key="img.slot"
            @click="activeImageIdx = idx"
            :class="[
              'w-16 h-16 hairline flex-shrink-0 overflow-hidden bg-[var(--color-bg-elevated)]',
              activeImageIdx === idx ? 'ring-1 ring-[var(--color-accent)]' : ''
            ]"
          >
            <img :src="img.url" :alt="`Slot ${img.slot}`" class="w-full h-full object-cover" loading="lazy" />
          </button>
        </div>
      </div>

      <!-- 右: 关键信息 (大字号 + 规格 + 统计) -->
      <div class="lg:col-span-7 flex flex-col justify-between">
        <div>
          <!-- 巨大标题 -->
          <h1 class="text-3xl md:text-4xl font-medium tracking-tight leading-tight">
            {{ data.productName1 || '—' }}
            <span class="text-[var(--color-text-muted)] font-normal">{{ data.productName2 }}</span>
          </h1>
          <!-- 副标题 -->
          <div class="text-sm text-muted mt-2 font-mono tabular-nums">
            {{ data.oem2 || '—' }} · {{ data.mr1 || '—' }} · {{ data.oemNoDisplay }}
          </div>

          <!-- 关键规格 4 卡片 -->
          <div class="grid grid-cols-2 md:grid-cols-4 gap-3 mt-8">
            <div v-for="spec in heroSpecs" :key="spec.label" class="hairline p-4 bg-[var(--color-bg-elevated)]">
              <div class="text-[10px] uppercase tracking-wider text-muted">{{ spec.label }}</div>
              <div class="text-xl font-medium mt-1 font-mono tabular-nums">{{ spec.value }}</div>
            </div>
          </div>

          <!-- 统计行 -->
          <div class="flex items-center gap-6 mt-6 text-sm">
            <div v-for="s in stats" :key="s.label" class="flex items-baseline gap-2">
              <span class="text-2xl font-medium font-mono tabular-nums">{{ s.value }}</span>
              <span class="text-xs text-muted uppercase tracking-wider">{{ s.label }}</span>
            </div>
          </div>
        </div>

        <!-- 操作按钮 (查询替代/对比/分享) -->
        <!-- P0 修复 (Day 14): 两个按钮都带上下文, 跳到目标后无需用户手输 -->
        <div class="flex gap-2 mt-8">
          <el-button @click="goToAlternatives" plain size="small">
            查询替代 ({{ data?.crossReferences?.length ?? 0 }})
          </el-button>
          <el-button @click="addToCompare" plain size="small">加入对比</el-button>
        </div>
      </div>
    </section>

    <!-- ===== 详细规格: 章节标题 + 4 卡片 2x2 ===== -->
    <section v-if="data" class="mb-12">
      <div class="flex items-baseline justify-between hairline-b pb-2 mb-6">
        <h2 class="text-sm font-medium uppercase tracking-wider">完整规格</h2>
        <span class="text-xs text-muted">4 项</span>
      </div>
      <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
        <!-- 基础信息 -->
        <div class="hairline p-6 bg-[var(--color-bg-elevated)]">
          <h3 class="text-xs uppercase tracking-wider text-muted mb-4">基础信息</h3>
          <div class="grid grid-cols-2 gap-y-3 text-sm">
            <div class="text-muted">Product Name 1</div><div class="font-mono">{{ data.productName1 || '—' }}</div>
            <div class="text-muted">Product Name 2</div><div class="font-mono">{{ data.productName2 || '—' }}</div>
            <div class="text-muted">Type</div><div class="font-mono">{{ data.type || '—' }}</div>
            <div class="text-muted">MR.1</div><div class="font-mono">{{ data.mr1 || '—' }}</div>
            <div class="text-muted">OEM 2</div><div class="font-mono">{{ data.oem2 || '—' }}</div>
            <div class="text-muted">OEM 1 (Display)</div><div class="font-mono text-[var(--color-accent)]">{{ data.oemNoDisplay || '—' }}</div>
            <div class="text-muted">上架</div>
            <div>
              <span v-if="data.isPublished" class="text-xs">✓ 是</span>
              <span v-else class="text-xs text-muted">✗ 否</span>
            </div>
          </div>
        </div>

        <!-- 尺寸 -->
        <div class="hairline p-6 bg-[var(--color-bg-elevated)]">
          <h3 class="text-xs uppercase tracking-wider text-muted mb-4">尺寸 (mm)</h3>
          <div class="grid grid-cols-4 gap-y-3 text-sm">
            <div class="text-muted text-xs">D1</div><div class="font-mono tabular-nums">{{ numOrDash(data.d1Mm) }}</div>
            <div class="text-muted text-xs">D2</div><div class="font-mono tabular-nums">{{ numOrDash(data.d2Mm) }}</div>
            <div class="text-muted text-xs">D3</div><div class="font-mono tabular-nums">{{ numOrDash(data.d3Mm) }}</div>
            <div class="text-muted text-xs">D4</div><div class="font-mono tabular-nums">{{ numOrDash(data.d4Mm) }}</div>
            <div class="text-muted text-xs">H1</div><div class="font-mono tabular-nums">{{ numOrDash(data.h1Mm) }}</div>
            <div class="text-muted text-xs">H2</div><div class="font-mono tabular-nums">{{ numOrDash(data.h2Mm) }}</div>
            <div class="text-muted text-xs">H3</div><div class="font-mono tabular-nums">{{ numOrDash(data.h3Mm) }}</div>
            <div class="text-muted text-xs">H4</div><div class="font-mono tabular-nums">{{ numOrDash(data.h4Mm) }}</div>
            <div class="text-muted text-xs">D7 螺纹</div><div class="font-mono">{{ data.d7Thread || '—' }}</div>
            <div class="text-muted text-xs">D8 螺纹</div><div class="font-mono">{{ data.d8Thread || '—' }}</div>
            <div class="text-muted text-xs">单向阀</div><div class="font-mono tabular-nums">{{ numOrDash(data.noCheckValves) }}</div>
            <div class="text-muted text-xs">旁通阀</div><div class="font-mono tabular-nums">{{ numOrDash(data.noBypassValves) }}</div>
          </div>
        </div>

        <!-- 性能 -->
        <div class="hairline p-6 bg-[var(--color-bg-elevated)]">
          <h3 class="text-xs uppercase tracking-wider text-muted mb-4">性能</h3>
          <div class="grid grid-cols-2 gap-y-3 text-sm">
            <div class="text-muted">Media Name</div><div class="font-mono">{{ data.media || '—' }}</div>
            <div class="text-muted">Media Model</div><div class="font-mono">{{ data.mediaModel || '—' }}</div>
            <div class="text-muted">Bypass LR</div><div class="font-mono tabular-nums">{{ numOrDash(data.bypassValveLr) }}</div>
            <div class="text-muted">Bypass HR</div><div class="font-mono tabular-nums">{{ numOrDash(data.bypassValveHr) }}</div>
            <div class="text-muted">Efficiency 1/2</div><div class="font-mono">{{ data.efficiency1 || '—' }} / {{ data.efficiency2 || '—' }}</div>
            <div class="text-muted">Bypass Pressure</div><div class="font-mono tabular-nums">{{ numOrDash(data.bypassPressure) }}</div>
            <div class="text-muted">Δ Collapse (bar)</div><div class="font-mono tabular-nums">{{ numOrDash(data.collapsePressureBar) }}</div>
            <div class="text-muted">Seal Material</div><div class="font-mono">{{ data.sealingMaterial || '—' }}</div>
            <div class="text-muted">Temp Range</div><div class="font-mono">{{ data.tempRange || '—' }}</div>
          </div>
        </div>

        <!-- 包装 -->
        <div class="hairline p-6 bg-[var(--color-bg-elevated)]">
          <h3 class="text-xs uppercase tracking-wider text-muted mb-4">包装</h3>
          <div class="grid grid-cols-2 gap-y-3 text-sm">
            <div class="text-muted">Carton QTY</div><div class="font-mono tabular-nums">{{ numOrDash(data.qtyPerCarton) }}</div>
            <div class="text-muted">Weight (KGS)</div><div class="font-mono tabular-nums">{{ numOrDash(data.weightKgs) }}</div>
            <div class="text-muted">Length (mm)</div><div class="font-mono tabular-nums">{{ numOrDash(data.cartonLengthMm) }}</div>
            <div class="text-muted">Width (mm)</div><div class="font-mono tabular-nums">{{ numOrDash(data.cartonWidthMm) }}</div>
            <div class="text-muted">Height (mm)</div><div class="font-mono tabular-nums">{{ numOrDash(data.cartonHeightMm) }}</div>
            <div class="text-muted">Volume (m³)</div>
            <div class="font-mono tabular-nums font-medium">{{ numOrDash(data.volumePerCartonM3) }}</div>
          </div>
        </div>
      </div>
    </section>

    <!-- ===== 替代 OEM (全宽表格) ===== -->
    <section v-if="data" class="mb-12" id="section-alternatives">
      <div class="flex items-baseline justify-between hairline-b pb-2 mb-6">
        <h2 class="text-sm font-medium uppercase tracking-wider">替代 OEM</h2>
        <span class="text-xs text-muted font-mono tabular-nums">{{ data.crossReferences?.length ?? 0 }} 条</span>
      </div>
      <el-table v-if="data.crossReferences && data.crossReferences.length > 0"
        :data="data.crossReferences" size="small" max-height="400">
        <el-table-column prop="oemBrand" label="OEM BRAND" width="180" />
        <el-table-column prop="oemNo3" label="OEM 3 NO." width="240" />
        <el-table-column prop="productName1" label="PRODUCT NAME 1" />
      </el-table>
      <div v-else class="text-muted text-sm py-8 text-center">暂无替代 OEM</div>
    </section>

    <!-- ===== 适配车型 (全宽表格) ===== -->
    <section v-if="data" class="mb-12">
      <div class="flex items-baseline justify-between hairline-b pb-2 mb-6">
        <h2 class="text-sm font-medium uppercase tracking-wider">适配车型</h2>
        <span class="text-xs text-muted font-mono tabular-nums">{{ data.machineApplications?.length ?? 0 }} 条</span>
      </div>
      <el-table v-if="data.machineApplications && data.machineApplications.length > 0"
        :data="data.machineApplications" size="small" max-height="500">
        <el-table-column prop="machineBrand" label="品牌" width="140" />
        <el-table-column prop="machineModel" label="型号" width="160" />
        <el-table-column prop="modelName" label="名称" width="160" />
        <el-table-column prop="engineBrand" label="发动机品牌" width="120" />
        <el-table-column prop="engineType" label="发动机型号" width="140" />
        <el-table-column prop="productionDateStart" label="生产起" width="100" />
        <el-table-column prop="productionDateEnd" label="生产止" width="100" />
        <el-table-column prop="power" label="功率" width="100" />
      </el-table>
      <div v-else class="text-muted text-sm py-8 text-center">暂无适配车型</div>
    </section>

    <!-- ===== 图片画廊 (全宽 grid) ===== -->
    <section v-if="data && imageUrls.length > 0" class="mb-12">
      <div class="flex items-baseline justify-between hairline-b pb-2 mb-6">
        <h2 class="text-sm font-medium uppercase tracking-wider">图片画廊</h2>
        <span class="text-xs text-muted font-mono tabular-nums">{{ imageUrls.length }} 张</span>
      </div>
      <div class="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-6 gap-3">
        <div v-for="(img, idx) in imageUrls" :key="img.slot"
          class="hairline aspect-square bg-[var(--color-bg-elevated)] overflow-hidden cursor-pointer">
          <el-image
            :src="img.url"
            :preview-src-list="previewSrcList"
            :initial-index="idx"
            fit="cover"
            :preview-teleported="true"
            class="w-full h-full"
            hide-on-click-modal
            loading="lazy"
          >
            <template #error>
              <div class="w-full h-full flex items-center justify-center text-muted text-xs">
                ⊘
              </div>
            </template>
          </el-image>
        </div>
      </div>
    </section>

    <!-- 备注 -->
    <section v-if="data?.remark" class="hairline p-6 bg-[var(--color-bg-elevated)]">
      <h3 class="text-xs uppercase tracking-wider text-muted mb-3">备注</h3>
      <div class="text-sm">{{ data.remark }}</div>
    </section>
  </div>
</template>

<style scoped>
/* 工业极简风: 等宽数字 + 字距优化 */
.font-mono {
  font-family: 'JetBrains Mono', 'SF Mono', 'Consolas', monospace;
}

/* tabular-nums: 数字等宽对齐, 工业风必备 */
.tabular-nums {
  font-variant-numeric: tabular-nums;
}

/* 章节标题 uppercase letter-spacing */
.uppercase {
  text-transform: uppercase;
}
.tracking-wider {
  letter-spacing: 0.05em;
}

/* 顶部细条分隔符 */
.text-\[var\(--color-border\)\] {
  color: var(--color-border);
}

/* el-image 容器透明背景, 让 var(--color-bg-elevated) 透出 */
:deep(.el-image__inner) {
  background: transparent;
}

/* 缩略图选中环 */
.ring-1 {
  box-shadow: 0 0 0 1px var(--color-accent);
}
</style>
