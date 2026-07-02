// Day 10+ P5.2 字段说明文案 (静态常量, 避免 dict_field_help 表的数据库 migration 复杂度)
//   - key 命名与 ProductDetail 字段名一致 (camelCase)
//   - 内容来自 新思路.xlsx "对比界面" / "后台新增产品格式" 规格
//   - 后台产品表单 (? 图标) 和 帮助页都引用这份文案, 单源真相
export interface FieldHelp {
  label: string
  unit?: string
  description: string
  example?: string
}

export const FIELD_HELP: Record<string, FieldHelp> = {
  // === 分区 1: 基础 ===
  productName1: { label: '产品名 1', description: '产品主名称, 通常为英文 (例: Oil Filter, Fuel Filter)', example: 'Oil Filter' },
  productName2: { label: '产品名 2', description: '产品副名称或型号后缀', example: 'OF100' },
  type:        { label: '类型 (Type)', description: '产品分类: oil (机油) / fuel (燃油) / air (空气) / cabin (空调) / others (其他)' },
  mr1:         { label: 'MR.1', description: '内部备注编号 1, 用于厂内追溯' },
  oem2:        { label: 'OEM 2', description: '对外发布的 OEM 主编号 (前台公开页用此字段)', example: '11427622448' },
  isPublished: { label: '上架', description: '上架 = 前台可访问; 下架 = 仅后台可见' },
  remark:      { label: '备注', description: '内部备注, 前台不显示' },

  // === 分区 3: 尺寸 ===
  d1Mm: { label: 'D1', unit: 'mm', description: '外径 1 (Outer Diameter 1)', example: '76' },
  d2Mm: { label: 'D2', unit: 'mm', description: '外径 2', example: '72' },
  d3Mm: { label: 'D3', unit: 'mm', description: '外径 3', example: '62' },
  d4Mm: { label: 'D4', unit: 'mm', description: '外径 4', example: '55' },
  h1Mm: { label: 'H1', unit: 'mm', description: '高度 1 (Height 1, 通常为总高)', example: '100' },
  h2Mm: { label: 'H2', unit: 'mm', description: '高度 2' },
  h3Mm: { label: 'H3', unit: 'mm', description: '高度 3' },
  h4Mm: { label: 'H4', unit: 'mm', description: '高度 4' },
  d7Thread: { label: 'D7 螺纹', description: '螺纹规格 1', example: 'M20x1.5' },
  d8Thread: { label: 'D8 螺纹', description: '螺纹规格 2', example: 'M12x1.75' },
  noCheckValves:  { label: '单向阀数', description: '止回阀数量, 通常 0-2' },
  noBypassValves: { label: '旁通阀数', description: '旁通阀数量, 通常 0-1' },

  // === 分区 5: 性能 ===
  media:               { label: '介质', description: '滤材名称 (例: Cellulose, Synthetic)', example: 'Cellulose' },
  mediaModel:          { label: '介质型号', description: '滤材厂商型号' },
  bypassValveLr:       { label: '旁通 LR', unit: 'L/min', description: '旁通阀开启流量 (低-高范围)' },
  bypassValveHr:       { label: '旁通 HR', unit: 'L/min', description: '旁通阀开启流量 (高范围)' },
  efficiency1:         { label: '效率 1', description: '过滤效率 1 (例: 99% @ 25μm)' },
  efficiency2:         { label: '效率 2', description: '过滤效率 2' },
  bypassPressure:      { label: '旁通压力', unit: 'bar', description: '旁通阀开启压力' },
  collapsePressureBar: { label: '耐压',   unit: 'bar', description: '破裂压力 (Collapse Pressure), 滤芯结构能承受的最大压差' },
  sealingMaterial:     { label: '密封材料', description: '密封圈材质 (例: NBR, Viton, Silicone)' },
  tempRange:           { label: '温度范围', unit: '°C', description: '工作温度范围', example: '-30 ~ +120' },

  // === 分区 6: 包装 ===
  qtyPerCarton:     { label: '箱/件',   description: '每箱件数 (QTY/CTN)' },
  weightKgs:        { label: '重量',     unit: 'kg', description: '单件毛重' },
  cartonLengthMm:   { label: '箱长',     unit: 'mm', description: '外箱长度 (L)' },
  cartonWidthMm:    { label: '箱宽',     unit: 'mm', description: '外箱宽度 (W)' },
  cartonHeightMm:   { label: '箱高',     unit: 'mm', description: '外箱高度 (H)' },
  volumePerCartonM3:{ label: '箱体积',   unit: 'm³', description: '外箱体积, 由 L×W×H/1e9 自动计算 (不可手填)' },

  // === 分区 6+: 外箱 (母箱) ===
  masterBoxQty:        { label: '母箱/件', description: '每个外箱 (母箱) 装入的内箱数' },
  masterBoxWeightKgs:  { label: '母箱重',  unit: 'kg', description: '母箱毛重' },
  masterBoxLengthMm:   { label: '母箱长',  unit: 'mm', description: '母箱长度' },
  masterBoxWidthMm:    { label: '母箱宽',  unit: 'mm', description: '母箱宽度' },
  masterBoxHeightMm:   { label: '母箱高',  unit: 'mm', description: '母箱高度' },

  // === 分区 2 (xref) ===
  oemBrand: { label: 'OEM 品牌', description: '替代品牌的厂家名 (来自 xref_oem_brand 字典)' },
  oemNo3:   { label: 'OEM 3 编号', description: '替代品牌的 OEM 编号' },

  // === 分区 7 (machine applications) ===
  machineBrand:  { label: '机器品牌', description: '适用机器品牌 (例: Caterpillar, Komatsu)' },
  machineModel:  { label: '机器型号', description: '机器型号 (例: 320D, PC200-8)' },
  machineName:   { label: '机器名称', description: '机器通用名 (例: Excavator, Tractor)' },
  engineBrand:   { label: '发动机品牌', description: '配套发动机品牌' },
  engineType:    { label: '发动机型号', description: '配套发动机型号' }
}

// 帮助函数: 取字段帮助, 缺省返回 fallback
export function getFieldHelp(key: string): FieldHelp | null {
  return FIELD_HELP[key] ?? null
}
