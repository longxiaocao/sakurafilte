/**
 * V2 Task V17-3.3: 开放重定向防护单测
 *   覆盖 isSafeRedirect + parseRedirectHosts 所有分支
 *
 * 测试目标:
 *   - 相对路径安全 (/admin, /products) 但拒绝协议相对 (//evil.com)
 *   - 协议白名单 (http/https 通过, javascript/data/file 拒绝)
 *   - 主机白名单 (localhost 通过, phishing.com 拒绝)
 *   - 空值/null/undefined 拒绝
 *   - parseRedirectHosts 解析逻辑 (逗号分隔/去空格/去重/大小写)
 *
 * WHY 必要: 开放重定向是 OWASP Top 10 漏洞,可被钓鱼攻击利用
 */
import { describe, it, expect } from 'vitest'
import { isSafeRedirect, parseRedirectHosts } from '@/utils/security'

describe('security - 开放重定向防护', () => {
  const allowedHosts = parseRedirectHosts('localhost,127.0.0.1')

  // ===== parseRedirectHosts 解析逻辑 =====
  describe('parseRedirectHosts', () => {
    it('逗号分隔解析为 Set', () => {
      const hosts = parseRedirectHosts('localhost,127.0.0.1,example.com')
      expect(hosts.size).toBe(3)
      expect(hosts.has('localhost')).toBe(true)
      expect(hosts.has('127.0.0.1')).toBe(true)
      expect(hosts.has('example.com')).toBe(true)
    })

    it('空值/null/undefined 返回空 Set', () => {
      expect(parseRedirectHosts(undefined).size).toBe(0)
      expect(parseRedirectHosts(null).size).toBe(0)
      expect(parseRedirectHosts('').size).toBe(0)
    })

    it('去空格 + 小写化 + 去重', () => {
      const hosts = parseRedirectHosts(' Localhost , 127.0.0.1 , LOCALHOST ')
      expect(hosts.size).toBe(2)
      expect(hosts.has('localhost')).toBe(true)
      expect(hosts.has('127.0.0.1')).toBe(true)
    })
  })

  // ===== isSafeRedirect - 相对路径 =====
  describe('isSafeRedirect - 相对路径', () => {
    it('同源相对路径 /admin/products 安全', () => {
      expect(isSafeRedirect('/admin/products', allowedHosts)).toBe(true)
    })

    it('根路径 / 安全', () => {
      expect(isSafeRedirect('/', allowedHosts)).toBe(true)
    })

    it('协议相对 URL //evil.com 不安全', () => {
      // WHY // 会被浏览器视为协议相对 URL,跳转到 evil.com
      expect(isSafeRedirect('//evil.com', allowedHosts)).toBe(false)
      expect(isSafeRedirect('//evil.com/admin', allowedHosts)).toBe(false)
    })
  })

  // ===== isSafeRedirect - 协议白名单 =====
  describe('isSafeRedirect - 协议白名单', () => {
    it('http/https + 白名单主机 安全', () => {
      expect(isSafeRedirect('http://localhost/admin', allowedHosts)).toBe(true)
      expect(isSafeRedirect('https://127.0.0.1/admin', allowedHosts)).toBe(true)
    })

    it('javascript: 协议 不安全', () => {
      expect(isSafeRedirect('javascript:alert(1)', allowedHosts)).toBe(false)
      expect(isSafeRedirect('javascript:evil()', allowedHosts)).toBe(false)
    })

    it('data: / vbscript: / file: 协议 不安全', () => {
      expect(isSafeRedirect('data:text/html,<script>alert(1)</script>', allowedHosts)).toBe(false)
      expect(isSafeRedirect('vbscript:msgbox(1)', allowedHosts)).toBe(false)
      expect(isSafeRedirect('file:///etc/passwd', allowedHosts)).toBe(false)
    })
  })

  // ===== isSafeRedirect - 主机白名单 =====
  describe('isSafeRedirect - 主机白名单', () => {
    it('非白名单主机的 https URL 不安全', () => {
      expect(isSafeRedirect('https://phishing.com/admin', allowedHosts)).toBe(false)
      expect(isSafeRedirect('https://evil.com/login', allowedHosts)).toBe(false)
    })

    it('主机大小写不敏感 (自动小写化)', () => {
      expect(isSafeRedirect('https://LOCALHOST/admin', allowedHosts)).toBe(true)
      expect(isSafeRedirect('https://Localhost/admin', allowedHosts)).toBe(true)
    })
  })

  // ===== isSafeRedirect - 边界情况 =====
  describe('isSafeRedirect - 边界', () => {
    it('空值/null/undefined 不安全', () => {
      expect(isSafeRedirect('', allowedHosts)).toBe(false)
      expect(isSafeRedirect(null, allowedHosts)).toBe(false)
      expect(isSafeRedirect(undefined, allowedHosts)).toBe(false)
    })

    it('纯空格 不安全', () => {
      expect(isSafeRedirect('   ', allowedHosts)).toBe(false)
    })

    it('非 URL 格式字符串 不安全', () => {
      // 纯文本不含协议,但也不以 / 开头,应拒绝
      expect(isSafeRedirect('admin', allowedHosts)).toBe(false)
      expect(isSafeRedirect('random text', allowedHosts)).toBe(false)
    })
  })
})
