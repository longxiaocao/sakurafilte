import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import path from 'node:path'

// Day 9: Vite 閰嶇疆
//   - @ 鍒悕: @/ -> src/
//   - 寮€鍙戞湇鍔″櫒: 5173 (CORS 鐧藉悕鍗?
//   - API 浠ｇ悊: /api -> http://localhost:5148 (鍚庣 Kestrel)
//   P5.5+: /health 浠ｇ悊 (鍋ュ悍鎺㈤拡绔偣涓嶅湪 /api 鍓嶇紑涓? 闇€鐙珛浠ｇ悊)
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src')
    }
  },
  server: {
    port: 5175,
    host: '0.0.0.0',
    proxy: {
      '/api': {
        target: 'http://localhost:5148',
        changeOrigin: true
      },
      '/health': {
        target: 'http://localhost:5148',
        changeOrigin: true
      }
    }
  },
  build: {
    outDir: 'dist',
    sourcemap: false,
    chunkSizeWarningLimit: 1500
  }
})

