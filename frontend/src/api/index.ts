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
  history(id: number, limit = 50): Promise<{ total: number; items: ProductHistoryItem[] }> {
    return http.get(`/admin/products/${id}/history`, { params: { limit } }).then((r) => r.data)
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
  cancel(): Promise<{ cancelled: boolean; reason?: string }> {
    return http.delete('/admin/etl/task').then((r) => r.data)
  },
  progress(): Promise<EtlActiveTaskInfo> {
    return http.get('/admin/etl/progress').then((r) => r.data)
  },
  legacyStatus(): Promise<EtlProgress> {
    return http.get('/etl/status').then((r) => r.data)
  }
}
