<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
// 修改密码页 (JWT 改造版)
//   - 表单: 旧密码 / 新密码 / 确认新密码
//   - 校验: 新密码 ≥ 8 字符, 两次输入一致
//   - 调用 POST /api/auth/change-password (需 Authorization Bearer)
//   - 成功后跳转 /admin/products
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Lock } from '@element-plus/icons-vue'
import { authApi } from '@/api'

const router = useRouter()

const form = reactive({
  oldPassword: '',
  newPassword: '',
  confirmPassword: ''
})
const loading = ref(false)

function validate(): string | null {
  if (!form.oldPassword) return '请输入旧密码'
  if (!form.newPassword) return '请输入新密码'
  if (form.newPassword.length < 8) return '新密码至少 8 个字符'
  if (form.newPassword === form.oldPassword) return '新密码不能与旧密码相同'
  if (form.newPassword !== form.confirmPassword) return '两次输入的新密码不一致'
  return null
}

async function handleSubmit() {
  const err = validate()
  if (err) {
    ElMessage.warning(err)
    return
  }
  loading.value = true
  try {
    await authApi.changePassword(form.oldPassword, form.newPassword)
    ElMessage.success(t('common.feedback.success_010'))
    router.push('/admin/products')
  } catch {
    // axios 拦截器已统一弹错误提示, 这里不重复
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="p-3 max-w-screen-sm mx-auto">
    <div class="flex items-center gap-2 mb-3">
      <h1 class="text-lg font-medium">修改密码</h1>
      <span class="text-xs text-muted">JWT 鉴权 · 修改后下次登录生效</span>
    </div>

    <div class="hairline p-6 bg-[var(--color-bg-elevated)]">
      <form class="space-y-4" @submit.prevent="handleSubmit">
        <div>
          <label class="block text-sm mb-1" for="old-password">旧密码</label>
          <el-input
            id="old-password"
            v-model="form.oldPassword"
            type="password"
            placeholder="请输入当前密码"
            size="large"
            show-password
            :prefix-icon="Lock"
            autocomplete="current-password"
          />
        </div>
        <div>
          <label class="block text-sm mb-1" for="new-password">新密码</label>
          <el-input
            id="new-password"
            v-model="form.newPassword"
            type="password"
            placeholder="至少 8 个字符"
            size="large"
            show-password
            :prefix-icon="Lock"
            autocomplete="new-password"
          />
        </div>
        <div>
          <label class="block text-sm mb-1" for="confirm-password">确认新密码</label>
          <el-input
            id="confirm-password"
            v-model="form.confirmPassword"
            type="password"
            placeholder="再次输入新密码"
            size="large"
            show-password
            :prefix-icon="Lock"
            autocomplete="new-password"
            @keyup.enter="handleSubmit"
          />
        </div>
        <el-button
          type="primary"
          size="large"
          class="w-full"
          :loading="loading"
          @click="handleSubmit"
        >
          确认修改
        </el-button>
      </form>
    </div>
  </div>
</template>

<style scoped>
/* Musk 风格: hairline 边框 + 无阴影 */
.bg-\[var\(--color-bg-elevated\)\] {
  border-radius: 0;
}
:deep(.el-button) {
  width: 100%;
}
</style>
