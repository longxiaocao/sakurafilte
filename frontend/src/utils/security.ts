// V2 Task V17-3.3: 开放重定向防护模块
// WHY 必要: LoginView.vue 之前用 `route.query.redirect as string` 强转,
//   攻击者可构造 redirect=javascript:evil() 或 redirect=https://phishing.com 窃取登录凭证
// 设计原则 (规则 4.1 安全与健壮性):
//   - 仅允许同源相对路径 (/admin/xxx) 或白名单主机的绝对路径
//   - 拒绝 javascript: / data: / vbscript: / file: 等危险协议
//   - 拒绝非白名单主机的绝对路径 (防钓鱼)
//   - 白名单通过 VITE_SAFE_REDIRECT_HOSTS 环境变量配置 (逗号分隔,如 "localhost,127.0.0.1")
//
// 使用场景:
//   import { isSafeRedirect, parseRedirectHosts } from '@/utils/security'
//   const hosts = parseRedirectHosts(import.meta.env.VITE_SAFE_REDIRECT_HOSTS)
//   if (isSafeRedirect(redirect, hosts)) router.push(redirect)
//   else router.push('/admin/products')

/**
 * 解析 VITE_SAFE_REDIRECT_HOSTS 环境变量为允许的主机集合
 * @param raw 环境变量原值,如 "localhost,127.0.0.1" 或 undefined
 * @returns 主机集合 (小写,去空格,去重),空数组表示仅允许相对路径
 */
export function parseRedirectHosts(raw: string | undefined | null): Set<string> {
  if (!raw) return new Set()
  return new Set(
    raw
      .split(',')
      .map(h => h.trim().toLowerCase())
      .filter(h => h.length > 0)
  )
}

/**
 * V2 Task V17-3.3: 校验 redirect URL 是否安全
 *
 * 安全规则:
 *   1. 空值/null/undefined → 不安全 (调用方应用默认值)
 *   2. 相对路径 (以 / 开头且不以 // 开头) → 安全 (同源)
 *   3. 协议白名单 (http/https) + 主机在白名单 → 安全
 *   4. 其他情况 (javascript:/data:/file: 或非白名单主机) → 不安全
 *
 * @param redirect 待校验的 redirect URL
 * @param allowedHosts 允许的主机集合 (parseRedirectHosts 返回值)
 * @returns true=安全可跳转, false=不安全应拒绝
 */
export function isSafeRedirect(redirect: string | null | undefined, allowedHosts: Set<string>): boolean {
  if (!redirect) return false

  const trimmed = redirect.trim()
  if (trimmed.length === 0) return false

  // 规则 2: 相对路径必须以 / 开头,但不能以 // 开头 (// 会被浏览器视为协议相对 URL)
  if (trimmed.startsWith('/')) {
    return !trimmed.startsWith('//')
  }

  // 规则 3: 绝对路径必须解析协议 + 主机
  let url: URL
  try {
    url = new URL(trimmed)
  } catch {
    // 非 URL 格式 (如 "javascript:evil()" 在某些浏览器可被 URL 解析,这里兜底拒绝)
    return false
  }

  // 协议白名单 (仅 http/https)
  const protocol = url.protocol.toLowerCase()
  if (protocol !== 'http:' && protocol !== 'https:') {
    return false
  }

  // 主机白名单校验
  const host = url.hostname.toLowerCase()
  return allowedHosts.has(host)
}
