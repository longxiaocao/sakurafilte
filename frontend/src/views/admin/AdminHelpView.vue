<script setup lang="ts">
// 后台帮助/文档页
//   5 个模块: 快速开始 / 字典使用规范 / 批量导入 / 搜索容差 / 常见问题
//   字段帮助文案从 data/field-help.ts 复用, 单源真相
//   el-anchor 锚点导航 + 章节卡片
//   整体 Musk 风格 (无阴影, 1px hairline, 8px 网格)
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { FIELD_HELP } from '@/data/field-help'

const { t } = useI18n()

// 8 个字典 (P1.3 + P2.2)
const dictList = [
  { name: t('common.field.oem_brand'), path: '/admin/dict/oem-brands', desc: t('admin.helpview.string.alternative_brand_cross_references') },
  { name: t('common.action.product_name_1'), path: '/admin/dict/product-name1s', desc: t('admin.helpview.string.product_name_e_g') },
  { name: t('common.action.product_name_2'), path: '/admin/dict/product-name2s', desc: t('admin.helpview.string.product_name_model_back') },
  { name: t('admin.helpview.string.type_type'), path: '/admin/dict/types', desc: t('admin.helpview.string.category_oil_fuel_air') },
  { name: 'OEM 3', path: '/admin/dict/oem-no3s', desc: t('admin.helpview.string.alternative_brand_oem_number') },
  { name: t('admin.helpview.string.media_media'), path: '/admin/dict/medias', desc: t('admin.helpview.string.filter_media_name_model') },
  { name: t('admin.helpview.string.machine_model_machine'), path: '/admin/dict/machines', desc: t('admin.helpview.string.machine_brand_model_name') },
  { name: t('admin.helpview.string.engine_engine'), path: '/admin/dict/engines', desc: t('admin.helpview.string.engine_brand_model') }
]

// FAQ 数据
const faqs = [
  {
    q: t('admin.helpview.string.for_input_oem_number'),
    a: t('admin.helpview.string.check_if_oem_is')
  },
  {
    q: t('admin.helpview.string.for_add_product_typeahead'),
    a: t('admin.helpview.string.dictionary_is_maintained_in')
  },
  {
    q: t('admin.helpview.string.dimensions_search_h_back'),
    a: t('admin.helpview.string.dimensions_search_default_mm')
  },
  {
    q: t('admin.helpview.string.etl_trigger_back_in'),
    a: t('admin.helpview.string.reading_phase_is_streaming')
  },
  {
    q: t('admin.helpview.string.batch_delete_product'),
    a: t('admin.helpview.string.in_admin_product_list')
  },
  {
    q: t('admin.helpview.string.upload_image_back_frontend_sho'),
    a: t('admin.helpview.string.check_product_ispublished_true')
  }
]

// 字段帮助预览 (前 10 个最常用)
const helpPreviewKeys = [
  'oem2', 'type', 'h1Mm', 'd1Mm', 'd7Thread',
  'media', 'sealingMaterial', 'collapsePressureBar',
  'cartonLengthMm', 'volumePerCartonM3'
]
const helpPreview = computed(() => helpPreviewKeys
  .map((k) => ({ key: k, ...(FIELD_HELP[k] || { label: k, description: '—' }) })))
</script>

