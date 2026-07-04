// Day 11 P4.2 + P2.6 单测扩展: vitest 配置
//   - contract: 契约测试 (node 环境)
//   - unit: 组件单元测试 (jsdom 环境, 含 @vue/test-utils + @vitejs/plugin-vue)
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
    reporters: ['verbose']
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  }
})
