// Day 11 P4.3 (Task 14.3): 字典管理页 Playwright 视觉回归
//   - 8 个字典管理页都截全页图
//   - 首次跑生成 baseline (tests/visual/baselines/*.png)
//   - 后续跑用 pixelmatch 像素 diff > 5% 失败
//   - CI: 跑 visual-regression step, 失败 ::error:: 注解 + exit 1
//
//   用法:
//     npm run test:visual   (package.json 已加)
//     npx playwright install chromium   (首次)
//
//   实现原则:
//     - 用最简 selector: .el-table (Element Plus 表格统一 class)
//     - 等数据加载 (.el-table__row 出现) 再截图
//     - 注入 admin token 到 localStorage
import { test, expect } from '@playwright/test'
import path from 'path'
import fs from 'fs'
import pixelmatch from 'pixelmatch'
import { PNG } from 'pngjs'

const BASE = process.env.BASE_URL || 'http://localhost:5173'
const ADMIN_TOKEN = process.env.ADMIN_TOKEN || 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'
const DIFF_THRESHOLD = 0.05  // 5% 像素差 视为视觉退化

// 8 个字典 (P1.3 OEM 品牌 + P2.2 7 个新字典)
const DICTS: Array<{ name: string; path: string; label: string }> = [
  { name: 'oem-brands',    path: '/admin/dict/oem-brands',    label: 'OEM 品牌' },
  { name: 'product-name1', path: '/admin/dict/product-name1', label: '产品名 1' },
  { name: 'product-name2', path: '/admin/dict/product-name2', label: '产品名 2' },
  { name: 'type',          path: '/admin/dict/type',          label: '类型' },
  { name: 'oem-no3',       path: '/admin/dict/oem-no3',       label: 'OEM 3' },
  { name: 'media',         path: '/admin/dict/media',         label: '介质' },
  { name: 'machine',       path: '/admin/dict/machine',       label: '机器' },
  { name: 'engine',        path: '/admin/dict/engine',        label: '发动机' }
]

const BASELINE_DIR = path.join(__dirname, '__screenshots__', 'baselines')
const CURRENT_DIR = path.join(__dirname, '__screenshots__', 'current')

for (const dict of DICTS) {
  test(`P4.3 dict ${dict.name} 视觉回归 (5% 阈值)`, async ({ page }) => {
    // 0. 准备目录
    if (!fs.existsSync(BASELINE_DIR)) fs.mkdirSync(BASELINE_DIR, { recursive: true })
    if (!fs.existsSync(CURRENT_DIR)) fs.mkdirSync(CURRENT_DIR, { recursive: true })

    // 1. 注入 admin token (走 useAdminAuth composable, key: sakura_admin_token)
    await page.addInitScript((token) => {
      localStorage.setItem('sakura_admin_token', token)
    }, ADMIN_TOKEN)

    // 2. 打开页面
    await page.goto(`${BASE}${dict.path}`, { waitUntil: 'networkidle', timeout: 15000 })

    // 3. 等 Element Plus 表格渲染 (.el-table)
    await page.waitForSelector('.el-table', { timeout: 10000 })
    // 等至少 1 行数据
    await page.waitForSelector('.el-table__row', { timeout: 10000 }).catch(() => null)
    // 给数据加载留 500ms
    await page.waitForTimeout(500)

    // 4. 截图全页
    const currentPath = path.join(CURRENT_DIR, `dict-${dict.name}.png`)
    await page.screenshot({ path: currentPath, fullPage: true })

    // 5. 与 baseline 比对
    const baselinePath = path.join(BASELINE_DIR, `dict-${dict.name}.png`)
    if (!fs.existsSync(baselinePath)) {
      // 首次跑: 写 baseline, 不视为失败
      fs.copyFileSync(currentPath, baselinePath)
      console.log(`[BASELINE] 创建: ${baselinePath}`)
      return
    }

    // 像素 diff
    const baselinePng = PNG.sync.read(fs.readFileSync(baselinePath))
    const currentPng = PNG.sync.read(fs.readFileSync(currentPath))

    // 尺寸不一致 (前端布局改了) → 直接 fail
    if (baselinePng.width !== currentPng.width || baselinePng.height !== currentPng.height) {
      throw new Error(
        `dict ${dict.name} 尺寸变化: baseline=${baselinePng.width}x${baselinePng.height}, current=${currentPng.width}x${currentPng.height}`
      )
    }

    const diff = new PNG({ width: baselinePng.width, height: baselinePng.height })
    const changed = pixelmatch(
      baselinePng.data,
      currentPng.data,
      diff.data,
      baselinePng.width,
      baselinePng.height,
      { threshold: 0.1 }  // 像素级 10% 颜色差
    )
    const totalPixels = baselinePng.width * baselinePng.height
    const changedRatio = changed / totalPixels

    if (changedRatio > DIFF_THRESHOLD) {
      // 写 diff 图便于人工 review
      fs.writeFileSync(path.join(CURRENT_DIR, `dict-${dict.name}.diff.png`), PNG.sync.write(diff))
      throw new Error(
        `dict ${dict.name} 视觉退化: ${(changedRatio * 100).toFixed(2)}% 像素差 > ${(DIFF_THRESHOLD * 100).toFixed(0)}% 阈值\n` +
        `  baseline: ${baselinePath}\n` +
        `  current:  ${currentPath}\n` +
        `  diff:     ${path.join(CURRENT_DIR, `dict-${dict.name}.diff.png`)}`
      )
    }

    console.log(`[OK] dict ${dict.name} 视觉差 ${(changedRatio * 100).toFixed(2)}% ≤ ${(DIFF_THRESHOLD * 100).toFixed(0)}%`)
  })
}
