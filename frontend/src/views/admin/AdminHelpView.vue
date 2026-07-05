<script setup lang="ts">
// Day 10+ P5.4 (Task 15.4): 后台帮助/文档页
//   - 5 个模块: 快速开始 / 字典使用规范 / 批量导入 / 搜索容差 / 常见问题
//   - 字段帮助文案从 data/field-help.ts 复用, 单源真相
//   - el-anchor 锚点导航 + 章节卡片
//   - 整体 Musk 风格 (无阴影, 1px hairline, 8px 网格)
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { FIELD_HELP } from '@/data/field-help'

const { t } = useI18n()

// 8 个字典 (P1.3 + P2.2)
const dictList = [
  { name: t('admin.helpview.string.l12_oem'), path: '/admin/dict/oem-brands', desc: t('admin.helpview.string.l12_cross_references_oem_brand_mann_bosch_ma') },
  { name: t('admin.helpview.string.l13_1'), path: '/admin/dict/product-name1s', desc: t('admin.helpview.string.l13_oil_filter_fuel_filter') },
  { name: t('admin.helpview.string.l14_2'), path: '/admin/dict/product-name2s', desc: t('admin.helpview.string.l14_of100') },
  { name: t('admin.helpview.string.l15_type'), path: '/admin/dict/types', desc: t('admin.helpview.string.l15_5_oil_fuel_air_cabin_others_sort_order') },
  { name: 'OEM 3', path: '/admin/dict/oem-no3s', desc: t('admin.helpview.string.l16_oem_5_27m_distinct_typeahead') },
  { name: t('admin.helpview.string.l17_media'), path: '/admin/dict/medias', desc: t('admin.helpview.string.l17_2_cellulose_a020') },
  { name: t('admin.helpview.string.l18_machine'), path: '/admin/dict/machines', desc: t('admin.helpview.string.l18_4_agriculture_commercial_construction_ot') },
  { name: t('admin.helpview.string.l19_engine'), path: '/admin/dict/engines', desc: t('admin.helpview.string.l19_') }
]

