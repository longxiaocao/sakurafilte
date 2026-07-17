// ===== Day 9: API 类型契约 (OpenAPI 风格) =====
//   这些 DTO 与后端 SakuraFilter.Core DTOs 一一对应
//   后续可用 openapi-typescript 自动生成, 暂手写

// ===== JWT 鉴权 (commit aff3ac3 后端 JWT 体系) =====
export interface LoginRequest {
  username: string
  password: string
}

// 角色字面量与后端 UserRole enum 一致
export type UserRole = 'admin' | 'operator' | 'viewer'

export interface AuthUser {
  id: number
  username: string
  role: UserRole
  email?: string
  fullName?: string
}

export interface LoginResponse {
  accessToken: string
  refreshToken: string
  expiresIn: number  // 秒
  user: AuthUser
}

// 后台用户管理列表项 (admin 视角)
export interface UserListItem extends AuthUser {
  isActive: boolean
  lastLoginAt?: string
  createdAt: string
}

export interface UserListResp {
  total: number
  page: number
  pageSize: number
  items: UserListItem[]
}

export interface UserCreateRequest {
  username: string
  password: string
  role: UserRole
  email?: string
  fullName?: string
}

export interface UserUpdateRequest {
  role?: UserRole
  email?: string
  fullName?: string
  isActive?: boolean
}

export interface ResetPasswordRequest {
  newPassword: string
}

export interface ChangePasswordRequest {
  oldPassword: string
  newPassword: string
}

export interface RefreshRequest {
  refreshToken: string
}

export interface LogoutRequest {
  refreshToken: string
}

// 登录审计日志 (login_audit_logs 表)
export interface LoginAuditLog {
  id: number
  userId?: number
  username: string
  loginAt: string
  ip?: string
  userAgent?: string
  success: boolean
  failureReason?: string
}

export interface LoginAuditResp {
  total: number
  page: number
  pageSize: number
  items: LoginAuditLog[]
}

// ===== 通用 =====
export interface PageResp<T> {
  total: number
  countMode?: string
  countModeUsed?: string
  pagingMode?: string
  hasMore?: boolean
  nextCursor?: string | null
  page?: number
  pageSize?: number
  items: T[]
}

// ===== 搜索 =====
// Day 9.2: 字段名改 PascalCase 匹配后端 (C# record 默认 System.Text.Json PascalCase 序列化)
//   之前用 snake_case,导致 result.hits undefined + 表格列 prop 找不到值
//   错误堆栈: SearchView.vue Proxy._sfc_render TypeError: Cannot read properties of undefined (reading 'length')
export interface SearchRequest {
  q?: string
  type?: string
  d1?: number
  d2?: number
  d3?: number
  h1?: number
  h2?: number
  h3?: number
  d7?: number
  d8?: number
  tolerance?: number
  includeDiscontinued?: boolean
  page?: number
  pageSize?: number
}

export interface SearchResult {
  total: number
  page: number
  pageSize: number
  totalPages: number
  elapsedMs: number
  // Day 9.2: 后端字段是 Items (PascalCase), 不是 hits
  items: SearchHit[]
  // 兼容老字段 (万一旧前端代码混用)
  hits?: SearchHit[]
}

export interface SearchHit {
  id: number
  oemNoDisplay: string
  remark?: string
  type: string
  d1Mm?: number
  d2Mm?: number
  h1Mm?: number
  imageKey?: string
  isDiscontinued: boolean
  // Day 9.2: 兼容老 snake_case 字段 (前几版 SearchView 用了这些列名)
  oem_no_display?: string
  product_name_1?: string
  mr_1?: string
  d1_mm?: number
  d2_mm?: number
  d3_mm?: number
  h1_mm?: number
  d7_thread?: string
  d8_thread?: string
  media?: string
  media_model?: string
  updated_at?: string
}

// ===== 产品 =====
export interface ProductListItem {
  id: number
  oemNoDisplay: string
  mr1?: string
  oem2?: string
  type?: string
  d1Mm?: number
  d2Mm?: number
  d3Mm?: number
  d4Mm?: number
  h1Mm?: number
  h2Mm?: number
  h3Mm?: number
  h4Mm?: number
  d7Thread?: string
  d8Thread?: string
  media?: string
  mediaModel?: string
  remark?: string
  qtyPerCarton?: number
  weightKgs?: number
  cartonLengthMm?: number
  cartonWidthMm?: number
  cartonHeightMm?: number
  volumePerCartonM3?: number
  isPublished?: boolean
  isDiscontinued?: boolean
  updatedAt?: string
}

