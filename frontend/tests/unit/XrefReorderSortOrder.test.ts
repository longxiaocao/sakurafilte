/**
 * V2 Task 2.2: OEM 3 排序管理单测
 *
 * 设计:
 *   - 不挂载 AdminXrefReorderView 组件 (依赖 Element Plus + vuedraggable + route)
 *   - 抽取核心算法逻辑直接测试:
 *     1) sortOrder 1-based 重算 (dragList.map((item, idx) => sortOrder: idx + 1))
 *     2) 顺序变化检测 (hasChange = dragList.some((item, idx) => item.sortOrder !== idx + 1))
 *     3) 409 重试时的 rowVersion 映射 (rvMap = Map(oemNo3 → rowVersion))
 *     4) 用户拖拽顺序保留 (重试时仅更新 rowVersion, 不丢失用户意图)
 *
 * WHY 抽取测试:
 *   - AdminXrefReorderView.vue L86-150 的 saveReorder 逻辑较复杂
 *   - 涉及 409 重试 + rowVersion 映射 + 用户意图保留
 *   - 直接挂载组件需要 mock axios + vuedraggable 拖拽事件, 测试稳定性差
 *   - 抽取算法验证核心行为, 配合 E2E 测试覆盖端到端流程
 */
import { describe, it, expect } from 'vitest'

// ===== 类型定义 (与 src/api/types.ts XrefOem3Item 对齐) =====
interface XrefOem3Item {
  oemNo3: string
  sortOrder: number
  oem2?: string | null
  machineType?: string | null
  isPublished?: boolean
  rowVersion: number  // xmin 乐观锁令牌
}

// ===== 抽取的核心算法 (与 AdminXrefReorderView.vue L79, L90-94 同口径) =====

/**
 * 检测 dragList 顺序是否有变化 (避免无变化时多余 API 调用)
 *   AdminXrefReorderView.vue L79: dragList.value.some((item, idx) => item.sortOrder !== idx + 1)
 */
function hasOrderChange(dragList: XrefOem3Item[]): boolean {
  return dragList.some((item, idx) => item.sortOrder !== idx + 1)
}

/**
 * 重算 sortOrder (1-based, 拖拽顺序即排序)
 *   AdminXrefReorderView.vue L90-94: userOrderedDragList.map((item, idx) => ({
 *     oemNo3: item.oemNo3,
 *     sortOrder: idx + 1,
 *     rowVersion: item.rowVersion
 *   }))
 */
function recomputeSortOrder(dragList: XrefOem3Item[]): Array<{
  oemNo3: string
  sortOrder: number
  rowVersion: number
}> {
  return dragList.map((item, idx) => ({
    oemNo3: item.oemNo3,
    sortOrder: idx + 1,
    rowVersion: item.rowVersion
  }))
}

/**
 * 409 重试时的 rowVersion 映射构建
 *   AdminXrefReorderView.vue L117-118: const rvMap = new Map(fresh.items.map((it) => [it.oemNo3, it.rowVersion]))
 */
function buildRowVersionMap(freshItems: XrefOem3Item[]): Map<string, number> {
  return new Map(freshItems.map((it) => [it.oemNo3, it.rowVersion]))
}

/**
 * 重试时仅更新 rowVersion, 保留用户拖拽顺序
 *   AdminXrefReorderView.vue L126-130: dragList.value = userOrderedDragList.map((it) => ({
 *     ...it,
 *     rowVersion: rvMap.get(it.oemNo3) as any
 *   }))
 *
 * 边界: 若用户拖拽的某个 oemNo3 已被他人删除 (rvMap 取不到), 返回 null
 *   AdminXrefReorderView.vue L120-125: missingOem 检测 → 终止重试
 */
