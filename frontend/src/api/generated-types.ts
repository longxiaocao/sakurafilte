// ============================================
// 自动生成 — 请勿手工修改
// 生成方式: python _gen_types.py
// 数据源: /swagger/v1/swagger.json
// 用途: 与手工 types.ts 对照, 发现字段漂移
// ============================================

export interface BatchOemRequest {
  oems?: string[] | null
}

export interface CancelRequest {
  reason?: string | null
  reasonCode?: string | null
}

export interface CompareRequest {
  ids?: number[] | null
}

export interface EngineCreateRequest {
  engineBrand?: string | null
  engineType?: string | null
  sortOrder?: number | null
}

export interface EngineReorderItem {
  id?: number | null
  sortOrder?: number | null
}

export interface EngineReorderRequest {
  items?: EngineReorderItem[] | null
}

export interface EngineUpdateRequest {
  engineBrand?: string | null
  engineType?: string | null
  sortOrder?: number | null
}

export interface EtlTriggerRequest {
  jsonlPath?: string | null
  mode?: string | null
  dryRun?: boolean | null
  entityType?: string | null
  cascade?: boolean | null
}

export interface ImportRequest {
  jsonlPath?: string | null
  mode?: string | null
  entityType?: string | null
  cascade?: boolean | null
}

export interface MachineAppInput {
  machineBrand?: string | null
  machineModel?: string | null
  modelName?: string | null
  engineBrand?: string | null
  engineType?: string | null
  engineEnergy?: string | null
  productionDateStart?: string | null
  productionDateEnd?: string | null
  power?: string | null
  serialNumberFrom?: string | null
  serialNumberTo?: string | null
  carBodyType?: string | null
  series?: string | null
  co2EmissionStandard?: string | null
  transmissionType?: string | null
  engineDisplacement?: string | null
  numberOfCylinders?: number | null
  gvwr?: string | null
  tonnage?: string | null
  geographicArea?: string | null
  chassisType?: string | null
  engineModel?: string | null
  cabinType?: string | null
  capacity?: string | null
  engineSerialNumber?: string | null
}

export interface MachineCreateRequest {
  machineBrand?: string | null
  machineModel?: string | null
  machineName?: string | null
  sortOrder?: number | null
  machineCategory?: string | null
}

export interface MachineReorderItem {
  id?: number | null
  sortOrder?: number | null
}

export interface MachineReorderRequest {
  items?: MachineReorderItem[] | null
}

export interface MachineUpdateRequest {
  machineBrand?: string | null
  machineModel?: string | null
  machineName?: string | null
  sortOrder?: number | null
  machineCategory?: string | null
}

export interface MediaCreateRequest {
  mediaName?: string | null
  mediaModel?: string | null
  sortOrder?: number | null
}

export interface MediaReorderItem {
  id?: number | null
  sortOrder?: number | null
}

export interface MediaReorderRequest {
  items?: MediaReorderItem[] | null
}

export interface MediaUpdateRequest {
  mediaName?: string | null
  mediaModel?: string | null
  sortOrder?: number | null
}

export interface OemBrandCreateRequest {
  brand?: string | null
  sortOrder?: number | null
}

export interface OemBrandReorderItem {
  id?: number | null
  sortOrder?: number | null
}

export interface OemBrandReorderRequest {
  items?: OemBrandReorderItem[] | null
}

export interface OemBrandUpdateRequest {
  brand?: string | null
  sortOrder?: number | null
}

export interface OemNo3CreateRequest {
  oemNo3?: string | null
  sortOrder?: number | null
}

export interface OemNo3ReorderItem {
  id?: number | null
  sortOrder?: number | null
}

export interface OemNo3ReorderRequest {
  items?: OemNo3ReorderItem[] | null
}

export interface OemNo3UpdateRequest {
  oemNo3?: string | null
  sortOrder?: number | null
}

export interface ProductFormDto {
  oem2?: string | null
  productName1?: string | null
  productName2?: string | null
  type?: string | null
  mr1?: string | null
  isPublished?: boolean | null
  remark?: string | null
  d1Mm?: number | null
  d2Mm?: number | null
  d3Mm?: number | null
  d4Mm?: number | null
  h1Mm?: number | null
  h2Mm?: number | null
  h3Mm?: number | null
  h4Mm?: number | null
  d7Thread?: string | null
  d8Thread?: string | null
  noCheckValves?: number | null
  noBypassValves?: number | null
  media?: string | null
  mediaModel?: string | null
  bypassValveLr?: number | null
  bypassValveHr?: number | null
  efficiency1?: string | null
  efficiency2?: string | null
  bypassPressure?: number | null
  collapsePressureBar?: number | null
  sealingMaterial?: string | null
  tempRange?: string | null
  qtyPerCarton?: number | null
  weightKgs?: number | null
  cartonLengthMm?: number | null
  cartonWidthMm?: number | null
  cartonHeightMm?: number | null
  masterBoxQty?: number | null
  masterBoxWeightKgs?: number | null
  masterBoxLengthMm?: number | null
  masterBoxWidthMm?: number | null
  masterBoxHeightMm?: number | null
  crossReferences?: XrefInput[] | null
  machineApplications?: MachineAppInput[] | null
}

export interface ProductName1CreateRequest {
  productName1?: string | null
  sortOrder?: number | null
}

export interface ProductName1ReorderItem {
  id?: number | null
  sortOrder?: number | null
}

export interface ProductName1ReorderRequest {
  items?: ProductName1ReorderItem[] | null
}

export interface ProductName1UpdateRequest {
  productName1?: string | null
  sortOrder?: number | null
}

export interface ProductName2CreateRequest {
  productName2?: string | null
  sortOrder?: number | null
}

export interface ProductName2ReorderItem {
  id?: number | null
  sortOrder?: number | null
}

export interface ProductName2ReorderRequest {
  items?: ProductName2ReorderItem[] | null
}

export interface ProductName2UpdateRequest {
  productName2?: string | null
  sortOrder?: number | null
}

export interface SearchRequest {
  q?: string | null
  type?: string | null
  d1?: number | null
  d2?: number | null
  d3?: number | null
  h1?: number | null
  h2?: number | null
  h3?: number | null
  d7?: number | null
  d8?: number | null
  tolerance?: number | null
  includeDiscontinued?: boolean | null
  page?: number | null
  pageSize?: number | null
}

export interface TypeCreateRequest {
  type?: string | null
  sortOrder?: number | null
}

export interface TypeReorderItem {
  id?: number | null
  sortOrder?: number | null
}

export interface TypeReorderRequest {
  items?: TypeReorderItem[] | null
}

export interface TypeUpdateRequest {
  type?: string | null
  sortOrder?: number | null
}

export interface XrefInput {
  productName1?: string | null
  oemBrand?: string | null
  oemNo3?: string | null
}
// 共生成 41 个 interface (跳过 0 个框架内置 schema)
