<script setup lang="ts">
import { computed, ref } from 'vue'
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
const { isAdmin, user, refreshToken, clearAuth } = useAdminAuth()
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
const navItems = computed(() => [
  { label: '产品搜索', path: '/search', icon: 'Search' },
  { label: 'OEM 查询', action: 'oemLookup', icon: 'Document' },
  // P0 (Day 14): 公开产品对比 — 游客无需登录可访问, 与后台 admin/compare 区分
  { label: '产品对比', path: '/compare', icon: 'DataAnalysis' },
  ...(isAdminPath.value
    ? [
        { label: '产品管理', path: '/admin/products', icon: 'Goods' },
        // Day 10+: 字典管理 (P1.3 OEM 品牌 + P2.2 7 个新字典) — 改为 el-dropdown 下拉
        { label: '字典管理', dropdown: 'dict', icon: 'Collection' },
        // JWT 改造: 用户管理 (仅 admin 角色显示)
        ...(isAdmin() ? [{ label: '用户管理', path: '/admin/users', icon: 'User' }] : []),
        { label: 'ETL 触发', path: '/admin/etl', icon: 'Loading' },
        // P3.5 (Task 12): 后台产品对比 (最多 6 个产品, 列可调序, 打印优化)
        //   注: admin 路径下保留此入口作为深度使用, 公开页 /compare 已覆盖大多数用例
        { label: '高级对比', path: '/admin/compare', icon: 'DataAnalysis' },
        // P5.5+: 性能监控 (P50/P95/P99 + 健康探针 + Token 轮转状态)
        { label: '性能', path: '/admin/perf', icon: 'TrendCharts' },
        // 批次 6c: 错误日志 (前端错误监控 + 导出 + 清空)
        { label: '错误', path: '/admin/errors', icon: 'Warning' },
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
  ElMessage.success('已退出登录')
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
    class="hairline-b bg-[var(--color-bg)] flex items-center px-3 h-12 gap-3"
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
    <nav class="hidden sm:flex items-center gap-1 ml-3" aria-label="主导航">
      <template v-for="item in navItems" :key="item.label">
        <!-- Day 10+: 字典管理下拉 (P1.3 + P2.2 共 8 个) -->
        <el-dropdown
          v-if="item.dropdown === 'dict'"
          :trigger="dictTrigger"
          @command="(cmd: string) => goDict(cmd)"
        >
          <button
            :class="[
              'px-2 py-1 text-sm hover:bg-[var(--color-bg-hover)]',
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
        <button
          v-else
          @click="go(item)"
          :class="[
            'px-2 py-1 text-sm hover:bg-[var(--color-bg-hover)]',
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
        <template v-for="item in navItems" :key="'m-' + item.label">
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
