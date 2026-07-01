// Day 9: 鉴权 composable
//   - token 存 localStorage (持久化)
//   - Pinia store 同步 (跨组件)
//   - useAdminAuth 提供 isAdmin/setToken
import { ref, watch } from 'vue'
import { defineStore } from 'pinia'

const STORAGE_KEY = 'sakura_admin_token'

export const useAdminAuthStore = defineStore('adminAuth', () => {
  const token = ref<string>(localStorage.getItem(STORAGE_KEY) || '')

  watch(token, (v) => {
    if (v) localStorage.setItem(STORAGE_KEY, v)
    else localStorage.removeItem(STORAGE_KEY)
  })

  function setToken(v: string) {
    token.value = v
  }

  function clearToken() {
    token.value = ''
  }

  return { token, setToken, clearToken }
})

export function useAdminAuth() {
  const store = useAdminAuthStore()
  return {
    token: store.token,
    isAdmin: () => !!store.token,
    setToken: store.setToken,
    clearToken: store.clearToken
  }
}
