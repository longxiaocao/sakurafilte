/**
 * V24-F35 (spec Task 4.9.1): http.ts ERROR_CODE_MAP 错误码映射单测
 *   覆盖错误码 → 友好提示的映射规则
 *
 * 测试目标:
 *   - 关键错误码 (400/401/403/404/409/422/429) 都有映射
 *   - 映射值是中文友好提示 (非英文/技术堆栈)
 *   - 未映射错误码 (500/502/503) 不在表中 (由 status >= 500 分支单独处理)
 *   - 映射值不为空串
 *
 * WHY 映射测试: ERROR_CODE_MAP 是用户可见错误提示的核心来源
 *   - 缺失映射会导致用户看到 "请求失败 (xxx)" 等无意义提示
 *   - 错误映射会导致用户误解 (如 401 显示 "没有权限" 误导用户认为账号问题)
 *
 * WHY 不测 5xx: 5xx 由 http.ts 中 status >= 500 分支单独处理
 *   - 显示 "服务器繁忙,请稍后重试 (错误码:500)"
 *   - 不在 ERROR_CODE_MAP 中, 避免代码重复
 */
import { describe, it, expect } from 'vitest'
import { ERROR_CODE_MAP } from '@/utils/http'

describe('ERROR_CODE_MAP', () => {
  // ===== 关键错误码覆盖 =====
  it('400 (Bad Request) 映射到 "请求参数错误"', () => {
    expect(ERROR_CODE_MAP[400]).toBe('请求参数错误')
  })

  it('401 (Unauthorized) 映射到 "未登录或登录已过期" (非 "没有权限")', () => {
    // WHY 严格区分 401 vs 403: 401 是认证失败 (未登录/token 过期), 403 是授权失败 (登录但无权限)
    // 错误映射 401 → "没有权限" 会误导用户认为账号问题, 实际是需重新登录
    expect(ERROR_CODE_MAP[401]).toBe('未登录或登录已过期')
    expect(ERROR_CODE_MAP[401]).not.toContain('权限')
  })

  it('403 (Forbidden) 映射到 "没有权限执行此操作"', () => {
    expect(ERROR_CODE_MAP[403]).toBe('没有权限执行此操作')
  })

  it('404 (Not Found) 映射到 "请求的资源不存在"', () => {
    expect(ERROR_CODE_MAP[404]).toBe('请求的资源不存在')
  })

  it('409 (Conflict) 映射到 "资源已存在 (冲突)"', () => {
    expect(ERROR_CODE_MAP[409]).toBe('资源已存在 (冲突)')
  })

  it('422 (Unprocessable Entity) 映射到 "请求参数验证失败"', () => {
    expect(ERROR_CODE_MAP[422]).toBe('请求参数验证失败')
  })

  it('429 (Too Many Requests) 映射到 "请求过于频繁,请稍后重试"', () => {
    expect(ERROR_CODE_MAP[429]).toBe('请求过于频繁,请稍后重试')
  })

  // ===== 5xx 不在映射表中 (由 status >= 500 分支单独处理) =====
  it('500 (Internal Server Error) 不在映射表中 (由 5xx 分支处理)', () => {
    expect(ERROR_CODE_MAP[500]).toBeUndefined()
  })

  it('502 (Bad Gateway) 不在映射表中', () => {
    expect(ERROR_CODE_MAP[502]).toBeUndefined()
  })

  it('503 (Service Unavailable) 不在映射表中', () => {
    expect(ERROR_CODE_MAP[503]).toBeUndefined()
  })

  // ===== 映射值质量验证 =====
  it('所有映射值都是非空字符串', () => {
    for (const [code, msg] of Object.entries(ERROR_CODE_MAP)) {
      expect(msg).toBeTruthy()
      expect(typeof msg).toBe('string')
      expect(msg.length).toBeGreaterThan(0)
    }
  })

  it('所有映射值都是中文 (含中文字符)', () => {
    // 简单中文检测: 至少含 1 个 CJK 统一汉字
    const cjkPattern = /[\u4e00-\u9fff]/
    for (const [code, msg] of Object.entries(ERROR_CODE_MAP)) {
      expect(msg).toMatch(cjkPattern)
    }
  })

  it('映射值不含技术堆栈 / SQL / 文件路径 (用户友好)', () => {
    // 不应出现 "Exception" / "Stack" / ".cs" / "SQL" 等技术术语
    const techPatterns = [/Exception/i, /\.cs\b/, /SQL/i, /stack trace/i, /at line \d+/i]
    for (const [code, msg] of Object.entries(ERROR_CODE_MAP)) {
      for (const p of techPatterns) {
        expect(msg).not.toMatch(p)
      }
    }
  })

  // ===== 表大小验证 =====
  it('ERROR_CODE_MAP 至少覆盖 7 个错误码 (4xx 全覆盖)', () => {
    const keys = Object.keys(ERROR_CODE_MAP)
    expect(keys.length).toBeGreaterThanOrEqual(7)
  })
})
