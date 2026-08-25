<script setup lang="ts">
// V3(2026-08-25) 用户反馈: 运维中心缺备份入口
//   展示主机 _backups 目录下的备份文件列表 (后端 GET /api/admin/backup/list 读 /backups mount)
//   实际执行: 在部署主机跑 bash scripts/backup-db.sh --verify --upload (脚本内含异机上传)
//   端点: backupApi.list() + backupApi.scriptInfo()
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Refresh, Document, QuestionFilled } from '@element-plus/icons-vue'
import { backupApi, type BackupFile } from '@/api'

const { t } = useI18n()

const loading = ref(false)
const files = ref<BackupFile[]>([])
const dirExists = ref(true)
const dir = ref('')
const hostCommand = ref('')
const scriptNote = ref('')
const copied = ref(false)

async function load() {
  loading.value = true
  try {
    const [list, info] = await Promise.all([backupApi.list(), backupApi.scriptInfo()])
    dir.value = list.dir
    dirExists.value = list.exists
    files.value = list.files
    hostCommand.value = info.hostCommand
    scriptNote.value = info.note
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.detail || t('admin.backupview.err_load'))
  } finally {
    loading.value = false
  }
}

async function copyCommand() {
  try {
    await navigator.clipboard.writeText(hostCommand.value)
    copied.value = true
    ElMessage.success(t('admin.backupview.copied'))
    setTimeout(() => (copied.value = false), 1500)
  } catch {
    ElMessage.error(t('admin.backupview.copy_failed'))
  }
}

function formatDate(s: string) {
  if (!s) return ''
  return s.replace('T', ' ').replace('Z', '').replace(/-/g, '-')
}

onMounted(load)
</script>

<template>
  <div class="p-4 max-w-4xl mx-auto">
    <h2 class="text-lg font-medium mb-2">{{ t('admin.backupview.title') }}</h2>
    <p class="text-sm text-[var(--color-text-muted)] mb-4">
      {{ t('admin.backupview.subtitle') }}
    </p>

    <!-- 执行指引 (主机 bash) -->
    <el-alert type="info" :closable="false" class="mb-4">
      <template #title>
        <span class="flex items-center gap-1">
          <el-icon><QuestionFilled /></el-icon>
          {{ t('admin.backupview.how_to_run') }}
        </span>
      </template>
      <div class="text-sm mt-1">
        <div class="flex items-center gap-2 flex-wrap">
          <code class="px-2 py-1 rounded bg-gray-100 dark:bg-[var(--color-bg-elevated)] text-sm font-mono">
            {{ hostCommand || 'bash scripts/backup-db.sh --verify --upload' }}
          </code>
          <el-button size="small" @click="copyCommand">
            {{ copied ? t('admin.backupview.copied') : t('admin.backupview.copy') }}
          </el-button>
        </div>
        <div class="mt-2 text-[var(--color-text-muted)]">
          {{ scriptNote || t('admin.backupview.run_note') }}
        </div>
      </div>
    </el-alert>

    <!-- 备份列表 -->
    <el-card shadow="never">
      <template #header>
        <div class="flex items-center justify-between">
          <span class="font-medium">
            {{ t('admin.backupview.list_title') }}
            <span v-if="!loading" class="text-xs text-[var(--color-text-muted)] ml-2">
              ({{ files.length }}{{ dirExists ? '' : ' · ' + t('admin.backupview.dir_missing') }})
            </span>
          </span>
          <el-button size="small" :loading="loading" @click="load">
            <el-icon class="mr-1"><Refresh /></el-icon>{{ t('common.action.refresh') }}
          </el-button>
        </div>
      </template>

      <el-skeleton v-if="loading && files.length === 0" :rows="4" animated />

      <el-empty v-else-if="!dirExists" :description="t('admin.backupview.dir_missing_desc')" />

      <el-table v-else :data="files" stripe>
        <el-table-column :label="t('admin.backupview.col.name')" min-width="280">
          <template #default="{ row }">
            <div class="flex items-center gap-2">
              <el-icon class="text-[var(--color-text-muted)]"><Document /></el-icon>
              <span class="font-mono text-sm">{{ row.name }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column :label="t('admin.backupview.col.size')" width="120">
          <template #default="{ row }">{{ row.sizeHuman }}</template>
        </el-table-column>
        <el-table-column :label="t('admin.backupview.col.created_at')" width="200">
          <template #default="{ row }">{{ formatDate(row.createdAt) }}</template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>
