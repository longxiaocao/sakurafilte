<script setup lang="ts">
import { computed, ref, nextTick, watch, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox, ElLoading } from 'element-plus'
import { useAdminAuth } from '@/composables/useAdminAuth'
import { useThemeStore } from '@/stores/theme'  // P5.3
import { authApi } from '@/api'
import { useI18n } from 'vue-i18n'  // P2.6
import { setLocale } from '@/i18n'  // P2.6

// UX P0-1: 移动端汉堡菜单 drawer 状态
const mobileNavOpen = ref(false)
function closeMobileNav() { mobileNavOpen.value = false }

const route = useRoute()
const router = useRouter()
const { isAdmin, user, token, refreshToken, clearAuth } = useAdminAuth()
const theme = useThemeStore()  // P5.3
const { locale, t } = useI18n()  // P2.6

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
//
// P-Admin-UX v4: 顶栏动态收纳 — 解决 v3 admin 路径跳公开页 admin 入口全部消失问题
//   v3 错: admin 6 按钮仅 isAdminPath 时渲染, 跳公开页后全部消失
//   v4 对: 公共 3 按钮始终在; admin 6 按钮 + 低优 5 按钮在"已登录用户"任意路径都在
//     - 已登录用户在公开页 (产品搜索 / 高级搜索 / OEM 查询 / 产品对比) 仍能看到 admin 入口, 体验一致
//     - 未登录用户只看到公共 3 按钮 (最精简, 不暴露 admin 入口)
//   优先级 (数字越小越靠前): 公共 3 (1-3) → admin 高优 5 (4-8) → 低优 5 (9-13)
//   动态收纳: ResizeObserver 监听 nav 容器宽度, 贪心决定哪些进 "更多"
const allNavItems = computed(() => {
  const items: Array<{ key: string; label: string; icon: string; path?: string; action?: string; dropdown?: string; priority: number }> = [
    // 公共区 (始终在池子里, 不论路径不论登录)
    { key: 'search', label: '产品搜索', path: '/search', icon: 'Search', priority: 1 },
    { key: 'oem', label: 'OEM 查询', action: 'oemLookup', icon: 'Document', priority: 2 },
    { key: 'compare', label: '产品对比', path: '/compare', icon: 'DataLine', priority: 3 }
  ]
  // 已登录用户: 在任何路径都看到 admin 入口, 解决 v3 跳公开页丢 admin 体验问题
  if (user.value) {
    // admin 高优 (必显示, 不可收纳)
    items.push(
      { key: 'products', label: '产品管理', path: '/admin/products', icon: 'Goods', priority: 4 },
      { key: 'adv-search', label: '高级搜索', path: '/public/search', icon: 'Filter', priority: 5 },
      { key: 'dict', label: '字典管理', dropdown: 'dict', icon: 'Collection', priority: 6 },
      // V2 Task 2.2.6: OEM 排序管理入口 (priority 6.5, 在字典和 ETL 之间)
      { key: 'xref-reorder', label: 'OEM 排序', path: '/admin/xrefs/reorder', icon: 'Sort', priority: 6.5 },
      { key: 'etl', label: 'ETL 触发', path: '/admin/etl', icon: 'Loading', priority: 7 }
    )
    if (isAdmin()) {
      items.push({ key: 'users', label: '用户管理', path: '/admin/users', icon: 'User', priority: 8 })
    }
    // admin 低优 (可收纳, 宽度不够时进 "更多" 下拉)
    items.push(
      { key: 'adv-compare', label: '高级对比', path: '/admin/compare', icon: 'DataBoard', priority: 9 },
      { key: 'perf', label: '性能', path: '/admin/perf', icon: 'TrendCharts', priority: 10 },
      { key: 'errors', label: '错误', path: '/admin/errors', icon: 'Warning', priority: 11 },
      { key: 'api', label: 'API', path: '/admin/api-docs', icon: 'Document', priority: 12 },
      { key: 'help', label: '帮助', path: '/admin/help', icon: 'QuestionFilled', priority: 13 }
    )
  }
  return items
})

// 已显示的 nav 容器引用 + 实际测量
const navContainerRef = ref<HTMLElement | null>(null)
// "更多" 里收纳的 keys (低优先级 + 已被挤出主顶栏的)
const overflowKeys = ref<Set<string>>(new Set())

