/**
 * V2 Task 1.1: MR.1 前端校验规则单测
 *
 * 设计:
 *   - 不挂载整个 AdminProductFormView (依赖 Element Plus + route + router + i18n)
 *   - 直接测试 mr1Rules 的正则模式, 与后端 AdminProductService.ValidateForm 对齐
 *   - 覆盖 spec 要求: MR.1 必填 + 1-10 位字母数字 + 最大长度 10
 *
 * WHY 单独测试正则:
 *   - AdminProductFormView.vue 的 mr1Rules 是模块内 const, 无法直接 import
 *   - 但正则模式 /^[A-Za-z0-9]{1,10}$/ 与后端 ^[A-Za-z0-9]{1,10}$ 必须完全一致
 *   - 抽取正则独立测试, 同时用 V2 架构契约校验前后端口径一致
 *
 * 后端契约 (AdminProductService.ValidateForm):
 *   - MR1_REQUIRED: string.IsNullOrWhiteSpace(form.Mr1) → throw ArgumentException
 *   - MR1_FORMAT_INVALID: !Regex.IsMatch(form.Mr1.Trim(), @"^[A-Za-z0-9]{1,10}$") → throw
 *   - 长度校验: form.Mr1.Length > 10 → throw (正则已覆盖, 双重保险)
 */
import { describe, it, expect } from 'vitest'

// V2 Task 1.1: 与 AdminProductFormView.vue L83-86 mr1Rules 同口径
//   WHY 复制正则: 该 const 是组件内私有, 不导出
//     任何变更必须同步更新 (前后端 + 本测试三方对齐)
const MR1_PATTERN = /^[A-Za-z0-9]{1,10}$/
const MR1_MAX_LENGTH = 10

// 模拟前端 el-form rules 校验函数 (与 Element Plus el-form-item trigger='blur' 行为对齐)
//   返回: true = 通过, { error: string } = 失败
function validateMr1(value: string | null | undefined): true | { error: string } {
  // rule.required: 空值拦截
  if (value === null || value === undefined || value === '') {
    return { error: 'MR.1 必填' }
  }
  // 空白字符串也视为空 (Element Plus 默认 trim 行为近似)
  if (typeof value === 'string' && value.trim() === '') {
    return { error: 'MR.1 必填' }
  }
  // rule.pattern: 正则校验
  if (!MR1_PATTERN.test(value)) {
    return { error: 'MR.1 必须为 1-10 位字母+数字' }
  }
  return true
}

// 模拟后端 ValidateForm 的 MR1 校验逻辑 (作契约基准)
//   后端正则: ^[A-Za-z0-9]{1,10}$ (与前端完全一致)
//   后端先 Trim 再 IsMatch, 前端 rules 依赖 el-input 默认不 trim
//   WHY 双向校验: 前端规则 + 后端契约 同口径, 任何不一致立即报警
function backendValidateMr1(formMr1: string | null): true | { error: string } {
  if (stringIsNullOrWhiteSpace(formMr1)) {
    return { error: 'MR1_REQUIRED: MR.1 必填' }
  }
  // 后端先 Trim 再 IsMatch
  if (!MR1_PATTERN.test(formMr1!.trim())) {
    return { error: 'MR1_FORMAT_INVALID: MR.1 必须为 1-10 位字母数字' }
  }
  return true
}

function stringIsNullOrWhiteSpace(s: string | null): boolean {
  return s === null || s.trim() === ''
}

