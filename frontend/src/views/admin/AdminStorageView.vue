<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { storageApi } from '@/api'

const { t } = useI18n()

const providers = [
  { value: 'minio', label: 'MinIO (自托管)' },
  { value: 'aliyun-oss', label: '阿里云 OSS' },
  { value: 'r2', label: 'Cloudflare R2 (推荐外贸)' },
]

const form = reactive({
  provider: 'minio',
  minio: { endpoint: '', accessKey: '', secretKey: '', bucketName: '', publicEndpoint: '' },
  aliyun: { endpoint: '', accessKeyId: '', accessKeySecret: '', bucketName: '', publicEndpoint: '', cdnEndpoint: '' },
  r2: { endpoint: '', accessKeyId: '', accessKeySecret: '', bucketName: '', publicEndpoint: '' },
})

// 保存的原配置 (用于密钥留空 = 不修改)
let savedRaw: any = null
const loading = ref(false)
const testing = ref(false)
const testResult = ref<{ ok: boolean; message: string; latencyMs?: number } | null>(null)

async function load() {
  loading.value = true
  try {
    const cfg = await storageApi.getConfig()
    savedRaw = cfg
    form.provider = cfg.provider || 'minio'
    Object.assign(form.minio, cfg.minio || {})
    Object.assign(form.aliyun, cfg.aliyun || {})
    Object.assign(form.r2, cfg.r2 || {})
  } catch {
    ElMessage.error(t('nav.storage.load_failed'))
  } finally {
    loading.value = false
  }
}

// 密钥脱敏值 (含 ****) 或空 → 沿用原值
function resolveSecret(field: string, val: string | undefined, savedVal: string | undefined) {
  if (!val || val.includes('****')) return savedVal
  return val
}

async function onTest() {
  testing.value = true
  testResult.value = null
  try {
    const res = await storageApi.testConfig(buildPayload())
    testResult.value = res
    if (res.ok) ElMessage.success(`${t('nav.storage.test_ok')} (${res.latencyMs ?? '-'}ms)`)
    else ElMessage.error(res.message || t('nav.storage.test_failed'))
  } catch (e: any) {
    testResult.value = { ok: false, message: e?.message || String(e) }
    ElMessage.error(t('nav.storage.test_failed'))
  } finally {
    testing.value = false
  }
}

function buildPayload() {
  const p = form.provider
  return {
    provider: p,
    minio: {
      ...form.minio,
      accessKey: resolveSecret('minio.accessKey', form.minio.accessKey, savedRaw?.minio?.accessKey),
      secretKey: resolveSecret('minio.secretKey', form.minio.secretKey, savedRaw?.minio?.secretKey),
    },
    aliyun: {
      ...form.aliyun,
      accessKeyId: resolveSecret('aliyun.accessKeyId', form.aliyun.accessKeyId, savedRaw?.aliyun?.accessKeyId),
      accessKeySecret: resolveSecret('aliyun.accessKeySecret', form.aliyun.accessKeySecret, savedRaw?.aliyun?.accessKeySecret),
    },
    r2: {
      ...form.r2,
      accessKeyId: resolveSecret('r2.accessKeyId', form.r2.accessKeyId, savedRaw?.r2?.accessKeyId),
      accessKeySecret: resolveSecret('r2.accessKeySecret', form.r2.accessKeySecret, savedRaw?.r2?.accessKeySecret),
    },
  }
}

async function onSave() {
  try {
    await storageApi.saveConfig(buildPayload())
    ElMessage.success(t('nav.storage.saved'))
  } catch {
    ElMessage.error(t('nav.storage.save_failed'))
  }
}

onMounted(load)
</script>