export interface ProductDetail {
  id: number
  oemNoDisplay: string
  oem2?: string
  mr1?: string
  productName1?: string
  productName2?: string
  type?: string
  isPublished: boolean
  remark?: string
  // E2E BD.3 修复 v2: 乐观锁并发令牌 (PG xmin), PUT 时带回实现跨请求冲突检测
  rowVersion: number
  d1Mm?: number
  d2Mm?: number
  d3Mm?: number
  d4Mm?: number
  h1Mm?: number
  h2Mm?: number
  h3Mm?: number
  h4Mm?: number
  d7Thread?: string
  d8Thread?: string
  noCheckValves?: number
  noBypassValves?: number
  media?: string
  mediaModel?: string
  bypassValveLr?: number
  bypassValveHr?: number
  efficiency1?: string
  efficiency2?: string
  bypassPressure?: number
  collapsePressureBar?: number
  sealingMaterial?: string
  tempRange?: string
  qtyPerCarton?: number
  weightKgs?: number
  cartonLengthMm?: number
  cartonWidthMm?: number
  cartonHeightMm?: number
  masterBoxQty?: number
  masterBoxWeightKgs?: number
  masterBoxLengthMm?: number
  masterBoxWidthMm?: number
  masterBoxHeightMm?: number
  volumePerCartonM3?: number
  isDiscontinued: boolean
  createdAt: string
  updatedAt: string
  crossReferences: XrefInfo[]
  machineApplications: MachineAppInfo[]
  images: ProductImageInfo[]
}

export interface XrefInfo {
  id: number
  productName1?: string
  oemBrand?: string
  oemNo3?: string
}

export interface MachineAppInfo {
  id: number
  machineBrand?: string
  machineModel?: string
  modelName?: string
  engineBrand?: string
  engineType?: string
  engineEnergy?: string
  productionDateStart?: string
  productionDateEnd?: string
  power?: string
  serialNumberFrom?: string
  serialNumberTo?: string
  carBodyType?: string
  series?: string
  co2EmissionStandard?: string
  transmissionType?: string
  engineDisplacement?: string
  numberOfCylinders?: number
  gvwr?: string
  tonnage?: string
  geographicArea?: string
  chassisType?: string
  engineModel?: string
  cabinType?: string
  capacity?: string
  engineSerialNumber?: string
}

export interface ProductImageInfo {
  slot: number
  imageKey: string
  imageUrl: string
  contentType: string
  sizeBytes: number
  width?: number
  height?: number
}

export interface ProductHistoryItem {
  id: number
  productId: number
  changeType: string
  changedBy?: string
  changedAt: string
  changedFields?: string
}

// Day 9.3: 历史分页响应 (含 total, 反映筛选后真实条数)
//   Day 9.4: 加 nextCursor, keyset 分页用 (前端存这个字符串翻下一页)
export interface ProductHistoryPage {
  total: number
  limit: number
  changeType?: string
  since?: string
  until?: string
  items: ProductHistoryItem[]
  nextCursor?: string | null
}

// ===== 高级搜索请求 (Day 8.2 DTO) =====
export interface AdminSearchRequest {
  page?: number
  pageSize?: number
  includeDiscontinued?: boolean
  isPublished?: boolean
  productName1?: string
  productName2?: string
  type?: string
  mr1?: string
  oem2?: string
  oemBrand?: string
  mediaName?: string
  mediaModel?: string
  sealingMaterial?: string
  efficiency1?: string
  oem2Batch?: string
  oem3Batch?: string
  d1Min?: number
  d1Max?: number
  d2Min?: number
  d2Max?: number
  d3Min?: number
  d3Max?: number
  d4Min?: number
  d4Max?: number
  h1Min?: number
  h1Max?: number
  h2Min?: number
  h2Max?: number
  h3Min?: number
  h3Max?: number
  h4Min?: number
  h4Max?: number
  d7Thread?: string
  d8Thread?: string
  sizeTolerance?: number
  machineBrand?: string
  machineModel?: string
  modelName?: string
  engineBrand?: string
  engineType?: string
  sortBy?: string
  sortDesc?: boolean
  countMode?: 'exact' | 'estimated' | 'none'
  pagingMode?: 'offset' | 'cursor'
  cursor?: string
}

