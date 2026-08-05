<script setup lang="ts">
// 🔧 fix(审查): 运维页面合并 — ETL / 性能 / 错误 / API 文档 集成到单页 el-tabs
//   (用户反馈: 分开 4 个菜单意义不大; 组件耦合度低, tab 懒加载互不影响)
import { ref } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import AdminEtlView from './AdminEtlView.vue'
import AdminPerfView from './AdminPerfView.vue'
import AdminErrorView from './AdminErrorView.vue'
import AdminApiDocsView from './AdminApiDocsView.vue'

const { t } = useI18n()

const activeTab = ref('etl')
const route = useRoute()
// 支持 URL 参数 ?tab=perf 直达 (从旧路由 /admin/perf 等跳转过来时定位)
if (typeof route.query.tab === 'string' && ['etl', 'perf', 'errors', 'api'].includes(route.query.tab)) {
  activeTab.value = route.query.tab
}
</script>

<template>
  <div class="p-4 w-full">
    <el-tabs v-model="activeTab" class="w-full">
      <el-tab-pane :label="t('nav.opsview.tab.etl')" name="etl" lazy>
        <AdminEtlView />
      </el-tab-pane>
      <el-tab-pane :label="t('nav.opsview.tab.perf')" name="perf" lazy>
        <AdminPerfView />
      </el-tab-pane>
      <el-tab-pane :label="t('nav.opsview.tab.errors')" name="errors" lazy>
        <AdminErrorView />
      </el-tab-pane>
      <el-tab-pane :label="t('nav.opsview.tab.api')" name="api" lazy>
        <AdminApiDocsView />
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<style scoped>
/* tab 内子组件自带 p-3/p-4 内边距, 这里用默认即可 */
</style>