// FAQ 数据
const faqs = [
  {
    q: t('admin.helpview.string.l25_oem'),
    a: t('admin.helpview.string.l25_a_oem')
  },
  {
    q: t('admin.helpview.string.l29_typeahead'),
    a: t('admin.helpview.string.l29_a_typeahead')
  },
  {
    q: t('admin.helpview.string.l33_h1_100_0'),
    a: t('admin.helpview.string.l34_5mm_95_105_h1_110_h1_id')
  },
  {
    q: t('admin.helpview.string.l37_etl_reading'),
    a: t('admin.helpview.string.l37_a_etl')
  },
  {
    q: t('admin.helpview.string.l41_'),
    a: t('admin.helpview.string.l41_a_batch')
  },
  {
    q: t('admin.helpview.string.l45_'),
    a: t('admin.helpview.string.l45_a_image')
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
  <div class="p-3 max-w-screen-xl mx-auto">
    <h1 class="text-lg font-medium mb-1">后台操作指南</h1>
    <p class="text-xs text-muted mb-3">
      Day 10+ P5.4 帮助页 — 5 模块: 快速开始 / 字典规范 / 批量导入 / 搜索容差 / FAQ
    </p>

    <el-anchor
      :offset="60"
      class="help-anchor hairline p-2 mb-3 bg-[var(--color-bg-elevated)]"
    >
      <el-anchor-link href="#quickstart" :title="t('admin.helpview.title.l71_')" />
      <el-anchor-link href="#dict" :title="t('admin.helpview.title.l72_')" />
      <el-anchor-link href="#import" :title="t('admin.helpview.title.l73_')" />
      <el-anchor-link href="#search" :title="t('admin.helpview.title.l74_')" />
      <el-anchor-link href="#faq" :title="t('admin.helpview.title.l75_')" />
    </el-anchor>

    <!-- 1. 快速开始 -->
    <section id="quickstart" class="hairline p-4 mb-3">
      <h2 class="text-base font-medium mb-2">1. 快速开始 (5 步入门)</h2>
      <ol class="text-sm leading-7 list-decimal pl-5 text-[var(--color-text-muted)]">
        <li>点击右上 t('admin.helpview.string.l82_'), 输入 <code class="bg-[var(--color-bg-hover)] px-1">X-Admin-Token</code> (与后端 <code>Auth:DevStaticToken</code> 一致)</li>
        <li>字典管理 → 8 个字典先 seed 数据 (首次部署): 走 spike-test/_seed_dict_*.py 6 个脚本</li>
        <li>ETL 触发 → 选择 "products.xlsx" / "xrefs.xlsx" / "apps.xlsxt('admin.helpview.string.l84_full_load_insert_only_upsert')触发"</li>
        <li>产品管理 → 用 8 字段 / OEM 查询 / 批量粘贴查询, 命中产品进入详情</li>
        <li>产品详情页支持上传 6 张图 (slot 1-6) + 编辑 7 分区字段 (后台产品表单)</li>
      </ol>
    </section>

    <!-- 2. 字典使用规范 -->
    <section id="dict" class="hairline p-4 mb-3">
      <h2 class="text-base font-medium mb-2">2. 字典使用规范 (8 个)</h2>
      <p class="text-xs text-muted mb-2">
        字典 = 后台可维护的标准值集合. 前台 typeahead / 后台表单 / 公开搜索均从字典取, 保证全站一致.
      </p>
      <table class="w-full text-sm">
        <thead>
          <tr class="hairline-b text-left text-xs text-muted">
            <th class="py-1 pr-2">字典</th>
            <th class="py-1 pr-2">引用字段</th>
            <th class="py-1 pr-2">说明</th>
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
        💡 拖拽排序: 字典管理页每行的 ≡ 按钮, sort_order 持久化, 前台展示按 sort_order 升序.
      </p>
    </section>

    <!-- 3. 批量导入 -->
    <section id="import" class="hairline p-4 mb-3">
      <h2 class="text-base font-medium mb-2">3. 批量导入流程 (XLSX 拖拽)</h2>
      <ol class="text-sm leading-7 list-decimal pl-5">
        <li>准备 Excel: products / xrefs / machine_applications 三张表 (列名见 ETL 触发页)</li>
        <li>ETL 触发页 → t('admin.helpview.string.l124_xlsx') → 自动识别 entity + 模式 (推荐 full-load 全量, insert-only 仅新增)</li>
        <li>进度条 5 阶段: reading → staging → inserting → committing → meili-sync, 任一阶段失败可暂停/恢复</li>
        <li>完成后会在 etl_progress_log 写一行 (含 read/stage/inserted/skipped/missing_oem/error 计数)</li>
        <li>后台产品管理用 t('admin.helpview.string.l127_') 验证导入数据是否可查</li>
      </ol>
      <p class="text-xs text-muted mt-2">
        ⚠ 性能: 1M products 全量约 2-3 分钟, 5M xrefs 约 5-8 分钟, 1M apps 约 2 分钟 (PG 本地测试数据)
      </p>
    </section>

    <!-- 4. 搜索容差 -->
    <section id="search" class="hairline p-4 mb-3">
      <h2 class="text-base font-medium mb-2">4. 搜索容差 (±5mm 固定)</h2>
      <p class="text-sm leading-6">
        尺寸字段 (H1-H4 / D1-D4) 搜索默认 <strong>±5mm</strong> 容差, 即 H1=100 命中 H1∈[95,105] 的所有产品。
        后端 AdminProductService 已 hardcode <code>tolerance=5</code>, 前端不暴露切换.
      </p>
      <p class="text-sm leading-6 mt-1">
        多字段组合走 AND 关系 (收窄), 单字段命中即返回 (公开搜索 8 字段同时支持).
      </p>

      <h3 class="text-sm font-medium mt-3 mb-1">字段说明 (常用 10 个)</h3>
      <table class="w-full text-sm">
        <thead>
          <tr class="hairline-b text-left text-xs text-muted">
            <th class="py-1 pr-2">字段</th>
            <th class="py-1 pr-2">单位</th>
            <th class="py-1 pr-2">说明</th>
            <th class="py-1 pr-2">示例</th>
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
        完整字段说明见后台产品表单每个字段后的 <code>?</code> 图标 (鼠标悬停).
      </p>
    </section>

    <!-- 5. FAQ -->
    <section id="faq" class="hairline p-4 mb-3">
      <h2 class="text-base font-medium mb-2">5. 常见问题 (FAQ)</h2>
      <el-collapse>
        <el-collapse-item v-for="(f, i) in faqs" :key="i" :title="`Q${i + 1}. ${f.q}`" :name="String(i)">
          <div class="text-sm text-[var(--color-text-muted)] pl-2 leading-6">
            {{ f.a }}
          </div>
        </el-collapse-item>
      </el-collapse>
    </section>

    <p class="text-xs text-muted text-center mt-4">
      SakuraFilter 后台 v0.1 · Day 10+ P5.4 帮助页
    </p>
  </div>
</template>

<style scoped>
.help-anchor :deep(.el-anchor__link) {
  font-size: 13px;
}
</style>
