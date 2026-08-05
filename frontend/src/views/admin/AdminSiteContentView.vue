<script setup lang="ts">
// 🔧 fix(审查): 站点内容维护页 (用户反馈: About/News/Contact 无内容且后台无维护入口)
//   - 站点名 / Logo URL / 关于我们 / 联系我们: 文本编辑
//   - News: 小型发布 (标题 + 正文 + 发布时间, 列表 CRUD)
//   - 数据: system_settings key-value (site.* keys), 公开端点供前台读取
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { siteContentApi } from '@/api'
import type { NewsItem, SiteContent } from '@/api'

const { t } = useI18n()

const saving = ref(false)
const loading = ref(true)

const form = reactive({
  siteName: '',
  logoUrl: '',
  about: '',
  contact: '',
})
const news = ref<NewsItem[]>([])

function parseNews(raw?: string | null): NewsItem[] {
  if (!raw) return []
  try {
    const arr = JSON.parse(raw)
    return Array.isArray(arr) ? arr : []
  } catch {
    return []
  }
}

async function load() {
  loading.value = true
  try {
    const data: SiteContent = await siteContentApi.get()
    form.siteName = data['site.name'] || ''
    form.logoUrl = data['site.logo_url'] || ''
    form.about = data['site.about'] || ''
    form.contact = data['site.contact'] || ''
    news.value = parseNews(data['site.news'])
  } finally {
    loading.value = false
  }
}

function toPayload(): SiteContent {
  return {
    'site.name': form.siteName,
    'site.logo_url': form.logoUrl,
    'site.about': form.about,
    'site.contact': form.contact,
    'site.news': JSON.stringify(news.value),
  }
}

async function save() {
  saving.value = true
  try {
    await siteContentApi.put(toPayload())
    ElMessage.success(t('dict.pageTitles.site.save') + ' ✓')
  } finally {
    saving.value = false
  }
}

// News CRUD
function addNews() {
  news.value.push({ id: `n${Date.now()}`, title: '', body: '', publishedAt: new Date().toISOString().slice(0, 10) })
}
function removeNews(idx: number) {
  news.value.splice(idx, 1)
}

onMounted(load)
</script>

<template>
  <!-- 🔧 fix(审查): max-w-4xl → w-full 撑满 (用户实测: 站点内容维护页整体偏左) -->
  <div class="p-4 w-full space-y-4" v-loading="loading">
    <div class="flex items-center justify-between">
      <h1 class="text-lg font-medium">{{ t('dict.pageTitles.siteContent') }}</h1>
      <el-button type="primary" size="small" :loading="saving" @click="save">{{ t('dict.pageTitles.site.save') }}</el-button>
    </div>

    <!-- 站点基础配置 -->
    <div class="hairline p-4 space-y-3">
      <div class="text-sm font-medium">{{ t('dict.pageTitles.site.basic_config') }}</div>
      <div class="grid grid-cols-2 gap-3">
        <div>
          <div class="text-xs text-muted mb-1">{{ t('dict.pageTitles.site.site_name') }}</div>
          <el-input v-model="form.siteName" placeholder="SakuraFilter" size="small" />
        </div>
        <div>
          <div class="text-xs text-muted mb-1">Logo URL</div>
          <el-input v-model="form.logoUrl" placeholder="https://.../logo.png (留空用默认)" size="small" />
        </div>
      </div>
    </div>

    <!-- About / Contact -->
    <div class="hairline p-4 space-y-3">
      <div class="text-sm font-medium">{{ t('dict.pageTitles.site.about') }}</div>
      <el-input v-model="form.about" type="textarea" :rows="5" placeholder="公司介绍 / 业务说明" />
      <div class="text-sm font-medium pt-2">{{ t('dict.pageTitles.site.contact') }}</div>
      <el-input v-model="form.contact" type="textarea" :rows="5" placeholder="联系方式 / 地址 / 邮箱 / 电话" />
    </div>

    <!-- News 发布 -->
    <div class="hairline p-4 space-y-3">
      <div class="flex items-center justify-between">
        <div class="text-sm font-medium">{{ t('dict.pageTitles.site.news') }}</div>
        <el-button size="small" @click="addNews">{{ t('dict.pageTitles.site.add_news') }}</el-button>
      </div>
      <div v-if="news.length === 0" class="text-xs text-muted py-3 text-center">{{ t('dict.pageTitles.site.no_news') }}</div>
      <div v-for="(n, idx) in news" :key="n.id" class="hairline p-3 space-y-2">
        <div class="flex gap-2">
          <el-input v-model="n.title" placeholder="新闻标题" size="small" class="flex-1" />
          <el-input v-model="n.publishedAt" placeholder="发布日期" size="small" style="width: 140px" />
          <el-button size="small" type="danger" plain @click="removeNews(idx)">{{ t('dict.pageTitles.site.delete') }}</el-button>
        </div>
        <el-input v-model="n.body" type="textarea" :rows="3" placeholder="新闻正文" />
      </div>
    </div>

    <div class="flex justify-end">
      <el-button type="primary" size="small" :loading="saving" @click="save">{{ t('dict.pageTitles.site.save') }}</el-button>
    </div>
  </div>
</template>
