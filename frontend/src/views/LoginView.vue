<script setup lang="ts">
// 后台登录界面 (JWT 改造版 + P2.6 i18n 全量接入)
//   - 调用后端 POST /api/auth/login, 写入 useAdminAuthStore (JWT 全字段)
//   - 支持 redirect 查询参数, 登录后回跳到原目标页
//   - 错误码 ERR_AUTH_FAILED → 友好提示 (通过 i18n 映射)
//   - 主题切换兼容: 全部使用 CSS 变量, 跟随 <html class="dark">
import { reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { User, Lock } from '@element-plus/icons-vue'
import { useAdminAuthStore } from '@/composables/useAdminAuth'
import { authApi } from '@/api'
import { useI18n } from 'vue-i18n'

const router = useRouter()
const route = useRoute()
const auth = useAdminAuthStore()
const { t } = useI18n()

const form = reactive({
  username: '',
  password: ''
})
const loading = ref(false)
const errorMsg = ref('')

// 后端 errorCode → i18n key 映射
const AUTH_ERROR_I18N: Record<string, string> = {
  ERR_AUTH_FAILED: 'auth.authFailed',
  ERR_USER_DISABLED: 'auth.userDisabled',
  ERR_USER_LOCKED: 'auth.userLocked'
}

async function handleLogin() {
  // 输入校验: 前端兜底, 避免空请求
  if (!form.username || !form.password) {
    errorMsg.value = t('auth.usernamePlaceholder') + ' / ' + t('auth.passwordPlaceholder')
    return
  }
  loading.value = true
  errorMsg.value = ''
  try {
    const payload = await authApi.login(form.username, form.password)
    auth.setAuth(payload)
    ElMessage.success(t('auth.loginSuccess'))
    const redirect = (route.query.redirect as string) || '/admin/products'
    router.push(redirect)
  } catch (e: any) {
    // 后端 ProblemDetails.errorCode 优先, 走 i18n 映射; title 兜底
    const errorCode = e?.response?.data?.errorCode
    const title = e?.response?.data?.title
    if (errorCode && AUTH_ERROR_I18N[errorCode]) {
      errorMsg.value = t(AUTH_ERROR_I18N[errorCode])
    } else if (title) {
      errorMsg.value = title
    } else {
      errorMsg.value = t('auth.loginFailed')
    }
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login-page min-h-screen flex items-center justify-center p-4 bg-[var(--color-bg)]">
    <div
      class="login-card w-full max-w-sm hairline p-8 bg-[var(--color-bg-elevated)]"
      role="main"
      :aria-label="t('auth.title')"
    >
      <div class="text-center mb-6">
        <h1 class="text-2xl font-medium tracking-tight">{{ t('auth.title') }}</h1>
        <p class="text-sm text-muted mt-1">{{ t('auth.subtitle') }}</p>
      </div>

      <form class="space-y-4" @submit.prevent="handleLogin" :aria-label="t('auth.login')">
        <div>
          <label class="block text-sm mb-1" for="login-username">{{ t('auth.username') }}</label>
          <el-input
            id="login-username"
            v-model="form.username"
            :placeholder="t('auth.usernamePlaceholder')"
            size="large"
            :prefix-icon="User"
            autocomplete="username"
            :aria-label="t('auth.username')"
            aria-required="true"
          />
        </div>
        <div>
          <label class="block text-sm mb-1" for="login-password">{{ t('auth.password') }}</label>
          <el-input
            id="login-password"
            v-model="form.password"
            type="password"
            :placeholder="t('auth.passwordPlaceholder')"
            size="large"
            show-password
            :prefix-icon="Lock"
            autocomplete="current-password"
            :aria-label="t('auth.password')"
            aria-required="true"
            @keyup.enter="handleLogin"
          />
        </div>
        <el-alert
          v-if="errorMsg"
          :title="errorMsg"
          type="error"
          :closable="false"
          role="alert"
          aria-live="assertive"
        />
        <el-button
          type="primary"
          size="large"
          class="w-full"
          :loading="loading"
          :aria-busy="loading"
          :aria-label="t('auth.login')"
          @click="handleLogin"
        >
          {{ t('auth.login') }}
        </el-button>
      </form>

      <div class="mt-6 text-center text-xs text-muted">
        <div>{{ t('auth.defaultAccount') }}</div>
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
