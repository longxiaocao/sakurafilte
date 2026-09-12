// Day 11 P4.3 + P0-E2E-2: Playwright 配置
//   testDir: './tests' 包含两个子目录
//     - visual/: 视觉回归 (依赖数据, 本地跑)
//     - functional/: 功能性 smoke (CI 空库友好, 只验证页面加载)
//   WHY 拆分: CI 空库跑视觉回归会因 baseline 不匹配失败, 功能性 smoke 不依赖数据
import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './tests',
  // P0-E2E 修复: 只匹配 .spec.ts, 排除 vitest 的 .test.ts (避免 Playwright 误扫 contract 目录)
  testMatch: '**/*.spec.ts',
  // 全并行: 各 spec 文件有独立 beforeAll/afterAll, 无跨文件共享状态; describe.serial 保护文件内串行
  //   WHY workers=2: 平衡速度和 AuthPermitsPerMinute=5 限流; 4 workers 同时登录会 429
  fullyParallel: true,
  workers: 2,
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
  use: {
    baseURL: process.env.BASE_URL || 'http://localhost:5173',
    headless: true,
    viewport: { width: 1440, height: 900 },
    actionTimeout: 10000,
    navigationTimeout: 15000,
    // 本地可用 BASE_URL=https://localhost (自签证书) 跑 E2E; CI 用 http dev server 不受影响
    ignoreHTTPSErrors: true,
    // 🔧 fix(2026-08-22): 本机无 playwright chromium 时可用系统浏览器 (BROWSER_CHANNEL=msedge/chrome),
    //   未设置则走 playwright 自带 chromium (CI 默认), 行为不变
    channel: process.env.BROWSER_CHANNEL as any || undefined
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] }
    },
    // 🔧 add(2026-09-13 生产测试): 跨浏览器兼容性 — ENABLE_CROSS_BROWSER=1 时启用 firefox/webkit
    //   默认关闭: firefox/webkit 渲染差异可能引入 flaky, 仅生产部署测试/发布前手动开启
    ...(process.env.ENABLE_CROSS_BROWSER === '1'
      ? [
          { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
          { name: 'webkit', use: { ...devices['Desktop Safari'] } }
        ]
      : [])
  ]
})
