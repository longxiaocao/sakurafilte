/**
 * V24-F43 (spec Task 0.5.5/F3-4): http.ts ERROR_CODE_I18N + resolveErrorMessage fallback 链单测
 *
 * 测试目标:
 *   - ERROR_CODE_I18N 25 个 errorCode 都有中文映射
 *   - resolveErrorMessage fallback 链顺序正确:
 *     errorCode → ERROR_CODE_I18N → i18n → ERROR_CODE_MAP[status] → data.title → fallback
 *   - 旧前端版本收到新错误码不会白屏 (至少有通用提示)
 *
 * WHY fallback 链测试: resolveErrorMessage 是用户可见错误提示的核心逻辑
 *   - 链顺序错误会导致用户看到技术堆栈或无意义提示
 *   - 缺失兜底会导致旧前端版本收到新错误码时白屏
 *
 * 注: i18n 在测试环境未加载, t(key) 返回 key 本身, safeT 返回 fallback (空字符串)
 *   所以 i18n 分支会被跳过, 测试聚焦 ERROR_CODE_I18N / ERROR_CODE_MAP / data.title / fallback
 */
import { describe, it, expect, vi } from 'vitest'
import { ERROR_CODE_I18N, ERROR_CODE_MAP, resolveErrorMessage } from '@/utils/http'
import type { ProblemDetails } from '@/utils/http'

