/**
 * V2 Task 1.3.4 / V24-F35 (spec Task 4.9.1): HTML 安全过滤器单测
 *   覆盖 sanitizeFormatted 纯函数的所有分支
 *
 * 测试目标:
 *   - 基础 HTML 转义 (< > & " ' 全部变实体)
 *   - <mark></mark> 白名单还原 (后端高亮标记)
 *   - XSS 防御 (script/iframe/style/a/img 全部转义为文本)
 *   - 空值兜底 (null/undefined/空串)
 *   - 控制字符与特殊字符
 *
 * WHY 纯函数测试: sanitizeFormatted 是聚合搜索 _formatted 字段 v-html 渲染前的最后防线
 *   - 后端 SanitizeFormatted 已做白名单, 前端双保险独立防御
 *   - 任何遗漏会导致 XSS (用户输入 <script> 直接执行)
 */
import { describe, it, expect } from 'vitest'
import { sanitizeFormatted } from '@/utils/html-sanitizer'

describe('sanitizeFormatted', () => {
  // ===== 空值兜底 =====
  it('null 输入返回空串', () => {
    expect(sanitizeFormatted(null)).toBe('')
  })

  it('undefined 输入返回空串', () => {
    expect(sanitizeFormatted(undefined)).toBe('')
  })

  it('空串输入返回空串', () => {
    expect(sanitizeFormatted('')).toBe('')
  })

  // ===== 基础 HTML 转义 =====
  it('转义 < > 为 &lt; &gt;', () => {
    expect(sanitizeFormatted('a < b > c')).toBe('a &lt; b &gt; c')
  })

  it('转义 & 为 &amp; (避免二次转义)', () => {
    // 输入 "Tom & Jerry" → 输出 "Tom &amp; Jerry"
    expect(sanitizeFormatted('Tom & Jerry')).toBe('Tom &amp; Jerry')
  })

  it('转义双引号和单引号', () => {
    expect(sanitizeFormatted('a "b" \'c\'')).toBe('a &quot;b&quot; &#39;c&#39;')
  })

  // ===== <mark> 白名单还原 =====
  it('还原 <mark></mark> 为真实标签 (后端高亮标记)', () => {
    // 后端 SanitizeFormatted 输出 raw HTML: 'Oil <mark>Filter</mark>'
    // 前端 sanitizeFormatted 接收 raw HTML, 先全量转义为 'Oil &lt;mark&gt;Filter&lt;/mark&gt;'
    // 再 replace 还原 <mark> 标签为 'Oil <mark>Filter</mark>'
    const input = 'Oil <mark>Filter</mark>'
    expect(sanitizeFormatted(input)).toBe('Oil <mark>Filter</mark>')
  })

  it('还原多个 <mark> 标签', () => {
    const input = '<mark>A</mark> and <mark>B</mark>'
    expect(sanitizeFormatted(input)).toBe('<mark>A</mark> and <mark>B</mark>')
  })

  // ===== XSS 防御 =====
  it('转义 <script> 标签为文本 (不执行)', () => {
    const malicious = '<script>alert("xss")</script>'
    const result = sanitizeFormatted(malicious)
    expect(result).toBe('&lt;script&gt;alert(&quot;xss&quot;)&lt;/script&gt;')
    // 验证 <script> 不在结果中作为标签
    expect(result).not.toContain('<script>')
  })

  it('转义 <iframe> 标签为文本', () => {
    const malicious = '<iframe src="evil.com"></iframe>'
    const result = sanitizeFormatted(malicious)
    expect(result).not.toContain('<iframe')
    expect(result).toContain('&lt;iframe')
  })

  it('转义 <a href="javascript:..."> 为文本 (阻止 javascript: 协议)', () => {
    const malicious = '<a href="javascript:alert(1)">click</a>'
    const result = sanitizeFormatted(malicious)
    expect(result).not.toContain('<a ')
    expect(result).toContain('&lt;a ')
  })

  it('转义 <img onerror=...> 为文本 (阻止 onerror 事件)', () => {
    const malicious = '<img src=x onerror=alert(1)>'
    const result = sanitizeFormatted(malicious)
    expect(result).not.toContain('<img')
    expect(result).toContain('&lt;img')
  })

  it('转义 <style> 标签 (阻止 CSS 注入)', () => {
    const malicious = '<style>body{background:url(javascript:alert(1))}</style>'
    const result = sanitizeFormatted(malicious)
    expect(result).not.toContain('<style>')
    expect(result).toContain('&lt;style&gt;')
  })

  // ===== 混合场景 =====
  it('混合 <mark> 与 <script> 时仅保留 <mark>', () => {
    // 输入 raw HTML: <mark>good</mark> <script>bad</script>
    // 期望: <mark>good</mark> &lt;script&gt;bad&lt;/script&gt; (script 转义, mark 保留)
    const input = '<mark>good</mark> <script>bad</script>'
    const result = sanitizeFormatted(input)
    expect(result).toBe('<mark>good</mark> &lt;script&gt;bad&lt;/script&gt;')
  })

  it('纯文本 (无 HTML) 原样返回', () => {
    expect(sanitizeFormatted('Hello World')).toBe('Hello World')
  })

  it('中文 + 数字 + 特殊字符正常处理', () => {
    expect(sanitizeFormatted('机油滤芯 123 + - = ?')).toBe('机油滤芯 123 + - = ?')
  })
})