// 测量每个按钮宽度, 贪心决定是否进 "更多"
// 注意: DOM 必须已经渲染才能读 offsetWidth, 流程:
//   1) 全部按钮先渲染 (v-show 不参与计算), 拿到 offsetWidth
//   2) 计算哪些超出, 设 overflowKeys
//   3) 模板内 v-if 隐藏溢出项
const BUTTON_WIDTHS: Record<string, number> = {}
let resizeObserver: ResizeObserver | null = null

function recalcOverflow() {
  if (!navContainerRef.value) return
  const containerW = navContainerRef.value.clientWidth
  // P-Admin-UX v3.1: nav 是 flex-1 占据 logo + 工具按钮之间的所有空间, RESERVED 只需预留给 "更多" 按钮自身 + 一点 gap
  const RESERVED = 80
  const GAP = 4
  const available = containerW - RESERVED
  const newOverflow = new Set<string>()
  if (available <= 0) {
    for (const i of allNavItems.value) newOverflow.add(i.key)
  } else {
    let used = 0
    for (const item of allNavItems.value) {
      const w = BUTTON_WIDTHS[item.key] ?? 90
      if (used + w + GAP > available) {
        newOverflow.add(item.key)
      } else {
        used += w + GAP
      }
    }
    // 至少保留 1 个低优先级项可见 (避免 "更多" 是唯一按钮)
    if (newOverflow.size === allNavItems.value.length && allNavItems.value.length > 0) {
      newOverflow.delete(allNavItems.value[allNavItems.value.length - 1].key)
    }
  }
  // P-Admin-UX v3.1: 仅在新集合与当前不同时才更新, 避免 reactive 触发循环
  if (
    newOverflow.size !== overflowKeys.value.size ||
    [...newOverflow].some((k) => !overflowKeys.value.has(k))
  ) {
    overflowKeys.value = newOverflow
  }
}

function measureButtons() {
  if (!navContainerRef.value) return
  const buttons = navContainerRef.value.querySelectorAll('[data-nav-key]')
  buttons.forEach((el) => {
    const key = (el as HTMLElement).dataset.navKey
    if (key) {
      // P-Admin-UX v3.1: 每次都更新宽度 (去掉 !BUTTON_WIDTHS[key] 守卫),
      //   避免首次测量时按钮尚未完全渲染 (i18n 文本未加载) 导致缓存值偏小
      const w = (el as HTMLElement).offsetWidth
      if (w > 0) BUTTON_WIDTHS[key] = w
    }
  })
  recalcOverflow()
}

// 实际渲染到主顶栏的项 (排除溢出, 但如果有溢出项, 末尾追加 "更多" 按钮)
const visibleNavItems = computed(() => {
  const visible = allNavItems.value.filter((i) => !overflowKeys.value.has(i.key))
  // 如果有溢出项, 在主顶栏末尾插入 "更多" 按钮 (虚拟项)
  if (overflowKeys.value.size > 0 && allNavItems.value.length > 0) {
    visible.push({ key: '__more__', label: '更多', icon: 'More', priority: 99 } as any)
  }
  return visible
})
// 收纳到 "更多" 的项
const overflowItems = computed(() => allNavItems.value.filter((i) => overflowKeys.value.has(i.key)))

// 监听窗口变化 + 路由变化 (路由变化时按钮可能增减, 需重测)
onMounted(() => {
  // 首次渲染后再测 (DOM 已就绪)
  nextTick(() => {
    measureButtons()
  })
  // P-Admin-UX v3.1: ResizeObserver 回调里加防抖 (50ms) + 仅在结果变化时才更新 overflowKeys,
  //   避免 "改 overflowKeys → 模板更新 → ResizeObserver 触发 → 改 overflowKeys" 反馈循环
  let debounceTimer: number | null = null
  resizeObserver = new ResizeObserver(() => {
    if (debounceTimer !== null) window.clearTimeout(debounceTimer)
    debounceTimer = window.setTimeout(() => {
      measureButtons()
    }, 50)
  })
  if (navContainerRef.value) {
    resizeObserver.observe(navContainerRef.value)
  }
})
onBeforeUnmount(() => {
  resizeObserver?.disconnect()
})
// 路由变化 → 重新计算
watch(() => route.path, () => {
  nextTick(() => measureButtons())
})
watch(() => isAdminPath.value, () => {
  nextTick(() => measureButtons())
})
// P-Admin-UX v4: 用户登录/登出时按钮池数量从 3→11 或 11→3, 需重算
watch(() => user.value, () => {
  nextTick(() => measureButtons())
})

