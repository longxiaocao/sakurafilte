<script setup lang="ts">
// 后台登录界面 (JWT 改造版)
//   - 调用后端 POST /api/auth/login, 写入 useAdminAuthStore (JWT 全字段)
//   - 支持 redirect 查询参数, 登录后回跳到原目标页
//   - 错误码 ERR_AUTH_FAILED → "用户名或密码错误"
//   - 主题切换兼容: 全部使用 CSS 变量, 跟随 <html class="dark">
import { reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { User, Lock } from '@element-plus/icons-vue'
import { useAdminAuthStore } from '@/composables/useAdminAuth'
import { authApi } from '@/api'

const router = useRouter()
const route = useRoute()
const auth = useAdminAuthStore()

const form = reactive({
  username: '',
  password: ''
})
const loading = ref(false)
const errorMsg = ref('')

// 后端 errorCode → 友好提示映射
const AUTH_ERROR_MAP: Record<string, string> = {
  ERR_AUTH_FAILED: '用户名或密码错误',
  ERR_USER_DISABLED: '账号已被禁用, 请联系管理员',
  ERR_USER_LOCKED: '账号已锁定, 请稍后重试'
}

async function handleLogin() {
  // 输入校验: 前端兜底, 避免空请求
  if (!form.username || !form.password) {
    errorMsg.value = '请输入用户名和密码'
    return
  }
  loading.value = true
  errorMsg.value = ''
  try {
    const payload = await authApi.login(form.username, form.password)
    auth.setAuth(payload)
    ElMessage.success('登录成功')
    const redirect = (route.query.redirect as string) || '/admin/products'
    router.push(redirect)
  } catch (e: any) {
    // 后端 ProblemDetails.errorCode 优先, title 兜底
    const errorCode = e?.response?.data?.errorCode
    const title = e?.response?.data?.title
    if (errorCode && AUTH_ERROR_MAP[errorCode]) {
      errorMsg.value = AUTH_ERROR_MAP[errorCode]
    } else if (title) {
      errorMsg.value = title
    } else {
      errorMsg.value = '登录失败, 请稍后重试'
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
        <div>默认账号: admin / (部署时配置)</div>
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
