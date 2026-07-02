// Day 11 P4.3 (Task 14.3): Playwright 视觉回归 — 产品对比页 (P3.5)
import { test, expect } from '@playwright/test'
import path from 'path'
import fs from 'fs'
import pixelmatch from 'pixelmatch'
import { PNG } from 'pngjs'

const BASE = process.env.BASE_URL || 'http://localhost:5173'
const ADMIN_TOKEN = process.env.ADMIN_TOKEN || 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'
const DIFF_THRESHOLD = 0.05

const BASELINE_DIR = path.join(__dirname, '__screenshots__', 'baselines')
const CURRENT_DIR = path.join(__dirname, '__screenshots__', 'current')

test('P4.3 产品对比页 (P3.5) 视觉回归', async ({ page }) => {
  if (!fs.existsSync(BASELINE_DIR)) fs.mkdirSync(BASELINE_DIR, { recursive: true })
  if (!fs.existsSync(CURRENT_DIR)) fs.mkdirSync(CURRENT_DIR, { recursive: true })

  await page.addInitScript((token) => {
    localStorage.setItem('sakura_admin_token', token)
  }, ADMIN_TOKEN)

  // 6 个产品 ID (需后端已导入 ≥ 6 个产品)
  await page.goto(`${BASE}/admin/compare?ids=1,2,3,4,5,6`, { waitUntil: 'networkidle', timeout: 15000 })
  await page.waitForSelector('.compare-grid', { timeout: 10000 })
  await page.waitForTimeout(500)

  const currentPath = path.join(CURRENT_DIR, 'compare-6.png')
  await page.screenshot({ path: currentPath, fullPage: true })

  const baselinePath = path.join(BASELINE_DIR, 'compare-6.png')
  if (!fs.existsSync(baselinePath)) {
    fs.copyFileSync(currentPath, baselinePath)
    console.log(`[BASELINE] 创建: ${baselinePath}`)
    return
  }

  const baselinePng = PNG.sync.read(fs.readFileSync(baselinePath))
  const currentPng = PNG.sync.read(fs.readFileSync(currentPath))

  if (baselinePng.width !== currentPng.width || baselinePng.height !== currentPng.height) {
    throw new Error(`对比页尺寸变化: ${baselinePng.width}x${baselinePng.height} → ${currentPng.width}x${currentPng.height}`)
  }

  const diff = new PNG({ width: baselinePng.width, height: baselinePng.height })
  const changed = pixelmatch(baselinePng.data, currentPng.data, diff.data, baselinePng.width, baselinePng.height, { threshold: 0.1 })
  const ratio = changed / (baselinePng.width * baselinePng.height)
  if (ratio > DIFF_THRESHOLD) {
    fs.writeFileSync(path.join(CURRENT_DIR, 'compare-6.diff.png'), PNG.sync.write(diff))
    throw new Error(`对比页视觉退化: ${(ratio * 100).toFixed(2)}% > ${(DIFF_THRESHOLD * 100).toFixed(0)}%`)
  }
  console.log(`[OK] 对比页视觉差 ${(ratio * 100).toFixed(2)}%`)
})
