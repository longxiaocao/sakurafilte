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
  {
    path: '/product/:oem',
    name: 'ProductDetail',
    component: () => import('@/views/ProductDetailView.vue'),
    meta: { title: '产品详情' }
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
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

// 鉴权守卫
router.beforeEach((to, _from, next) => {
  if (to.meta.requireAuth) {
    const auth = useAdminAuthStore()
    if (!auth.token) {
      ElMessage.warning('请先输入 X-Admin-Token 进入后台')
      next('/search')
      return
    }
  }
  next()
})

export default router
