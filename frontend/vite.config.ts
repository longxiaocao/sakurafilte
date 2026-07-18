import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import path from 'node:path'
// V24-F44 (spec F3-13 修复方案 4): 读取 package.json 版本号作为 API 版本标识
//   WHY import json: tsconfig.json 已开启 resolveJsonModule, Vite 默认支持 json import
//   用途: define __API_VERSION__ 全局常量, http.ts 请求拦截器读取后写入 X-Client-Version 头
//   后端可根据 X-Client-Version 路由到对应 API (灰度发布兼容, 旧前端走旧 API)
import pkg from './package.json' with { type: 'json' }

// Vite 配置
//   - @ 别名: @/ -> src/
//   - 开发服务器: 5175 (CORS 白名单)
//   - API 代理: /api -> http://localhost:5148 (后端 Kestrel)
//   - /health 代理 (健康探针端点不在 /api 前缀中, 需独立代理)
//   V2 Task 4.5.5: 多入口 + manualChunks vue
//     - main: index.html (SPA)
//     - product-detail-client: src/product-detail-client.ts (SEO 详情页 client mount)
//       产物 /assets/product-detail-client.js, 由 Detail.cshtml 静态引用 (无 hash)
//     - manualChunks: vue/vue-router/pinia 单独 chunk, 主 bundle 更小
export default defineConfig({
  plugins: [vue()],
  // V24-F44 (spec F3-13 修复方案 4): build flag 标记 API 版本
  //   __API_VERSION__ 在构建时被替换为 package.json 的 version 字符串
  //   http.ts 请求拦截器读取后写入 X-Client-Version 头, 后端可选择性路由
  define: {
    __API_VERSION__: JSON.stringify(pkg.version)
  },
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
    chunkSizeWarningLimit: 1500,
    rollupOptions: {
      // V2 Task 4.5.5: 多入口 (SPA main + SEO 详情页 client mount)
      input: {
        main: path.resolve(__dirname, 'index.html'),
        'product-detail-client': path.resolve(__dirname, 'src/product-detail-client.ts')
      },
      output: {
        // 入口文件: product-detail-client 固定文件名 (Detail.cshtml 静态引用), 其他入口带 hash
        entryFileNames: 'assets/[name].js',
        // chunk 文件: 带 hash (缓存策略)
        chunkFileNames: 'assets/[name]-[hash].js',
        assetFileNames: 'assets/[name]-[hash].[ext]',
        // V2 Task 4.5.5: vue 系列单独 chunk, 避免重复打包
        manualChunks: {
          vue: ['vue', 'vue-router', 'pinia']
        }
      }
    }
  }
})
