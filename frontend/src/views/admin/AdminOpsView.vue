<script setup lang="ts">
// V3(2026-08-25) 用户反馈:
//   - 数据导入 ETL 已独立为 /admin/etl 路由 (顶栏"数据导入"入口), 运维中心去除重复 tab
//   - 新增"数据备份" tab (用户反馈运维中心缺备份入口; 端点 GET /api/admin/backup/list)
// 运维页面: 性能 / 错误 / API 文档 / 存储配置 / 数据备份
import { ref } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import AdminPerfView from './AdminPerfView.vue'
import AdminErrorView from './AdminErrorView.vue'
import AdminApiDocsView from './AdminApiDocsView.vue'
import AdminStorageView from './AdminStorageView.vue'
import AdminBackupView from './AdminBackupView.vue'

const { t } = useI18n()

const activeTab = ref('perf')
const route = useRoute()
// 支持 URL 参数 ?tab=perf 直达 (从旧路由 /admin/perf 等跳转过来时定位)
if (typeof route.query.tab === 'string' && ['perf', 'errors', 'api', 'storage', 'backup'].includes(route.query.tab)) {
  activeTab.value = route.query.tab
}
</script>

<template>
  <div class="p-4 w-full">
    <el-tabs v-model="activeTab" class="w-full">
      <el-tab-pane :label="t('nav.opsview.tab.perf')" name="perf" lazy>
        <AdminPerfView />
      </el-tab-pane>
      <el-tab-pane :label="t('nav.opsview.tab.errors')" name="errors" lazy>
        <AdminErrorView />
      </el-tab-pane>
      <el-tab-pane :label="t('nav.opsview.tab.api')" name="api" lazy>
        <AdminApiDocsView />
      </el-tab-pane>
      <el-tab-pane :label="t('nav.opsview.tab.storage')" name="storage" lazy>
        <AdminStorageView />
      </el-tab-pane>
      <el-tab-pane :label="t('nav.opsview.tab.backup')" name="backup" lazy>
        <AdminBackupView />
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<style scoped>
/* tab 内子组件自带 p-3/p-4 内边距, 这里用默认即可 */
</style>
