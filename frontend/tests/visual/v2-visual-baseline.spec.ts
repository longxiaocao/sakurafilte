// V2 Task 5.3.5: V2 架构迁移视觉回归基线
//
// 设计:
//   - V2 改动了产品详情页 (Razor SSR 替代纯 Vue), 原 public-product.png 基线已失效
//   - V2 新增聚合搜索页 (AggregateSearchView) + OEM 3 排序管理页 (AdminXrefReorderView)
//   - 此文件为 V2 新页面建立视觉回归基线
//
// 基线重置说明 (V2 Task 5.3.5):
//   1. public-product.png (旧基线): V2 Razor SSR 渲染后页面结构变化, 需重置
//      - 重置命令: cd frontend && npm run test:visual:update -- tests/visual/public-product.spec.ts
//      - 重置原因: V2 用 Razor Pages SSR 替代纯 Vue SPA, HTML 结构 + CSS 类名变化
//      - 影响范围: 产品详情页 (旧 /product/{oem} → 新 /products/{pn1-mr1}/{pn2}/{brand}/{oem3})
//
//   2. compare-6.png / compare.png (对比页): V2 对比页 UI 基本不变, 暂不重置
//      - 若对比页引用了 SEO URL 跳转, 可能需重置
//      - 重置命令: cd frontend && npm run test:visual:update -- tests/visual/compare-6.spec.ts
//
//   3. dict-*.png (8 个字典页): V2 未改 dict 表, 但导航/布局可能有微小变化
//      - 若 dict 页测试失败, 先检查是否为 V2 导航改动导致
//      - 重置命令: cd frontend && npm run test:visual:update -- tests/visual/dict-pages.spec.ts
//
//   4. V2 新页面基线 (本文件创建):
//      - v2-aggregate-search.png: 聚合搜索页
//      - v2-admin-xref-reorder.png: OEM 3 排序管理页
//      - v2-admin-product-form.png: V2 产品表单 (含 MR.1 输入 + 主图/详情图分层)
//
// 重置策略:
//   - 首次运行此测试时, 基线不存在 → 自动创建基线 (不报错)
//   - 后续运行时, 与基线对比, 差异 > 5% 报视觉退化
//   - V2 升级后若 UI 故意变化, 用 npm run test:visual:update 重置基线
//
// 依赖:
//   - 前端 dev server 或 prod build
//   - 后端 API (含 V2 产品数据)
//   - Playwright + pixelmatch + pngjs
import { test, expect } from '@playwright/test'
import path, { dirname } from 'path'
import { fileURLToPath } from 'url'
import fs from 'fs'
import pixelmatch from 'pixelmatch'
import { PNG } from 'pngjs'

const __filename = fileURLToPath(import.meta.url)
const __dirname = dirname(__filename)

const BASE = process.env.BASE_URL || 'http://localhost:5173'
const ADMIN_TOKEN = process.env.ADMIN_TOKEN || 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'
const DIFF_THRESHOLD = 0.05

const BASELINE_DIR = path.join(__dirname, '__screenshots__', 'baselines')
const CURRENT_DIR = path.join(__dirname, '__screenshots__', 'current')

async function injectAdminToken(page: import('@playwright/test').Page) {
  await page.addInitScript((token) => {
    localStorage.setItem('sakura_admin_token', token)
  }, ADMIN_TOKEN)
}

/**
 * 视觉回归对比通用函数
 *   - 首次运行: 自动创建基线 (不报错)
 *   - 后续运行: 与基线对比, 差异 > DIFF_THRESHOLD 报视觉退化
 */
