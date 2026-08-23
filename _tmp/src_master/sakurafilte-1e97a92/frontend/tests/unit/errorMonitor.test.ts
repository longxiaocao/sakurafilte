// 批次 6c: 错误监控单测
//   覆盖: 捕获 / 脱敏 / 去重 / 持久化 / 面包屑
//   WHY 离线优先: 不依赖任何外部 SDK, 在 jsdom 环境即可完整跑
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import {
  initMonitor, captureException, captureMessage, addBreadcrumb,
  getEvents, clearEvents, exportEvents, shutdownMonitor,
  type ErrorEvent,
} from '@/utils/errorMonitor'

describe('errorMonitor', () => {
  beforeEach(() => {
    // 每次测试前清空 localStorage + 重置模块状态
    localStorage.clear()
    shutdownMonitor()
    initMonitor()
  })

  afterEach(() => {
    shutdownMonitor()
  })

  it('captures basic Error', () => {
    const id = captureException(new Error('test error'))
    expect(id).toBeTruthy()
    const events = getEvents()
    expect(events.length).toBe(1)
    expect(events[0].message).toBe('test error')
    expect(events[0].level).toBe('error')
    expect(events[0].exception?.type).toBe('Error')
  })

  it('captures string error', () => {
    captureException('just a string error')
    const events = getEvents()
    expect(events.length).toBe(1)
    expect(events[0].exception?.type).toBe('StringError')
    expect(events[0].message).toBe('just a string error')
  })

  it('captures unknown object error', () => {
    captureException({ code: 1, reason: 'custom' })
    const events = getEvents()
    expect(events[0].exception?.type).toBe('UnknownError')
    expect(events[0].message).toContain('"code":1')
  })

  it('captures message with custom level', () => {
    captureMessage('info note', { level: 'info' })
    const events = getEvents()
    expect(events[0].level).toBe('info')
    expect(events[0].message).toBe('info note')
  })

  it('deduplicates same error within 5 min', () => {
    const e = new Error('dup')
    e.stack = 'Error: dup\n  at A\n  at B\n  at C'
    captureException(e)
    captureException(e)
    captureException(e)
    const events = getEvents()
    expect(events.length).toBe(1)
  })

  it('does not dedupe different errors', () => {
    captureException(new Error('err A'))
    captureException(new Error('err B'))
    expect(getEvents().length).toBe(2)
  })

  it('sanitizes JWT in error message', () => {
    const jwt = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4ifQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c'
    captureException(new Error(`bearer ${jwt} leaked`))
    const ev = getEvents()[0]
    expect(ev.message).not.toContain(jwt)
    expect(ev.message).toContain('[REDACTED_JWT]')
  })

  it('sanitizes sensitive fields in extra', () => {
    captureException(new Error('test'), {
      extra: {
        password: '12345',
        Authorization: 'Bearer abc',
        cookie: 'session=xyz',
        safe: 'visible',
      },
    })
    const ev = getEvents()[0]
    expect(ev.extra.password).toBe('[REDACTED]')
    expect(ev.extra.Authorization).toBe('[REDACTED]')
    expect(ev.extra.cookie).toBe('[REDACTED]')
    expect(ev.extra.safe).toBe('visible')
  })

  it('attaches tags and extra', () => {
    captureException(new Error('tagged'), {
      tags: { source: 'unit-test', kind: 'sample' },
      extra: { url: '/api/x' },
    })
    const ev = getEvents()[0]
    expect(ev.tags.source).toBe('unit-test')
    expect(ev.tags.kind).toBe('sample')
    expect(ev.extra.url).toBe('/api/x')
  })

  it('records breadcrumbs in event', () => {
    addBreadcrumb({ category: 'http', type: 'http', level: 'info', message: 'GET /api/x' })
    addBreadcrumb({ category: 'ui', type: 'ui', level: 'info', message: 'click btn' })
    captureException(new Error('with bread'))
    const ev = getEvents()[0]
    expect(ev.breadcrumbs.length).toBe(2)
    expect(ev.breadcrumbs[0].message).toBe('GET /api/x')
    expect(ev.breadcrumbs[1].type).toBe('ui')
  })

  it('truncates breadcrumbs to MAX_BREADCRUMBS', () => {
    for (let i = 0; i < 30; i++) {
      addBreadcrumb({ message: `crumb-${i}` })
    }
    captureException(new Error('trunc test'))
    const ev = getEvents()[0]
    expect(ev.breadcrumbs.length).toBeLessThanOrEqual(20)
  })

  it('persists to localStorage and survives reload', () => {
    captureException(new Error('persistent'))
    // 模拟重新加载
    shutdownMonitor()
    initMonitor()
    const events = getEvents()
    expect(events.length).toBe(1)
    expect(events[0].message).toBe('persistent')
  })

  it('truncates events ring buffer at 200', () => {
    // 直接 push 210 条, 验证环形缓冲
    for (let i = 0; i < 210; i++) {
      captureException(new Error(`e${i}`))
    }
    expect(getEvents().length).toBe(200)
    // 最早的事件应已被淘汰
    const msgs = getEvents().map((e) => e.message)
    expect(msgs).not.toContain('e0')
    expect(msgs).toContain('e209')
  })

  it('clearEvents removes all', () => {
    captureException(new Error('will be cleared'))
    expect(getEvents().length).toBe(1)
    clearEvents()
    expect(getEvents().length).toBe(0)
  })

  it('exportEvents returns valid JSON with metadata', () => {
    captureException(new Error('export me'))
    const json = exportEvents()
    const parsed = JSON.parse(json)
    expect(parsed.eventCount).toBe(1)
    expect(parsed.exportedAt).toBeTruthy()
    expect(parsed.events[0].message).toBe('export me')
  })

  it('event has url and userAgent', () => {
    captureException(new Error('ctx'))
    const ev = getEvents()[0]
    expect(ev.url).toContain('http')  // jsdom default
    expect(ev.userAgent).toBeTruthy()
  })

  it('does not throw on non-Error rejection reason', () => {
    expect(() => captureException(undefined)).not.toThrow()
    expect(() => captureException(null)).not.toThrow()
    expect(() => captureException(42)).not.toThrow()
    // undefined / null 会触发 throw, 42 会作为 UnknownError
    const events = getEvents()
    expect(events.length).toBeGreaterThanOrEqual(1)
  })
})
