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
// P-Admin-UX v3: 顶栏动态收纳 — 解决 v2 公共+admin 合并后 10 按钮溢出 1280px 问题
//   核心: ResizeObserver 监听 nav 容器宽度, 按优先级贪心决定每个按钮进主顶栏还是 "更多" 下拉
//   优先级 (数字越小越靠前, 必显示): 公共 3 按钮 + 字典/产品/ETL/用户管理/高级搜索
//   低优先 (可收纳): 高级对比 / 性能 / 错误 / API / 帮助
//   算法: 从高到低累加 offsetWidth, 超过可用宽度则后续进 "更多" 下拉
//   触发: window resize + 路由变化 + 用户名切换
//
// 完整按钮池 (按优先级 1-13 排序)
const allNavItems = computed(() => {
  const items: Array<{ key: string; label: string; icon: string; path?: string; action?: string; dropdown?: string; priority: number }> = [
    // 公共区 (始终在池子里, 公共路径优先, admin 路径也保留以便跳转)
    { key: 'search', label: '产品搜索', path: '/search', icon: 'Search', priority: 1 },
    { key: 'oem', label: 'OEM 查询', action: 'oemLookup', icon: 'Document', priority: 2 },
    { key: 'compare', label: '产品对比', path: '/compare', icon: 'DataLine', priority: 3 }
  ]
  if (isAdminPath.value) {
    // 后台区
    items.push(
      { key: 'products', label: '产品管理', path: '/admin/products', icon: 'Goods', priority: 4 },
      { key: 'adv-search', label: '高级搜索', path: '/public/search', icon: 'Filter', priority: 5 },
      { key: 'dict', label: '字典管理', dropdown: 'dict', icon: 'Collection', priority: 6 },
      { key: 'etl', label: 'ETL 触发', path: '/admin/etl', icon: 'Loading', priority: 7 }
    )
    if (isAdmin()) {
      items.push({ key: 'users', label: '用户管理', path: '/admin/users', icon: 'User', priority: 8 })
    }
    // 低优先级 (可收纳)
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
  // 已用宽度: 工具区 (logo + 浅色/中/EN + admin 角标 ≈ 280px, 留 60px 安全边距)
  // 更准确做法: 测 nav 容器的 clientWidth, 测每个按钮 offsetWidth
  // 简单做法: 假设每个按钮 90px, 加上 gap 4px, 5 个工具按钮 ≈ 250px
  const RESERVED = 280
  const GAP = 4
  const available = containerW - RESERVED
  if (available <= 0) {
    overflowKeys.value = new Set(allNavItems.value.map((i) => i.key))
    return
  }
  const overflow = new Set<string>()
  let used = 0
  for (const item of allNavItems.value) {
    // 首次测量后缓存宽度; 后续直接用缓存
    const w = BUTTON_WIDTHS[item.key] ?? 90
    if (used + w + GAP > available) {
      overflow.add(item.key)
    } else {
      used += w + GAP
    }
  }
  // 至少保留 1 个低优先级项可见 (避免 "更多" 是唯一按钮)
  // 如果全部被收纳, 把优先级最低的(13 帮)强行拿出
  if (overflow.size === allNavItems.value.length && allNavItems.value.length > 0) {
    overflow.delete(allNavItems.value[allNavItems.value.length - 1].key)
  }
  overflowKeys.value = overflow
}

function measureButtons() {
  if (!navContainerRef.value) return
  const buttons = navContainerRef.value.querySelectorAll('[data-nav-key]')
  buttons.forEach((el) => {
    const key = (el as HTMLElement).dataset.navKey
    if (key && !BUTTON_WIDTHS[key]) {
      BUTTON_WIDTHS[key] = (el as HTMLElement).offsetWidth
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
  resizeObserver = new ResizeObserver(() => {
    measureButtons()
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
    <!-- UX P0-1: 桌面端 nav (sm 以上显示, 移动端隐藏) -->
    <!-- P-Admin-UX v3: ref 绑定到 nav 容器, ResizeObserver 监听宽度变化做动态收纳 -->
    <nav
      ref="navContainerRef"
      class="hidden sm:flex items-center gap-1 ml-3 overflow-hidden"
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
    <div class="flex-1" />
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
    <el-dropdown
      v-if="isAdminPath && user"
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
    <!-- P-Admin-UX v3: 公共路径下, 已登录用户显示"已登录 admin"角标, 直接跳后台无需经 /login -->
    <button
      v-else-if="!isAdminPath && token"
      @click="router.push('/admin/products')"
      class="hidden sm:flex px-2 py-1 text-sm hairline hover:bg-[var(--color-bg-hover)] items-center gap-1"
      :aria-label="'已登录, 跳到后台'"
      title="已登录, 点击进入后台"
    >
      <el-icon aria-hidden="true"><User /></el-icon>
      <el-tag
        size="small"
        :type="user?.role === 'admin' ? 'danger' : 'primary'"
        class="ml-1"
      >{{ user?.role || '已登录' }}</el-tag>
      <span class="ml-1">进入后台</span>
      <el-icon class="ml-1" aria-hidden="true"><Right /></el-icon>
    </button>
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
