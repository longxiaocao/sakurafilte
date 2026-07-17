// Day 11 P4.3 (Task 14.3): Playwright 视觉回归 — 前台产品详情页 (P3.3)
// P0-E2E-1 修复: ESM 模式下 __dirname 不存在, 用 fileURLToPath 兼容
//
// V2 Task 5.3.5 基线重置说明:
//   V2 架构迁移将产品详情页改为 Razor Pages SSR + Vue partial hydration,
//   原 public-product.png 基线基于纯 Vue SPA (el-collapse-item 结构), 已失效。
//
//   重置步骤:
//     1. 启动后端 (dotnet run --project backend/src/SakuraFilter.Api) 含 V2 产品数据
//     2. 启动前端 (cd frontend && npm run dev)
//     3. 重置基线: cd frontend && npm run test:visual:update -- tests/visual/public-product.spec.ts
//     4. 验证新基线: cd frontend && npm run test:visual -- tests/visual/public-product.spec.ts
//
//   V2 新增视觉基线 (v2-visual-baseline.spec.ts):
//     - v2-aggregate-search.png (聚合搜索页)
//     - v2-admin-xref-reorder.png (OEM 3 排序管理页)
//     - v2-admin-product-form.png (V2 产品表单)
//     - v2-public-product-seo.png (Razor SSR 产品详情页)
//
//   注意: 重置 public-product.png 前确认 V2 Razor SSR 路由已生效 (/products/... 而非 /product/...)
import { test, expect } from '@playwright/test'
import path, { dirname } from 'path'
import { fileURLToPath } from 'url'
import fs from 'fs'
import pixelmatch from 'pixelmatch'
import { PNG } from 'pngjs'

const __filename = fileURLToPath(import.meta.url)
const __dirname = dirname(__filename)

const BASE = process.env.BASE_URL || 'http://localhost:5173'
const DIFF_THRESHOLD = 0.05

const BASELINE_DIR = path.join(__dirname, '__screenshots__', 'baselines')
const CURRENT_DIR = path.join(__dirname, '__screenshots__', 'current')

// P0505921 是 spike-test 库中的公开产品 (Air filter)
const PRODUCT_OEM = 'P0505921'

test('P4.3 前台产品详情页 (P3.3) 视觉回归', async ({ page }) => {
  if (!fs.existsSync(BASELINE_DIR)) fs.mkdirSync(BASELINE_DIR, { recursive: true })
  if (!fs.existsSync(CURRENT_DIR)) fs.mkdirSync(CURRENT_DIR, { recursive: true })

  // 公开页无需 token
  await page.goto(`${BASE}/product/${PRODUCT_OEM}`, { waitUntil: 'networkidle', timeout: 15000 })
  await page.waitForSelector('.el-collapse-item', { timeout: 10000 }).catch(() => null)
  await page.waitForTimeout(500)

  const currentPath = path.join(CURRENT_DIR, 'public-product.png')
  await page.screenshot({ path: currentPath, fullPage: true })

  const baselinePath = path.join(BASELINE_DIR, 'public-product.png')
  if (!fs.existsSync(baselinePath)) {
    fs.copyFileSync(currentPath, baselinePath)
    console.log(`[BASELINE] 创建: ${baselinePath}`)
    return
  }

  const baselinePng = PNG.sync.read(fs.readFileSync(baselinePath))
  const currentPng = PNG.sync.read(fs.readFileSync(currentPath))

  if (baselinePng.width !== currentPng.width || baselinePng.height !== currentPng.height) {
    throw new Error(`详情页尺寸变化: ${baselinePng.width}x${baselinePng.height} → ${currentPng.width}x${currentPng.height}`)
  }

  const diff = new PNG({ width: baselinePng.width, height: baselinePng.height })
  const changed = pixelmatch(baselinePng.data, currentPng.data, diff.data, baselinePng.width, baselinePng.height, { threshold: 0.1 })
  const ratio = changed / (baselinePng.width * baselinePng.height)
  if (ratio > DIFF_THRESHOLD) {
    fs.writeFileSync(path.join(CURRENT_DIR, 'public-product.diff.png'), PNG.sync.write(diff))
    throw new Error(`详情页视觉退化: ${(ratio * 100).toFixed(2)}% > ${(DIFF_THRESHOLD * 100).toFixed(0)}%`)
  }
  console.log(`[OK] 详情页视觉差 ${(ratio * 100).toFixed(2)}%`)
})
