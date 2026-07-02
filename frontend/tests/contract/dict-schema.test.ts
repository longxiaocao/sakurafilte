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
})