describe('ERROR_CODE_I18N', () => {
  // ===== 旧 ERR_ 前缀错误码 (10 个) =====
  it('ERR_VALIDATION_FAILED 映射到 "请求参数验证失败"', () => {
    expect(ERROR_CODE_I18N['ERR_VALIDATION_FAILED']).toBe('请求参数验证失败')
  })

  it('ERR_NOT_FOUND 映射到 "请求的资源不存在"', () => {
    expect(ERROR_CODE_I18N['ERR_NOT_FOUND']).toBe('请求的资源不存在')
  })

  it('ERR_CONFLICT 映射到 "资源已存在或冲突"', () => {
    expect(ERROR_CODE_I18N['ERR_CONFLICT']).toBe('资源已存在或冲突')
  })

  it('ERR_FORBIDDEN 映射到 "没有权限执行此操作"', () => {
    expect(ERROR_CODE_I18N['ERR_FORBIDDEN']).toBe('没有权限执行此操作')
  })

  it('ERR_CANCELLED 映射到 "请求已取消"', () => {
    expect(ERROR_CODE_I18N['ERR_CANCELLED']).toBe('请求已取消')
  })

  it('ERR_INTERNAL 映射到 "服务器内部错误,请稍后重试"', () => {
    expect(ERROR_CODE_I18N['ERR_INTERNAL']).toBe('服务器内部错误,请稍后重试')
  })

  it('ERR_DB_CONFLICT 映射到 "数据冲突..." (含刷新提示)', () => {
    expect(ERROR_CODE_I18N['ERR_DB_CONFLICT']).toContain('数据冲突')
    expect(ERROR_CODE_I18N['ERR_DB_CONFLICT']).toContain('刷新')
  })

  it('ERR_DB_CONSTRAINT 映射到 "数据约束失败..."', () => {
    expect(ERROR_CODE_I18N['ERR_DB_CONSTRAINT']).toContain('数据约束失败')
  })

  it('ERR_DB_TIMEOUT 映射到 "数据库繁忙..."', () => {
    expect(ERROR_CODE_I18N['ERR_DB_TIMEOUT']).toContain('数据库繁忙')
  })

  it('ERR_AUTH_FAILED 映射到 "用户名或密码错误"', () => {
    expect(ERROR_CODE_I18N['ERR_AUTH_FAILED']).toBe('用户名或密码错误')
  })

  // ===== V2 错误码 (15 个,无 ERR_ 前缀) =====
  it('MR1_REQUIRED 映射到 "MR.1 编号必填"', () => {
    expect(ERROR_CODE_I18N['MR1_REQUIRED']).toBe('MR.1 编号必填')
  })

  it('MR1_FORMAT_INVALID 映射到 "MR.1 编号格式无效"', () => {
    expect(ERROR_CODE_I18N['MR1_FORMAT_INVALID']).toBe('MR.1 编号格式无效')
  })

  it('MR1_ALREADY_EXISTS 映射到 "MR.1 编号已存在"', () => {
    expect(ERROR_CODE_I18N['MR1_ALREADY_EXISTS']).toBe('MR.1 编号已存在')
  })

  it('OEM3_ALREADY_EXISTS 映射到 "OEM 3 编号已存在"', () => {
    expect(ERROR_CODE_I18N['OEM3_ALREADY_EXISTS']).toBe('OEM 3 编号已存在')
  })

  it('MACHINE_TYPE_INVALID 映射到 "机型类型无效"', () => {
    expect(ERROR_CODE_I18N['MACHINE_TYPE_INVALID']).toBe('机型类型无效')
  })

  it('XREF_CONFLICT 映射到 "交叉引用冲突..." (含刷新提示)', () => {
    expect(ERROR_CODE_I18N['XREF_CONFLICT']).toContain('交叉引用冲突')
    expect(ERROR_CODE_I18N['XREF_CONFLICT']).toContain('刷新')
  })

  it('SEARCH_PAGE_TOO_DEEP 映射到 "搜索页数过深..."', () => {
    expect(ERROR_CODE_I18N['SEARCH_PAGE_TOO_DEEP']).toContain('搜索页数过深')
  })

  it('CURSOR_INVALID 映射到 "分页游标无效..."', () => {
    expect(ERROR_CODE_I18N['CURSOR_INVALID']).toContain('分页游标无效')
  })

  it('CURSOR_EXPIRED 映射到 "分页游标已过期..."', () => {
    expect(ERROR_CODE_I18N['CURSOR_EXPIRED']).toContain('分页游标已过期')
  })

  it('IMAGE_ROLE_SLOT_MISMATCH 映射到 "图片角色与槽位不匹配"', () => {
    expect(ERROR_CODE_I18N['IMAGE_ROLE_SLOT_MISMATCH']).toBe('图片角色与槽位不匹配')
  })

  it('IMAGE_DETAIL_SLOT_INVALID 映射到 "图片详情槽位无效..."', () => {
    expect(ERROR_CODE_I18N['IMAGE_DETAIL_SLOT_INVALID']).toContain('图片详情槽位无效')
  })

  it('IMAGE_PRIMARY_DUPLICATE 映射到 "主图已存在..."', () => {
    expect(ERROR_CODE_I18N['IMAGE_PRIMARY_DUPLICATE']).toContain('主图已存在')
  })

  it('IMAGE_DETAIL_SLOT_DUPLICATE 映射到 "图片详情槽位重复"', () => {
    expect(ERROR_CODE_I18N['IMAGE_DETAIL_SLOT_DUPLICATE']).toBe('图片详情槽位重复')
  })

  it('MR1_NOT_FOUND 映射到 "MR.1 编号不存在"', () => {
    expect(ERROR_CODE_I18N['MR1_NOT_FOUND']).toBe('MR.1 编号不存在')
  })

  it('OEM3_NOT_FOUND 映射到 "OEM 3 编号不存在"', () => {
    expect(ERROR_CODE_I18N['OEM3_NOT_FOUND']).toBe('OEM 3 编号不存在')
  })

  // ===== 映射值质量验证 =====
  it('所有映射值都是非空字符串', () => {
    for (const [code, msg] of Object.entries(ERROR_CODE_I18N)) {
      expect(msg).toBeTruthy()
      expect(typeof msg).toBe('string')
      expect(msg.length).toBeGreaterThan(0)
    }
  })

  it('所有映射值都是中文 (含中文字符)', () => {
    const cjkPattern = /[\u4e00-\u9fff]/
    for (const [code, msg] of Object.entries(ERROR_CODE_I18N)) {
      expect(msg).toMatch(cjkPattern)
    }
  })

  it('ERROR_CODE_I18N 至少覆盖 25 个错误码 (10 旧 + 15 V2)', () => {
    const keys = Object.keys(ERROR_CODE_I18N)
    expect(keys.length).toBeGreaterThanOrEqual(25)
  })
})

