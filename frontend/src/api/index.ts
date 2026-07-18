// Day 9: API 客户端 (按业务域拆分)
import { http } from '@/utils/http'
import { AxiosError } from 'axios'
import { captureException } from '@/utils/errorMonitor'
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
  EtlReasonCodeAggregate,
  ReindexResult,
  LoginResponse,
  AuthUser,
  UserListResp,
  UserCreateRequest,
  UserUpdateRequest,
  LoginAuditResp,
  AlertHistoryResp,
  AlertHistoryDetail,
  AlertStats,
  AlertRuleItem,
  AlertTestRequest,
  AlertTestResult
} from './types'

// ===== JWT 鉴权 API (commit aff3ac3 后端 JWT 体系) =====
//   login:        POST   /api/auth/login              { username, password }
//   refresh:      POST   /api/auth/refresh            { refreshToken }
//   logout:       POST   /api/auth/logout             { refreshToken }   (需 Authorization)
//   me:           GET    /api/auth/me                                                  (需 Authorization)
//   changePassword: POST /api/auth/change-password    { oldPassword, newPassword }   (需 Authorization)
export const authApi = {
  login(username: string, password: string): Promise<LoginResponse> {
    return http.post('/auth/login', { username, password }).then((r) => r.data)
  },
  refresh(refreshToken: string): Promise<LoginResponse> {
    return http.post('/auth/refresh', { refreshToken }).then((r) => r.data)
  },
  logout(refreshToken: string): Promise<void> {
    return http.post('/auth/logout', { refreshToken }).then((r) => r.data)
  },
  me(): Promise<AuthUser> {
    return http.get('/auth/me').then((r) => r.data)
  },
  changePassword(oldPassword: string, newPassword: string): Promise<void> {
    return http.post('/auth/change-password', { oldPassword, newPassword }).then((r) => r.data)
  }
}

// ===== 后台用户管理 API (admin 角色) =====
//   list:           GET    /api/admin/users?page=&pageSize=
//   create:         POST   /api/admin/users                  { username, password, role, email?, fullName? }
//   getById:        GET    /api/admin/users/{id}
//   update:         PATCH  /api/admin/users/{id}             { role?, email?, fullName?, isActive? }
//   remove:         DELETE /api/admin/users/{id}             (软删除)
//   resetPassword:  POST   /api/admin/users/{id}/reset-password  { newPassword }
//   auditLogin:     GET    /api/admin/audit/login?page=&pageSize=
export const usersApi = {
  list(page = 1, pageSize = 20): Promise<UserListResp> {
    return http.get('/admin/users', { params: { page, pageSize } }).then((r) => r.data)
  },
  create(data: UserCreateRequest): Promise<AuthUser> {
    return http.post('/admin/users', data).then((r) => r.data)
  },
  getById(id: number): Promise<AuthUser> {
    return http.get(`/admin/users/${id}`).then((r) => r.data)
  },
  update(id: number, data: UserUpdateRequest): Promise<AuthUser> {
    return http.patch(`/admin/users/${id}`, data).then((r) => r.data)
  },
  remove(id: number): Promise<void> {
    return http.delete(`/admin/users/${id}`).then((r) => r.data)
  },
  resetPassword(id: number, newPassword: string): Promise<void> {
    return http.post(`/admin/users/${id}/reset-password`, { newPassword }).then((r) => r.data)
  },
  auditLogin(page = 1, pageSize = 20): Promise<LoginAuditResp> {
    return http.get('/admin/audit/login', { params: { page, pageSize } }).then((r) => r.data)
  }
}

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
  // P2-8.1: 增加 signal 透传, 支持调用方取消请求 (快速切换容差时取消上一次未完成请求)
  search(req: SearchRequest, config?: { signal?: AbortSignal }): Promise<{ provider: string; result: SearchResult }> {
    return http.post('/search', req, { signal: config?.signal }).then((r) => r.data)
  },
  health(): Promise<{ provider: string; healthy: boolean }> {
    return http.get('/search/health').then((r) => r.data)
  },
  // P3.2 (Task 10): 批量 OEM 查询 (Excel 多行粘贴)
  //   POST /public/search/batch-oem { oems: [...] }
  //   返: { total, hits, miss, results: [{ oem, hit, productId, oemBrand, productName1, oem2 }] }
  batchOem(req: import('./types').BatchOemRequest): Promise<import('./types').BatchOemResponse> {
    return http.post('/public/search/batch-oem', req).then((r) => r.data)
  },

  // V24-F34 (spec Task 4.8.2/4.5.27): 聚合搜索 API (POST /public/search/aggregate)
  //   WHY: spec Task 4.8.2 要求 searchApi.aggregate 对接聚合 API
  //        之前 aggregate 挂在 publicSearchApi 上, 与 spec 不符, 此处补充 searchApi.aggregate
  //        publicSearchApi.aggregate 保留 (向后兼容, 后续版本可移除)
  aggregate(
    req: import('./types').AggregateSearchRequest,
    config?: { signal?: AbortSignal }
  ): Promise<import('./types').AggregateSearchResponse> {
    return http.post('/public/search/aggregate', req, { signal: config?.signal }).then((r) => r.data)
  }
}