// ===== ETL =====
// Day 11 Phase 1 BUG FIX: 补 entityType 字段 (之前 UI 有选择器但 API 类型缺失, 后端硬编码 products)
export interface EtlTriggerRequest {
  jsonlPath: string
  mode?: 'full-load' | 'insert-only' | 'upsert'
  entityType?: 'products' | 'xrefs' | 'apps'  // 不传默认 products
  cascade?: boolean  // 仅 products + full-load 生效, 默认 true
  dryRun?: boolean
}

export interface EtlProgress {
  status: string
  currentFile?: string
  read: number
  inserted: number
  updated: number
  skipped: number
  skippedMissingOem?: number
  skippedNullField?: number
  skippedDuplicate?: number
  errors: number
  indexed: number
  indexPending: number
  elapsedSec?: number
  startedAt?: string
  finishedAt?: string
  lastError?: string
  recentErrors?: { at: string; message: string }[]
}

export interface EtlActiveTaskInfo {
  inProgress: boolean
  activeTask?: {
    status: string
    currentFile?: string
    stage: string
    read: number
    inserted: number
    updated: number
    skipped: number
    errors: number
    indexed: number
    indexPending: number
    rowsProcessed: number
    rowsTotal: number | null
    progressPct: number | null
    startedAt?: string
    elapsedSec?: number
    lastError?: string
  }
}

// Day 9.1: dry-run 返回的样本行 (最多 5 行原始 JSON)
export interface EtlDryRunResult {
  dryRun: boolean
  file: string
  mode: string
  lines: number
  sizeBytes: number
  samples: string[]
}

// Day 9.8: ETL 历史日志条目 (来自 /api/admin/etl/history)
export interface EtlHistoryItem {
  id: number
  entityType: string
  mode: string
  status: string
  reasonCode?: string | null
  cancelReason?: string | null
  cancelledAt?: string | null
  readCount: number
  insertedCount: number
  updatedCount: number
  skippedCount: number
  skippedMissingOem: number
  skippedNullField: number
  skippedDuplicate: number
  errorCount: number
  indexedCount: number
  indexPendingCount: number
  lastError?: string | null
  startedAt: string
  finishedAt: string
  durationSec: number
}

// Day 9.8: reason_code 聚合数据 (来自 /api/admin/etl/history/aggregate)
export interface EtlReasonCodeBreakdown {
  code: string
  count: number
  pct: number
}

export interface EtlReasonCodeAggregate {
  total: number
  breakdown: EtlReasonCodeBreakdown[]
}

// ===== P2-1 告警系统 (来自 /api/admin/alerts/*) =====
export interface AlertHistoryItem {
  id: number
  type: string
  severity: string
  title: string
  channel: string
  status: string
  sentAt: string
  correlationId?: string
  error?: string | null
}

export interface AlertHistoryResp {
  total: number
  limit: number
  offset: number
  items: AlertHistoryItem[]
}

export interface AlertHistoryDetail {
  id: number
  type: string
  severity: string
  title: string
  channel: string
  status: string
  sentAt: string
  correlationId?: string
  content: string         // 完整 JSON
  recipients?: string     // JSON 数组
  response?: string | null
  error?: string | null
}

export interface AlertStats {
  total: number
  sent: number
  failed: number
  suppressed: number
  p0: number
  p1: number
  p2: number
  warn: number
  info: number
}

export interface AlertRuleItem {
  id: number
  type: string
  enabled: boolean
  severity: string
  channels: string         // JSON 数组
  conditions?: string      // JSON 对象
  recipients?: string      // JSON 数组
  description?: string
  createdAt: string
  updatedAt: string
}

export interface AlertTestRequest {
  type?: string
  severity?: string
  title?: string
  markdown?: string
}

export interface AlertTestResult {
  success: boolean
  disabled?: boolean
  noChannel?: boolean
  suppressed?: boolean
  correlationId?: string
  sentCount: number
  failedCount: number
  results: { channel: string; success: boolean; error?: string; response?: string }[]
}

// ===== P3.2 (Task 10): 批量 OEM 查询 (公开) =====
export interface BatchOemRequest {
  oems: string[]
}

export interface BatchOemResult {
  oem: string
  hit: boolean
  productId?: number | null
  oemBrand?: string | null
  productName1?: string | null
  oem2?: string | null
}

export interface BatchOemResponse {
  total: number
  hits: number
  miss: number
  results: BatchOemResult[]
}

