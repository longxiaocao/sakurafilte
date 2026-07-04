// Day 9: 路由
//   /search                  — 前台产品搜索
//   /product/:oem            — 前台产品详情
//   /admin/products          — 后台产品管理列表
//   /admin/products/new      — 后台新增产品
//   /admin/products/:id/edit — 后台编辑产品
//   /admin/etl               — 后台 ETL 触发 + 进度
import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { useAdminAuthStore } from '@/composables/useAdminAuth'
import { ElMessage } from 'element-plus'

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    redirect: '/search'
  },
  {
    path: '/search',
    name: 'Search',
    component: () => import('@/views/SearchView.vue'),
    meta: { title: '产品搜索' }
  },
  // ===== P3.4 (Task 11.5): 公开搜索页 8 字段多框 (公开, 无需 token) =====
  {
    path: '/public/search',
    name: 'PublicSearch',
    component: () => import('@/views/public/PublicSearchView.vue'),
    meta: { title: '产品搜索 (8 字段)' }
  },
  {
    path: '/product/:oem',
    name: 'ProductDetail',
    component: () => import('@/views/public/PublicProductView.vue'),
    meta: { title: '产品详情' }
  },
  // ===== 需求 4: 后台登录页 (替换 TOKEN 直接输入弹窗) =====
  //   - 公开路由 (requireAuth 不设置, 默认 falsy)
  //   - 登录页内本地映射验证, 成功后写入 useAdminAuthStore.token
  //   - 支持 redirect 查询参数回跳
  {
    path: '/login',
    name: 'Login',
    component: () => import('@/views/LoginView.vue'),
    meta: { title: '后台登录' }
  },
  {
    path: '/admin',
    redirect: '/admin/products'
  },
  {
    path: '/admin/products',
    name: 'AdminProducts',
    component: () => import('@/views/admin/AdminProductsView.vue'),
    meta: { title: '后台产品管理', requireAuth: true }
  },
  {
    path: '/admin/products/new',
    name: 'AdminProductNew',
    component: () => import('@/views/admin/AdminProductFormView.vue'),
    meta: { title: '新增产品', requireAuth: true }
  },
  {
    path: '/admin/products/:id/edit',
    name: 'AdminProductEdit',
    component: () => import('@/views/admin/AdminProductFormView.vue'),
    meta: { title: '编辑产品', requireAuth: true }
  },
  {
    path: '/admin/etl',
    name: 'AdminEtl',
    component: () => import('@/views/admin/AdminEtlView.vue'),
    meta: { title: 'ETL 触发', requireAuth: true }
  },
  {
    path: '/admin/dict/oem-brands',
    name: 'AdminOemBrands',
    component: () => import('@/views/admin/AdminOemBrandsView.vue'),
    meta: { title: 'OEM 品牌字典', requireAuth: true }
  },
  // ===== Day 10+ P2.2: 7 个新字典管理页 =====
  {
    path: '/admin/dict/product-name1s',
    name: 'AdminProductName1s',
    component: () => import('@/views/admin/AdminProductName1sView.vue'),
    meta: { title: '产品名 1 字典', requireAuth: true }
  },
  {
    path: '/admin/dict/product-name2s',
    name: 'AdminProductName2s',
    component: () => import('@/views/admin/AdminProductName2sView.vue'),
    meta: { title: '产品名 2 字典', requireAuth: true }
  },
  {
    path: '/admin/dict/types',
    name: 'AdminTypes',
    component: () => import('@/views/admin/AdminTypesView.vue'),
    meta: { title: '类型字典 (Type)', requireAuth: true }
  },
  {
    path: '/admin/dict/oem-no3s',
    name: 'AdminOemNo3s',
    component: () => import('@/views/admin/AdminOemNo3sView.vue'),
    meta: { title: 'OEM 3 字典', requireAuth: true }
  },
  {
    path: '/admin/dict/medias',
    name: 'AdminMedias',
    component: () => import('@/views/admin/AdminMediasView.vue'),
    meta: { title: '介质字典 (Media)', requireAuth: true }
  },
  {
    path: '/admin/dict/machines',
    name: 'AdminMachines',
    component: () => import('@/views/admin/AdminMachinesView.vue'),
    meta: { title: '机型字典 (Machine)', requireAuth: true }
  },
  {
    path: '/admin/dict/engines',
    name: 'AdminEngines',
    component: () => import('@/views/admin/AdminEnginesView.vue'),
    meta: { title: '发动机字典 (Engine)', requireAuth: true }
  },
  // ===== P3.5 (Task 12): 产品对比 UI 完整版 =====
  //   URL: /admin/compare?ids=1,2,3,4,5,6
  //   最多 6 个产品, 列可调序 (持久化到 localStorage), 打印友好
  {
    path: '/admin/compare',
    name: 'AdminCompare',
    component: () => import('@/views/admin/AdminCompareView.vue'),
    meta: { title: '产品对比', requireAuth: true }
  },
  // ===== P5.4 (Task 15.4): 后台帮助/文档页 =====
  //   5 模块: 快速开始 / 字典规范 / 批量导入 / 搜索容差 / FAQ
  //   字段说明文案从 data/field-help.ts 复用
  {
    path: '/admin/help',
    name: 'AdminHelp',
    component: () => import('@/views/admin/AdminHelpView.vue'),
    meta: { title: '后台帮助', requireAuth: true }
  },
  // ===== P5.5+: 后端性能监控面板 =====
  //   P50/P95/P99/ErrorRate + 健康探针 + Token 轮转状态
  //   调 /api/perf (公开) + /api/admin/auth/status (需 token) + /health/* (公开)
  {
    path: '/admin/perf',
    name: 'AdminPerf',
    component: () => import('@/views/admin/AdminPerfView.vue'),
    meta: { title: '性能监控', requireAuth: true }
  },
  // ===== 需求 6: 前端优化 Demo 演示页 =====
  //   - 整合展示需求 1-5 的所有优化点
  //   - 提供产品详情页 3 种布局方案对比 (A/B/C)
  //   - 公开路由 (无需 token), 用于演示和决策参考
  {
    path: '/demo',
    name: 'Demo',
    component: () => import('@/views/DemoView.vue'),
    meta: { title: '前端优化 Demo' }
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

// 鉴权守卫
//   需求 4: 未登录访问 /admin/* 时重定向到 /login?redirect=xxx
//   登录页 LoginView 验证成功后回跳到 redirect 目标
router.beforeEach((to, _from, next) => {
  if (to.meta.requireAuth) {
    const auth = useAdminAuthStore()
    if (!auth.token) {
      ElMessage.warning('请先登录')
      next({ path: '/login', query: { redirect: to.fullPath } })
      return
    }
  }
  next()
})

export default router
