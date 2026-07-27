<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'

const props = defineProps<{ page: 'about' | 'news' | 'contact' }>()
const router = useRouter()

const content = computed(() => ({
  about: { title: 'About SakuraFilter', text: import.meta.env.VITE_PUBLIC_ABOUT_TEXT?.trim(), action: '浏览产品', path: '/search' },
  news: { title: 'News', text: import.meta.env.VITE_PUBLIC_NEWS_TEXT?.trim(), action: '查看目录', path: '/search' },
  contact: { title: 'Contact us', text: import.meta.env.VITE_PUBLIC_CONTACT_TEXT?.trim(), action: '查找产品', path: '/search' }
}[props.page]))
</script>

<template>
  <section class="max-w-3xl mx-auto px-6 py-16">
    <p class="text-xs tracking-[0.2em] uppercase text-[var(--color-accent)]">SakuraFilter</p>
    <h1 class="mt-3 text-3xl font-semibold">{{ content.title }}</h1>
    <p v-if="content.text" class="mt-6 text-base leading-8 text-[var(--color-text-secondary)]">{{ content.text }}</p>
    <p v-else class="mt-6 text-base leading-8 text-[var(--color-text-secondary)]">内容正在准备中。</p>
    <el-button class="mt-8" type="primary" @click="router.push(content.path)">{{ content.action }}</el-button>
  </section>
</template>