/**
 * V24-F34 (spec F5-8/Task 4.5.27): searchWithFallback — 聚合搜索 API 404 降级到旧 API
 *
 * 触发条件 (v7 最终版, 三重判断):
 *   1. HTTP 状态码 === 404 (聚合 API 端点不存在, 如 nginx 路由漏配)
 *   2. console.error + captureException Sentry 上报 (避免掩盖配置错误)
 *   3. import.meta.env.VITE_ENABLE_LEGACY_FALLBACK === 'true' 才真正 fallback
 *      - dev (true): 降级到 searchApi.search, 便于本地开发
 *      - prod (false): 直接抛错 "聚合搜索 API 不可用,请联系管理员"
 *
 * 非 404 错误 (5xx / network error) 一律 throw err, 不降级
 *
 * @param req 聚合搜索请求
 * @param signal AbortSignal 用于取消请求
 * @returns AggregateSearchResponse 或降级后的兼容响应
 */
export async function searchWithFallback(
  req: import('./types').AggregateSearchRequest,
  signal?: AbortSignal
): Promise<import('./types').AggregateSearchResponse> {
  try {
    return await searchApi.aggregate(req, { signal })
  } catch (err) {
    // F5-8: 仅 HTTP 404 触发 fallback 逻辑 (5xx / network error 不降级)
    if (err instanceof AxiosError && err.response?.status === 404) {
      // F5-8: 404 时记录 error 级别日志 + Sentry 上报
      //   WHY: 404 可能是 API 路径配置错误 (nginx 路由漏配), 降级会掩盖问题
      console.error('[searchWithFallback] 聚合搜索 API 返回 404,可能 API 路径配置错误', {
        url: err.config?.url,
        method: err.config?.method,
      })
      captureException(err, {
        tags: { component: 'searchWithFallback' },
        extra: { url: err.config?.url, status: 404 },
      })

      // F5-8: 仅在明确降级模式 (配置开关) 下才 fallback, 默认不 fallback 直接抛错
      if (import.meta.env.VITE_ENABLE_LEGACY_FALLBACK !== 'true') {
        throw new Error('聚合搜索 API 不可用,请联系管理员')
      }

      // 降级到旧 API (searchApi.search, POST /search)
      //   WHY: 旧 API 仅返回基础搜索结果, 无聚合 oemList/machineList 嵌套
      //        兼容期返回空 oemList/machineList, 前端按基础字段渲染
      console.warn('[searchWithFallback] 降级到旧 API (searchApi.search)')
      const legacyResp = await searchApi.search(
        {
          q: req.q,
          page: req.page ?? 1,
          pageSize: req.pageSize ?? 20,
          tolerance: req.tolerance,
          includeDiscontinued: req.includeDiscontinued ?? false,
          type: req.type
        } as SearchRequest,
        { signal }
      )
      // 将旧 SearchResult 适配为 AggregateSearchResponse 形状 (空 oemList/machineList)
      return {
        total: legacyResp.result.total,
        page: req.page ?? 1,
        pageSize: req.pageSize ?? 20,
        totalPages: Math.ceil(legacyResp.result.total / (req.pageSize ?? 20)),
        processingTimeMs: legacyResp.result.elapsedMs,
        provider: legacyResp.provider,
        hits: (legacyResp.result.items || []).map((item) => ({
          mr1: '',  // 旧 API 无 mr1 字段, 留空
          type: item.type || '',
          isPublished: !item.isDiscontinued,
          isDiscontinued: item.isDiscontinued,
          oemList: [],
          machineList: []
        }))
      } as import('./types').AggregateSearchResponse
    }
    // 非 404 错误 (5xx / network error / AbortError) 直接抛出
    throw err
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
  },
  // V2 Task 2.3.5: 同 MR.1 其他 OEM 3 列表 (详情页推荐区块, 后端已排序)
  //   GET /api/public/products/{mr1}/sibling-oem3
  //   返回排序后列表 (brand_sort_order → sort_order), 前端不再二次排序
  siblingOem3(mr1: string): Promise<{ total: number; items: import('./types').SiblingOem3Item[] }> {
    return http.get(`/public/products/${encodeURIComponent(mr1)}/sibling-oem3`).then((r) => r.data)
  }
}

