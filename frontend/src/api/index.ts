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

// ===== 搜索 (公开, 无需 token) =====
export const searchApi = {
  search(req: SearchRequest): Promise<{ provider: string; result: SearchResult }> {
    return http.post('/search', req).then((r) => r.data)
  },
  health(): Promise<{ provider: string; healthy: boolean }> {
    return http.get('/search/health').then((r) => r.data)
  }
}

// ===== 产品详情 (公开) =====
export const productApi = {
  getByOem(oem: string): Promise<ProductDetail> {
    return http.get(`/products/${encodeURIComponent(oem)}`).then((r) => r.data)
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
  }
}

