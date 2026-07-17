// Day 11 P4.2 (Task 14.2): 后端 dict schema 契约测试
//   - 拉取 GET /api/admin/dict/_schema
//   - 用 zod 校验响应结构 + 字段必填
//   - 校验关键字段: 8 个字典必现, 每个字典有 Id + SortOrder + 主字段
//   - 改后端字段不改前端 → 此测试 CI fail (契约破坏即报)
import { describe, expect, test } from 'vitest'
import { z } from 'zod'

// === zod schemas ===
const FieldSchema = z.object({
  name: z.string(),
  cSharpType: z.string(),
  nullable: z.boolean(),
  hasColumn: z.boolean()
})

const DictSchema = z.object({
  entity: z.string(),
  table: z.string(),
  fields: z.array(FieldSchema)
})

const SchemaResponse = z.object({
  generatedAt: z.string(),
  count: z.number(),
  dictionaries: z.array(DictSchema)
})

// === 必填字段清单 (后端 + 前端必须一致) ===
//   8 个字典都至少有: Id, 主字段 (Brand/ProductName1/...), SortOrder, CreatedAt, UpdatedAt, DeletedAt
const REQUIRED_FIELDS: Record<string, string[]> = {
  XrefOemBrand:    ['Id', 'Brand', 'SortOrder', 'CreatedAt', 'UpdatedAt', 'DeletedAt'],
  DictProductName1: ['Id', 'ProductName1', 'SortOrder', 'CreatedAt', 'UpdatedAt', 'DeletedAt'],
  DictProductName2: ['Id', 'ProductName2', 'SortOrder', 'CreatedAt', 'UpdatedAt', 'DeletedAt'],
  DictType:        ['Id', 'Type', 'SortOrder', 'CreatedAt', 'UpdatedAt', 'DeletedAt'],
  DictOemNo3:      ['Id', 'OemNo3', 'SortOrder', 'CreatedAt', 'UpdatedAt', 'DeletedAt'],
  DictMedia:       ['Id', 'MediaName', 'MediaModel', 'SortOrder', 'CreatedAt', 'UpdatedAt', 'DeletedAt'],
  DictMachine:     ['Id', 'MachineBrand', 'MachineModel', 'MachineName', 'MachineCategory', 'SortOrder', 'CreatedAt', 'UpdatedAt', 'DeletedAt'],
  DictEngine:      ['Id', 'EngineBrand', 'EngineType', 'SortOrder', 'CreatedAt', 'UpdatedAt', 'DeletedAt']
}

// 8 个字典全部必现 (改后端字典清单 → 改此处)
const EXPECTED_DICTS = Object.keys(REQUIRED_FIELDS)

const BASE = process.env.BACKEND_URL || 'http://localhost:5148'
const TOKEN = process.env.ADMIN_TOKEN || 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'