// ===== P0 (Day 14): 公开产品对比 (游客无需登录) =====
//   GET /api/public/compare?ids=1,2,3,4,5,6 (后端 AllowAnonymous, 排除下架产品)
//   用途: 产品详情页"加入对比" 按钮跳转目标; 也可作为公开 URL 分享
//   限位: 最多 6 个产品 (后端校验, 超限 400)
export const publicCompareApi = {
  compare(ids: number[]): Promise<{ count: number; items: ProductDetail[] }> {
    if (ids.length === 0) {
      return Promise.resolve({ count: 0, items: [] })
    }
    return http.get('/public/compare', { params: { ids: ids.join(',') } }).then((r) => r.data)
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
  },

  // P-Demo: 8 字段 typeahead 候选项 (输入 2 字符起返回 distinct 候选, 最多 20 条)
  //   field ∈ oem-brand | oem-no2 | oem-no3 | machine-brand | machine-model | model-name | engine-brand | engine-type
  //   支持 AbortSignal 取消上一次请求 (快速输入时只保留最后一次)
  typeahead(field: string, q: string, limit = 20, signal?: AbortSignal): Promise<{ count: number; items: string[] }> {
    return http.get(`/public/typeahead/${field}`, { params: { q, limit }, signal })
      .then((r) => r.data)
      .catch((err: any) => {
        // AbortError 静默 (用户快速输入时正常取消)
        if (err?.name === 'CanceledError' || err?.code === 'ERR_CANCELED') return { count: 0, items: [] }
        throw err
      })
  },

  // P-Demo: 公开搜索页进入时展示的"最新产品明细表"
  //   GET /api/public/featured?limit=20
  //   返: { total, items: PublicSearchHit[] } (与 eightField.items 形状一致)
  //   后端: OrderByDescending(Id) Take(limit), 仅排除 IsDiscontinued=true
  featured(limit = 20): Promise<{ total: number; items: import('./types').PublicSearchHit[] }> {
    return http.get('/public/featured', { params: { limit } }).then((r) => r.data)
  },

  // V2 Task 1.3.6: 聚合搜索 (POST /api/public/search/aggregate)
  //   文档级返回: mr1 + oemList 嵌套数组 + _formatted 高亮 + _rankingScore
  //   支持 AbortSignal: 500ms 防抖 + 取消前序请求
  //   provider 字段: "meilisearch" / "postgres" (Meili 离线时降级)
  aggregate(
    req: import('./types').AggregateSearchRequest,
    config?: { signal?: AbortSignal }
  ): Promise<import('./types').AggregateSearchResponse> {
    return http.post('/public/search/aggregate', req, { signal: config?.signal }).then((r) => r.data)
  }
}

