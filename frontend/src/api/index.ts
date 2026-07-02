// Day 9: API 客户端 (按业务域拆分)
import { http } from '@/utils/http'
import type {
  SearchRequest,
  SearchResult,
  ProductDetail,
  ProductHistoryItem,
  AdminSearchRequest,
  PageResp,
  ProductListItem,
  EtlTriggerRequest,
  EtlProgress,
  EtlActiveTaskInfo,
  EtlHistoryItem,
  EtlReasonCodeAggregate
} from './types'

// ===== Day 10: 字典类型 (P1.3 OEM 品牌字典) =====
export interface OemBrandItem {
  id: number
  brand: string
  sortOrder: number
  createdAt: string
  updatedAt: string
  deletedAt: string | null
  xrefCount: number
}

export interface OemBrandTypeaheadItem {
  id: number
  brand: string
}

export interface OemBrandReorderItem {
  id: number
  sortOrder: number
}

// ===== Day 10+ P2.2: 字典类型 (Product Name 1/2, Type, OEM 3, Media, Machine, Engine) =====
export interface ProductName1Item {
  id: number
  productName1: string
  sortOrder: number
  createdAt: string
  updatedAt: string
  deletedAt: string | null
  xrefCount: number
}
export interface ProductName1TypeaheadItem { id: number; productName1: string }
export interface ProductName1ReorderItem { id: number; sortOrder: number }

export interface ProductName2Item {
  id: number
  productName2: string
  sortOrder: number
  createdAt: string
  updatedAt: string
  deletedAt: string | null
  xrefCount: number
}
export interface ProductName2TypeaheadItem { id: number; productName2: string }
export interface ProductName2ReorderItem { id: number; sortOrder: number }

export interface TypeItem {
  id: number
  type: string
  sortOrder: number
  createdAt: string
  updatedAt: string
  deletedAt: string | null
  xrefCount: number
}
export interface TypeTypeaheadItem { id: number; type: string }
export interface TypeReorderItem { id: number; sortOrder: number }

export interface OemNo3Item {
  id: number
  oemNo3: string
  sortOrder: number
  createdAt: string
  updatedAt: string
  deletedAt: string | null
  xrefCount: number
}
export interface OemNo3TypeaheadItem { id: number; oemNo3: string }
export interface OemNo3ReorderItem { id: number; sortOrder: number }

export interface MediaItem {
  id: number
  mediaName: string
  mediaModel: string | null
  sortOrder: number
  createdAt: string
  updatedAt: string
  deletedAt: string | null
  xrefCount: number
}
export interface MediaTypeaheadItem { id: number; mediaName: string; mediaModel: string | null }
export interface MediaReorderItem { id: number; sortOrder: number }

export interface MachineItem {
  id: number
  machineBrand: string
  machineModel: string | null
  machineName: string | null
  // P2.3: 4 大类 (Agriculture/Commercial/Construction/others)
  machineCategory: 'Agriculture' | 'Commercial' | 'Construction' | 'others'
  sortOrder: number
  createdAt: string
  updatedAt: string
  deletedAt: string | null
  xrefCount: number
}
export interface MachineTypeaheadItem { id: number; machineBrand: string; machineModel: string | null; machineName: string | null }
export interface MachineReorderItem { id: number; sortOrder: number }

export interface EngineItem {
  id: number
  engineBrand: string
  engineType: string | null
  sortOrder: number
  createdAt: string
  updatedAt: string
  deletedAt: string | null
  xrefCount: number
}
export interface EngineTypeaheadItem { id: number; engineBrand: string; engineType: string | null }
export interface EngineReorderItem { id: number; sortOrder: number }

// ===== 搜索 (公开, 无需 token) =====
export const searchApi = {
  search(req: SearchRequest): Promise<{ provider: string; result: SearchResult }> {
    return http.post('/search', req).then((r) => r.data)
  },
  health(): Promise<{ provider: string; healthy: boolean }> {
    return http.get('/search/health').then((r) => r.data)
  },
  // P3.2 (Task 10): 批量 OEM 查询 (Excel 多行粘贴)
  //   POST /public/search/batch-oem { oems: [...] }
  //   返: { total, hits, miss, results: [{ oem, hit, productId, oemBrand, productName1, oem2 }] }
  batchOem(req: import('./types').BatchOemRequest): Promise<import('./types').BatchOemResponse> {
    return http.post('/public/search/batch-oem', req).then((r) => r.data)
  }
}

