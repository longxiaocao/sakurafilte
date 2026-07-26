<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'

const props = defineProps<{ page: 'about' | 'news' | 'contact' }>()
const router = useRouter()

const content = computed(() => ({
  about: { title: 'About SakuraFilter', text: '我们专注于滤清器产品的匹配、参数管理与可靠交付。', action: '浏览产品', path: '/search' },
  news: { title: 'News', text: '产品、数据与服务动态将在此发布。当前可通过产品目录查询最新上架信息。', action: '查看目录', path: '/search' },
  contact: { title: 'Contact us', text: '请通过产品详情页的询盘入口提交 OEM 编号、数量和交期需求，我们会尽快回复。', action: '查找产品', path: '/search' }
}[props.page]))
</script>

<template>
  <section class="max-w-3xl mx-auto px-6 py-16">
    <p class="text-xs tracking-[0.2em] uppercase text-[var(--color-accent)]">SakuraFilter</p>
    <h1 class="mt-3 text-3xl font-semibold">{{ content.title }}</h1>
    <p class="mt-6 text-base leading-8 text-[var(--color-text-secondary)]">{{ content.text }}</p>
    <el-button class="mt-8" type="primary" @click="router.push(content.path)">{{ content.action }}</el-button>
  </section>
</template>