// ===== 后台产品管理 (需 token) =====
export const adminProductApi = {
  // P2-8.1: 增加 signal 透传, 支持调用方取消请求 (AdminProductsView 列表快速翻页/筛选切换时取消上一次)
  search(req: AdminSearchRequest, config?: { signal?: AbortSignal }): Promise<PageResp<ProductListItem>> {
    return http.get('/admin/products/search', { params: req, signal: config?.signal }).then((r) => r.data)
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

// ===== V2 Task 2.2.7: OEM 3 排序管理 API =====
export const adminXrefApi = {
  // GET /api/admin/xrefs/reorder/brands — 品牌 + sortOrder + oem3Count
  listBrands(): Promise<{ total: number; items: import('./types').XrefBrandItem[] }> {
    return http.get('/admin/xrefs/reorder/brands').then((r) => r.data)
  },
  // GET /api/admin/xrefs/reorder?oemBrand=BOSCH — 某 Brand 下 OEM 3 列表 (含 rowVersion)
  listByBrand(oemBrand: string): Promise<{ total: number; items: import('./types').XrefOem3Item[] }> {
    return http.get('/admin/xrefs/reorder', { params: { oemBrand } }).then((r) => r.data)
  },
  // POST /api/admin/xrefs/reorder — 批量更新 sort_order (含 xmin 乐观锁, 冲突返 409)
  reorder(req: import('./types').XrefReorderRequest): Promise<{ updated: number }> {
    return http.post('/admin/xrefs/reorder', req).then((r) => r.data)
  }
}

// ===== 图片管理 (V2 Task 3.3.3: 主图/详情图分层) =====
//   改进 3.1: uploadPrimary/uploadDetail 新增 onUploadProgress 参数, 支持 UI 进度条
//     类型 AxiosProgressEvent = { loaded: number; total?: number; progress?: number; bytes: number; rate?: number; estimated?: number; upload?: boolean; download?: boolean; event?: ProgressEvent }
export const imageApi = {
  // V2: 按 mr1 列出图片 (含 primary + detail, 后端已按 imageRole + slot 排序)
  list(mr1: string): Promise<ProductDetail['images']> {
    return http.get(`/admin/products/${encodeURIComponent(mr1)}/images`).then((r) => r.data)
  },
  // V2 Task 3.3.3: 上传主图 (slot=1, 按 OEM 3 命名)
  //   改进 3.1: onUploadProgress 回调由调用方传入, 用于 UI 进度条更新
  uploadPrimary(
    mr1: string,
    oemNo3: string,
    file: File,
    onProgress?: (progress: number) => void
  ): Promise<import('./types').ProductImageV2> {
    const fd = new FormData()
    fd.append('file', file)
    return http
      .post(`/admin/products/${encodeURIComponent(mr1)}/images/primary`, fd, {
        params: { oemNo3 },
        headers: { 'Content-Type': 'multipart/form-data' },
        onUploadProgress: onProgress
          ? (e: any) => {
              // e.progress 已是 0-1 之间小数 (axios 5+), 兼容老版本用 loaded/total 计算
              const pct = e.progress != null ? e.progress * 100 : (e.total ? (e.loaded / e.total) * 100 : 0)
              onProgress(Math.min(100, Math.max(0, Math.round(pct))))
            }
          : undefined
      })
      .then((r) => r.data)
  },
  // V2 Task 3.3.3: 上传详情图 (slot 2-6, 按 MR.1 命名)
  //   改进 3.1: onUploadProgress 回调由调用方传入
  uploadDetail(
    mr1: string,
    slot: number,
    file: File,
    onProgress?: (progress: number) => void
  ): Promise<import('./types').ProductImageV2> {
    const fd = new FormData()
    fd.append('file', file)
    return http
      .post(`/admin/products/${encodeURIComponent(mr1)}/images/detail`, fd, {
        params: { slot },
        headers: { 'Content-Type': 'multipart/form-data' },
        onUploadProgress: onProgress
          ? (e: any) => {
              const pct = e.progress != null ? e.progress * 100 : (e.total ? (e.loaded / e.total) * 100 : 0)
              onProgress(Math.min(100, Math.max(0, Math.round(pct))))
            }
          : undefined
      })
      .then((r) => r.data)
  },
  // V2: 删除图片 (按 mr1 + imageRole + slot)
  remove(mr1: string, imageRole: 'primary' | 'detail', slot: number): Promise<void> {
    return http
      .delete(`/admin/products/${encodeURIComponent(mr1)}/images/${imageRole}/${slot}`)
      .then((r) => r.data)
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
  // V2 Task V17-3.1: 全量重建 Meilisearch 索引
  //   后端 ReindexAllAsync 内部 AcquireActiveCts 防止与 ETL 并发,冲突返回 409
  reindexAll(): Promise<ReindexResult> {
    return http.post('/admin/etl/reindex-all', {}).then((r) => r.data)
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

// ===== P2-1 告警系统 API (admin 角色) =====
//   history:     GET    /api/admin/alerts/history?type=&severity=&status=&limit=&offset=
//   detail:      GET    /api/admin/alerts/history/{id}
//   stats:       GET    /api/admin/alerts/stats               (7 日 KPI)
//   rules:       GET    /api/admin/alerts/rules
//   updateRule:  PUT    /api/admin/alerts/rules/{id}         { enabled?, severity?, channels?, recipients? }
//   test:        POST   /api/admin/alerts/test               { type?, severity?, title?, markdown? }
export const alertsApi = {
  history(opts: { type?: string; severity?: string; status?: string; limit?: number; offset?: number } = {}): Promise<AlertHistoryResp> {
    return http.get('/admin/alerts/history', { params: opts }).then((r) => r.data)
  },
  detail(id: number): Promise<AlertHistoryDetail> {
    return http.get(`/admin/alerts/history/${id}`).then((r) => r.data)
  },
  stats(): Promise<AlertStats> {
    return http.get('/admin/alerts/stats').then((r) => r.data)
  },
  rules(): Promise<AlertRuleItem[]> {
    return http.get('/admin/alerts/rules').then((r) => r.data)
  },
  updateRule(id: number, body: { enabled?: boolean; severity?: string; channels?: string[]; recipients?: string[]; description?: string }): Promise<{ success: boolean }> {
    return http.put(`/admin/alerts/rules/${id}`, body).then((r) => r.data)
  },
  test(body: AlertTestRequest = {}): Promise<AlertTestResult> {
    return http.post('/admin/alerts/test', body).then((r) => r.data)
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