<template>
  <div v-loading="loading" class="p-4">
    <!-- Provider 选择 -->
    <div class="mb-4">
      <div class="text-sm font-medium text-gray-700 mb-2">{{ t('nav.storage.provider') }}</div>
      <el-radio-group v-model="form.provider">
        <el-radio v-for="p in providers" :key="p.value" :value="p.value" class="mr-4">{{ p.label }}</el-radio>
      </el-radio-group>
      <div class="text-xs text-gray-400 mt-1">{{ t('nav.storage.provider_tip') }}</div>
    </div>

    <el-divider />

    <!-- MinIO -->
    <el-form v-if="form.provider === 'minio'" label-width="140px" class="max-w-2xl">
      <el-form-item :label="t('nav.storage.endpoint')"><el-input v-model="form.minio.endpoint" placeholder="minio:9000" /></el-form-item>
      <el-form-item :label="t('nav.storage.access_key')"><el-input v-model="form.minio.accessKey" show-password placeholder="sakura-minio-admin" /></el-form-item>
      <el-form-item :label="t('nav.storage.secret_key')"><el-input v-model="form.minio.secretKey" show-password /></el-form-item>
      <el-form-item :label="t('nav.storage.bucket')"><el-input v-model="form.minio.bucketName" placeholder="sakurafilter" /></el-form-item>
      <el-form-item :label="t('nav.storage.public_endpoint')"><el-input v-model="form.minio.publicEndpoint" placeholder="http://localhost:9000" /></el-form-item>
    </el-form>

    <!-- 阿里云 OSS -->
    <el-form v-else-if="form.provider === 'aliyun-oss'" label-width="140px" class="max-w-2xl">
      <el-form-item :label="t('nav.storage.endpoint')"><el-input v-model="form.aliyun.endpoint" placeholder="oss-cn-hangzhou.aliyuncs.com" /></el-form-item>
      <el-form-item :label="t('nav.storage.access_key_id')"><el-input v-model="form.aliyun.accessKeyId" show-password /></el-form-item>
      <el-form-item :label="t('nav.storage.access_key_secret')"><el-input v-model="form.aliyun.accessKeySecret" show-password /></el-form-item>
      <el-form-item :label="t('nav.storage.bucket')"><el-input v-model="form.aliyun.bucketName" /></el-form-item>
      <el-form-item :label="t('nav.storage.public_endpoint')"><el-input v-model="form.aliyun.publicEndpoint" placeholder="https://bucket.oss-cn-hangzhou.aliyuncs.com" /></el-form-item>
      <el-form-item :label="t('nav.storage.cdn_endpoint')"><el-input v-model="form.aliyun.cdnEndpoint" placeholder="https://img.yoursite.com (可选, CDN 加速)" /></el-form-item>
    </el-form>

    <!-- Cloudflare R2 -->
    <el-form v-else label-width="140px" class="max-w-2xl">
      <el-form-item :label="t('nav.storage.endpoint')"><el-input v-model="form.r2.endpoint" placeholder="https://<account>.r2.cloudflarestorage.com" /></el-form-item>
      <el-form-item :label="t('nav.storage.access_key_id')"><el-input v-model="form.r2.accessKeyId" show-password placeholder="R2 S3 API Token (Access Key ID)" /></el-form-item>
      <el-form-item :label="t('nav.storage.access_key_secret')"><el-input v-model="form.r2.accessKeySecret" show-password /></el-form-item>
      <el-form-item :label="t('nav.storage.bucket')"><el-input v-model="form.r2.bucketName" /></el-form-item>
      <el-form-item :label="t('nav.storage.public_endpoint')"><el-input v-model="form.r2.publicEndpoint" placeholder="https://pub-xxx.r2.dev (可选)" /></el-form-item>
    </el-form>

    <!-- 操作 -->
    <div class="mt-4 flex items-center gap-3">
      <el-button type="primary" :loading="testing" @click="onTest">{{ t('nav.storage.test') }}</el-button>
      <el-button type="success" @click="onSave">{{ t('nav.storage.save') }}</el-button>
    </div>
    <div v-if="testResult" class="mt-3 text-sm" :class="testResult.ok ? 'text-green-600' : 'text-red-600'">
      {{ testResult.ok ? '✓' : '✗' }} {{ testResult.message }}
      <span v-if="testResult.latencyMs != null" class="text-gray-400"> ({{ testResult.latencyMs }}ms)</span>
    </div>
    <div class="mt-2 text-xs text-gray-400">{{ t('nav.storage.restart_tip') }}</div>
  </div>
</template>
