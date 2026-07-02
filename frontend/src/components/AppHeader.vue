<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useAdminAuth } from '@/composables/useAdminAuth'

const route = useRoute()
const router = useRouter()
const { isAdmin, setToken } = useAdminAuth()

const isAdminPath = computed(() => route.path.startsWith('/admin'))

// Day 9.2: 修复 - "产品详情" 路由是 /product/:oem, 单独一个 nav 项无法满足参数化路径
//   改方案: nav 中 "产品详情" 改为 "OEM 查询", 点击后弹 ElMessageBox.prompt 收 oem, 再跳详情
//   避免之前直接 router.push('/product') 触发 "No match found" 警告
const navItems = computed(() => [
  { label: '产品搜索', path: '/search', icon: 'Search' },
  { label: 'OEM 查询', action: 'oemLookup', icon: 'Document' },
  ...(isAdminPath.value
    ? [
        { label: '产品管理', path: '/admin/products', icon: 'Goods' },
        // Day 10: 字典管理 (P1.3) — 后续 P5 会扩展为多字典入口
        { label: '品牌字典', path: '/admin/dict/oem-brands', icon: 'Collection' },
        { label: 'ETL 触发', path: '/admin/etl', icon: 'Loading' }
      ]
    : [])
])

async function go(item: { path?: string; action?: string }) {
  if (item.action === 'oemLookup') {
    // Day 9.2: 用 ElMessageBox.prompt 取代原生 prompt() (后者在嵌入式浏览器/受限环境不支持)
    try {
      const { value: oem } = await ElMessageBox.prompt('请输入 OEM 编号', 'OEM 查询', {
        inputPattern: /^.+$/,
        inputErrorMessage: 'OEM 不能为空',
        inputPlaceholder: '例: 11427622448',
        confirmButtonText: '查询',
        cancelButtonText: '取消'
      })
      if (oem && oem.trim()) {
        router.push(`/product/${encodeURIComponent(oem.trim())}`)
      }
    } catch {
      // 用户取消
    }
    return
  }
  if (item.path) {
    router.push(item.path)
  }
}

async function toggleAdmin() {
  if (isAdminPath.value) {
    setToken('')
    router.push('/search')
  } else {
    // Day 9.2: 用 ElMessageBox.prompt 取代原生 prompt() (后者在 Playwright/headless 等环境抛 "prompt() is not supported")
    try {
      const { value: token } = await ElMessageBox.prompt('请输入 X-Admin-Token', '进入后台', {
        inputType: 'password',
        inputPlaceholder: '后台 token (与后端 Auth:DevStaticToken 一致)',
        inputValue: localStorage.getItem('sakura_admin_token') || '',
        confirmButtonText: '进入',
        cancelButtonText: '取消'
      })
      if (token && token.trim()) {
        setToken(token.trim())
        ElMessage.success('已进入后台')
        router.push('/admin/products')
      }
    } catch {
      // 用户取消
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
        :key="item.label"
        @click="go(item)"
        :class="[
          'px-2 py-1 text-sm hover:bg-neutral-100',
          item.path && route.path === item.path ? 'text-accent font-medium' : 'text-neutral-700'
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