// ===== 产品详情 (公开) =====
// P3.3 (Task 11): 调公开端点 /public/product/{slug} (无 token)
//   URL 格式 (R1 规格): {name1}-{name2}-{oemBrand}-{oemNo}
//   后端 GetBySlug 内部解析 slug 末段为 oem
export const productApi = {
  getByOem(slug: string): Promise<ProductDetail> {
    // 注意: 走 http 拦截器, 即使已登录后台 (有 token) 也可访问公开端点 (后端 [AllowAnonymous])
    return http.get(`/public/product/${encodeURIComponent(slug)}`).then((r) => r.data)
  }
}

// ===== P3.4 (Task 11.5): 公开搜索 (8 字段多框, 无需 token) =====
//   GET /api/public/search?oemBrand=...&oemNo2=...&oemNo3=...&machineBrand=...&machineModel=...&modelName=...&engineBrand=...&engineType=...
//   返: { total, page, pageSize, totalPages, elapsedMs, countMode, items: [{id, oemNoDisplay, oem2, productName1, type, d1Mm, h1Mm}] }
import type { PublicEightRequest, PublicEightResponse } from './types'
export const publicSearchApi = {
  eightField(req: PublicEightRequest): Promise<PublicEightResponse> {
    // 过滤 undefined / 空字符串, axios 不会把空串当未传, 显式构造 params
    const params: Record<string, string | number> = {}
    if (req.oemBrand) params.oemBrand = req.oemBrand
    if (req.oemNo2) params.oemNo2 = req.oemNo2
    if (req.oemNo3) params.oemNo3 = req.oemNo3
    if (req.machineBrand) params.machineBrand = req.machineBrand
    if (req.machineModel) params.machineModel = req.machineModel
    if (req.modelName) params.modelName = req.modelName
    if (req.engineBrand) params.engineBrand = req.engineBrand
    if (req.engineType) params.engineType = req.engineType
    params.page = req.page ?? 1
    params.pageSize = req.pageSize ?? 20
    return http.get('/public/search', { params }).then((r) => r.data)
  }
}

// ===== 后台产品管理 (需 token) =====
export const adminProductApi = {
  search(req: AdminSearchRequest): Promise<PageResp<ProductListItem>> {
    return http.get('/admin/products/search', { params: req }).then((r) => r.data)
  },
  get(id: number): Promise<ProductDetail> {
    return http.get(`/admin/products/${id}`).then((r) => r.data)
  },
  update(id: number, form: any, by: string): Promise<ProductDetail> {
    return http.put(`/admin/products/${id}`, form, { headers: { 'X-User': by } }).then((r) => r.data)
  },
  create(form: any, by: string): Promise<ProductDetail> {
    return http.post('/admin/products', form, { headers: { 'X-User': by } }).then((r) => r.data)
  },
  discontinue(id: number, by: string): Promise<void> {
    return http.delete(`/admin/products/${id}`, { headers: { 'X-User': by } }).then((r) => r.data)
  },
  restore(id: number, by: string): Promise<void> {
    return http.post(`/admin/products/${id}/restore`, null, { headers: { 'X-User': by } }).then((r) => r.data)
  },
  // Day 9.3: 返回 ProductHistoryPage, total 反映筛选后真实总数
  //   Day 9.4: 加 cursor 字段, keyset 翻下一页
  history(
    id: number,
    options?: { limit?: number; changeType?: string; since?: string; until?: string; cursor?: string }
  ): Promise<import('./types').ProductHistoryPage> {
    const params: Record<string, any> = {}
    if (options?.limit) params.limit = options.limit
    if (options?.changeType) params.changeType = options.changeType
    if (options?.since) params.since = options.since
    if (options?.until) params.until = options.until
    if (options?.cursor) params.cursor = options.cursor
    return http.get(`/admin/products/${id}/history`, { params }).then((r) => r.data)
  },
  compare(ids: number[]): Promise<{ count: number; items: ProductDetail[] }> {
    return http.post('/admin/products/compare', { ids }).then((r) => r.data)
  }
}

