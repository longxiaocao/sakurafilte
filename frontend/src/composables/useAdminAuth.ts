// 鉴权 composable (JWT 改造版)
//   - accessToken / refreshToken / user / expiresAt 持久化到 localStorage
//   - Pinia store 同步 (跨组件)
//   - useAdminAuth 提供 setAuth/clearAuth/isAuthenticated/isAdmin
//   - 兼容旧 token 字段 (供 axios 拦截器读 X-Admin-Token 兜底)
import { ref, watch } from 'vue'
import { defineStore, storeToRefs } from 'pinia'
import type { AuthUser, LoginResponse } from '@/api/types'

const STORAGE_KEY = 'sakura_admin_auth'
// 兼容旧 localStorage key (迁移期读取一次, 之后写入新 key)
const LEGACY_STORAGE_KEY = 'sakura_admin_token'

export interface AuthPersistShape {
  token: string
  refreshToken: string
  user: AuthUser | null
  expiresAt: number  // 毫秒时间戳
}

// 一次性迁移: 旧 key 仅含纯 token 字符串, 转为空 user 占位, 触发后端 401 走 refresh 流程
function loadPersisted(): AuthPersistShape {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw) {
      const parsed = JSON.parse(raw) as AuthPersistShape
      if (parsed && typeof parsed.token === 'string') {
        return {
          token: parsed.token,
          refreshToken: parsed.refreshToken || '',
          user: parsed.user || null,
          expiresAt: parsed.expiresAt || 0
        }
      }
    }
  } catch {
    // 损坏数据忽略, 走兜底
  }
  // 兼容旧 key: 仅 token 字符串
  const legacy = localStorage.getItem(LEGACY_STORAGE_KEY)
  if (legacy) {
    return { token: legacy, refreshToken: '', user: null, expiresAt: 0 }
  }
  return { token: '', refreshToken: '', user: null, expiresAt: 0 }
}

const initial = loadPersisted()

export const useAdminAuthStore = defineStore('adminAuth', () => {
  // token 等价于 accessToken, 保留字段名以兼容现有 axios 拦截器与业务代码
  const token = ref<string>(initial.token)
  const refreshToken = ref<string>(initial.refreshToken)
  const user = ref<AuthUser | null>(initial.user)
  const expiresAt = ref<number>(initial.expiresAt)

  // 持久化: 任一字段变化都写回新 key
  watch(
    [token, refreshToken, user, expiresAt],
    () => {
      const payload: AuthPersistShape = {
        token: token.value,
        refreshToken: refreshToken.value,
        user: user.value,
        expiresAt: expiresAt.value
      }
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(payload))
      } catch {
        // localStorage 写入失败 (隐私模式/配额) 静默降级, 仅内存态
      }
      // 同步清理旧 key (迁移完成后清掉, 避免下次启动再走 legacy 分支)
      if (token.value) {
        localStorage.removeItem(LEGACY_STORAGE_KEY)
      }
    },
    { deep: true }
  )

  // 一次性写入所有 JWT 字段 + 计算 expiresAt
  function setAuth(payload: LoginResponse) {
    token.value = payload.accessToken
    refreshToken.value = payload.refreshToken
    user.value = payload.user
    // expiresIn 单位秒, 转毫秒时间戳
    expiresAt.value = Date.now() + (payload.expiresIn || 1800) * 1000
  }

  function clearAuth() {
    token.value = ''
    refreshToken.value = ''
    user.value = null
    expiresAt.value = 0
    localStorage.removeItem(STORAGE_KEY)
    localStorage.removeItem(LEGACY_STORAGE_KEY)
  }

  // 兼容旧接口: setToken('') 等价于 clearAuth; setToken(x) 仅写 token (供旧调用方)
  function setToken(v: string) {
    if (!v) {
      clearAuth()
    } else {
      token.value = v
    }
  }

  function clearToken() {
    clearAuth()
  }

  // 检查 token 是否存在且未过期 (留 30s 缓冲, 避免 boundary 竞态)
  function isAuthenticated(): boolean {
    return !!token.value && Date.now() < expiresAt.value - 30_000
  }

  function isAdmin(): boolean {
    return user.value?.role === 'admin'
  }

  return {
    token,
    refreshToken,
    user,
    expiresAt,
    setAuth,
    clearAuth,
    setToken,
    clearToken,
    isAuthenticated,
    isAdmin
  }
})

export function useAdminAuth() {
  const store = useAdminAuthStore()
  // storeToRefs: 把 state (token/refreshToken/user/expiresAt) 包成 ref, 保持模板响应性
  // actions (setAuth/clearAuth/isAdmin 等) 直接取, 无需 ref 包装
  const { token, refreshToken, user, expiresAt } = storeToRefs(store)
  return {
    token,
    refreshToken,
    user,
    expiresAt,
    isAdmin: store.isAdmin,
    isAuthenticated: store.isAuthenticated,
    setAuth: store.setAuth,
    clearAuth: store.clearAuth,
    setToken: store.setToken,
    clearToken: store.clearToken
  }
}