function goDict(path: string) {
  router.push(path)
}

async function go(item: { path?: string; action?: string }) {
  if (item.action === 'oemLookup') {
    // Day 9.2: 用 ElMessageBox.prompt 取代原生 prompt() (后者在嵌入式浏览器/受限环境不支持)
    // 修复: 友好提示 + 占位符示例, 避免用户输入不完整 OEM 后跳详情页 404
    try {
      const { value: oem } = await ElMessageBox.prompt('请输入完整 OEM 编号', 'OEM 查询', {
        inputPattern: /^.+$/,
        inputErrorMessage: 'OEM 不能为空',
        inputPlaceholder: '请输入完整 OEM 编号 (如 P00050000 或 11427622448)',
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

// JWT 改造: 已登录显示用户菜单, 未登录跳 /login
function toggleAdmin() {
  if (isAdminPath.value) {
    // 退出后台: 跳前台搜索页 (不清除 token, 用户仍处于登录态)
    router.push('/search')
  } else {
    // 进入后台: 跳转登录页 (路由守卫会处理已登录用户的回跳)
    router.push('/login')
  }
}

// 用户下拉菜单 command 路由
function onUserCommand(cmd: string) {
  if (cmd === 'changePassword') {
    router.push('/change-password')
  } else if (cmd === 'logout') {
    handleLogout()
  }
}

async function handleLogout() {
  try {
    await ElMessageBox.confirm('确定退出登录吗?', '确认', { type: 'warning' })
  } catch {
    return
  }
  // WHY ElLoading: 全屏遮罩, 防止用户点击其他导航项导致路由跳转到需要登录的页面
  const loading = ElLoading.service({ lock: true, text: '退出中...' })
  try {
    if (refreshToken.value) {
      await authApi.logout(refreshToken.value)
    }
  } catch {
    // 即使后端 logout 失败也前端清场
  } finally {
    loading.close()
  }
  clearAuth()
  ElMessage.success(t('common.feedback.success_019'))
  router.push('/login')
}

// el-dropdown 触发方式: hover / click
const dictTrigger = 'click'
const userTrigger = 'click'

// P2.6: 语言切换 (中英双语, ElConfigProvider 响应式跟随, 无需刷新)
function toggleLocale() {
  const next = locale.value === 'zh-CN' ? 'en-US' : 'zh-CN'
  setLocale(next)
  ElMessage.success(next === 'zh-CN' ? '已切换到中文' : 'Switched to English')
}

// 改进 1.1: 全局搜索框 — 回车跳转聚合搜索页 (V2 /search/aggregate?q=)
//   WHY 顶栏搜索: 用户在任意页面 (admin/产品详情/对比) 都能一键搜索, 无需先跳到 /search
//   设计: 输入 → 回车 → router.push({ path: '/search/aggregate', query: { q } })
//   边界: 空输入不跳转, 避免空查询触发后端聚合
const globalSearchQ = ref('')

function doGlobalSearch() {
  const q = globalSearchQ.value.trim()
  if (!q) {
    ElMessage.warning('请输入搜索关键词')
    return
  }
  router.push({ path: '/search/aggregate', query: { q } })
  // 移动端: 触发搜索后关闭 drawer
  closeMobileNav()
}
</script>

<template>
  <header
    class="app-header hairline-b bg-[var(--color-bg)] flex items-center px-3 h-12 gap-3"
    role="banner"
  >
    <!-- UX P0-1: 移动端汉堡按钮 (sm 以下显示, 桌面端隐藏) -->
    <button
      class="sm:hidden -ml-1 p-2 hover:bg-[var(--color-bg-hover)] flex items-center"
      @click="mobileNavOpen = true"
      aria-label="打开导航菜单"
      :aria-expanded="mobileNavOpen"
    >
      <el-icon aria-hidden="true"><Menu /></el-icon>
    </button>
    <div class="font-medium text-base tracking-tight">SakuraFilter</div>
    <!-- 改进 1.1: 全局搜索框 (桌面端 md 以上显示, 移动端由 drawer 接管) -->
    <!--   Musk 风格: 1px 细线 + 240px 窄宽度 + Search 前缀图标 -->
    <el-input
      v-model="globalSearchQ"
      placeholder="搜索 MR.1 / OEM / 机型"
      size="small"
      class="hidden md:block w-60 ml-3"
      @keyup.enter="doGlobalSearch"
      clearable
      aria-label="全局搜索框, 回车跳转聚合搜索页"
    >
      <template #prefix>
        <el-icon aria-hidden="true"><Search /></el-icon>
      </template>
    </el-input>
    <!-- UX P0-1: 桌面端 nav (sm 以上显示, 移动端隐藏) -->
    <!-- P-Admin-UX v3.1: flex-1 + min-w-0 让 nav 占据 logo 和工具按钮之间的所有可用空间, -->
    <!--   这样 clientWidth 才是真实的"可用宽度"而非"内容宽度", 避免 v3 死循环 (nav 收窄 → 更多塞入 → nav 收窄) -->
    <nav
      ref="navContainerRef"
      class="hidden sm:flex items-center gap-1 ml-3 flex-1 min-w-0 overflow-hidden"
      aria-label="主导航"
    >
      <template v-for="item in visibleNavItems" :key="item.key">
        <!-- 字典管理下拉 (P1.3 + P2.2 共 8 个) -->
        <el-dropdown
          v-if="item.dropdown === 'dict'"
          :trigger="dictTrigger"
          @command="(cmd: string) => goDict(cmd)"
        >
          <button
            :data-nav-key="item.key"
            :class="[
              'px-1.5 py-1 text-xs hover:bg-[var(--color-bg-hover)] whitespace-nowrap flex items-center',
              route.path.startsWith('/admin/dict/') ? 'text-accent font-medium' : 'text-neutral-700'
            ]"
            :aria-label="`展开${item.label}下拉菜单`"
            :aria-expanded="false"
          >
            <el-icon class="mr-1" aria-hidden="true"><component :is="item.icon" /></el-icon>
            {{ item.label }}
            <el-icon class="ml-1" aria-hidden="true"><ArrowDown /></el-icon>
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
        <!-- P-Admin-UX v3: "更多"按钮 (条件渲染, 仅 overflowItems 非空时出现) -->
        <el-dropdown
          v-else-if="item.key === '__more__'"
          :trigger="dictTrigger"
        >
          <button
            :data-nav-key="item.key"
            class="px-1.5 py-1 text-xs hover:bg-[var(--color-bg-hover)] whitespace-nowrap flex items-center"
            aria-label="展开更多功能菜单"
            :aria-expanded="false"
          >
            <el-icon class="mr-1" aria-hidden="true"><More /></el-icon>
            更多
            <el-tag
              v-if="overflowItems.length > 0"
              size="small"
              type="info"
              class="ml-1"
            >{{ overflowItems.length }}</el-tag>
            <el-icon class="ml-1" aria-hidden="true"><ArrowDown /></el-icon>
          </button>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item
                v-for="m in overflowItems"
                :key="m.key"
                @click="go(m as any)"
                :disabled="m.path && route.path === m.path"
              >
                <el-icon class="mr-2" aria-hidden="true"><component :is="m.icon" /></el-icon>
                {{ m.label }}
              </el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
        <button
          v-else
          :data-nav-key="item.key"
          @click="go(item as any)"
          :class="[
            'px-1.5 py-1 text-xs hover:bg-[var(--color-bg-hover)] whitespace-nowrap flex items-center',
            item.path && route.path === item.path ? 'text-accent font-medium' : 'text-neutral-700'
          ]"
          :aria-label="item.label"
          :aria-current="item.path && route.path === item.path ? 'page' : undefined"
        >
          <el-icon class="mr-1" aria-hidden="true"><component :is="item.icon" /></el-icon>
          {{ item.label }}
        </button>
      </template>
    </nav>
    <!-- P-Admin-UX v3.1: 原本 <div class="flex-1" /> spacer 已由 nav.flex-1 接管, 删除避免布局冲突 -->
    <!-- P5.3 主题切换按钮 (桌面端显示, 移动端由 drawer 接管) -->
    <button
      @click="theme.toggle()"
      class="hidden sm:flex px-2 py-1 text-sm hairline hover:bg-[var(--color-bg-hover)] items-center gap-1"
      :title="theme.mode === 'dark' ? '切换到浅色' : '切换到深色'"
      aria-label="主题切换"
      :aria-pressed="theme.mode === 'dark'"
    >
      <el-icon aria-hidden="true"><Moon v-if="theme.mode === 'light'" /><Sunny v-else /></el-icon>
      <span class="hidden sm:inline">{{ theme.mode === 'dark' ? '深色' : '浅色' }}</span>
    </button>
    <!-- P2.6: 语言切换按钮 (中英双语, 移动端由 drawer 接管) -->
    <button
      @click="toggleLocale"
      class="hidden sm:flex px-2 py-1 text-sm hairline hover:bg-[var(--color-bg-hover)] items-center gap-1"
      :aria-label="`切换语言, 当前 ${locale === 'zh-CN' ? '中文' : 'English'}`"
      title="切换语言"
    >
      <el-icon aria-hidden="true"><Promotion /></el-icon>
      <span class="hidden sm:inline">{{ locale === 'zh-CN' ? '中' : 'EN' }}</span>
    </button>
    <!-- JWT 改造: 用户菜单 (已登录显示 el-dropdown, 未登录显示进入后台按钮; 移动端由 drawer 接管) -->
    <!-- P-Admin-UX v4: 用户菜单改为 v-if=user (任何路径已登录都显示), 与 v4 admin 入口保留一致 -->
    <el-dropdown
      v-if="user"
      :trigger="userTrigger"
      @command="onUserCommand"
      class="hidden sm:inline-block"
    >
      <button
        class="px-2 py-1 text-sm hairline hover:bg-[var(--color-bg-hover)] flex items-center gap-1"
        :aria-label="`用户菜单: ${user.username}, 角色 ${user.role}`"
        :aria-expanded="false"
      >
        <el-icon aria-hidden="true"><User /></el-icon>
        <span>{{ user.username }}</span>
        <el-tag
          size="small"
          :type="user.role === 'admin' ? 'danger' : user.role === 'operator' ? 'primary' : 'info'"
          class="ml-1"
        >
          {{ user.role }}
        </el-tag>
        <el-icon class="ml-1" aria-hidden="true"><ArrowDown /></el-icon>
      </button>
      <template #dropdown>
        <el-dropdown-menu>
          <el-dropdown-item command="changePassword">修改密码</el-dropdown-item>
          <el-dropdown-item command="logout" divided>退出登录</el-dropdown-item>
        </el-dropdown-menu>
      </template>
    </el-dropdown>
    <!-- P-Admin-UX v4: 移除 v3 的"已登录 admin 角标"按钮, 因为 v4 admin 6 入口在任意路径都保留, 此按钮冗余 -->
    <button
      v-else
      @click="toggleAdmin"
      class="hidden sm:flex px-2 py-1 text-sm hairline hover:bg-[var(--color-bg-hover)] items-center gap-1"
      :aria-label="isAdminPath ? '退出后台' : '进入后台登录'"
    >
      <el-icon aria-hidden="true"><Lock v-if="!isAdminPath" /><Unlock v-else /></el-icon>
      {{ isAdminPath ? '退出后台' : '进入后台' }}
    </button>
  </header>

  <!-- UX P0-1: 移动端导航抽屉 (sm 以下显示, 桌面端不渲染) -->
  <!-- WHY: 9 个 nav 按钮 + 3 个工具按钮在 390px 移动端溢出 701px (1.8x 视口宽度), -->
  <!--   用 drawer 收纳全部 nav, 桌面端 <nav> 完全不受影响 -->
  <el-drawer
    v-model="mobileNavOpen"
    direction="rtl"
    size="85%"
    :with-header="false"
    class="sm:hidden"
    aria-label="移动端导航菜单"
  >
    <div
      class="h-full flex flex-col p-4"
      style="background: var(--color-bg-elevated); color: var(--color-text);"
    >
      <div class="font-medium text-base tracking-tight mb-4">SakuraFilter</div>
      <!-- 改进 1.1: 移动端 drawer 内全局搜索框 (与桌面端保持一致体验) -->
      <el-input
        v-model="globalSearchQ"
        placeholder="搜索 MR.1 / OEM / 机型"
        size="default"
        class="mb-4"
        @keyup.enter="doGlobalSearch"
        clearable
        aria-label="全局搜索框, 回车跳转聚合搜索页"
      >
        <template #prefix>
          <el-icon aria-hidden="true"><Search /></el-icon>
        </template>
      </el-input>
      <nav class="flex flex-col gap-1 flex-1" aria-label="移动端主导航">
        <!-- P-Admin-UX v3: 移动端 drawer 展示全部按钮 (无视 overflow), 简化交互 -->
        <template v-for="item in allNavItems" :key="'m-' + item.key">
          <!-- 字典管理: drawer 内展开为分组列表, 避免嵌套 dropdown -->
          <div v-if="item.dropdown === 'dict'" class="flex flex-col">
            <div class="text-xs uppercase px-2 py-1 text-muted">字典管理</div>
            <button
              v-for="d in dictItems"
              :key="d.path"
              @click="goDict(d.path); closeMobileNav()"
              class="px-2 py-2 text-left text-sm flex items-center hover:bg-[var(--color-bg-hover)]"
              :class="route.path === d.path ? 'text-accent font-medium' : ''"
              :aria-label="d.label"
              :aria-current="route.path === d.path ? 'page' : undefined"
            >
              {{ d.label }}
            </button>
          </div>
          <button
            v-else
            @click="go(item); closeMobileNav()"
            class="px-2 py-2 text-left text-sm flex items-center hover:bg-[var(--color-bg-hover)]"
            :class="item.path && route.path === item.path ? 'text-accent font-medium' : ''"
            :aria-label="item.label"
            :aria-current="item.path && route.path === item.path ? 'page' : undefined"
          >
            <el-icon class="mr-2" aria-hidden="true"><component :is="item.icon" /></el-icon>
            {{ item.label }}
          </button>
        </template>
      </nav>
      <!-- drawer 底部: 主题 + 语言 + 用户/进入后台 -->
      <div class="hairline-t pt-3 flex flex-col gap-1">
        <button
          @click="theme.toggle(); closeMobileNav()"
          class="px-2 py-2 text-left text-sm flex items-center hover:bg-[var(--color-bg-hover)]"
          :aria-label="theme.mode === 'dark' ? '切换到浅色' : '切换到深色'"
        >
          <el-icon class="mr-2" aria-hidden="true"><Moon v-if="theme.mode === 'light'" /><Sunny v-else /></el-icon>
          {{ theme.mode === 'dark' ? '浅色' : '深色' }}
        </button>
        <button
          @click="toggleLocale(); closeMobileNav()"
          class="px-2 py-2 text-left text-sm flex items-center hover:bg-[var(--color-bg-hover)]"
          :aria-label="`切换语言, 当前 ${locale === 'zh-CN' ? '中文' : 'English'}`"
        >
          <el-icon class="mr-2" aria-hidden="true"><Promotion /></el-icon>
          {{ locale === 'zh-CN' ? 'English' : '中文' }}
        </button>
        <div v-if="user" class="px-2 py-2 text-sm flex items-center gap-2">
          <el-icon aria-hidden="true"><User /></el-icon>
          {{ user.username }}
          <el-tag size="small" :type="user.role === 'admin' ? 'danger' : user.role === 'operator' ? 'primary' : 'info'">
            {{ user.role }}
          </el-tag>
        </div>
        <button
          v-if="user"
          @click="handleLogout(); closeMobileNav()"
          class="px-2 py-2 text-left text-sm flex items-center text-red-500 hover:bg-[var(--color-bg-hover)]"
          aria-label="退出登录"
        >
          <el-icon class="mr-2" aria-hidden="true"><SwitchButton /></el-icon>
          退出登录
        </button>
        <button
          v-else
          @click="toggleAdmin(); closeMobileNav()"
          class="px-2 py-2 text-left text-sm flex items-center hover:bg-[var(--color-bg-hover)]"
          :aria-label="isAdminPath ? '退出后台' : '进入后台登录'"
        >
          <el-icon class="mr-2" aria-hidden="true"><Lock v-if="!isAdminPath" /><Unlock v-else /></el-icon>
          {{ isAdminPath ? '退出后台' : '进入后台' }}
        </button>
      </div>
    </div>
  </el-drawer>
</template>