// ===== 图片管理 =====
export const imageApi = {
  list(productId: number): Promise<ProductDetail['images']> {
    return http.get(`/admin/products/${productId}/images`).then((r) => r.data)
  },
  upload(productId: number, slot: number, file: File): Promise<{ slot: number; imageKey: string; imageUrl: string }> {
    const fd = new FormData()
    fd.append('file', file)
    return http
      .post(`/admin/products/${productId}/images/${slot}`, fd, {
        headers: { 'Content-Type': 'multipart/form-data' }
      })
      .then((r) => r.data)
  },
  remove(productId: number, slot: number): Promise<void> {
    return http.delete(`/admin/products/${productId}/images/${slot}`).then((r) => r.data)
  }
}

// ===== ETL =====
export const etlApi = {
  trigger(req: EtlTriggerRequest): Promise<EtlProgress> {
    return http.post('/admin/etl/trigger', req).then((r) => r.data)
  },
  // Day 9.4: 取消 ETL, 接受 reason 写到 etl_progress_log.cancel_reason
  // Day 9.5: 接受 reasonCode 写到 etl_progress_log.reason_code (USER_REQUEST/TIMEOUT/SYSTEM_SHUTDOWN/ADMIN_OVERRIDE/OTHER)
  cancel(reason?: string, reasonCode?: string): Promise<{ cancelled: boolean; reason?: string; reasonCode?: string; normalizedCode?: string }> {
    return http.delete('/admin/etl/task', { data: { reason, reasonCode } }).then((r) => r.data)
  },
  // P1.1 (Task 3): 暂停 ETL 任务 — 区别于 cancel, 当前批次跑完后优雅退出
  //   返回 { paused: true/false, checkpointId?: number, entity?: string }
  pause(): Promise<{ paused: boolean; reason?: string; checkpointId?: number; entity?: string }> {
    return http.post('/admin/etl/pause', {}).then((r) => r.data)
  },
  // P1.1 (Task 3): 恢复 ETL 任务 — 从最近 paused 记录的 checkpoint_id 续读
  //   返回 { resumed: true/false, checkpointId?: number, batchSize?: number, nextLineNo?: number }
  resume(): Promise<{ resumed: boolean; error?: string; entity?: string; mode?: string; checkpointId?: number; batchSize?: number; nextLineNo?: number }> {
    return http.post('/admin/etl/resume', {}).then((r) => r.data)
  },


  progress(): Promise<EtlActiveTaskInfo> {
    return http.get('/admin/etl/progress').then((r) => r.data)
  },
  legacyStatus(): Promise<EtlProgress> {
    return http.get('/etl/status').then((r) => r.data)
  },
  // Day 9.8: 历史日志 + reason_code 聚合 (运营审计饼图)
  history(limit = 50, status?: string): Promise<{ count: number; items: EtlHistoryItem[] }> {
    const params: Record<string, any> = { limit }
    if (status) params.status = status
    return http.get('/admin/etl/history', { params }).then((r) => r.data)
  },
  reasonCodeAggregate(): Promise<EtlReasonCodeAggregate> {
    return http.get('/admin/etl/history/aggregate').then((r) => r.data)
  }
}

