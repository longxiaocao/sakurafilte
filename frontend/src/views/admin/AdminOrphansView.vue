<script setup lang="ts">
// V25: 孤儿机型管理页 — 列出 product_id=NULL 的 MachineApplication 记录
//   支持关键字搜索 (brand / model / name) + 分页
//   操作: 关联到产品 (弹出产品搜索对话框, 选产品后 PATCH /orphan/{id}/link)
import { ref, reactive, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { orphanApi, adminProductApi } from '@/api'
import type { OrphanApp } from '@/api/types'

const { t } = useI18n()

// ===== 列表 =====
const loading = ref(false)
const items = ref<OrphanApp[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const keyword = ref('')
const searchKeyword = ref('')

async function load() {
  loading.value = true
  try {
    const res = await orphanApi.list(page.value, pageSize.value, keyword.value || undefined)
    items.value = res.items
    total.value = res.total
  } catch (e: any) {
    ElMessage.error(t('admin.orphansview.error.load_failed'))
  } finally {
    loading.value = false
  }
}

function doSearch() {
  keyword.value = searchKeyword.value.trim()
  page.value = 1
  load()
}

function handlePageChange(p: number) {
  page.value = p
  load()
}

function handleSizeChange(s: number) {
  pageSize.value = s
  page.value = 1
  load()
}

// ===== 关联对话框 =====
const linkDialogOpen = ref(false)
const linkOrphanId = ref<number | null>(null)
const linkProductId = ref<number | null>(null)
const linkLoading = ref(false)
const linkError = ref<string | null>(null)
const linkResult = ref<{ mr1: string } | null>(null)

// 产品搜索 (用于关联对话框)
const prodSearchKeyword = ref('')
const prodSearchResults = ref<{ id: number; mr1: string }[]>([])
const prodSearchLoading = ref(false)

function openLinkDialog(row: OrphanApp) {
  linkOrphanId.value = row.id
  linkProductId.value = null
  linkLoading.value = false
  linkError.value = null
  linkResult.value = null
  prodSearchKeyword.value = ''
  prodSearchResults.value = []
  linkDialogOpen.value = true
}

async function searchProducts() {
  const kw = prodSearchKeyword.value.trim()
  if (!kw) { prodSearchResults.value = []; return }
  prodSearchLoading.value = true
  try {
    const res = await adminProductApi.search({
      mr1: kw,
      page: 1,
      pageSize: 20,
    })
    prodSearchResults.value = res.items.map((p: any) => ({ id: p.id, mr1: p.mr1 }))
  } catch {
    prodSearchResults.value = []
  } finally {
    prodSearchLoading.value = false
  }
}

async function confirmLink() {
  if (!linkOrphanId.value || !linkProductId.value) {
    linkError.value = t('admin.orphansview.error.select_product')
    return
  }
  linkLoading.value = true
  linkError.value = null
  try {
    const res = await orphanApi.link(linkOrphanId.value, linkProductId.value)
    linkResult.value = { mr1: res.mr1 }
    ElMessage.success(t('admin.orphansview.success.linked', { mr1: res.mr1 }))
    load()
  } catch (e: any) {
    linkError.value = e?.response?.data?.error || t('admin.orphansview.error.link_failed')
  } finally {
    linkLoading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="orphans-view">
    <!-- 标题 + 搜索栏 -->
    <div class="orphans-view__header">
      <h2 class="orphans-view__title">{{ t('admin.orphansview.title') }}</h2>
      <div class="orphans-view__search">
        <span class="orphans-view__hint">{{ t('admin.orphansview.hint') }}</span>
        <el-input
          v-model="searchKeyword"
          :placeholder="t('admin.orphansview.search_placeholder')"
          clearable
          class="orphans-view__input"
          @keyup.enter="doSearch"
        >
          <template #append>
            <el-button @click="doSearch">{{ t('common.search') }}</el-button>
          </template>
        </el-input>
        <el-button type="primary" @click="doSearch">{{ t('common.search') }}</el-button>
      </div>
    </div>

    <!-- 数据量统计 -->
    <div class="orphans-view__stats" v-if="!loading">
      <el-tag type="warning" effect="plain">{{ t('admin.orphansview.total_orphans', { total }) }}</el-tag>
      <el-tag v-if="keyword" type="info" effect="plain">{{ t('admin.orphansview.filtered', { keyword, total }) }}</el-tag>
    </div>

    <!-- 表格 -->
    <el-table :data="items" v-loading="loading" stripe border class="orphans-view__table">
      <el-table-column prop="id" :label="t('admin.orphansview.col.id')" width="80" />
      <el-table-column prop="machineBrand" :label="t('admin.orphansview.col.brand')" min-width="140" show-overflow-tooltip />
      <el-table-column prop="machineModel" :label="t('admin.orphansview.col.model')" min-width="160" show-overflow-tooltip />
      <el-table-column prop="modelName" :label="t('admin.orphansview.col.model_name')" min-width="140" show-overflow-tooltip />
      <el-table-column prop="machineCategory" :label="t('admin.orphansview.col.category')" width="120" show-overflow-tooltip />
      <el-table-column :label="t('admin.orphansview.col.created_at')" width="170">
        <template #default="{ row }">
          {{ new Date(row.createdAt).toLocaleDateString('zh-CN') }}
        </template>
      </el-table-column>
      <el-table-column :label="t('common.action')" width="100" fixed="right">
        <template #default="{ row }">
          <el-button type="primary" link @click="openLinkDialog(row)">
            {{ t('admin.orphansview.action.link') }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- 分页 -->
    <el-pagination
      v-if="total > 0"
      v-model:current-page="page"
      v-model:page-size="pageSize"
      :total="total"
      :page-sizes="[10, 20, 50, 100]"
      layout="total, sizes, prev, pager, next"
      class="orphans-view__pagination"
      @current-change="handlePageChange"
      @size-change="handleSizeChange"
    />

    <!-- 关联对话框 -->
    <el-dialog
      v-model="linkDialogOpen"
      :title="t('admin.orphansview.dialog.title')"
      width="520px"
      destroy-on-close
    >
      <div v-if="linkError" class="orphans-view__error">{{ linkError }}</div>
      <div v-if="linkResult" class="orphans-view__success">
        {{ t('admin.orphansview.dialog.linked_success', { mr1: linkResult.mr1 }) }}
      </div>

      <!-- 产品搜索 -->
      <div class="orphans-view__link-form">
        <label>{{ t('admin.orphansview.dialog.search_product') }}</label>
        <el-input
          v-model="prodSearchKeyword"
          :placeholder="t('admin.orphansview.dialog.search_placeholder')"
          clearable
          @input="searchProducts"
        />
        <el-table
          v-if="prodSearchResults.length > 0"
          :data="prodSearchResults"
          stripe
          class="orphans-view__prod-list"
          @row-click="(row: any) => { linkProductId = row.id; prodSearchKeyword = row.mr1; }"
        >
          <el-table-column prop="id" :label="t('admin.orphansview.dialog.col_id')" width="80" />
          <el-table-column prop="mr1" :label="t('admin.orphansview.dialog.col_mr1')" />
        </el-table>
        <div v-if="prodSearchLoading" class="orphans-view__loading">{{ t('common.loading') }}</div>
        <div v-if="linkOrphanId && !linkProductId && !linkResult" class="orphans-view__hint">
          {{ t('admin.orphansview.dialog.select_hint') }}
        </div>
      </div>

      <template #footer>
        <el-button @click="linkDialogOpen = false">{{ t('common.cancel') }}</el-button>
        <el-button
          type="primary"
          :loading="linkLoading"
          :disabled="!linkProductId"
          @click="confirmLink"
        >
          {{ t('admin.orphansview.dialog.confirm_link') }}
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.orphans-view {
  width: 100%;
}
.orphans-view__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 12px;
  margin-bottom: 16px;
}
.orphans-view__title {
  font-size: 16px;
  font-weight: 600;
  margin: 0;
  color: #1a1a1a;
}
.orphans-view__search {
  display: flex;
  align-items: center;
  gap: 8px;
}
.orphans-view__hint {
  font-size: 12px;
  color: #888;
  white-space: nowrap;
}
.orphans-view__input {
  width: 280px;
}
.orphans-view__stats {
  display: flex;
  gap: 8px;
  margin-bottom: 12px;
}
.orphans-view__table {
  width: 100%;
  border-radius: 4px;
}
.orphans-view__pagination {
  margin-top: 16px;
  display: flex;
  justify-content: flex-end;
}
.orphans-view__error {
  color: #f56c6c;
  font-size: 13px;
  margin-bottom: 12px;
  padding: 8px 12px;
  background: #fef0f0;
  border-radius: 4px;
}
.orphans-view__success {
  color: #67c23a;
  font-size: 13px;
  margin-bottom: 12px;
  padding: 8px 12px;
  background: #f0f9eb;
  border-radius: 4px;
}
.orphans-view__link-form {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.orphans-view__link-form label {
  font-size: 13px;
  font-weight: 500;
  color: #333;
}
.orphans-view__prod-list {
  border: 1px solid #e4e7ed;
  border-radius: 4px;
  max-height: 200px;
  overflow-y: auto;
}
.orphans-view__loading,
.orphans-view__hint {
  font-size: 12px;
  color: #999;
}
</style>