describe('resolveErrorMessage', () => {
  // ===== fallback 链顺序测试 =====
  it('errorCode 命中 ERROR_CODE_I18N → 返回静态映射 (优先级 1)', () => {
    const msg = resolveErrorMessage('ERR_AUTH_FAILED', 401, undefined, '请求失败 (401)')
    expect(msg).toBe('用户名或密码错误')
  })

  it('errorCode 命中 V2 错误码 → 返回静态映射 (优先级 1)', () => {
    const msg = resolveErrorMessage('MR1_REQUIRED', 400, undefined, '请求失败 (400)')
    expect(msg).toBe('MR.1 编号必填')
  })

  it('errorCode 未命中 + status 命中 ERROR_CODE_MAP → 返回 status 映射 (优先级 3)', () => {
    // 未知 errorCode 'UNKNOWN_CODE' 不在 ERROR_CODE_I18N 中
    // i18n 在测试环境未加载, t(key) 返回 key 本身, safeT 返回 '' (空 fallback)
    // 跳过 i18n 分支, 进入 ERROR_CODE_MAP[404] = '请求的资源不存在'
    const msg = resolveErrorMessage('UNKNOWN_CODE', 404, undefined, '请求失败 (404)')
    expect(msg).toBe('请求的资源不存在')
  })

  it('errorCode + status 都未命中 + data.title 存在 → 返回 data.title (优先级 4)', () => {
    // 未知 errorCode + 未知 status (503 不在 ERROR_CODE_MAP 中)
    // 跳过 ERROR_CODE_I18N / i18n / ERROR_CODE_MAP, 返回 data.title
    const data: ProblemDetails = { title: '自定义业务错误', status: 503 }
    const msg = resolveErrorMessage('UNKNOWN_CODE', 503, data, '请求失败 (503)')
    expect(msg).toBe('自定义业务错误')
  })

  it('全部未命中 → 返回 fallback (优先级 5)', () => {
    // 未知 errorCode + 未知 status + 无 data.title
    const msg = resolveErrorMessage('UNKNOWN_CODE', 599, undefined, '请求失败 (599)')
    expect(msg).toBe('请求失败 (599)')
  })

  it('errorCode 为 undefined → 跳过 ERROR_CODE_I18N 和 i18n, 直接走 ERROR_CODE_MAP', () => {
    // 无 errorCode 时, 跳过前两个分支, 直接走 ERROR_CODE_MAP[401]
    const msg = resolveErrorMessage(undefined, 401, undefined, '请求失败 (401)')
    expect(msg).toBe('未登录或登录已过期')
  })

  it('errorCode 为 undefined + status 未命中 + data.title 存在 → 返回 data.title', () => {
    const data: ProblemDetails = { title: '业务错误', status: 418 }
    const msg = resolveErrorMessage(undefined, 418, data, '请求失败 (418)')
    expect(msg).toBe('业务错误')
  })

  it('errorCode 为 undefined + status 为 undefined → 返回 data.title 或 fallback', () => {
    // 网络层错误 (无响应): status undefined, errorCode undefined
    const data: ProblemDetails = { title: '网络错误', status: 0 }
    const msg = resolveErrorMessage(undefined, undefined, data, '网络异常')
    expect(msg).toBe('网络错误')
  })

  it('errorCode 为 undefined + 全部未命中 → 返回 fallback', () => {
    const msg = resolveErrorMessage(undefined, undefined, undefined, '未知错误')
    expect(msg).toBe('未知错误')
  })

  // ===== 优先级冲突测试 =====
  it('errorCode 命中 ERROR_CODE_I18N 时, 即使 status 也命中 ERROR_CODE_MAP, 优先返回 errorCode 映射', () => {
    // ERR_NOT_FOUND 命中 ERROR_CODE_I18N, 404 也命中 ERROR_CODE_MAP
    // 应该优先返回 ERROR_CODE_I18N 的 "请求的资源不存在" (而非 ERROR_CODE_MAP 的同值, 但逻辑上优先级更高)
    const msg = resolveErrorMessage('ERR_NOT_FOUND', 404, undefined, '请求失败 (404)')
    expect(msg).toBe('请求的资源不存在')
  })

  it('data.title 存在但 errorCode 命中 ERROR_CODE_I18N → 优先返回 errorCode 映射 (不透传 title)', () => {
    // 防止后端 ProblemDetails.title 含技术细节, 优先用前端友好映射
    const data: ProblemDetails = { title: '后端原始 title (可能含技术细节)', status: 401 }
    const msg = resolveErrorMessage('ERR_AUTH_FAILED', 401, data, '请求失败 (401)')
    expect(msg).toBe('用户名或密码错误')
    expect(msg).not.toBe(data.title)
  })
})