<template>
  <div class="p-3 w-full">
    <h1 class="text-lg font-medium mb-1">{{ t('admin.helpview.string.page_title') }}</h1>
    <p class="text-xs text-muted mb-3">
      {{ t('admin.helpview.string.page_subtitle') }}
    </p>

    <el-anchor
      :offset="60"
      class="help-anchor hairline p-2 mb-3 bg-[var(--color-bg-elevated)]"
    >
      <el-anchor-link href="#quickstart" :title="t('admin.helpview.title.start')" />
      <el-anchor-link href="#dict" :title="t('admin.helpview.title.en_v4')" />
      <el-anchor-link href="#import" :title="t('admin.helpview.title.batch_import')" />
      <el-anchor-link href="#search" :title="t('admin.helpview.title.search_v2')" />
      <el-anchor-link href="#faq" :title="t('admin.helpview.title.common')" />
    </el-anchor>

    <!-- 1. 快速开始 -->
    <section id="quickstart" class="hairline p-4 mb-3">
      <h2 class="text-base font-medium mb-2">{{ t('admin.helpview.string.quick_start_title') }}</h2>
      <ol class="text-sm leading-7 list-decimal pl-5 text-[var(--color-text-muted)]">
        <li>{{ t('admin.helpview.string.quick_start_step1') }}</li>
        <li>{{ t('admin.helpview.string.quick_start_step2') }}</li>
        <li>{{ t('admin.helpview.string.quick_start_step3') }}</li>
        <li>{{ t('admin.helpview.string.quick_start_step4') }}</li>
        <li>{{ t('admin.helpview.string.quick_start_step5') }}</li>
      </ol>
    </section>

    <!-- 2. 字典使用规范 -->
    <section id="dict" class="hairline p-4 mb-3">
      <h2 class="text-base font-medium mb-2">{{ t('admin.helpview.string.dict_norms_title') }}</h2>
      <p class="text-xs text-muted mb-2">
        {{ t('admin.helpview.string.dict_norms_desc') }}
      </p>
      <table class="w-full text-sm">
        <thead>
          <tr class="hairline-b text-left text-xs text-muted">
            <th class="py-1 pr-2">{{ t('admin.helpview.string.col_dict') }}</th>
            <th class="py-1 pr-2">{{ t('admin.helpview.string.col_field') }}</th>
            <th class="py-1 pr-2">{{ t('admin.helpview.string.col_desc') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="d in dictList" :key="d.path" class="hairline-b">
            <td class="py-1 pr-2 font-medium">
              <a :href="d.path" class="text-blue-600 hover:underline">{{ d.name }}</a>
            </td>
            <td class="py-1 pr-2"><code class="text-xs">{{ d.path }}</code></td>
            <td class="py-1 pr-2 text-muted">{{ d.desc }}</td>
          </tr>
        </tbody>
      </table>
      <p class="text-xs text-muted mt-2">
        {{ t('admin.helpview.string.dict_drag_tip') }}
      </p>
    </section>

    <!-- 3. 批量导入 -->
    <section id="import" class="hairline p-4 mb-3">
      <h2 class="text-base font-medium mb-2">{{ t('admin.helpview.string.batch_import_title') }}</h2>
      <ol class="text-sm leading-7 list-decimal pl-5">
        <li>{{ t('admin.helpview.string.batch_import_step1') }}</li>
        <li>{{ t('admin.helpview.string.batch_import_step2') }}</li>
        <li>{{ t('admin.helpview.string.batch_import_step3') }}</li>
        <li>{{ t('admin.helpview.string.batch_import_step4') }}</li>
        <li>{{ t('admin.helpview.string.batch_import_step5') }}</li>
      </ol>
      <p class="text-xs text-muted mt-2">
        {{ t('admin.helpview.string.batch_import_perf') }}
      </p>
    </section>

    <!-- 4. 搜索容差 -->
    <section id="search" class="hairline p-4 mb-3">
      <h2 class="text-base font-medium mb-2">{{ t('admin.helpview.string.search_tolerance_title') }}</h2>
      <p class="text-sm leading-6">
        {{ t('admin.helpview.string.search_tolerance_desc') }}
      </p>
      <p class="text-sm leading-6 mt-1">
        {{ t('admin.helpview.string.search_tolerance_combo') }}
      </p>

      <h3 class="text-sm font-medium mt-3 mb-1">{{ t('admin.helpview.string.field_help_title') }}</h3>
      <table class="w-full text-sm">
        <thead>
          <tr class="hairline-b text-left text-xs text-muted">
            <th class="py-1 pr-2">{{ t('admin.helpview.string.col_field') }}</th>
            <th class="py-1 pr-2">{{ t('admin.helpview.string.col_unit') }}</th>
            <th class="py-1 pr-2">{{ t('admin.helpview.string.col_desc') }}</th>
            <th class="py-1 pr-2">{{ t('admin.helpview.string.col_example') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="h in helpPreview" :key="h.key" class="hairline-b">
            <td class="py-1 pr-2 font-medium">{{ h.label }} <code class="text-xs text-muted">({{ h.key }})</code></td>
            <td class="py-1 pr-2 text-muted">{{ h.unit || '—' }}</td>
            <td class="py-1 pr-2 text-muted">{{ h.description }}</td>
            <td class="py-1 pr-2 text-muted"><code class="text-xs">{{ h.example || '—' }}</code></td>
          </tr>
        </tbody>
      </table>
      <p class="text-xs text-muted mt-2">
        {{ t('admin.helpview.string.field_help_tip') }}
      </p>
    </section>

    <!-- 5. FAQ -->
    <section id="faq" class="hairline p-4 mb-3">
      <h2 class="text-base font-medium mb-2">{{ t('admin.helpview.string.faq_title') }}</h2>
      <el-collapse>
        <!-- V24-F86 (P2-1): 保留 index key — faqs 是静态数组(6 项永不增删),
             el-collapse-item 的 :name="String(i)" 依赖 index, :title Q${i+1} 依赖序号,
             改 key 需同步改 :name/:title 逻辑且无收益, 豁免 -->
        <el-collapse-item v-for="(f, i) in faqs" :key="i" :title="`Q${i + 1}. ${f.q}`" :name="String(i)">
          <div class="text-sm text-[var(--color-text-muted)] pl-2 leading-6">
            {{ f.a }}
          </div>
        </el-collapse-item>
      </el-collapse>
    </section>

    <p class="text-xs text-muted text-center mt-4">
      {{ t('admin.helpview.string.footer') }}
    </p>
  </div>
</template>

<style scoped>
.help-anchor :deep(.el-anchor__link) {
  font-size: 13px;
}
</style>
