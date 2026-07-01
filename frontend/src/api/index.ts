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
  EtlActiveTaskInfo
} from './types'

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


  progress(): Promise<EtlActiveTaskInfo> {
    return http.get('/admin/etl/progress').then((r) => r.data)
  },
  legacyStatus(): Promise<EtlProgress> {
    return http.get('/etl/status').then((r) => r.data)
  }
}

