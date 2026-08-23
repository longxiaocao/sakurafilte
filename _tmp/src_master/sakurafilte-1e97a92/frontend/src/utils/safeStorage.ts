// V24-F31 (spec F4-8 + F5-2): Safari 隐私模式 / 配额超限降级存储
//
// 设计要点:
//   1. F4-8 基础: sessionStorage 写入用 try-catch 包裹, 失败降级到内存 Map
//   2. F5-2 修复 F4-8 逻辑漏洞:
//      - Safari 隐私模式 getItem 不抛错, 返回 null
//      - 旧版 safeSessionStorage.getItem 永远不走 memoryStore 分支, 数据丢失
//      - 修复: 启动时探测 sessionStorage 可用性, getItem 返回 null 时回退 memoryStore
//   3. setItem 总是写入 memoryStore, sessionStorage 可用且不抛错时再写 sessionStorage
//      WHY: 即使 sessionStorage 写入失败 (隐私模式/配额超限), memoryStore 仍保留数据
//
// 单元测试 (spec F5-2): SafeStorage_SafariPrivateMode_ReadFromMemory
//   - mock sessionStorage.setItem 抛 QuotaExceededError
//   - safeSetItem('k', 'v') 不抛错
//   - safeGetItem('k') 应返回 'v' (从 memoryStore 读取)

const memoryStore = new Map<string, string>()

// F5-2: 启动时一次性探测 sessionStorage 可用性, 避免每次调用都走 try-catch
//   WHY: Safari 隐私模式 sessionStorage.setItem 抛 QuotaExceededError
//        探测一次后缓存结果, 性能更优
const sessionStorageAvailable = checkSessionStorageAvailable()

function checkSessionStorageAvailable(): boolean {
  try {
    // 模拟 Safari 隐私模式: setItem 抛错即视为不可用
    const test = '__safeStorage_test__'
    sessionStorage.setItem(test, '1')
    sessionStorage.removeItem(test)
    return true
  } catch {
    return false
  }
}

/**
 * F5-2: 安全读取 sessionStorage
 *   - sessionStorage 不可用 (隐私模式) → 读 memoryStore
 *   - sessionStorage 可用但返回 null (隐私模式读取不抛错) → 回退 memoryStore
 *   - sessionStorage 抛错 (极少见, 隐私模式极端情况) → 回退 memoryStore
 */
export function safeGetItem(key: string): string | null {
  if (sessionStorageAvailable) {
    try {
      const value = sessionStorage.getItem(key)
      if (value !== null) return value
    } catch {
      // fallthrough to memoryStore
    }
  }
  return memoryStore.has(key) ? memoryStore.get(key)! : null
}

/**
 * F5-2: 安全写入 sessionStorage
 *   - 总是写入 memoryStore (保证数据不丢)
 *   - sessionStorage 可用且不抛错时, 同步写入 sessionStorage (跨页面刷新可读)
 *   - 抛错时仅写 memoryStore (隐私模式降级)
 */
export function safeSetItem(key: string, value: string): void {
  memoryStore.set(key, value)
  if (sessionStorageAvailable) {
    try {
      sessionStorage.setItem(key, value)
    } catch {
      // Safari 隐私模式 / QuotaExceededError, 仅写 memoryStore
    }
  }
}

/**
 * F5-2: 安全移除 sessionStorage 项
 *   - 总是从 memoryStore 移除
 *   - sessionStorage 可用时尝试移除, 抛错忽略
 */
export function safeRemoveItem(key: string): void {
  memoryStore.delete(key)
  if (sessionStorageAvailable) {
    try {
      sessionStorage.removeItem(key)
    } catch {
      // 忽略
    }
  }
}

/**
 * F5-2: 清空 memoryStore + sessionStorage (测试用, 生产环境一般不调)
 *   注: 不清空整个 sessionStorage (可能含其他模块数据), 仅清 memoryStore
 */
export function clearMemoryStore(): void {
  memoryStore.clear()
}

/**
 * 测试辅助: 返回 sessionStorage 可用性 (单元测试用)
 */
export function isSessionStorageAvailable(): boolean {
  return sessionStorageAvailable
}
