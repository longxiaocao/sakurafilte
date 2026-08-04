<script setup lang="ts">
// 🔧 fix(审查): About/News/Contact 从后端 site-content API 读取 (原仅构建变量 VITE_PUBLIC_*_TEXT, 生产未注入时无内容)
//   后台 AdminSiteContentView 可维护 (站点名/logo/about/contact/news)
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { siteContentApi } from '@/api'
import type { NewsItem } from '@/api'

const props = defineProps<{ page: 'about' | 'news' | 'contact' }>()
const router = useRouter()

const about = ref('')
const contact = ref('')
const news = ref<NewsItem[]>([])

const content = computed(() => ({
  about: {
    title: 'About SakuraFilter',
    text: about.value || import.meta.env.VITE_PUBLIC_ABOUT_TEXT?.trim() || '',
    action: '浏览产品',
    path: '/search',
  },
  news: {
    title: 'News',
    text: '',
    action: '查看目录',
    path: '/search',
  },
  contact: {
    title: 'Contact us',
    text: contact.value || import.meta.env.VITE_PUBLIC_CONTACT_TEXT?.trim() || '',
    action: '查找产品',
    path: '/search',
  },
}[props.page]))

onMounted(async () => {
  try {
    const data = await siteContentApi.publicGet()
    about.value = data['site.about'] || ''
    contact.value = data['site.contact'] || ''
    try {
      const arr = JSON.parse(data['site.news'] || '[]')
      if (Array.isArray(arr)) news.value = arr
    } catch { /* 非法 JSON 忽略 */ }
  } catch {
    // API 不可用时降级到构建变量/占位 (不阻塞页面)
  }
})
</script>

<template>
  <section class="max-w-3xl mx-auto px-6 py-16">
    <p class="text-xs tracking-[0.2em] uppercase text-[var(--color-accent)]">SakuraFilter</p>
    <h1 class="mt-3 text-3xl font-semibold">{{ content.title }}</h1>

    <!-- News 列表 (多篇) -->
    <div v-if="props.page === 'news'" class="mt-6 space-y-6">
      <article v-for="n in news" :key="n.id" class="hairline p-4">
        <div class="flex items-baseline justify-between gap-3">
          <h2 class="text-base font-medium">{{ n.title }}</h2>
          <span class="text-xs text-muted shrink-0">{{ n.publishedAt }}</span>
        </div>
        <p class="mt-2 text-sm leading-6 whitespace-pre-line text-[var(--color-text-secondary)]">{{ n.body }}</p>
      </article>
      <p v-if="news.length === 0" class="text-sm text-[var(--color-text-secondary)]">暂无新闻。</p>
    </div>

    <!-- About / Contact 文本 -->
    <template v-else>
      <p v-if="content.text" class="mt-6 text-base leading-8 whitespace-pre-line text-[var(--color-text-secondary)]">{{ content.text }}</p>
      <p v-else class="mt-6 text-base leading-8 text-[var(--color-text-secondary)]">内容正在准备中。</p>
    </template>

    <el-button class="mt-8" type="primary" @click="router.push(content.path)">{{ content.action }}</el-button>
  </section>
</template>
