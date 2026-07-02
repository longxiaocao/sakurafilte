<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useAdminAuth } from '@/composables/useAdminAuth'
import { useThemeStore } from '@/stores/theme'  // P5.3

const route = useRoute()
const router = useRouter()
const { isAdmin, setToken } = useAdminAuth()
const theme = useThemeStore()  // P5.3

const isAdminPath = computed(() => route.path.startsWith('/admin'))

// Day 10: 字典管理下拉菜单 (P1.3 OEM 品牌 + P2.2 7 个新字典)
const dictItems = [
  { label: 'OEM 品牌', path: '/admin/dict/oem-brands' },
  { label: '产品名 1', path: '/admin/dict/product-name1s' },
  { label: '产品名 2', path: '/admin/dict/product-name2s' },
  { label: '类型 (Type)', path: '/admin/dict/types' },
  { label: 'OEM 3', path: '/admin/dict/oem-no3s' },
  { label: '介质 (Media)', path: '/admin/dict/medias' },
  { label: '机型 (Machine)', path: '/admin/dict/machines' },
  { label: '发动机 (Engine)', path: '/admin/dict/engines' }
]

// Day 9.2: 修复 - "产品详情" 路由是 /product/:oem, 单独一个 nav 项无法满足参数化路径
//   改方案: nav 中 "产品详情" 改为 "OEM 查询", 点击后弹 ElMessageBox.prompt 收 oem, 再跳详情
//   避免之前直接 router.push('/product') 触发 "No match found" 警告
const navItems = computed(() => [
  { label: '产品搜索', path: '/search', icon: 'Search' },
  { label: 'OEM 查询', action: 'oemLookup', icon: 'Document' },
  ...(isAdminPath.value
    ? [
        { label: '产品管理', path: '/admin/products', icon: 'Goods' },
        // Day 10+: 字典管理 (P1.3 OEM 品牌 + P2.2 7 个新字典) — 改为 el-dropdown 下拉
        { label: '字典管理', dropdown: 'dict', icon: 'Collection' },
        { label: 'ETL 触发', path: '/admin/etl', icon: 'Loading' },
        // P3.5 (Task 12): 产品对比 (最多 6 个产品, 列可调序, 打印优化)
        { label: '产品对比', path: '/admin/compare', icon: 'DataAnalysis' },
        // P5.4 (Task 15): 帮助页 (字典规范 + 搜索 + 导入)
        { label: '帮助', path: '/admin/help', icon: 'QuestionFilled' }
      ]
    : [])
])

function goDict(path: string) {
  router.push(path)
}

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

// el-dropdown 触发方式: hover / click
const dictTrigger = 'click'
</script>

<template>
  <header class="hairline-b bg-white flex items-center px-3 h-12 gap-3">
    <div class="font-medium text-base tracking-tight">SakuraFilter</div>
    <nav class="flex items-center gap-1 ml-3">
      <template v-for="item in navItems" :key="item.label">
        <!-- Day 10+: 字典管理下拉 (P1.3 + P2.2 共 8 个) -->
        <el-dropdown
          v-if="item.dropdown === 'dict'"
          :trigger="dictTrigger"
          @command="(cmd: string) => goDict(cmd)"
        >
          <button
            :class="[
              'px-2 py-1 text-sm hover:bg-neutral-100',
              route.path.startsWith('/admin/dict/') ? 'text-accent font-medium' : 'text-neutral-700'
            ]"
          >
            <el-icon class="mr-1"><component :is="item.icon" /></el-icon>
            {{ item.label }}
            <el-icon class="ml-1"><ArrowDown /></el-icon>
          </button>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item
                v-for="d in dictItems"
                :key="d.path"
                :command="d.path"
                :disabled="route.path === d.path"
              >
                {{ d.label }}
              </el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
        <button
          v-else
          @click="go(item)"
          :class="[
            'px-2 py-1 text-sm hover:bg-neutral-100',
            item.path && route.path === item.path ? 'text-accent font-medium' : 'text-neutral-700'
          ]"
        >
          <el-icon class="mr-1"><component :is="item.icon" /></el-icon>
          {{ item.label }}
        </button>
      </template>
    </nav>
    <div class="flex-1" />
    <!-- P5.3 主题切换按钮 -->
    <button
      @click="theme.toggle()"
      class="px-2 py-1 text-sm hairline hover:bg-neutral-100 flex items-center gap-1"
      :title="theme.mode === 'dark' ? '切换到浅色' : '切换到深色'"
      aria-label="主题切换"
    >
      <el-icon><Moon v-if="theme.mode === 'light'" /><Sunny v-else /></el-icon>
      <span class="hidden sm:inline">{{ theme.mode === 'dark' ? '深色' : '浅色' }}</span>
    </button>
    <button
      @click="toggleAdmin"
      class="px-2 py-1 text-sm hairline hover:bg-neutral-100 flex items-center gap-1"
    >
      <el-icon><Lock v-if="!isAdminPath" /><Unlock v-else /></el-icon>
      {{ isAdminPath ? '退出后台' : '进入后台' }}
    </button>
  </header>
</template>
