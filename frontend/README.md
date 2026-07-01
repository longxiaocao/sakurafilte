# SakuraFilter 前端 (Day 9)

Vue 3 + Vite + Element Plus + Tailwind CSS + TypeScript

## 启动

```bash
# 1. 安装依赖
npm install

# 2. 启动 dev server (默认 http://localhost:5173)
npm run dev

# 3. 类型检查
npm run type-check

# 4. 生产构建
npm run build
```

启动前确保后端已运行 (默认 `http://localhost:5000`)。
Vite dev server 已配置 `/api` 代理，自动转发到后端。

## 后端鉴权

后台页面 (`/admin/*`) 需输入 `X-Admin-Token`，与后端 `appsettings.json` 中 `Auth:DevStaticToken` 保持一致。

Token 保存在 `localStorage.sakurafilter_admin_token`，刷新页面后自动恢复。

## 目录结构

```
frontend/
├── src/
│   ├── api/             # API 客户端 + 类型定义
│   │   ├── index.ts     # 按业务域拆分的 API 方法
│   │   └── types.ts     # TypeScript 接口
│   ├── components/      # 通用组件
│   │   └── AppHeader.vue
│   ├── composables/     # 组合式函数
│   │   └── useAdminAuth.ts
│   ├── router/          # 路由 + 鉴权守卫
│   │   └── index.ts
│   ├── styles/          # 全局样式 (Tailwind + Musk 风格)
│   │   └── index.css
│   ├── utils/           # 工具函数
│   │   └── http.ts      # Axios 实例 + 拦截器
│   ├── views/           # 页面
│   │   ├── SearchView.vue           # 前台产品搜索
│   │   ├── ProductDetailView.vue    # 前台产品详情
│   │   └── admin/
│   │       ├── AdminProductsView.vue     # 后台产品管理
│   │       ├── AdminProductFormView.vue  # 后台产品表单 (新增/编辑)
│   │       └── AdminEtlView.vue          # 后台 ETL 触发 + 进度
│   ├── App.vue
│   └── main.ts
├── index.html
├── package.json
├── tailwind.config.js
├── postcss.config.js
├── tsconfig.json
└── vite.config.ts
```

## 页面清单

| 路径 | 说明 | 鉴权 |
| --- | --- | --- |
| `/search` | 前台产品搜索 (Meili + PG 兜底) | 否 |
| `/product/:oem` | 产品详情 (规格 + xref + apps + 图片) | 否 |
| `/admin/products` | 后台产品管理 (高级搜索 + 批量对比) | 是 |
| `/admin/products/new` | 后台新增产品 (7 分区表单) | 是 |
| `/admin/products/:id/edit` | 后台编辑产品 | 是 |
| `/admin/etl` | 后台 ETL 触发 + 实时进度 | 是 |

## 设计风格 (Musk 极简)

- 纯黑/白 + 单一蓝色强调色
- 无阴影, 1px hairline 边框
- Inter / SF Pro 字体
- 8px 网格间距
- Element Plus 圆角/阴影全局去除

## 错误处理

所有 API 请求统一通过 `src/utils/http.ts` 中的 Axios 拦截器处理:

- `401` — 鉴权失败, 提示检查 `X-Admin-Token`
- `403` — 权限不足
- `404` — 资源不存在
- `409` — 状态冲突 (如 ETL 任务正在运行)
- `429` — 限流, 按 `Retry-After` 头提示
- `5xx` — 服务器错误
- ProblemDetails (RFC 7807) 格式 `detail` 字段透传

## 限流

- 后台 CRUD 路径: 600 次/分钟 (全局)
- 前台搜索路径: 300 次/分钟
- ETL 触发: 30 次/分钟
