<script setup lang="ts">
// 后台用户管理页 (JWT 改造版, 仅 admin 角色)
//   Tab 1: 用户列表 — 分页表格 + CRUD + 重置密码 + 软删除
//   Tab 2: 登录审计 — login_audit_logs 表展示
//   风格: Musk 极简 hairline + el-tag 区分角色/状态
import { ref, reactive, onMounted, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { authApi, usersApi } from '@/api'
import { useAdminAuthStore } from '@/composables/useAdminAuth'
import type { UserListItem, UserCreateRequest, UserUpdateRequest, UserRole, LoginAuditLog } from '@/api/types'

const auth = useAdminAuthStore()
const activeTab = ref<'users' | 'audit'>('users')

// ===== 用户列表 =====
const users = ref<UserListItem[]>([])
const usersLoading = ref(false)
const usersTotal = ref(0)
const usersPage = ref(1)
const usersPageSize = ref(20)

// 新增用户对话框
const createOpen = ref(false)
const createForm = reactive<UserCreateRequest>({
  username: '',
  password: '',
  role: 'viewer',
  email: '',
  fullName: ''
})

// 编辑用户对话框
const editOpen = ref(false)
const editForm = reactive<UserUpdateRequest & { id?: number; username?: string }>({
  id: undefined,
  username: '',
  role: 'viewer',
  email: '',
  fullName: '',
  isActive: true
})

// 重置密码对话框
const resetOpen = ref(false)
const resetForm = reactive<{ id?: number; username?: string; newPassword: string }>({
  id: undefined,
  username: '',
  newPassword: ''
})

// 角色选项 (与后端 UserRole enum 一致)
const ROLE_OPTIONS: { value: UserRole; label: string; tagType: 'danger' | 'primary' | 'info' }[] = [
  { value: 'admin', label: '管理员 (admin)', tagType: 'danger' },
  { value: 'operator', label: '操作员 (operator)', tagType: 'primary' },
  { value: 'viewer', label: '只读 (viewer)', tagType: 'info' }
]

function roleTagType(role: UserRole): 'danger' | 'primary' | 'info' {
  return ROLE_OPTIONS.find((r) => r.value === role)?.tagType || 'info'
}

function roleLabel(role: UserRole): string {
  return ROLE_OPTIONS.find((r) => r.value === role)?.label || role
}

async function loadUsers() {
  usersLoading.value = true
  try {
    const resp = await usersApi.list(usersPage.value, usersPageSize.value)
    users.value = resp.items
    usersTotal.value = resp.total
  } catch {
    // axios 拦截器已统一弹错误
  } finally {
    usersLoading.value = false
  }
}

function onUsersPageChange(p: number) {
  usersPage.value = p
  loadUsers()
}

function openCreate() {
  createForm.username = ''
  createForm.password = ''
  createForm.role = 'viewer'
  createForm.email = ''
  createForm.fullName = ''
  createOpen.value = true
}

async function saveCreate() {
  if (!createForm.username.trim()) {
    ElMessage.warning('用户名不能为空')
    return
  }
  if (createForm.password.length < 8) {
    ElMessage.warning('密码至少 8 个字符')
    return
  }
  try {
    await usersApi.create({
      username: createForm.username.trim(),
      password: createForm.password,
      role: createForm.role,
      email: createForm.email || undefined,
      fullName: createForm.fullName || undefined
    })
    ElMessage.success('用户已创建')
    createOpen.value = false
    await loadUsers()
  } catch {
    // axios 拦截器已统一弹错误
  }
}

function openEdit(row: UserListItem) {
  editForm.id = row.id
  editForm.username = row.username
  editForm.role = row.role
  editForm.email = row.email || ''
  editForm.fullName = row.fullName || ''
  editForm.isActive = row.isActive
  editOpen.value = true
}

async function saveEdit() {
  if (editForm.id == null) return
  try {
    const patch: UserUpdateRequest = {
      role: editForm.role,
      email: editForm.email || undefined,
      fullName: editForm.fullName || undefined,
      isActive: editForm.isActive
    }
    await usersApi.update(editForm.id, patch)
    ElMessage.success('用户已更新')
    editOpen.value = false
    await loadUsers()
  } catch {
    // axios 拦截器已统一弹错误
  }
}

async function softDelete(row: UserListItem) {
  try {
    await ElMessageBox.confirm(
      `确定删除用户 "${row.username}" 吗? (软删除, 数据保留)`,
      '确认',
      { type: 'warning' }
    )
  } catch {
    return
  }
  try {
    await usersApi.remove(row.id)
    ElMessage.success('已删除')
    await loadUsers()
  } catch {
    // axios 拦截器已统一弹错误
  }
}

function openReset(row: UserListItem) {
  resetForm.id = row.id
  resetForm.username = row.username
  resetForm.newPassword = ''
  resetOpen.value = true
}

async function saveReset() {
  if (resetForm.id == null) return
  if (resetForm.newPassword.length < 8) {
    ElMessage.warning('新密码至少 8 个字符')
    return
  }
  try {
    await usersApi.resetPassword(resetForm.id, resetForm.newPassword)
    ElMessage.success(`已重置 ${resetForm.username} 的密码`)
    resetOpen.value = false
  } catch {
    // axios 拦截器已统一弹错误
  }
}

// ===== 登录审计 =====
const audit = ref<LoginAuditLog[]>([])
const auditLoading = ref(false)
const auditTotal = ref(0)
const auditPage = ref(1)
const auditPageSize = ref(20)

async function loadAudit() {
  auditLoading.value = true
  try {
    const resp = await usersApi.auditLogin(auditPage.value, auditPageSize.value)
    audit.value = resp.items
    auditTotal.value = resp.total
  } catch {
    // axios 拦截器已统一弹错误
  } finally {
    auditLoading.value = false
  }
}

function onAuditPageChange(p: number) {
  auditPage.value = p
  loadAudit()
}

// Tab 切换首次加载审计数据
function onTabChange(tab: string) {
  if (tab === 'audit' && audit.value.length === 0 && !auditLoading.value) {
    loadAudit()
  }
}

// ===== 顶部用户菜单 (退出登录) =====
async function handleLogout() {
  try {
    if (auth.refreshToken) {
      await authApi.logout(auth.refreshToken)
    }
  } catch {
    // 即使后端 logout 失败也前端清场
  }
  auth.clearAuth()
  ElMessage.success('已退出登录')
  window.location.href = '/login'
}

function fmtDate(iso?: string): string {
  if (!iso) return ''
  return iso.substring(0, 19).replace('T', ' ')
}

// 当前用户是否为 admin (UI 守卫: 仅 admin 可见 CRUD 按钮)
const canManage = computed(() => auth.isAdmin())

onMounted(loadUsers)
</script>

<template>
  <div class="p-3 max-w-screen-xl mx-auto">
    <!-- 顶部工具条 -->
    <div class="flex items-center gap-2 mb-3 flex-wrap">
      <h1 class="text-lg font-medium">用户管理</h1>
      <span class="text-xs text-muted">JWT 鉴权 · 仅 admin 角色可管理</span>
      <div class="flex-1" />
      <el-button v-if="canManage" type="primary" size="small" @click="openCreate">新增用户</el-button>
    </div>

    <el-tabs v-model="activeTab" @tab-change="onTabChange">
      <!-- ===== Tab 1: 用户列表 ===== -->
      <el-tab-pane label="用户列表" name="users">
        <div class="hairline" v-loading="usersLoading">
          <!-- 表头 -->
          <div class="user-head">
            <div class="cell-id">ID</div>
            <div class="cell-username">用户名</div>
            <div class="cell-role">角色</div>
            <div class="cell-email">邮箱</div>
            <div class="cell-status">状态</div>
            <div class="cell-last-login">最后登录</div>
            <div class="cell-created">创建时间</div>
            <div class="cell-action">操作</div>
          </div>
          <!-- 表体 -->
          <div v-for="row in users" :key="row.id" class="user-row">
            <div class="cell-id">{{ row.id }}</div>
            <div class="cell-username" :title="row.username">{{ row.username }}</div>
            <div class="cell-role">
              <el-tag :type="roleTagType(row.role)" size="small">{{ roleLabel(row.role) }}</el-tag>
            </div>
            <div class="cell-email" :title="row.email || ''">{{ row.email || '—' }}</div>
            <div class="cell-status">
              <el-tag v-if="row.isActive" type="success" size="small">启用</el-tag>
              <el-tag v-else type="danger" size="small">禁用</el-tag>
            </div>
            <div class="cell-last-login">{{ fmtDate(row.lastLoginAt) || '—' }}</div>
            <div class="cell-created">{{ fmtDate(row.createdAt) }}</div>
            <div class="cell-action">
              <el-button size="small" text @click="openEdit(row)">编辑</el-button>
              <el-button size="small" text type="warning" @click="openReset(row)">重置密码</el-button>
              <el-button
                size="small"
                text
                type="danger"
                :disabled="row.username === auth.user?.username"
                @click="softDelete(row)"
              >删除</el-button>
            </div>
          </div>
          <!-- 空状态 -->
          <div v-if="!usersLoading && users.length === 0" class="user-empty">
            暂无用户数据
          </div>
        </div>

        <div class="mt-2 flex justify-end">
          <el-pagination
            background
            layout="total, prev, pager, next"
            :total="usersTotal"
            :page-size="usersPageSize"
            :current-page="usersPage"
            @current-change="onUsersPageChange"
          />
        </div>
      </el-tab-pane>

      <!-- ===== Tab 2: 登录审计 ===== -->
      <el-tab-pane label="登录审计" name="audit">
        <div class="hairline" v-loading="auditLoading">
          <div class="audit-head">
            <div class="cell-id">ID</div>
            <div class="cell-username">用户名</div>
            <div class="cell-login-at">登录时间</div>
            <div class="cell-ip">IP</div>
            <div class="cell-ua">User-Agent</div>
            <div class="cell-status">结果</div>
            <div class="cell-reason">失败原因</div>
          </div>
          <div v-for="row in audit" :key="row.id" class="audit-row">
            <div class="cell-id">{{ row.id }}</div>
            <div class="cell-username" :title="row.username">{{ row.username }}</div>
            <div class="cell-login-at">{{ fmtDate(row.loginAt) }}</div>
            <div class="cell-ip" :title="row.ip || ''">{{ row.ip || '—' }}</div>
            <div class="cell-ua" :title="row.userAgent || ''">{{ row.userAgent || '—' }}</div>
            <div class="cell-status">
              <el-tag v-if="row.success" type="success" size="small">成功</el-tag>
              <el-tag v-else type="danger" size="small">失败</el-tag>
            </div>
            <div class="cell-reason" :title="row.failureReason || ''">{{ row.failureReason || '—' }}</div>
          </div>
          <div v-if="!auditLoading && audit.length === 0" class="user-empty">
            暂无审计数据
          </div>
        </div>

        <div class="mt-2 flex justify-end">
          <el-pagination
            background
            layout="total, prev, pager, next"
            :total="auditTotal"
            :page-size="auditPageSize"
            :current-page="auditPage"
            @current-change="onAuditPageChange"
          />
        </div>
      </el-tab-pane>
    </el-tabs>

    <!-- 新增用户对话框 -->
    <el-dialog v-model="createOpen" title="新增用户" width="480px">
      <el-form :model="createForm" label-width="80px" size="small">
        <el-form-item label="用户名" required>
          <el-input v-model="createForm.username" placeholder="登录用户名" maxlength="50" show-word-limit />
        </el-form-item>
        <el-form-item label="密码" required>
          <el-input
            v-model="createForm.password"
            type="password"
            placeholder="至少 8 个字符"
            show-password
            autocomplete="new-password"
          />
        </el-form-item>
        <el-form-item label="角色" required>
          <el-select v-model="createForm.role" style="width: 100%">
            <el-option
              v-for="opt in ROLE_OPTIONS"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="邮箱">
          <el-input v-model="createForm.email" placeholder="可选" />
        </el-form-item>
        <el-form-item label="全名">
          <el-input v-model="createForm.fullName" placeholder="可选" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createOpen = false">取消</el-button>
        <el-button type="primary" @click="saveCreate">创建</el-button>
      </template>
    </el-dialog>

    <!-- 编辑用户对话框 -->
    <el-dialog v-model="editOpen" :title="`编辑用户: ${editForm.username}`" width="480px">
      <el-form :model="editForm" label-width="80px" size="small">
        <el-form-item label="用户名">
          <el-input :model-value="editForm.username" disabled />
        </el-form-item>
        <el-form-item label="角色">
          <el-select v-model="editForm.role" style="width: 100%">
            <el-option
              v-for="opt in ROLE_OPTIONS"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="邮箱">
          <el-input v-model="editForm.email" placeholder="可选" />
        </el-form-item>
        <el-form-item label="全名">
          <el-input v-model="editForm.fullName" placeholder="可选" />
        </el-form-item>
        <el-form-item label="启用状态">
          <el-switch v-model="editForm.isActive" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editOpen = false">取消</el-button>
        <el-button type="primary" @click="saveEdit">保存</el-button>
      </template>
    </el-dialog>

    <!-- 重置密码对话框 -->
    <el-dialog v-model="resetOpen" :title="`重置密码: ${resetForm.username}`" width="480px">
      <el-form :model="resetForm" label-width="80px" size="small">
        <el-form-item label="新密码" required>
          <el-input
            v-model="resetForm.newPassword"
            type="password"
            placeholder="至少 8 个字符"
            show-password
            autocomplete="new-password"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="resetOpen = false">取消</el-button>
        <el-button type="primary" @click="saveReset">确认重置</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
/* Musk 风格: 1px hairline + 高密度 + 无阴影 */
.user-head,
.user-row {
  display: grid;
  grid-template-columns: 60px 1fr 160px 200px 80px 140px 140px 240px;
  align-items: center;
  font-size: 12px;
  border-bottom: 1px solid #e5e7eb;
}
.user-head {
  font-weight: 500;
  color: #6b7280;
  background: #f9fafb;
  height: 32px;
}
.user-row {
  height: 40px;
  background: #fff;
}
.user-row:hover { background: #f9fafb; }
.user-head > div,
.user-row > div {
  padding: 0 8px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.cell-id { text-align: right; }
.user-empty {
  padding: 24px 0;
  text-align: center;
  color: #9ca3af;
  font-size: 12px;
}

/* 登录审计表格 */
.audit-head,
.audit-row {
  display: grid;
  grid-template-columns: 60px 1fr 160px 140px 2fr 80px 1fr;
  align-items: center;
  font-size: 12px;
  border-bottom: 1px solid #e5e7eb;
}
.audit-head {
  font-weight: 500;
  color: #6b7280;
  background: #f9fafb;
  height: 32px;
}
.audit-row {
  height: 36px;
  background: #fff;
}
.audit-row:hover { background: #f9fafb; }
.audit-head > div,
.audit-row > div {
  padding: 0 8px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
