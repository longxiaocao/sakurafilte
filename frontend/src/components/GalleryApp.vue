<script setup lang="ts">
// V2 Task 4.5.1: 产品详情页图片画廊子组件 (Vue client mount)
//   - 接收 images 数组, 主图 + 缩略图列表
//   - 点击缩略图切换主图
//   - 主图加载失败兜底 (placeholder)
//   - SSR 已渲染主图 (Detail.cshtml gallery-app 内 <img>), Vue mount 时清空并重新渲染
//   - 注意: props.oemNo3 实际传值为 product.oemNoDisplay (产品 OEM 编号, 用于 alt 文本)
import { ref, computed } from 'vue'

interface GalleryImage {
  imageKey: string
  imageUrl: string
  oemNo3?: string | null
  imageRole?: string | null
  isPrimary?: boolean
  slot?: number
}

interface GalleryProps {
  images: GalleryImage[]
  oemNo3: string
  mr1: string | null
}

const props = defineProps<GalleryProps>()

const placeholderUrl = '/static/placeholder.png'

// 主图初始: 优先 isPrimary, 否则取第一张
const primaryInitial = computed(() => {
  return props.images.find(i => i.isPrimary) ?? props.images[0]
})

const currentImageUrl = ref<string>(primaryInitial.value?.imageUrl ?? placeholderUrl)
const currentAlt = ref<string>(props.oemNo3 ?? '产品图片')

function selectImage(img: GalleryImage): void {
  currentImageUrl.value = img.imageUrl
  currentAlt.value = `${props.oemNo3 ?? ''} - slot ${img.slot ?? ''}`.trim()
}

function onImageError(): void {
  // 主图加载失败兜底
  currentImageUrl.value = placeholderUrl
}
</script>

<template>
  <div class="gallery-app">
    <!-- 主图 -->
    <div class="gallery-main">
      <img
        :src="currentImageUrl"
        :alt="currentAlt"
        loading="lazy"
        @error="onImageError"
      />
    </div>
    <!-- 缩略图列表 (仅多图时显示) -->
    <div v-if="props.images.length > 1" class="gallery-thumbs">
      <button
        v-for="(img, idx) in props.images"
        :key="img.imageKey || idx"
        type="button"
        :class="['gallery-thumb', { active: img.imageUrl === currentImageUrl }]"
        :title="`Slot ${img.slot ?? idx + 1}`"
        @click="selectImage(img)"
      >
        <img :src="img.imageUrl" :alt="`Slot ${img.slot ?? idx + 1}`" loading="lazy" />
      </button>
    </div>
    <p v-else-if="props.images.length === 1" class="gallery-hint">仅 1 张图片</p>
    <p v-else class="gallery-hint gallery-empty">暂无图片</p>
  </div>
</template>

<style scoped>
.gallery-app {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.gallery-main {
  width: 100%;
  aspect-ratio: 1 / 1;
  background: #fafafa;
  overflow: hidden;
  border: 1px solid #e5e7eb;
  border-radius: 4px;
}
.gallery-main img {
  width: 100%;
  height: 100%;
  object-fit: contain;
}
.gallery-thumbs {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(60px, 1fr));
  gap: 4px;
}
.gallery-thumb {
  width: 100%;
  aspect-ratio: 1 / 1;
  padding: 0;
  border: 1px solid #e5e7eb;
  background: #fff;
  cursor: pointer;
  overflow: hidden;
  border-radius: 2px;
  transition: border-color 0.15s;
}
.gallery-thumb:hover {
  border-color: #999;
}
.gallery-thumb.active {
  border-color: #000;
  border-width: 2px;
}
.gallery-thumb img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.gallery-hint {
  margin: 0;
  padding: 4px 8px;
  font-size: 12px;
  color: #999;
}
.gallery-empty {
  color: #ccc;
  text-align: center;
  padding: 16px;
}
</style>