describe('P4.2 字典 schema 契约 (后端 _schema 端点)', () => {
  test('1. /api/admin/dict/_schema 返回 200 + zod 通过', async () => {
    const res = await fetch(`${BASE}/api/admin/dict/_schema`, {
      headers: { 'X-Admin-Token': TOKEN }
    })
    expect(res.status, `期望 200, 实际 ${res.status}`).toBe(200)
    const body = await res.json()
    const parsed = SchemaResponse.safeParse(body)
    expect(parsed.success, `zod 校验失败: ${JSON.stringify(parsed.error?.issues)}`).toBe(true)
  })

  test('2. 字典数量 = 8 (P1.3 + P2.2)', async () => {
    const res = await fetch(`${BASE}/api/admin/dict/_schema`, {
      headers: { 'X-Admin-Token': TOKEN }
    })
    const body = await res.json()
    expect(body.count).toBe(8)
    expect(body.dictionaries).toHaveLength(8)
  })

  test('3. 必现 8 个字典 (XrefOemBrand + 7 个 Dict*)', async () => {
    const res = await fetch(`${BASE}/api/admin/dict/_schema`, {
      headers: { 'X-Admin-Token': TOKEN }
    })
    const body = await res.json()
    const entityNames = body.dictionaries.map((d: any) => d.entity).sort()
    expect(entityNames).toEqual(EXPECTED_DICTS.slice().sort())
  })

  test('4. 每个字典的必填字段全部存在', async () => {
    const res = await fetch(`${BASE}/api/admin/dict/_schema`, {
      headers: { 'X-Admin-Token': TOKEN }
    })
    const body = await res.json()
    for (const dict of body.dictionaries) {
      const required = REQUIRED_FIELDS[dict.entity]
      if (!required) continue
      const fieldNames = dict.fields.map((f: any) => f.name)
      for (const r of required) {
        expect(fieldNames, `${dict.entity} 缺字段 ${r}`).toContain(r)
      }
    }
  })

  test('5. Id 字段 type=long + nullable=false (P0 派生键约定)', async () => {
    const res = await fetch(`${BASE}/api/admin/dict/_schema`, {
      headers: { 'X-Admin-Token': TOKEN }
    })
    const body = await res.json()
    for (const dict of body.dictionaries) {
      const idField = dict.fields.find((f: any) => f.name === 'Id')
      expect(idField, `${dict.entity} 缺 Id`).toBeTruthy()
      expect(idField.cSharpType, `${dict.entity}.Id 类型应为 long, 实际 ${idField.cSharpType}`).toBe('long')
      expect(idField.nullable, `${dict.entity}.Id 应 NOT NULL`).toBe(false)
    }
  })

  test('6. SortOrder 字段 type=int + nullable=false (拖拽排序)', async () => {
    const res = await fetch(`${BASE}/api/admin/dict/_schema`, {
      headers: { 'X-Admin-Token': TOKEN }
    })
    const body = await res.json()
    for (const dict of body.dictionaries) {
      const so = dict.fields.find((f: any) => f.name === 'SortOrder')
      expect(so, `${dict.entity} 缺 SortOrder`).toBeTruthy()
      expect(so.cSharpType, `${dict.entity}.SortOrder 类型`).toBe('int')
      expect(so.nullable, `${dict.entity}.SortOrder NOT NULL`).toBe(false)
    }
  })

  test('7. DeletedAt 字段 type=datetime? + nullable=true (软删)', async () => {
    const res = await fetch(`${BASE}/api/admin/dict/_schema`, {
      headers: { 'X-Admin-Token': TOKEN }
    })
    const body = await res.json()
    for (const dict of body.dictionaries) {
      const da = dict.fields.find((f: any) => f.name === 'DeletedAt')
      expect(da, `${dict.entity} 缺 DeletedAt (软删字段)`).toBeTruthy()
      expect(da.cSharpType, `${dict.entity}.DeletedAt 类型`).toBe('datetime?')
      expect(da.nullable, `${dict.entity}.DeletedAt nullable`).toBe(true)
    }
  })

  test('8. DictMachine.MachineCategory 字段存在 (P2.3 4 大类)', async () => {
    const res = await fetch(`${BASE}/api/admin/dict/_schema`, {
      headers: { 'X-Admin-Token': TOKEN }
    })
    const body = await res.json()
    const machine = body.dictionaries.find((d: any) => d.entity === 'DictMachine')
    expect(machine, '缺 DictMachine').toBeTruthy()
    const mc = machine.fields.find((f: any) => f.name === 'MachineCategory')
    expect(mc, 'DictMachine 缺 MachineCategory 字段').toBeTruthy()
  })

  // ========== V2 Task 5.3.3: V2 架构迁移兼容性契约 ==========

  test('9. V2 兼容: 8 个 dict 表在 V2 迁移后仍全部存在 (主表迁移不影响字典)', async () => {
    // WHY V2 兼容性: V2 将 products 主键从 oem_no_normalized 迁移到 mr_1,
    //   但 8 个字典表 (XrefOemBrand/DictProductName1/...) 是独立实体, 不受主表迁移影响
    //   此测试确保 V2 迁移未误删/重命名任何字典表
    const res = await fetch(`${BASE}/api/admin/dict/_schema`, {
      headers: { 'X-Admin-Token': TOKEN }
    })
    const body = await res.json()
    expect(body.count, 'V2 迁移后 dict 数量仍应为 8').toBe(8)
    expect(body.dictionaries, 'V2 迁移后 dict 数组长度仍应为 8').toHaveLength(8)
  })

  test('10. V2 兼容: DictMachine.MachineCategory 在 V2 machine_type 枚举引入后仍保留', async () => {
    // WHY 双轨字段区分:
    //   - DictMachine.MachineCategory: 字典表自带字段 (P2.3 4 大类, 后台字典管理)
    //   - cross_references.MachineType: V2 新增字段 (5 类白名单 agriculture/commercial/construction/industrial/others)
    //   两者是不同概念, V2 引入 machine_type 不应影响 DictMachine.MachineCategory
    const res = await fetch(`${BASE}/api/admin/dict/_schema`, {
      headers: { 'X-Admin-Token': TOKEN }
    })
    const body = await res.json()
    const machine = body.dictionaries.find((d: any) => d.entity === 'DictMachine')
    expect(machine, 'V2 后 DictMachine 仍应存在').toBeTruthy()
    const mc = machine.fields.find((f: any) => f.name === 'MachineCategory')
    expect(mc, 'V2 后 DictMachine.MachineCategory 仍应存在 (不被 machine_type 替代)').toBeTruthy()
    expect(mc.nullable, 'MachineCategory nullable 应保持稳定').toBe(false)
  })

  test('11. V2 兼容: DictOemNo3 字段结构在 V2 主键迁移后不变 (Mr1 在主表不在字典)', async () => {
    // WHY V2 主键迁移边界:
    //   - V2 把 products 主键改为 mr_1, 但 DictOemNo3 是 OEM 3 编号字典, 不含 mr_1 字段
    //   - DictOemNo3 字段: Id, OemNo3, SortOrder, CreatedAt, UpdatedAt, DeletedAt (V2 不变)
    //   此测试确保 V2 主键迁移未误给 DictOemNo3 加 mr_1 字段 (主键迁移仅作用于 products 主表)
    const res = await fetch(`${BASE}/api/admin/dict/_schema`, {
      headers: { 'X-Admin-Token': TOKEN }
    })
    const body = await res.json()
    const oemNo3 = body.dictionaries.find((d: any) => d.entity === 'DictOemNo3')
    expect(oemNo3, '缺 DictOemNo3').toBeTruthy()
    const fieldNames = oemNo3.fields.map((f: any) => f.name)
    // V2 后 DictOemNo3 仍应有 OemNo3 主字段
    expect(fieldNames, 'DictOemNo3.OemNo3 应仍存在').toContain('OemNo3')
    // V2 后 DictOemNo3 不应有 Mr1 (Mr1 是 products 主表字段, 不污染字典)
    expect(fieldNames, 'DictOemNo3 不应含 Mr1 (主键迁移仅作用于 products 主表)').not.toContain('Mr1')
    // V2 后 DictOemNo3 不应有 MachineType (MachineType 是 cross_references 字段, 不污染字典)
    expect(fieldNames, 'DictOemNo3 不应含 MachineType').not.toContain('MachineType')
  })

  test('12. V2 兼容: XrefOemBrand 字段结构稳定 (V2 SortOrder 改进不影响字典表)', async () => {
    // WHY V2 SortOrder 改进边界:
    //   - V2 在 cross_references 表加了 SortOrder (OEM 3 排序管理, AdminXrefReorderView)
    //   - 但 XrefOemBrand 字典表自带 SortOrder (P1.3 字典排序), 字段独立
    //   - 此测试确保 V2 改进未误删 XrefOemBrand.SortOrder
    const res = await fetch(`${BASE}/api/admin/dict/_schema`, {
      headers: { 'X-Admin-Token': TOKEN }
    })
    const body = await res.json()
    const brand = body.dictionaries.find((d: any) => d.entity === 'XrefOemBrand')
    expect(brand, '缺 XrefOemBrand').toBeTruthy()
    const fieldNames = brand.fields.map((f: any) => f.name)
    expect(fieldNames, 'XrefOemBrand.Brand 应仍存在').toContain('Brand')
    expect(fieldNames, 'XrefOemBrand.SortOrder 应仍存在 (字典表自带, 不受 V2 cross_references.SortOrder 影响)').toContain('SortOrder')
    // V2 不应给 XrefOemBrand 加 MachineType (那是 cross_references 字段)
    expect(fieldNames, 'XrefOemBrand 不应含 MachineType').not.toContain('MachineType')
  })
})