// ===== P3.4 (Task 11.5): 公开搜索 8 字段多框 =====
export interface PublicEightRequest {
  oemBrand?: string
  oemNo2?: string
  oemNo3?: string
  machineBrand?: string
  machineModel?: string
  modelName?: string
  engineBrand?: string
  engineType?: string
  page?: number
  pageSize?: number
}

export interface PublicSearchHit {
  id: number
  oemNoDisplay: string
  oem2?: string | null
  productName1?: string | null
  type?: string | null
  d1Mm?: string | null
  h1Mm?: string | null
}

export interface PublicEightResponse {
  total: number
  page: number
  pageSize: number
  totalPages: number
  elapsedMs: number
  countMode: string  // 'exact' | 'estimated'
  items: PublicSearchHit[]
}

// ===== V2 Task 1.3: 聚合搜索 (POST /api/public/search/aggregate) =====
//   文档级返回: mr1 + oemList 嵌套数组 + _formatted 高亮 + _rankingScore
//   与后端 AggregateSearchDto.cs 一一对应 (PascalCase 序列化)
export interface AggregateSearchRequest {
  q?: string
  page?: number
  pageSize?: number
  tolerance?: number
  includeDiscontinued?: boolean
  machineCategory?: string  // agriculture/commercial/construction/industrial/others
  type?: string  // oil/fuel/air/cabin/others
  d1?: number
  d2?: number
  d3?: number
  h1?: number
  h2?: number
  h3?: number
}

export interface AggregateOemItem {
  oemBrand?: string | null
  oemNo3?: string | null
  oem2?: string | null
  sortOrder: number
  machineType?: string | null
  isPublished: boolean
  brandSortOrder?: number | null
}

export interface AggregateMachineItem {
  machineBrand?: string | null
  machineModel?: string | null
  machineCategory?: string | null
}

export interface AggregateSearchHit {
  mr1: string
  productName1?: string | null
  productName2?: string | null
  oem2?: string | null
  type: string
  remark?: string | null
  media?: string | null
  isPublished: boolean
  isDiscontinued: boolean
  oemList: AggregateOemItem[]
  machineList: AggregateMachineItem[]
  // _formatted 高亮字段 (后端已做 XSS 防御, 前端 sanitizeFormatted 双保险)
  formatted?: Record<string, unknown> | null
  rankingScore?: number | null
}

export interface AggregateSearchResponse {
  total: number
  page: number
  pageSize: number
  totalPages: number
  processingTimeMs: number
  provider: string  // "meilisearch" | "postgres"
  hits: AggregateSearchHit[]
}

// ===== Day 11 改进 1: 自动生成的 Request DTO (re-export) =====
// WHY: 手工 types.ts 历史遗漏 38 个 Request/Reorder/Input DTO,
//   导致前端调用 dictApi.machines.create() 等接口时参数无类型保护 (BUG B 根因).
//   现从 generated-types.ts re-export, 与后端 Swagger 契约保持自动同步.
//   重名的 interface (如 EtlTriggerRequest) 保留手工版 (含精确字面量类型,
//   如 mode: 'full-load' | 'insert-only' | 'upsert', 比生成的 string 更严格).
//   开发者新增 DTO 时, 优先在 generated-types.ts 检查是否已自动生成.
export type {
  CancelRequest,
  CompareRequest,
  EngineCreateRequest,
  EngineReorderItem,
  EngineReorderRequest,
  EngineUpdateRequest,
  ImportRequest,
  MachineAppInput,
  MachineCreateRequest,
  MachineReorderItem,
  MachineReorderRequest,
  MachineUpdateRequest,
  MediaCreateRequest,
  MediaReorderItem,
  MediaReorderRequest,
  MediaUpdateRequest,
  OemBrandCreateRequest,
  OemBrandReorderItem,
  OemBrandReorderRequest,
  OemBrandUpdateRequest,
  OemNo3CreateRequest,
  OemNo3ReorderItem,
  OemNo3ReorderRequest,
  OemNo3UpdateRequest,
  ProductFormDto,
  ProductName1CreateRequest,
  ProductName1ReorderItem,
  ProductName1ReorderRequest,
  ProductName1UpdateRequest,
  ProductName2CreateRequest,
  ProductName2ReorderItem,
  ProductName2ReorderRequest,
  ProductName2UpdateRequest,
  TypeCreateRequest,
  TypeReorderItem,
  TypeReorderRequest,
  TypeUpdateRequest,
  XrefInput
} from './generated-types'

