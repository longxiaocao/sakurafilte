<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAdminAuth } from '@/composables/useAdminAuth'

const route = useRoute()
const router = useRouter()
const { isAdmin, setToken } = useAdminAuth()

const isAdminPath = computed(() => route.path.startsWith('/admin'))

const navItems = computed(() => [
  { label: '产品搜索', path: '/search', icon: 'Search' },
  { label: '产品详情', path: '/product', icon: 'Document' },
  ...(isAdminPath.value
    ? [
        { label: '产品管理', path: '/admin/products', icon: 'Goods' },
        { label: 'ETL 触发', path: '/admin/etl', icon: 'Loading' }
      ]
    : [])
])

function go(path: string) {
  router.push(path)
}

function toggleAdmin() {
  if (isAdminPath.value) {
    setToken('')
    router.push('/search')
  } else {
    const token = prompt('请输入 X-Admin-Token:', localStorage.getItem('adminToken') || '')
    if (token) {
      setToken(token)
      router.push('/admin/products')
    }
  }
}
</script>

<template>
  <header class="hairline-b bg-white flex items-center px-3 h-12 gap-3">
    <div class="font-medium text-base tracking-tight">SakuraFilter</div>
    <nav class="flex items-center gap-1 ml-3">
      <button
        v-for="item in navItems"
        :key="item.path"
        @click="go(item.path)"
        :class="[
          'px-2 py-1 text-sm hover:bg-neutral-100',
          route.path === item.path ? 'text-accent font-medium' : 'text-neutral-700'
        ]"
      >
        <el-icon class="mr-1"><component :is="item.icon" /></el-icon>
        {{ item.label }}
      </button>
    </nav>
    <div class="flex-1" />
    <button
      @click="toggleAdmin"
      class="px-2 py-1 text-sm hairline hover:bg-neutral-100 flex items-center gap-1"
    >
      <el-icon><Lock v-if="!isAdminPath" /><Unlock v-else /></el-icon>
      {{ isAdminPath ? '退出后台' : '进入后台' }}
    </button>
  </header>
</template>