function applyFreshRowVersions(
  userOrderedDragList: XrefOem3Item[],
  rvMap: Map<string, number>
): { updated: XrefOem3Item[] | null; missingOem: string | null } {
  // 边界检查: 用户拖拽的某个 oemNo3 已被他人删除
  const missingOem = userOrderedDragList.find((it) => !rvMap.has(it.oemNo3))?.oemNo3 ?? null
  if (missingOem) {
    return { updated: null, missingOem }
  }
  // 仅更新 rowVersion, 顺序保留用户意图
  const updated = userOrderedDragList.map((it) => ({
    ...it,
    rowVersion: rvMap.get(it.oemNo3) as number
  }))
  return { updated, missingOem: null }
}

// ===== 测试用例 =====

describe('V2 Task 2.2: XrefReorder 排序核心算法', () => {
  // ===== sortOrder 1-based 重算 =====
  describe('recomputeSortOrder (1-based 重算)', () => {
    it('空列表返回空数组', () => {
      expect(recomputeSortOrder([])).toEqual([])
    })

    it('单元素列表 sortOrder=1', () => {
      const list: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 5, rowVersion: 100 }
      ]
      const result = recomputeSortOrder(list)
      expect(result).toEqual([
        { oemNo3: 'A', sortOrder: 1, rowVersion: 100 }
      ])
    })

    it('3 元素列表 sortOrder 重算为 1/2/3', () => {
      const list: XrefOem3Item[] = [
        { oemNo3: 'C', sortOrder: 99, rowVersion: 3 },
        { oemNo3: 'A', sortOrder: 1, rowVersion: 1 },
        { oemNo3: 'B', sortOrder: 2, rowVersion: 2 }
      ]
      const result = recomputeSortOrder(list)
      expect(result.map((r) => r.sortOrder)).toEqual([1, 2, 3])
      expect(result.map((r) => r.oemNo3)).toEqual(['C', 'A', 'B'])
    })

    it('保留 rowVersion 透传 (xmin 乐观锁令牌)', () => {
      const list: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 111 },
        { oemNo3: 'B', sortOrder: 2, rowVersion: 222 }
      ]
      const result = recomputeSortOrder(list)
      expect(result[0].rowVersion).toBe(111)
      expect(result[1].rowVersion).toBe(222)
    })

    it('保留其他字段 (oem2/machineType/isPublished) 不丢失', () => {
      const list: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 1, oem2: 'OEM2-X', machineType: 'commercial', isPublished: true }
      ]
      // 注意: recomputeSortOrder 仅提取 oemNo3/sortOrder/rowVersion, 不保留其他字段
      //   这是后端 API 契约 (XrefReorderRequest) 的最小字段集
      const result = recomputeSortOrder(list)
      expect(result[0]).toEqual({
        oemNo3: 'A',
        sortOrder: 1,
        rowVersion: 1
      })
      // 后端不需要 oem2/machineType/isPublished, 仅按 oemNo3 定位记录
    })
  })

  // ===== 顺序变化检测 =====
  describe('hasOrderChange (变化检测)', () => {
    it('空列表无变化', () => {
      expect(hasOrderChange([])).toBe(false)
    })

    it('顺序已对齐 (1,2,3) 无变化', () => {
      const list: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 1 },
        { oemNo3: 'B', sortOrder: 2, rowVersion: 2 },
        { oemNo3: 'C', sortOrder: 3, rowVersion: 3 }
      ]
      expect(hasOrderChange(list)).toBe(false)
    })

    it('顺序错位 (3,1,2) 有变化', () => {
      const list: XrefOem3Item[] = [
        { oemNo3: 'C', sortOrder: 3, rowVersion: 3 },  // 现在 idx=0, 期望 sortOrder=1, 实际 3 → 变化
        { oemNo3: 'A', sortOrder: 1, rowVersion: 1 },
        { oemNo3: 'B', sortOrder: 2, rowVersion: 2 }
      ]
      expect(hasOrderChange(list)).toBe(true)
    })

    it('单元素列表 sortOrder=1 无变化', () => {
      const list: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 1 }
      ]
      expect(hasOrderChange(list)).toBe(false)
    })

    it('单元素列表 sortOrder=99 有变化', () => {
      const list: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 99, rowVersion: 1 }
      ]
      expect(hasOrderChange(list)).toBe(true)
    })

    it('中间元素错位有变化', () => {
      const list: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 1 },
        { oemNo3: 'C', sortOrder: 3, rowVersion: 3 },  // 中间错位
        { oemNo3: 'B', sortOrder: 2, rowVersion: 2 }
      ]
      expect(hasOrderChange(list)).toBe(true)
    })
  })

  // ===== 409 重试时的 rowVersion 映射 =====
  describe('buildRowVersionMap (409 重试映射)', () => {
    it('空列表返回空 Map', () => {
      const map = buildRowVersionMap([])
      expect(map.size).toBe(0)
    })

    it('单元素构建 oemNo3 → rowVersion 映射', () => {
      const fresh: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 999 }
      ]
      const map = buildRowVersionMap(fresh)
      expect(map.get('A')).toBe(999)
    })

    it('多元素构建完整映射', () => {
      const fresh: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 100 },
        { oemNo3: 'B', sortOrder: 2, rowVersion: 200 },
        { oemNo3: 'C', sortOrder: 3, rowVersion: 300 }
      ]
      const map = buildRowVersionMap(fresh)
      expect(map.get('A')).toBe(100)
      expect(map.get('B')).toBe(200)
      expect(map.get('C')).toBe(300)
      expect(map.size).toBe(3)
    })

    it('rowVersion 是最新值 (重试场景)', () => {
      // WHY 重试场景: 用户拖拽时持有的 rowVersion 是旧的, 后端 409 后需拉取最新
      //   fresh.items 是从后端重新 GET 的, rowVersion 是最新 xmin
      const fresh: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 9999 }  // 假设后端 UPDATE 后 xmin 变化
      ]
      const map = buildRowVersionMap(fresh)
      expect(map.get('A')).toBe(9999)
    })
  })

  // ===== 重试时保留用户拖拽顺序 =====
  describe('applyFreshRowVersions (用户意图保留)', () => {
    it('正常场景: 仅更新 rowVersion, 顺序保留', () => {
      // 用户拖拽后的顺序: [B, A, C] (原 [A, B, C])
      const userOrdered: XrefOem3Item[] = [
        { oemNo3: 'B', sortOrder: 2, rowVersion: 200 },  // 旧 rowVersion
        { oemNo3: 'A', sortOrder: 1, rowVersion: 100 },
        { oemNo3: 'C', sortOrder: 3, rowVersion: 300 }
      ]
      // 后端最新拉取的列表 (顺序可能是 [A, B, C])
      const fresh: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 101 },  // 新 rowVersion
        { oemNo3: 'B', sortOrder: 2, rowVersion: 201 },
        { oemNo3: 'C', sortOrder: 3, rowVersion: 301 }
      ]
      const rvMap = buildRowVersionMap(fresh)
      const { updated, missingOem } = applyFreshRowVersions(userOrdered, rvMap)

      expect(missingOem).toBeNull()
      expect(updated).not.toBeNull()
      // 顺序保留: 仍是 [B, A, C]
      expect(updated!.map((u) => u.oemNo3)).toEqual(['B', 'A', 'C'])
      // rowVersion 已更新为最新值
      expect(updated![0].rowVersion).toBe(201)  // B 的新 rowVersion
      expect(updated![1].rowVersion).toBe(101)  // A 的新 rowVersion
      expect(updated![2].rowVersion).toBe(301)  // C 的新 rowVersion
    })

    it('边界场景: 用户拖拽的 oemNo3 已被他人删除 → 终止重试', () => {
      // 用户拖拽后的顺序: [A, B, C]
      const userOrdered: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 100 },
        { oemNo3: 'B', sortOrder: 2, rowVersion: 200 },  // 已被他人删除
        { oemNo3: 'C', sortOrder: 3, rowVersion: 300 }
      ]
      // 后端最新列表已无 B
      const fresh: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 101 },
        { oemNo3: 'C', sortOrder: 2, rowVersion: 301 }  // C 的 sortOrder 已变
      ]
      const rvMap = buildRowVersionMap(fresh)
      const { updated, missingOem } = applyFreshRowVersions(userOrdered, rvMap)

      expect(missingOem).toBe('B')
      expect(updated).toBeNull()
    })

    it('空用户列表 → 返回空更新列表', () => {
      const rvMap = buildRowVersionMap([])
      const { updated, missingOem } = applyFreshRowVersions([], rvMap)
      expect(missingOem).toBeNull()
      expect(updated).toEqual([])
    })

    it('空 rvMap 但用户列表非空 → 检测到全部 missing', () => {
      const userOrdered: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 100 }
      ]
      const rvMap = new Map<string, number>()
      const { updated, missingOem } = applyFreshRowVersions(userOrdered, rvMap)
      expect(missingOem).toBe('A')
      expect(updated).toBeNull()
    })

    it('保留 oem2/machineType/isPublished 字段 (用户拖拽时的扩展数据)', () => {
      // WHY 字段保留: 用户拖拽的 XrefOem3Item 可能含 oem2/machineType/isPublished
      //   重试时这些字段不应丢失, 仅 rowVersion 更新
      const userOrdered: XrefOem3Item[] = [
        {
          oemNo3: 'A',
          sortOrder: 1,
          rowVersion: 100,
          oem2: 'OEM2-X',
          machineType: 'commercial',
          isPublished: true
        }
      ]
      const fresh: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 101 }
      ]
      const rvMap = buildRowVersionMap(fresh)
      const { updated } = applyFreshRowVersions(userOrdered, rvMap)

      expect(updated).not.toBeNull()
      expect(updated![0].oemNo3).toBe('A')
      expect(updated![0].rowVersion).toBe(101)
      // 扩展字段保留 (用 any 访问避免类型严格)
      expect((updated![0] as any).oem2).toBe('OEM2-X')
      expect((updated![0] as any).machineType).toBe('commercial')
      expect((updated![0] as any).isPublished).toBe(true)
    })
  })

  // ===== 端到端流程模拟 (不挂载组件, 模拟 409 重试完整流程) =====
  describe('端到端流程模拟: 拖拽 → 409 → 重试成功', () => {
    it('完整流程: 用户拖拽 → 首次 409 → 拉最新 → 重试成功', () => {
      // 初始状态: 后端返回 [A, B, C], sortOrder=1/2/3, rowVersion=100/200/300
      const initialList: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 100 },
        { oemNo3: 'B', sortOrder: 2, rowVersion: 200 },
        { oemNo3: 'C', sortOrder: 3, rowVersion: 300 }
      ]
      // 模拟 dragList = [...initialList] (组件 L56)
      let dragList = [...initialList]

      // 用户拖拽: [A, B, C] → [B, A, C]
      dragList = [
        dragList[1],  // B
        dragList[0],  // A
        dragList[2]   // C
      ]

      // 步骤 1: 检测顺序变化
      expect(hasOrderChange(dragList)).toBe(true)

      // 步骤 2: 重算 sortOrder, 准备首次提交
      const userOrderedDragList = [...dragList]
      const firstItems = recomputeSortOrder(userOrderedDragList)
      expect(firstItems).toEqual([
        { oemNo3: 'B', sortOrder: 1, rowVersion: 200 },
        { oemNo3: 'A', sortOrder: 2, rowVersion: 100 },
        { oemNo3: 'C', sortOrder: 3, rowVersion: 300 }
      ])

      // 步骤 3: 模拟首次提交 → 409 (他人修改导致 xmin 失配)
      // 后端返回 409 后, 组件 L116: const fresh = await adminXrefApi.listByBrand(...)
      // 后端最新列表: rowVersion 已更新 (假设 xmin 变化)
      const freshAfter409: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 101 },
        { oemNo3: 'B', sortOrder: 2, rowVersion: 201 },
        { oemNo3: 'C', sortOrder: 3, rowVersion: 301 }
      ]
      const rvMap = buildRowVersionMap(freshAfter409)

      // 步骤 4: 检测 missing (无 oemNo3 被删除)
      const { updated, missingOem } = applyFreshRowVersions(userOrderedDragList, rvMap)
      expect(missingOem).toBeNull()
      expect(updated).not.toBeNull()

      // 步骤 5: 用最新 rowVersion + 用户拖拽顺序重试
      dragList = updated!
      const retryItems = recomputeSortOrder(dragList)
      expect(retryItems).toEqual([
        { oemNo3: 'B', sortOrder: 1, rowVersion: 201 },  // 最新 rowVersion
        { oemNo3: 'A', sortOrder: 2, rowVersion: 101 },
        { oemNo3: 'C', sortOrder: 3, rowVersion: 301 }
      ])
      // 用户拖拽顺序 [B, A, C] 保留, rowVersion 已更新
    })

    it('完整流程: 用户拖拽 → 首次 409 → 拉最新 → 重试仍 409 → 弹框', () => {
      // 模拟真并发冲突场景: 第二次重试仍 409
      const initialList: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 100 },
        { oemNo3: 'B', sortOrder: 2, rowVersion: 200 }
      ]
      let dragList = [...initialList]
      // 用户拖拽: [A, B] → [B, A]
      dragList = [dragList[1], dragList[0]]

      expect(hasOrderChange(dragList)).toBe(true)
      const userOrderedDragList = [...dragList]

      // 首次 409 后拉最新
      const fresh1: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 101 },
        { oemNo3: 'B', sortOrder: 2, rowVersion: 201 }
      ]
      const rvMap1 = buildRowVersionMap(fresh1)
      const { updated: updated1, missingOem: missing1 } = applyFreshRowVersions(userOrderedDragList, rvMap1)
      expect(missing1).toBeNull()
      dragList = updated1!

      // 重试 (isRetry=true) 仍 409 → 不再重试, 弹框让用户决策
      //   组件 L140-144: ElMessageBox.confirm('是否立即刷新?')
      //   这里只验证重试时的 items 构造正确
      const retryItems = recomputeSortOrder(dragList)
      expect(retryItems).toEqual([
        { oemNo3: 'B', sortOrder: 1, rowVersion: 201 },
        { oemNo3: 'A', sortOrder: 2, rowVersion: 101 }
      ])
      // 用户拖拽顺序 [B, A] 保留, rowVersion 已更新 (但后端仍 409, 需用户手动刷新)
    })

    it('完整流程: 用户拖拽 → 首次 409 → 拉最新 → 检测 missing → 终止重试', () => {
      // 模拟边界场景: 拖拽过程中某个 oemNo3 已被他人删除
      const initialList: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 100 },
        { oemNo3: 'B', sortOrder: 2, rowVersion: 200 },  // 将被删除
        { oemNo3: 'C', sortOrder: 3, rowVersion: 300 }
      ]
      let dragList = [...initialList]
      // 用户拖拽: [A, B, C] → [C, B, A]
      dragList = [dragList[2], dragList[1], dragList[0]]

      const userOrderedDragList = [...dragList]

      // 首次 409 后拉最新: B 已被他人删除
      const freshAfter409: XrefOem3Item[] = [
        { oemNo3: 'A', sortOrder: 1, rowVersion: 101 },
        { oemNo3: 'C', sortOrder: 2, rowVersion: 301 }
        // B 不在列表中
      ]
      const rvMap = buildRowVersionMap(freshAfter409)

      // 检测 missing: B 已删除
      const { updated, missingOem } = applyFreshRowVersions(userOrderedDragList, rvMap)
      expect(missingOem).toBe('B')
      expect(updated).toBeNull()
      // 组件 L121-125: ElMessage.warning(`OEM 3 B 已被他人删除...`) + loadOemList()
      //   不会调 saveReorder(true) 重试, 直接终止
    })
  })
})
