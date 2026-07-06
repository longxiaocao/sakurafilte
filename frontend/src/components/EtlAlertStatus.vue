<script setup lang="ts">
// EtlAlertStatus — 告警状态占位卡片 (P1)
// WHY 新增 (P1 重构 2026-07-06):
//   告警系统 (钉钉/微信/通用 webhook) 计划在 P2 阶段实施。
//   本卡片作为 UI 锚点先占位, 避免 P2 时改整体布局, 同时给用户预告。
//
// 当前状态:
//   - 显示"告警系统即将上线"提示
//   - 列出已规划的告警类型 (ETL/性能/登录安全/资源监控)
//   - 提供配置入口占位按钮 (跳转设计文档, P2 改为告警配置页)
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'

const { t } = useI18n()
const router = useRouter()

const plannedTypes = [
  { key: 'etl', icon: '⚙️', label: t('admin.etlview.alert.type_etl') },
  { key: 'perf', icon: '📈', label: t('admin.etlview.alert.type_perf') },
  { key: 'security', icon: '🔒', label: t('admin.etlview.alert.type_security') },
  { key: 'access', icon: '🌐', label: t('admin.etlview.alert.type_access') },
  { key: 'resource', icon: '💾', label: t('admin.etlview.alert.type_resource') }
]

const plannedChannels = [
  { icon: '📱', label: t('admin.etlview.alert.channel_dingtalk') },
  { icon: '💬', label: t('admin.etlview.alert.channel_wechat') },
  { icon: '🔗', label: t('admin.etlview.alert.channel_webhook') }
]
</script>

<template>
  <div class="alert-status">
    <div class="alert-left">
      <div class="alert-header">
        <el-tag size="small" type="info">{{ t('admin.etlview.alert.p2_tag') }}</el-tag>
        <span class="alert-title">{{ t('admin.etlview.alert.title') }}</span>
      </div>
      <div class="alert-desc">{{ t('admin.etlview.alert.description') }}</div>
      <div class="alert-rows">
        <div class="alert-row">
          <span class="row-label">{{ t('admin.etlview.alert.planned_types') }}</span>
          <span class="row-items">
            <span v-for="t in plannedTypes" :key="t.key" class="row-item">
              <span class="row-icon">{{ t.icon }}</span>{{ t.label }}
            </span>
          </span>
        </div>
        <div class="alert-row">
          <span class="row-label">{{ t('admin.etlview.alert.planned_channels') }}</span>
          <span class="row-items">
            <span v-for="c in plannedChannels" :key="c.label" class="row-item">
              <span class="row-icon">{{ c.icon }}</span>{{ c.label }}
            </span>
          </span>
        </div>
      </div>
    </div>
    <div class="alert-right">
      <el-button
        type="primary"
        plain
        size="small"
        @click="router.push('/admin/help')"
      >
        {{ t('admin.etlview.alert.view_design_btn') }}
      </el-button>
    </div>
  </div>
</template>

<style scoped>
.alert-status {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 4px 0;
}
.alert-left { flex: 1 1 auto; min-width: 0; }
.alert-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}
.alert-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}
.alert-desc {
  font-size: 12px;
  color: var(--color-text-muted);
  margin-bottom: 12px;
  line-height: 1.5;
}
.alert-rows { display: flex; flex-direction: column; gap: 6px; }
.alert-row {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  font-size: 12px;
}
.row-label {
  flex: 0 0 80px;
  color: var(--color-text-muted);
}
.row-items {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.row-item {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 2px 8px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 10px;
  font-size: 11px;
  color: var(--el-text-color-regular);
}
.row-icon { font-size: 12px; }
.alert-right { flex: 0 0 auto; }
@media (max-width: 768px) {
  .alert-status { flex-direction: column; align-items: flex-start; }
  .alert-right { align-self: stretch; }
  .alert-right :deep(.el-button) { width: 100%; }
}
</style>