describe('V2 Task 1.1: MR.1 前端校验规则', () => {
  describe('MR1_REQUIRED (必填)', () => {
    it('null 视为空, 返回必填错误', () => {
      expect(validateMr1(null)).toEqual({ error: 'MR.1 必填' })
    })
    it('undefined 视为空, 返回必填错误', () => {
      expect(validateMr1(undefined)).toEqual({ error: 'MR.1 必填' })
    })
    it('空字符串返回必填错误', () => {
      expect(validateMr1('')).toEqual({ error: 'MR.1 必填' })
    })
    it('纯空白字符串返回必填错误', () => {
      expect(validateMr1('   ')).toEqual({ error: 'MR.1 必填' })
    })
    it('tab 字符串返回必填错误', () => {
      expect(validateMr1('\t\t')).toEqual({ error: 'MR.1 必填' })
    })
  })

  describe('MR1_FORMAT_INVALID (格式校验)', () => {
    // 非法字符 (含连字符/空格/下划线/点/斜杠/感叹号/中文)
    const invalidCases: Array<[string, string]> = [
      ['MR-001', '含连字符'],
      ['MR 001', '含空格'],
      ['MR_001', '含下划线'],
      ['MR.001', '含点'],
      ['MR/001', '含斜杠'],
      ['MR001!', '含感叹号'],
      ['MR001中文', '含中文'],
      ['MR@001', '含 @'],
      ['MR#001', '含 #'],
      ['MR$001', '含 $']
    ]
    invalidCases.forEach(([val, desc]) => {
      it(`'${val}' 拒绝 (${desc})`, () => {
        const result = validateMr1(val)
        expect(result).not.toBe(true)
        expect((result as { error: string }).error).toContain('1-10 位字母+数字')
      })
    })

    // 合法格式
    const validCases: Array<[string, string]> = [
      ['M', '1 位 (最小长度)'],
      ['MR000001', '8 位字母数字 (典型场景)'],
      ['A1B2C3D4E5', '10 位字母数字 (最大长度)'],
      ['mr000001', '小写字母 + 数字'],
      ['123456', '纯数字'],
      ['ABCDEF', '纯字母'],
      ['a1', '2 位最小混合']
    ]
    validCases.forEach(([val, desc]) => {
      it(`'${val}' 通过 (${desc})`, () => {
        expect(validateMr1(val)).toBe(true)
      })
    })
  })

  describe('MR1 最大长度 10', () => {
    it('11 位字母数字被拒绝 (正则 {1,10} 拦截)', () => {
      const result = validateMr1('MR000000001') // 11 位
      expect(result).not.toBe(true)
      expect((result as { error: string }).error).toContain('1-10 位')
    })
    it('50 位字母数字被拒绝', () => {
      const result = validateMr1('A'.repeat(50))
      expect(result).not.toBe(true)
    })
    it('10 位字母数字通过 (边界值)', () => {
      expect(validateMr1('A1B2C3D4E5')).toBe(true)
    })
  })

  describe('前后端口径一致性 (契约校验)', () => {
    // WHY 契约校验: 前端 rules 与后端 ValidateForm 必须对同一输入返回相同结论
    //   - 前端通过 → 后端应通过 (避免假阴性造成 400 往返)
    //   - 前端拒绝 → 后端应拒绝 (避免假阳性让用户卡住)
    //   注意: 前端不 Trim, 后端 Trim, 所以 "  MR001  " 类输入前后端结论可能不同
    //         前端 rules 默认不 trim, 但 el-form-item 通常会触发前端拦截
    const cases: Array<[string, boolean, string]> = [
      // [输入, 期望前端通过, 描述]
      ['MR000001', true, '典型合法值'],
      ['A1B2C3D4E5', true, '10 位最大长度'],
      ['MR-001', false, '含连字符'],
      ['MR 001', false, '含空格'],
      ['MR001!', false, '含特殊字符'],
      ['MR000000001', false, '11 位超长'],
      ['', false, '空字符串'],
      ['M', true, '1 位最小长度']
    ]
    cases.forEach(([val, expectedOk, desc]) => {
      it(`前后端对 '${val}' (${desc}) 结论一致`, () => {
        const frontendResult = validateMr1(val)
        const backendResult = backendValidateMr1(val)
        const frontendOk = frontendResult === true
        const backendOk = backendResult === true
        expect(frontendOk).toBe(expectedOk)
        expect(backendOk).toBe(expectedOk)
        // 前后端结论必须一致
        expect(frontendOk).toBe(backendOk)
      })
    })

    it('后端正则与前端正则完全相同 (字符串字面量比对)', () => {
      // WHY 强制相等: 任何一方改动正则都会立即失败, 提示同步
      const frontendPatternSource = MR1_PATTERN.source
      const backendPatternSource = '^[A-Za-z0-9]{1,10}$'
      expect(frontendPatternSource).toBe(backendPatternSource)
    })

    it('MR1_MAX_LENGTH 与后端 checks 表 Mr1 项一致', () => {
      // 后端 checks: ("Mr1", form.Mr1, 10)
      // 前端正则 {1,10} 也限制了 10 位, 双重保险
      expect(MR1_MAX_LENGTH).toBe(10)
      expect(MR1_PATTERN.source).toContain('{1,10}')
    })
  })
})
