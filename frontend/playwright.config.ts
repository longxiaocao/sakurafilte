// Day 11 P4.3 + P0-E2E-2: Playwright 配置
//   testDir: './tests' 包含两个子目录
//     - visual/: 视觉回归 (依赖数据, 本地跑)
//     - functional/: 功能性 smoke (CI 空库友好, 只验证页面加载)
//   WHY 拆分: CI 空库跑视觉回归会因 baseline 不匹配失败, 功能性 smoke 不依赖数据
import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,  // 共享一个 dev server, 顺序跑
  workers: 1,
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
  use: {
    baseURL: process.env.BASE_URL || 'http://localhost:5173',
    headless: true,
    viewport: { width: 1440, height: 900 },
    actionTimeout: 10000,
    navigationTimeout: 15000
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] }
    }
  ]
})
