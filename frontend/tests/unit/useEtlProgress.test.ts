/**
 * useEtlProgress 纯函数单元测试 (V24-F78)
 *
 * 测试矩阵:
 *   parseSseChunk:
 *     1. 单条完整消息 "data: {json}\n\n"
 *     2. 多条消息连续推送
 *     3. 注释行 ": keepalive\n\n" 被忽略
 *     4. 空行 (SSE 消息分隔符) 被忽略
 *     5. 跨 chunk 不完整行: "data: {json}\n" + "data: {json2}\n\n"
 *     6. JSON 解析失败: 不影响后续消息
 *     7. data: 后无 payload: 忽略
 *     8. 非 data: 开头的行: 忽略
 *
 *   computeReconnectDelay:
 *     9. 第 1 次: 1000ms
 *     10. 第 2 次: 2000ms
 *     11. 第 3 次: 4000ms
 *     12. 第 5 次: 16000ms
 *     13. 第 10 次: 30000ms (封顶)
 *     14. 第 0 次或负数: 当作第 1 次 (1000ms)
 *
 * WHY 纯函数测试: useEtlProgress 主体依赖 Vue 运行时 (onMounted/onBeforeUnmount/Pinia store),
 *   抽取 parseSseChunk + computeReconnectDelay 为纯函数后, 可独立测试 SSE 解析与退避逻辑,
 *   无需 mock Vue 运行时 (规则 3.3 最小闭环)
 */
import { describe, it, expect } from 'vitest'
import { parseSseChunk, computeReconnectDelay } from '@/composables/useEtlProgress'

describe('parseSseChunk', () => {
  it('1. 单条完整消息: 解析出 1 条 EtlActiveTaskInfo', () => {
    const buf = { buf: '' }
    const text = 'data: {"inProgress":false,"activeTask":null}\n\n'
    const result = parseSseChunk(text, buf)
    expect(result).toHaveLength(1)
    expect(result[0]).toEqual({ inProgress: false, activeTask: null })
    expect(buf.buf).toBe('')  // 缓冲清空
  })

  it('2. 多条消息连续推送: 解析出 N 条', () => {
    const buf = { buf: '' }
    const text = [
      'data: {"inProgress":true,"activeTask":{"status":"running"}}',
      '',
      'data: {"inProgress":false,"activeTask":{"status":"completed"}}',
      '',
      ''
    ].join('\n')
    const result = parseSseChunk(text, buf)
    expect(result).toHaveLength(2)
    expect(result[0].inProgress).toBe(true)
    expect(result[0].activeTask.status).toBe('running')
    expect(result[1].inProgress).toBe(false)
    expect(result[1].activeTask.status).toBe('completed')
  })

  it('3. 注释行 ": keepalive" 被忽略', () => {
    const buf = { buf: '' }
    const text = ': keepalive\n\ndata: {"inProgress":false}\n\n'
    const result = parseSseChunk(text, buf)
    expect(result).toHaveLength(1)
    expect(result[0].inProgress).toBe(false)
  })

  it('4. 空行 (SSE 消息分隔符) 被忽略', () => {
    const buf = { buf: '' }
    const text = '\n\ndata: {"inProgress":false}\n\n\n\n'
    const result = parseSseChunk(text, buf)
    expect(result).toHaveLength(1)
  })

  it('5. 跨 chunk 不完整行: 第一 chunk 留半行, 第二 chunk 补全', () => {
    const buf = { buf: '' }
    // 第一 chunk: data: 后 JSON 未闭合, 行尾无 \n
    const chunk1 = 'data: {"inProgress":'
    let result = parseSseChunk(chunk1, buf)
    expect(result).toHaveLength(0)  // 无完整行
    expect(buf.buf).toBe('data: {"inProgress":')  // 半行保留

    // 第二 chunk: 补全 JSON + \n\n
    const chunk2 = 'false}\n\ndata: {"inProgress":true}\n\n'
    result = parseSseChunk(chunk2, buf)
    expect(result).toHaveLength(2)
    expect(result[0].inProgress).toBe(false)
    expect(result[1].inProgress).toBe(true)
    expect(buf.buf).toBe('')
  })

  it('6. JSON 解析失败: 不影响后续消息', () => {
    const buf = { buf: '' }
    const text = [
      'data: {invalid json}',
      '',
      'data: {"inProgress":false}',
      '',
      ''
    ].join('\n')
    const result = parseSseChunk(text, buf)
    expect(result).toHaveLength(1)  // 只解析出第二条
    expect(result[0].inProgress).toBe(false)
  })

  it('7. data: 后无 payload: 忽略', () => {
    const buf = { buf: '' }
    const text = 'data: \n\ndata: {"inProgress":false}\n\n'
    const result = parseSseChunk(text, buf)
    expect(result).toHaveLength(1)
  })

  it('8. 非 data: 开头的行: 忽略', () => {
    const buf = { buf: '' }
    const text = 'event: ping\nid: 42\ndata: {"inProgress":false}\n\n'
    const result = parseSseChunk(text, buf)
    expect(result).toHaveLength(1)
    expect(result[0].inProgress).toBe(false)
  })
})

describe('computeReconnectDelay', () => {
  it('9. 第 1 次: 1000ms', () => {
    expect(computeReconnectDelay(1)).toBe(1000)
  })

  it('10. 第 2 次: 2000ms', () => {
    expect(computeReconnectDelay(2)).toBe(2000)
  })

  it('11. 第 3 次: 4000ms', () => {
    expect(computeReconnectDelay(3)).toBe(4000)
  })

  it('12. 第 5 次: 16000ms', () => {
    expect(computeReconnectDelay(5)).toBe(16000)
  })

  it('13. 第 10 次: 30000ms (封顶)', () => {
    expect(computeReconnectDelay(10)).toBe(30000)
  })

  it('14. 第 0 次或负数: 当作第 1 次 (1000ms)', () => {
    expect(computeReconnectDelay(0)).toBe(1000)
    expect(computeReconnectDelay(-5)).toBe(1000)
  })
})