async function compareVisual(page: import('@playwright/test').Page, baselineName: string) {
  if (!fs.existsSync(BASELINE_DIR)) fs.mkdirSync(BASELINE_DIR, { recursive: true })
  if (!fs.existsSync(CURRENT_DIR)) fs.mkdirSync(CURRENT_DIR, { recursive: true })

  const currentPath = path.join(CURRENT_DIR, baselineName)
  await page.screenshot({ path: currentPath, fullPage: true })

  const baselinePath = path.join(BASELINE_DIR, baselineName)
  if (!fs.existsSync(baselinePath)) {
    fs.copyFileSync(currentPath, baselinePath)
    console.log(`[V2 BASELINE] 创建: ${baselinePath}`)
    return
  }

  const baselinePng = PNG.sync.read(fs.readFileSync(baselinePath))
  const currentPng = PNG.sync.read(fs.readFileSync(currentPath))

  if (baselinePng.width !== currentPng.width || baselinePng.height !== currentPng.height) {
    throw new Error(`V2 视觉尺寸变化 (${baselineName}): ${baselinePng.width}x${baselinePng.height} → ${currentPng.width}x${currentPng.height}`)
  }

  const diff = new PNG({ width: baselinePng.width, height: baselinePng.height })
  const changed = pixelmatch(baselinePng.data, currentPng.data, diff.data, baselinePng.width, baselinePng.height, { threshold: 0.1 })
  const ratio = changed / (baselinePng.width * baselinePng.height)
  if (ratio > DIFF_THRESHOLD) {
    fs.writeFileSync(path.join(CURRENT_DIR, baselineName.replace('.png', '.diff.png')), PNG.sync.write(diff))
    throw new Error(`V2 视觉退化 (${baselineName}): ${(ratio * 100).toFixed(2)}% > ${(DIFF_THRESHOLD * 100).toFixed(0)}%`)
  }
  console.log(`[V2 OK] ${baselineName} 视觉差 ${(ratio * 100).toFixed(2)}%`)
}

test.describe('V2 Task 5.3.5: V2 视觉回归基线', () => {

  test('1. V2 聚合搜索页视觉基线', async ({ page }) => {
    // WHY 聚合搜索: V2 新增页面, 需建立视觉基线
    await page.goto(`${BASE}/aggregate-search`, { waitUntil: 'networkidle', timeout: 15000 })
    await page.waitForSelector('.el-input, .el-empty, body', { timeout: 10000 }).catch(() => null)
    await page.waitForTimeout(500)
    await compareVisual(page, 'v2-aggregate-search.png')
  })

  test('2. V2 OEM 3 排序管理页视觉基线', async ({ page }) => {
    // WHY XrefReorder: V2 Task 2.2 新增页面, 需建立视觉基线
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/xrefs/reorder`, { waitUntil: 'networkidle', timeout: 15000 })
    await page.waitForSelector('.el-table, .el-list, ul, body', { timeout: 10000 }).catch(() => null)
    await page.waitForTimeout(500)
    await compareVisual(page, 'v2-admin-xref-reorder.png')
  })

  test('3. V2 产品表单视觉基线 (含 MR.1 + 主图/详情图分层)', async ({ page }) => {
    // WHY V2 产品表单: V2 Task 1.1 + 3.3 改动产品表单 UI
    //   - 新增 MR.1 输入框 (主信息区)
    //   - 主图/详情图分层 (slot=1 主图按 OEM 3 命名, slot=2-6 详情图按 MR.1)
    //   - 需建立新视觉基线 (旧 AdminProductForm 截图已失效)
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/products/new`, { waitUntil: 'networkidle', timeout: 15000 })
    await page.waitForSelector('.el-form, form, body', { timeout: 10000 }).catch(() => null)
    await page.waitForTimeout(500)
    await compareVisual(page, 'v2-admin-product-form.png')
  })

  test('4. V2 SEO URL 产品详情页视觉基线 (Razor SSR)', async ({ page }) => {
    // WHY V2 SEO URL: V2 用 Razor Pages SSR 替代纯 Vue SPA, 页面结构变化
    //   - 原 public-product.png 基线基于纯 Vue SPA, 已失效
    //   - 此测试为 V2 Razor SSR 渲染的产品详情页建立新基线
    //   注: 访问旧 URL /product/{oem} 会 301 到新 SEO URL, 这里直接访问新 URL
    const seoUrl = `${BASE}/products/air-filter-000001/premium/bosch/f0001`
    await page.goto(seoUrl, { waitUntil: 'networkidle', timeout: 15000 })
    await page.waitForSelector('.el-collapse-item, .el-empty, body', { timeout: 10000 }).catch(() => null)
    await page.waitForTimeout(500)
    await compareVisual(page, 'v2-public-product-seo.png')
  })
})