// ===== Day 10: 字典 API (P1.3 OEM 品牌字典) =====
//   list:        GET    /api/admin/dict/oem-brands?q=&includeDeleted=&limit=
//   typeahead:   GET    /api/admin/dict/oem-brands/typeahead?q=&limit=
//   create:      POST   /api/admin/dict/oem-brands                { brand, sortOrder? }
//   update:      PUT    /api/admin/dict/oem-brands/:id            { brand?, sortOrder? }
//   delete:      DELETE /api/admin/dict/oem-brands/:id            (软删除)
//   restore:     POST   /api/admin/dict/oem-brands/:id/restore
//   reorder:     POST   /api/admin/dict/oem-brands/reorder        { items: [{id, sortOrder}] }
// ===== P2.2: 复用 BaseDictService 抽象, 7 个新字典走同 URL 模式 =====
export const dictApi = {
  oemBrands: {
    list(q?: string, includeDeleted = false, limit?: number): Promise<{ count: number; items: OemBrandItem[] }> {
      const params: Record<string, any> = {}
      if (q) params.q = q
      if (includeDeleted) params.includeDeleted = true
      if (limit) params.limit = limit
      return http.get('/admin/dict/oem-brands', { params }).then((r) => r.data)
    },
    typeahead(q?: string, limit = 20): Promise<{ count: number; items: OemBrandTypeaheadItem[] }> {
      const params: Record<string, any> = { limit }
      if (q) params.q = q
      return http.get('/admin/dict/oem-brands/typeahead', { params }).then((r) => r.data)
    },
    create(brand: string, sortOrder?: number): Promise<OemBrandItem> {
      return http.post('/admin/dict/oem-brands', { brand, sortOrder }).then((r) => r.data)
    },
    update(id: number, body: { brand?: string; sortOrder?: number }): Promise<OemBrandItem> {
      return http.put(`/admin/dict/oem-brands/${id}`, body).then((r) => r.data)
    },
    delete(id: number): Promise<void> {
      return http.delete(`/admin/dict/oem-brands/${id}`).then((r) => r.data)
    },
    restore(id: number): Promise<OemBrandItem> {
      return http.post(`/admin/dict/oem-brands/${id}/restore`).then((r) => r.data)
    },
    reorder(items: OemBrandReorderItem[]): Promise<{ updated: number }> {
      return http.post('/admin/dict/oem-brands/reorder', { items }).then((r) => r.data)
    }
  },
  // ===== P2.2: Product Name 1 =====
  productName1s: {
    list(q?: string, includeDeleted = false, limit?: number): Promise<{ count: number; items: ProductName1Item[] }> {
      const params: Record<string, any> = {}
      if (q) params.q = q
      if (includeDeleted) params.includeDeleted = true
      if (limit) params.limit = limit
      return http.get('/admin/dict/product-name1s', { params }).then((r) => r.data)
    },
    typeahead(q?: string, limit = 20): Promise<{ count: number; items: ProductName1TypeaheadItem[] }> {
      const params: Record<string, any> = { limit }
      if (q) params.q = q
      return http.get('/admin/dict/product-name1s/typeahead', { params }).then((r) => r.data)
    },
    create(productName1: string, sortOrder?: number): Promise<ProductName1Item> {
      return http.post('/admin/dict/product-name1s', { productName1, sortOrder }).then((r) => r.data)
    },
    update(id: number, body: { productName1?: string; sortOrder?: number }): Promise<ProductName1Item> {
      return http.put(`/admin/dict/product-name1s/${id}`, body).then((r) => r.data)
    },
    delete(id: number): Promise<void> {
      return http.delete(`/admin/dict/product-name1s/${id}`).then((r) => r.data)
    },
    restore(id: number): Promise<ProductName1Item> {
      return http.post(`/admin/dict/product-name1s/${id}/restore`).then((r) => r.data)
    },
    reorder(items: ProductName1ReorderItem[]): Promise<{ updated: number }> {
      return http.post('/admin/dict/product-name1s/reorder', { items }).then((r) => r.data)
    }
  },
  // ===== P2.2: Product Name 2 =====
  productName2s: {
    list(q?: string, includeDeleted = false, limit?: number): Promise<{ count: number; items: ProductName2Item[] }> {
      const params: Record<string, any> = {}
      if (q) params.q = q
      if (includeDeleted) params.includeDeleted = true
      if (limit) params.limit = limit
      return http.get('/admin/dict/product-name2s', { params }).then((r) => r.data)
    },
    typeahead(q?: string, limit = 20): Promise<{ count: number; items: ProductName2TypeaheadItem[] }> {
      const params: Record<string, any> = { limit }
      if (q) params.q = q
      return http.get('/admin/dict/product-name2s/typeahead', { params }).then((r) => r.data)
    },
    create(productName2: string, sortOrder?: number): Promise<ProductName2Item> {
      return http.post('/admin/dict/product-name2s', { productName2, sortOrder }).then((r) => r.data)
    },
    update(id: number, body: { productName2?: string; sortOrder?: number }): Promise<ProductName2Item> {
      return http.put(`/admin/dict/product-name2s/${id}`, body).then((r) => r.data)
    },
    delete(id: number): Promise<void> {
      return http.delete(`/admin/dict/product-name2s/${id}`).then((r) => r.data)
    },
    restore(id: number): Promise<ProductName2Item> {
      return http.post(`/admin/dict/product-name2s/${id}/restore`).then((r) => r.data)
    },
    reorder(items: ProductName2ReorderItem[]): Promise<{ updated: number }> {
      return http.post('/admin/dict/product-name2s/reorder', { items }).then((r) => r.data)
    }
  },
  // ===== P2.2: Type (固定 5 值: oil/fuel/air/cabin/others) =====
  types: {
    list(q?: string, includeDeleted = false, limit?: number): Promise<{ count: number; items: TypeItem[] }> {
      const params: Record<string, any> = {}
      if (q) params.q = q
      if (includeDeleted) params.includeDeleted = true
      if (limit) params.limit = limit
      return http.get('/admin/dict/types', { params }).then((r) => r.data)
    },
    typeahead(q?: string, limit = 20): Promise<{ count: number; items: TypeTypeaheadItem[] }> {
      const params: Record<string, any> = { limit }
      if (q) params.q = q
      return http.get('/admin/dict/types/typeahead', { params }).then((r) => r.data)
    },
    create(type: string, sortOrder?: number): Promise<TypeItem> {
      return http.post('/admin/dict/types', { type, sortOrder }).then((r) => r.data)
    },
    update(id: number, body: { type?: string; sortOrder?: number }): Promise<TypeItem> {
      return http.put(`/admin/dict/types/${id}`, body).then((r) => r.data)
    },
    delete(id: number): Promise<void> {
      return http.delete(`/admin/dict/types/${id}`).then((r) => r.data)
    },
    restore(id: number): Promise<TypeItem> {
      return http.post(`/admin/dict/types/${id}/restore`).then((r) => r.data)
    },
    reorder(items: TypeReorderItem[]): Promise<{ updated: number }> {
      return http.post('/admin/dict/types/reorder', { items }).then((r) => r.data)
    }
  },
  // ===== P2.2: OEM 3 =====
  oemNo3s: {
    list(q?: string, includeDeleted = false, limit?: number): Promise<{ count: number; items: OemNo3Item[] }> {
      const params: Record<string, any> = {}
      if (q) params.q = q
      if (includeDeleted) params.includeDeleted = true
      if (limit) params.limit = limit
      return http.get('/admin/dict/oem-no3s', { params }).then((r) => r.data)
    },
    typeahead(q?: string, limit = 20): Promise<{ count: number; items: OemNo3TypeaheadItem[] }> {
      const params: Record<string, any> = { limit }
      if (q) params.q = q
      return http.get('/admin/dict/oem-no3s/typeahead', { params }).then((r) => r.data)
    },
    create(oemNo3: string, sortOrder?: number): Promise<OemNo3Item> {
      return http.post('/admin/dict/oem-no3s', { oemNo3, sortOrder }).then((r) => r.data)
    },
    update(id: number, body: { oemNo3?: string; sortOrder?: number }): Promise<OemNo3Item> {
      return http.put(`/admin/dict/oem-no3s/${id}`, body).then((r) => r.data)
    },
    delete(id: number): Promise<void> {
      return http.delete(`/admin/dict/oem-no3s/${id}`).then((r) => r.data)
    },
    restore(id: number): Promise<OemNo3Item> {
      return http.post(`/admin/dict/oem-no3s/${id}/restore`).then((r) => r.data)
    },
    reorder(items: OemNo3ReorderItem[]): Promise<{ updated: number }> {
      return http.post('/admin/dict/oem-no3s/reorder', { items }).then((r) => r.data)
    }
  },
  // ===== P2.2: Media (2 字段: media_name + media_model) =====
  medias: {
    list(q?: string, includeDeleted = false, limit?: number): Promise<{ count: number; items: MediaItem[] }> {
      const params: Record<string, any> = {}
      if (q) params.q = q
      if (includeDeleted) params.includeDeleted = true
      if (limit) params.limit = limit
      return http.get('/admin/dict/medias', { params }).then((r) => r.data)
    },
    typeahead(q?: string, limit = 20): Promise<{ count: number; items: MediaTypeaheadItem[] }> {
      const params: Record<string, any> = { limit }
      if (q) params.q = q
      return http.get('/admin/dict/medias/typeahead', { params }).then((r) => r.data)
    },
    create(mediaName: string, mediaModel?: string, sortOrder?: number): Promise<MediaItem> {
      return http.post('/admin/dict/medias', { mediaName, mediaModel, sortOrder }).then((r) => r.data)
    },
    update(id: number, body: { mediaName?: string; mediaModel?: string; sortOrder?: number }): Promise<MediaItem> {
      return http.put(`/admin/dict/medias/${id}`, body).then((r) => r.data)
    },
    delete(id: number): Promise<void> {
      return http.delete(`/admin/dict/medias/${id}`).then((r) => r.data)
    },
    restore(id: number): Promise<MediaItem> {
      return http.post(`/admin/dict/medias/${id}/restore`).then((r) => r.data)
    },
    reorder(items: MediaReorderItem[]): Promise<{ updated: number }> {
      return http.post('/admin/dict/medias/reorder', { items }).then((r) => r.data)
    }
  },
  // ===== P2.2: Machine (3 字段: machine_brand + machine_model + machine_name) =====
  machines: {
    list(q?: string, includeDeleted = false, limit?: number): Promise<{ count: number; items: MachineItem[] }> {
      const params: Record<string, any> = {}
      if (q) params.q = q
      if (includeDeleted) params.includeDeleted = true
      if (limit) params.limit = limit
      return http.get('/admin/dict/machines', { params }).then((r) => r.data)
    },
    typeahead(q?: string, limit = 20): Promise<{ count: number; items: MachineTypeaheadItem[] }> {
      const params: Record<string, any> = { limit }
      if (q) params.q = q
      return http.get('/admin/dict/machines/typeahead', { params }).then((r) => r.data)
    },
    // Day 11 Phase 1 BUG FIX B: 补 machineCategory 参数 (之前 create 漏传, update 有)
    create(machineBrand: string, machineModel?: string, machineName?: string, sortOrder?: number, machineCategory?: string): Promise<MachineItem> {
      return http.post('/admin/dict/machines', { machineBrand, machineModel, machineName, sortOrder, machineCategory }).then((r) => r.data)
    },
    update(id: number, body: { machineBrand?: string; machineModel?: string; machineName?: string; machineCategory?: 'Agriculture' | 'Commercial' | 'Construction' | 'others'; sortOrder?: number }): Promise<MachineItem> {
      return http.put(`/admin/dict/machines/${id}`, body).then((r) => r.data)
    },
    delete(id: number): Promise<void> {
      return http.delete(`/admin/dict/machines/${id}`).then((r) => r.data)
    },
    restore(id: number): Promise<MachineItem> {
      return http.post(`/admin/dict/machines/${id}/restore`).then((r) => r.data)
    },
    reorder(items: MachineReorderItem[]): Promise<{ updated: number }> {
      return http.post('/admin/dict/machines/reorder', { items }).then((r) => r.data)
    }
  },
  // ===== P2.2: Engine (2 字段: engine_brand + engine_type) =====
  engines: {
    list(q?: string, includeDeleted = false, limit?: number): Promise<{ count: number; items: EngineItem[] }> {
      const params: Record<string, any> = {}
      if (q) params.q = q
      if (includeDeleted) params.includeDeleted = true
      if (limit) params.limit = limit
      return http.get('/admin/dict/engines', { params }).then((r) => r.data)
    },
    typeahead(q?: string, limit = 20): Promise<{ count: number; items: EngineTypeaheadItem[] }> {
      const params: Record<string, any> = { limit }
      if (q) params.q = q
      return http.get('/admin/dict/engines/typeahead', { params }).then((r) => r.data)
    },
    create(engineBrand: string, engineType?: string, sortOrder?: number): Promise<EngineItem> {
      return http.post('/admin/dict/engines', { engineBrand, engineType, sortOrder }).then((r) => r.data)
    },
    update(id: number, body: { engineBrand?: string; engineType?: string; sortOrder?: number }): Promise<EngineItem> {
      return http.put(`/admin/dict/engines/${id}`, body).then((r) => r.data)
    },
    delete(id: number): Promise<void> {
      return http.delete(`/admin/dict/engines/${id}`).then((r) => r.data)
    },
    restore(id: number): Promise<EngineItem> {
      return http.post(`/admin/dict/engines/${id}/restore`).then((r) => r.data)
    },
    reorder(items: EngineReorderItem[]): Promise<{ updated: number }> {
      return http.post('/admin/dict/engines/reorder', { items }).then((r) => r.data)
    }
  }
}

