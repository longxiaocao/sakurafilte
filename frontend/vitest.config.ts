// Day 11 P4.2 + P2.6 单测扩展: vitest 配置
//   - contract: 契约测试 (node 环境)
//   - unit: 组件单元测试 (jsdom 环境, 含 @vue/test-utils + @vitejs/plugin-vue)
// V24-F79 (2026-07-18): 添加 coverage 配置 (spec 26.15.7 建议 2)
//   - provider: 'v8' 用 V8 引擎原生覆盖率, 比 istanbul 快 (规则 4.3 已评估必要性)
//   - reporter: ['text', 'cobertura', 'html'] → CI 用 cobertura, 本地用 html 可视化
//   - include: 只统计 src/ 下业务代码, 排除 tests/ + node_modules/
//   - thresholds: 不在此强制 (避免本地开发被阻塞), 由 CI workflow 用 reportgenerator 做门禁
import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath } from 'url'

export default defineConfig({
  plugins: [vue()],
  test: {
    globals: true,
    // P2.6 单测: 使用 jsdom 支持 DOM 操作 (mount/find)
    environment: 'jsdom',
    // 包含 contract + 新增 unit 测试
    include: ['tests/contract/**/*.test.ts', 'tests/unit/**/*.test.ts'],
    testTimeout: 30000,
    reporters: ['verbose'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'cobertura', 'html'],
      reportsDirectory: './coverage',
      include: ['src/**/*.{ts,vue}'],
      exclude: [
        'src/**/*.d.ts',
        'src/**/*.test.ts',
        'src/**/__tests__/**',
        'src/main.ts',              // 入口文件, 无可测逻辑
        'src/i18n/**',              // i18n 资源文件, 纯数据
        'src/api/generated-types.ts' // 自动生成, 不应统计
      ]
    }
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  }
})
