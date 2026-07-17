/**
 * 告警相关格式化工具 (P2-1)
 * - 集中管理 channel 名称 → 图标、状态 → el-tag 颜色 等映射
 * - 复用点: AdminAlertsView (历史表 + 详情 + 规则)、EtlAlertStatus (后续扩展)
 * - i18n 友好: 显示文本由调用方传入 t() 结果, 此处只返回样式元数据
 */

export type AlertChannel = 'dingtalk' | 'wechat' | 'webhook' | 'wechat-mp' | string

export interface ChannelMeta {
  icon: string
  label: string
  color: string
}

/**
 * 渠道 → 图标 + 标签 + 颜色
 * - 图标用 Unicode 表情 (无外部依赖, 适合无网络环境)
 * - 颜色用 Element Plus el-tag / el-button 的 type 字段
 */
export function channelMeta(ch: string): ChannelMeta {
  switch (ch) {
    case 'dingtalk':
      return { icon: '📱', label: 'DingTalk', color: '1677FF' }
    case 'wechat':
      return { icon: '💬', label: 'WeChat Work', color: '07C160' }
    case 'webhook':
      return { icon: '🔗', label: 'Webhook', color: '909399' }
    case 'wechat-mp':
      return { icon: '📧', label: 'WeChat MP', color: 'FA9D3B' }
    default:
      return { icon: '·', label: ch || '-', color: '909399' }
  }
}

/**
 * 渠道 → 仅图标 (轻量场景: 表格行内联)
 */
export function channelIcon(ch: string): string {
  return channelMeta(ch).icon
}

/**
 * 状态 → el-tag type
 * - sent → success (绿)
 * - failed → danger (红)
 * - suppressed → info (灰)
 * - pending → warning (橙)
 */
export function statusTagType(status: string): 'success' | 'danger' | 'info' | 'warning' {
  switch (status) {
    case 'sent':
    case 'success':
      return 'success'
    case 'failed':
    case 'failure':
    case 'error':
      return 'danger'
    case 'suppressed':
    case 'skipped':
      return 'info'
    case 'pending':
    case 'running':
      return 'warning'
    default:
      return 'info'
  }
}

/**
 * 严重度 → 颜色 (hex)
 * - P0 critical → 红, P1 → 橙, P2 → 黄, INFO → 灰
 */
export function severityColor(severity: string): string {
  switch ((severity || '').toUpperCase()) {
    case 'P0':
    case 'CRITICAL':
      return 'F56C6C'
    case 'P1':
    case 'ERROR':
    case 'HIGH':
      return 'E6A23C'
    case 'P2':
    case 'WARN':
    case 'MEDIUM':
      return 'F0B83C'
    case 'INFO':
    case 'P3':
    case 'LOW':
      return '909399'
    default:
      return '909399'
  }
}

/**
 * 严重度 → 标签
 * - 后端存的是 P0/P1/P2/INFO, 前端直接展示
 */
export function severityLabel(severity: string): string {
  return severity || '-'
}
