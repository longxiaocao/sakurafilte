<script setup lang="ts">
// 需求 4: 后台登录界面 (替换 TOKEN 直接输入弹窗)
//   - 用户名 + 密码本地映射验证 (离线工具, 无后端用户系统)
//   - 验证成功后写入 useAdminAuthStore.token (与 axios 拦截器/路由守卫保持兼容)
//   - 支持 redirect 查询参数, 登录后回跳到原目标页
//   - 主题切换兼容: 全部使用 CSS 变量, 跟随 <html class="dark">
import { reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { User, Lock } from '@element-plus/icons-vue'
import { useAdminAuthStore } from '@/composables/useAdminAuth'

const router = useRouter()
const route = useRoute()
const auth = useAdminAuthStore()

const form = reactive({
  username: '',
  password: ''
})
const loading = ref(false)
const errorMsg = ref('')

// 本地用户名/密码映射 (离线工具, 无后端用户系统)
// WHY: 项目要求完全离线、无密钥, 用本地映射替代后端鉴权
//   token 与后端 Auth:DevStaticToken 保持一致, 由 useAdminAuthStore 持久化到 localStorage
//   生产环境如需多用户/多 token 轮转, 可扩展为后端 API 验证
const LOCAL_USERS = [
  { username: 'admin', password: 'admin123', token: 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C' },
  { username: 'operator', password: 'op123456', token: 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C' }
]

async function handleLogin() {
  // 输入校验: 前端兜底, 避免空请求
  if (!form.username || !form.password) {
    errorMsg.value = '请输入用户名和密码'
    return
  }
  loading.value = true
  errorMsg.value = ''
  try {
    // 模拟网络延迟, 提供 loading 反馈 (本地验证本身无延迟)
    await new Promise((r) => setTimeout(r, 300))
    const user = LOCAL_USERS.find(
      (u) => u.username === form.username && u.password === form.password
    )
    if (user) {
      auth.setToken(user.token)
      ElMessage.success('登录成功')
      const redirect = (route.query.redirect as string) || '/admin/products'
      router.push(redirect)
    } else {
      errorMsg.value = '用户名或密码错误'
    }
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login-page min-h-screen flex items-center justify-center p-4 bg-[var(--color-bg)]">
    <div class="login-card w-full max-w-sm hairline p-8 bg-[var(--color-bg-elevated)]">
      <div class="text-center mb-6">
        <h1 class="text-2xl font-medium tracking-tight">SakuraFilter</h1>
        <p class="text-sm text-muted mt-1">后台管理系统</p>
      </div>

      <form class="space-y-4" @submit.prevent="handleLogin">
        <div>
          <label class="block text-sm mb-1" for="login-username">用户名</label>
          <el-input
            id="login-username"
            v-model="form.username"
            placeholder="请输入用户名"
            size="large"
            :prefix-icon="User"
            autocomplete="username"
          />
        </div>
        <div>
          <label class="block text-sm mb-1" for="login-password">密码</label>
          <el-input
            id="login-password"
            v-model="form.password"
            type="password"
            placeholder="请输入密码"
            size="large"
            show-password
            :prefix-icon="Lock"
            autocomplete="current-password"
            @keyup.enter="handleLogin"
          />
        </div>
        <el-alert v-if="errorMsg" :title="errorMsg" type="error" :closable="false" />
        <el-button
          type="primary"
          size="large"
          class="w-full"
          :loading="loading"
          @click="handleLogin"
        >
          登录
        </el-button>
      </form>

      <div class="mt-6 text-center text-xs text-muted">
        <div>默认账号: admin / admin123</div>
        <div class="mt-1">operator / op123456</div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* 卡片整体使用 hairline 边框, Musk 风格无阴影 */
.login-card {
  border-radius: 0;
}

/* el-button 在全局样式中已去圆角, 这里仅保证宽度铺满 */
.login-card :deep(.el-button) {
  width: 100%;
}
</style>
