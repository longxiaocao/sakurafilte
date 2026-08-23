<script setup lang="ts">
// 需求 6: 前端优化 Demo 演示页 + 多版方案对比
//   - 整合展示需求 1-5 的所有优化点
//   - 提供产品详情页 3 种布局方案 (A/B/C) 演示, 供整体重构决策
//   - 全部使用 CSS 变量, 跟随主题切换
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useThemeStore } from '@/stores/theme'
import { Search, ArrowLeft, Moon, Sunny, Lock, User } from '@element-plus/icons-vue'

const router = useRouter()
const theme = useThemeStore()

// 当前展示的优化点 Tab
const activeTab = ref<'search' | 'detail' | 'oem' | 'login' | 'theme'>('search')

// ===== 需求 1: 搜索框修复对比演示 =====
const demoQuery = ref('')
const demoSearched = ref(false)
const demoHits = ref<Array<{ id: number; oem: string; name: string }>>([])

// 模拟数据库 (用于演示"未搜索不显示暂无结果"的修复效果)
const MOCK_DB = [
  { id: 1, oem: 'P00050000', name: '机油滤清器 OF-500' },
  { id: 2, oem: 'P00049999', name: '空气滤清器 AF-499' },
  { id: 3, oem: 'P00049998', name: '燃油滤清器 FF-498' },
  { id: 4, oem: '11427622448', name: '液压滤清器 HF-448' }
]

function demoSearch() {
  demoSearched.value = true
  const q = demoQuery.value.trim().toLowerCase()
  demoHits.value = q
    ? MOCK_DB.filter((x) => x.oem.toLowerCase().includes(q) || x.name.toLowerCase().includes(q))
    : []
}

function resetDemo() {
  demoQuery.value = ''
  demoSearched.value = false
  demoHits.value = []
}

// 模拟旧版自动搜索行为 (有 bug: 输入即查询, 立即显示"暂无结果")
const oldAutoHits = computed(() => {
  const q = demoQuery.value.trim().toLowerCase()
  if (!q) return []
  return MOCK_DB.filter((x) => x.oem.toLowerCase().includes(q) || x.name.toLowerCase().includes(q))
})

// ===== 需求 2: 详情页布局方案对比 =====
const layoutOption = ref<'A' | 'B' | 'C' | 'D'>('D')

// 演示数据
const demoProduct = {
  oemNoDisplay: 'P00050000',
  productName1: '机油滤清器',
  productName2: 'OF-500',
  type: 'Oil Filter',
  mr1: 'MR-OF-500',
  oem2: 'SAKURA',
  isPublished: true,
  isDiscontinued: false,
  d1Mm: 95, d2Mm: 65, d3Mm: 80, d4Mm: 70,
  h1Mm: 120, h2Mm: 100, h3Mm: 85, h4Mm: 75,
  d7Thread: 'M20×1.5', d8Thread: 'M18×1.5',
  media: 'Paper', mediaModel: 'P-500',
  bypassValveLr: 0.8, bypassValveHr: 1.2,
  efficiency1: '99.5%', efficiency2: '99.8%',
  bypassPressure: 1.0, collapsePressureBar: 10,
  sealingMaterial: 'NBR', tempRange: '-30~120℃',
  qtyPerCarton: 50, weightKgs: 12.5,
  cartonLengthMm: 400, cartonWidthMm: 300, cartonHeightMm: 250,
  volumePerCartonM3: 0.03,
  crossReferences: [
    { oemBrand: 'TOYOTA', oemNo3: '04152-31010', productName1: 'Oil Filter' },
    { oemBrand: 'HONDA', oemNo3: '15400-PLA-503', productName1: 'Oil Filter' }
  ],
  machineApplications: [
    { machineBrand: 'TOYOTA', machineModel: 'HILUX', engineBrand: 'TOYOTA', engineType: '1GD-FTV' },
    { machineBrand: 'HONDA', machineModel: 'CIVIC', engineBrand: 'HONDA', engineType: 'L15B' }
  ]
}

// ===== 需求 4: 登录界面预览 =====
const loginForm = ref({ username: '', password: '' })

// ===== 跳转到实际页面 =====
function goReal(route: string) {
  router.push(route)
}

// 方案对比表数据
const planComparison = [
  { dimension: '布局风格', planD: '工业极简融合风', planA: '卡片网格 (2 列)', planB: '时间线折叠', planC: '左右分栏' },
  { dimension: '信息密度', planD: '高 (Hero 突出关键)', planA: '中', planB: '高', planC: '高' },
  { dimension: '视觉呼吸感', planD: '强 (大字号+大留白)', planA: '强', planB: '弱', planC: '中' },
  { dimension: '设计感', planD: '★★★★★ (Linear/Vercel)', planA: '★★★', planB: '★★', planC: '★★' },
  { dimension: '移动端友好', planD: '优 (3 档响应)', planA: '优 (自动 1 列)', planB: '良', planC: '差 (分栏需重写)' },
  { dimension: '图片灯箱', planD: '✓ (el-image 预览)', planA: '✗', planB: '✗', planC: '✗' },
  { dimension: '实现复杂度', planD: '中 (已实现)', planA: '低', planB: '中', planC: '高' },
  { dimension: '推荐度', planD: '★★★★★', planA: '★★★★', planB: '★★★', planC: '★★' }
]
</script>

<template>
  <div class="p-4 max-w-6xl mx-auto">
    <!-- 顶部 Hero -->
    <div class="hairline-b pb-4 mb-6">
      <div class="flex items-center gap-2 mb-2">
        <el-button size="small" @click="goReal('/search')" plain>
          <el-icon><ArrowLeft /></el-icon> 返回
        </el-button>
        <h1 class="text-2xl font-medium tracking-tight">前端优化 Demo 演示</h1>
      </div>
      <p class="text-sm text-muted">
        整合展示 5 个优化点 + 3 版详情页布局方案, 供整体重构决策参考
      </p>
    </div>

    <!-- Tab 切换 -->
    <el-tabs v-model="activeTab" class="mb-4">
      <!-- ===== 需求 1: 搜索框修复 ===== -->
      <el-tab-pane label="1. 搜索框修复" name="search">
        <div class="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <!-- 旧版 (有 bug) -->
          <section class="hairline p-4 bg-[var(--color-bg-elevated)]">
            <header class="mb-3 pb-2 hairline-b">
              <h2 class="text-sm font-medium text-red-600">❌ 旧版 (自动搜索 + 即时"暂无结果")</h2>
              <p class="text-xs text-muted mt-1">输入即触发查询, 未匹配时立即显示"暂无结果", 体验差</p>
            </header>
            <el-input v-model="demoQuery" placeholder="输入 045090 试试" size="small" clearable>
              <template #prefix><el-icon><Search /></el-icon></template>
            </el-input>
            <div class="mt-3 text-sm">
              <div v-if="!demoQuery" class="text-muted py-4 text-center">等待输入...</div>
              <div v-else-if="oldAutoHits.length === 0" class="text-red-600 py-4 text-center">
                暂无结果 <span class="text-xs">(用户尚未点搜索就提示, 困惑)</span>
              </div>
              <div v-else>
                <div v-for="h in oldAutoHits" :key="h.id" class="hairline-b py-1">
                  {{ h.oem }} - {{ h.name }}
                </div>
              </div>
            </div>
          </section>

          <!-- 新版 (已修复) -->
          <section class="hairline p-4 bg-[var(--color-bg-elevated)]">
            <header class="mb-3 pb-2 hairline-b">
              <h2 class="text-sm font-medium text-green-600">✓ 新版 (手动触发 + 中间态提示)</h2>
              <p class="text-xs text-muted mt-1">仅点搜索按钮/回车才查询, 已输入未搜索时给中间态提示</p>
            </header>
            <div class="flex gap-2">
              <el-input v-model="demoQuery" placeholder="输入 045090 试试" size="small" clearable
                @keyup.enter="demoSearch" />
              <el-button type="primary" size="small" @click="demoSearch">搜索</el-button>
              <el-button size="small" @click="resetDemo">重置</el-button>
            </div>
            <div class="mt-3 text-sm">
              <div v-if="!demoQuery" class="text-muted py-4 text-center">输入关键词开始搜索</div>
              <div v-else-if="demoQuery && !demoSearched" class="text-muted py-4 text-center">
                <el-icon class="text-2xl mb-1"><Search /></el-icon>
                <div>点击搜索按钮或按回车查询</div>
                <div class="text-xs mt-1">当前关键词: {{ demoQuery }}</div>
              </div>
              <div v-else-if="demoSearched && demoHits.length === 0" class="text-muted py-4 text-center">
                暂无结果
              </div>
              <div v-else>
                <div v-for="h in demoHits" :key="h.id" class="hairline-b py-1">
                  {{ h.oem }} - {{ h.name }}
                </div>
              </div>
            </div>
          </section>
        </div>

        <div class="hairline mt-4 p-4 bg-[var(--color-bg-elevated)]">
          <h3 class="text-sm font-medium mb-2">修复说明</h3>
          <ul class="text-xs text-muted space-y-1 list-disc pl-4">
            <li>新增 <code class="text-accent">searched</code> 标志位, 区分"已输入未搜索"与"已搜索无结果"</li>
            <li>watch(q) 修改关键词时重置 searched, 让中间态提示重新出现</li>
            <li>doSearch 函数开头设置 searched=true, 仅在主动触发后查询</li>
            <li>查看实际页面: <el-link type="primary" @click="goReal('/search')">/search</el-link></li>
          </ul>
        </div>
      </el-tab-pane>

      <!-- ===== 需求 2: 详情页布局方案 ===== -->
      <el-tab-pane label="2. 详情页布局方案" name="detail">
        <div class="flex items-center gap-2 mb-4 flex-wrap">
          <span class="text-sm text-muted">方案选择:</span>
          <el-radio-group v-model="layoutOption" size="small">
            <!-- V24-F72: el-radio label→value (Element Plus 3.0.0 弃用 label 作 value) -->
            <el-radio-button value="D">D. 工业极简融合风 (已实现, 推荐)</el-radio-button>
            <el-radio-button value="A">A. 卡片网格</el-radio-button>
            <el-radio-button value="B">B. 时间线折叠</el-radio-button>
            <el-radio-button value="C">C. 左右分栏</el-radio-button>
          </el-radio-group>
          <el-button size="small" @click="goReal('/product/P00050000')" plain>
            查看实际页面
          </el-button>
        </div>

        <!-- 方案 D: 工业极简融合风 (当前实现) -->
        <div v-if="layoutOption === 'D'" class="hairline p-4 bg-[var(--color-bg-elevated)]">
          <h3 class="text-sm font-medium mb-3 pb-2 hairline-b">
            方案 D: 工业极简融合风
            <span class="text-xs text-muted ml-2">(Linear/Vercel/Stripe 风格 + Musk 极简)</span>
          </h3>
          <!-- Hero 区迷你预览 -->
          <div class="grid grid-cols-1 lg:grid-cols-12 gap-4 mb-4">
            <div class="lg:col-span-5">
              <div class="hairline aspect-square bg-[var(--color-bg)] flex items-center justify-center text-muted text-xs">
                主图大图 + 灯箱
              </div>
            </div>
            <div class="lg:col-span-7">
              <div class="text-2xl font-medium tracking-tight">{{ demoProduct.productName1 }}</div>
              <div class="text-xs text-muted mt-1 font-mono">{{ demoProduct.oem2 }} · {{ demoProduct.mr1 }} · {{ demoProduct.oemNoDisplay }}</div>
              <div class="grid grid-cols-4 gap-2 mt-4">
                <div v-for="spec in [{l:'D1',v:'95mm'},{l:'H1',v:'120mm'},{l:'Media',v:'Paper'},{l:'Type',v:'Oil'}]" :key="spec.l"
                  class="hairline p-2">
                  <div class="text-[10px] uppercase tracking-wider text-muted">{{ spec.l }}</div>
                  <div class="text-sm font-medium mt-1 font-mono">{{ spec.v }}</div>
                </div>
              </div>
              <div class="flex gap-4 mt-3 text-xs">
                <span><strong class="text-lg font-mono">2</strong> 替代</span>
                <span><strong class="text-lg font-mono">2</strong> 车型</span>
              </div>
            </div>
          </div>
          <div class="text-xs text-muted">设计语言: 大字号对比 + 大留白 + 1px hairline + tabular-nums + uppercase tracking-wider</div>
        </div>

        <!-- 方案 A: 卡片网格 -->
        <div v-else-if="layoutOption === 'A'" class="hairline p-4 bg-[var(--color-bg-elevated)]">
          <h3 class="text-sm font-medium mb-3 pb-2 hairline-b">方案 A: 卡片网格 (2 列 + 表格跨整行)</h3>
          <div class="grid grid-cols-1 lg:grid-cols-2 gap-3">
            <section class="hairline p-3">
              <header class="mb-2 pb-1 hairline-b"><h4 class="text-xs font-medium">基础信息</h4></header>
              <div class="grid grid-cols-2 gap-y-1 text-xs">
                <div class="text-muted">OEM</div><div>{{ demoProduct.oemNoDisplay }}</div>
                <div class="text-muted">名称</div><div>{{ demoProduct.productName1 }} {{ demoProduct.productName2 }}</div>
              </div>
            </section>
            <section class="hairline p-3">
              <header class="mb-2 pb-1 hairline-b"><h4 class="text-xs font-medium">尺寸</h4></header>
              <div class="grid grid-cols-4 gap-y-1 text-xs">
                <div class="text-muted">D1</div><div>{{ demoProduct.d1Mm }}</div>
                <div class="text-muted">D2</div><div>{{ demoProduct.d2Mm }}</div>
              </div>
            </section>
            <section class="hairline p-3 lg:col-span-2">
              <header class="mb-2 pb-1 hairline-b"><h4 class="text-xs font-medium">替代 OEM (跨整行)</h4></header>
              <div class="text-xs">{{ demoProduct.crossReferences.length }} 条数据</div>
            </section>
          </div>
        </div>

        <!-- 方案 B: 时间线折叠 -->
        <div v-else-if="layoutOption === 'B'" class="hairline p-4 bg-[var(--color-bg-elevated)]">
          <h3 class="text-sm font-medium mb-3 pb-2 hairline-b">方案 B: 时间线折叠 (紧凑单列, 适合长内容)</h3>
          <div class="space-y-0">
            <div v-for="(stage, idx) in [
              { title: '基础信息', content: `${demoProduct.oemNoDisplay} ${demoProduct.productName1}` },
              { title: '替代 OEM', content: `${demoProduct.crossReferences.length} 条` },
              { title: '尺寸', content: `D1=${demoProduct.d1Mm} D2=${demoProduct.d2Mm}` },
              { title: '性能', content: `${demoProduct.media} ${demoProduct.efficiency1}` },
              { title: '包装', content: `${demoProduct.qtyPerCarton} 件/箱` }
            ]" :key="stage.title" class="flex gap-3 hairline-b py-3">
              <div class="flex flex-col items-center">
                <div class="w-6 h-6 rounded-full hairline flex items-center justify-center text-xs font-medium">
                  {{ idx + 1 }}
                </div>
                <div v-if="idx < 4" class="w-px flex-1 bg-[var(--color-border)] mt-1" />
              </div>
              <div class="flex-1">
                <div class="text-xs font-medium mb-1">{{ stage.title }}</div>
                <div class="text-xs text-muted">{{ stage.content }}</div>
              </div>
            </div>
          </div>
        </div>

        <!-- 方案 C: 左右分栏 -->
        <div v-else class="hairline p-4 bg-[var(--color-bg-elevated)]">
          <h3 class="text-sm font-medium mb-3 pb-2 hairline-b">方案 C: 左右分栏 (左导航 + 右内容)</h3>
          <div class="grid grid-cols-1 lg:grid-cols-4 gap-3">
            <aside class="hairline p-3">
              <div class="text-xs font-medium mb-2 text-muted">分区导航</div>
              <ul class="space-y-1 text-xs">
                <li v-for="s in ['基础信息', '替代 OEM', '尺寸', '性能', '包装', '适配车型']" :key="s"
                    class="cursor-pointer hover:text-accent py-1 hairline-b">
                  {{ s }}
                  <!-- V24-F86 (P2-1): 移除 i+1 序号前缀, 用 s 作 key (静态字符串唯一稳定) -->
                </li>
              </ul>
            </aside>
            <div class="lg:col-span-3 space-y-3">
              <section class="hairline p-3">
                <div class="text-xs font-medium mb-1">基础信息</div>
                <div class="text-xs">{{ demoProduct.oemNoDisplay }} - {{ demoProduct.productName1 }}</div>
              </section>
              <section class="hairline p-3">
                <div class="text-xs font-medium mb-1">尺寸</div>
                <div class="text-xs text-muted">D1={{ demoProduct.d1Mm }} D2={{ demoProduct.d2Mm }}</div>
              </section>
            </div>
          </div>
        </div>

        <!-- 方案对比表 -->
        <div class="hairline mt-4 p-4 bg-[var(--color-bg-elevated)]">
          <h3 class="text-sm font-medium mb-3">四版方案对比</h3>
          <el-table :data="planComparison" size="small" border>
            <el-table-column prop="dimension" label="对比维度" width="120" />
            <el-table-column prop="planD" label="方案 D (工业极简)" width="180" />
            <el-table-column prop="planA" label="方案 A (卡片网格)" />
            <el-table-column prop="planB" label="方案 B (时间线)" />
            <el-table-column prop="planC" label="方案 C (左右分栏)" />
          </el-table>
          <div class="text-xs text-muted mt-3">
            推荐: <strong class="text-accent">方案 D 工业极简融合风</strong> (已实现, 工业风 + Musk 极简 + 设计感强)
          </div>
        </div>
      </el-tab-pane>

      <!-- ===== 需求 3: OEM 查询修复 ===== -->
      <el-tab-pane label="3. OEM 查询修复" name="oem">
        <div class="hairline p-4 bg-[var(--color-bg-elevated)]">
          <h3 class="text-sm font-medium mb-3 pb-2 hairline-b">OEM 查询 404 修复说明</h3>
          <div class="space-y-3 text-sm">
            <div>
              <strong class="text-red-600">根因:</strong>
              <span class="text-muted ml-2">数据库 OEM 格式为 <code>P00050000</code> (P 开头), 用户输入 <code>045090</code> 自然 404</span>
            </div>
            <div>
              <strong class="text-green-600">修复方式:</strong>
              <span class="text-muted ml-2">优化 ElMessageBox.prompt 占位符, 给出明确格式示例</span>
            </div>
            <div class="hairline p-3 bg-[var(--color-bg-hover)]">
              <div class="text-xs text-muted mb-1">修复后的提示文案:</div>
              <div class="text-sm">"请输入完整 OEM 编号 (如 P00050000 或 11427622448)"</div>
            </div>
            <div>
              <strong>测试验证:</strong>
              <div class="mt-2 grid grid-cols-2 gap-2">
                <div class="hairline p-2">
                  <div class="text-xs text-muted">输入 P00050000</div>
                  <div class="text-xs text-green-600">✓ 返回 200 + 完整产品</div>
                </div>
                <div class="hairline p-2">
                  <div class="text-xs text-muted">输入 045090</div>
                  <div class="text-xs text-red-600">✗ 404 (格式不符)</div>
                </div>
              </div>
            </div>
            <el-button type="primary" size="small" @click="goReal('/search')">
              前往 OEM 查询入口 (顶部导航)
            </el-button>
          </div>
        </div>
      </el-tab-pane>

      <!-- ===== 需求 4: 登录界面 ===== -->
      <el-tab-pane label="4. 后台登录界面" name="login">
        <div class="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <section class="hairline p-4 bg-[var(--color-bg-elevated)]">
            <h3 class="text-sm font-medium mb-3 pb-2 hairline-b">登录界面预览</h3>
            <div class="max-w-sm mx-auto hairline p-6 bg-[var(--color-bg)]">
              <div class="text-center mb-4">
                <h4 class="text-lg font-medium">SakuraFilter</h4>
                <p class="text-xs text-muted mt-1">后台管理系统</p>
              </div>
              <div class="space-y-3">
                <div>
                  <label class="block text-xs mb-1">用户名</label>
                  <el-input v-model="loginForm.username" placeholder="请输入用户名" size="small" :prefix-icon="User" />
                </div>
                <div>
                  <label class="block text-xs mb-1">密码</label>
                  <el-input v-model="loginForm.password" type="password" placeholder="请输入密码" size="small" show-password :prefix-icon="Lock" />
                </div>
                <el-button type="primary" size="small" class="w-full">登录</el-button>
              </div>
              <div class="mt-4 text-center text-xs text-muted">
                <div>默认账号: admin / admin123</div>
                <div class="mt-1">operator / op123456</div>
              </div>
            </div>
          </section>

          <section class="hairline p-4 bg-[var(--color-bg-elevated)]">
            <h3 class="text-sm font-medium mb-3 pb-2 hairline-b">实现说明</h3>
            <ul class="text-xs text-muted space-y-2 list-disc pl-4">
              <li>替换原 TOKEN 直接输入弹窗 (ElMessageBox.prompt)</li>
              <li>用户名 + 密码本地映射验证 (离线工具, 无后端用户系统)</li>
              <li>验证成功后写入 useAdminAuthStore.token (与 axios 拦截器/路由守卫兼容)</li>
              <li>支持 redirect 查询参数, 登录后回跳到原目标页</li>
              <li>本地账号与后端 Auth:DevStaticToken 共用同一 token</li>
              <li>路由守卫: 未登录访问 /admin/* 时重定向到 /login?redirect=xxx</li>
            </ul>
            <el-button type="primary" size="small" class="mt-3" @click="goReal('/login')">
              查看实际登录页
            </el-button>
          </section>
        </div>
      </el-tab-pane>

      <!-- ===== 需求 5: 主题切换 ===== -->
      <el-tab-pane label="5. 主题切换修复" name="theme">
        <div class="hairline p-4 bg-[var(--color-bg-elevated)]">
          <h3 class="text-sm font-medium mb-3 pb-2 hairline-b">主题切换演示</h3>
          <p class="text-xs text-muted mb-4">
            点击下方按钮切换主题, 整个页面背景应跟随变化 (而非仅按钮/输入框变色)
          </p>

          <div class="flex gap-2 mb-4">
            <el-button :type="theme.mode === 'light' ? 'primary' : 'default'" size="small" @click="theme.set('light')">
              <el-icon><Sunny /></el-icon> 浅色
            </el-button>
            <el-button :type="theme.mode === 'dark' ? 'primary' : 'default'" size="small" @click="theme.set('dark')">
              <el-icon><Moon /></el-icon> 深色
            </el-button>
            <el-button size="small" @click="theme.toggle()">切换</el-button>
          </div>

          <div class="grid grid-cols-2 gap-3 mb-4">
            <div class="hairline p-4 bg-[var(--color-bg)]">
              <div class="text-xs text-muted mb-1">当前背景 (var(--color-bg))</div>
              <div class="text-sm">{{ theme.mode === 'dark' ? '#0a0a0c' : '#ffffff' }}</div>
            </div>
            <div class="hairline p-4 bg-[var(--color-bg-elevated)]">
              <div class="text-xs text-muted mb-1">卡片背景 (var(--color-bg-elevated))</div>
              <div class="text-sm">{{ theme.mode === 'dark' ? '#131318' : '#ffffff' }}</div>
            </div>
          </div>

          <div class="hairline p-4 bg-[var(--color-bg-hover)]">
            <h4 class="text-sm font-medium mb-2">修复说明</h4>
            <ul class="text-xs text-muted space-y-1 list-disc pl-4">
              <li>原问题: 多处使用硬编码 <code>bg-white</code> / <code>bg-neutral-50</code>, 主题切换时不变色</li>
              <li>修复: 替换为 CSS 变量 <code>bg-[var(--color-bg)]</code> / <code>bg-[var(--color-bg-hover)]</code> / <code>bg-[var(--color-bg-elevated)]</code></li>
              <li>涉及文件: SearchView.vue / PublicSearchView.vue / AppHeader.vue / PublicProductView.vue</li>
              <li>主题系统: :root 定义浅色变量, html.dark 覆盖深色变量, body 用 background: var(--color-bg)</li>
              <li>Element Plus: html.dark 覆盖 --el-bg-color / --el-text-color-primary / --el-border-color 等变量</li>
            </ul>
          </div>
        </div>
      </el-tab-pane>
    </el-tabs>

    <!-- 底部方案总结 -->
    <div class="hairline mt-6 p-4 bg-[var(--color-bg-elevated)]">
      <h3 class="text-sm font-medium mb-3">前端整体重构建议</h3>
      <div class="grid grid-cols-1 lg:grid-cols-3 gap-3 text-xs">
        <div class="hairline p-3">
          <div class="font-medium mb-2 text-accent">推荐保留 (已实现)</div>
          <ul class="text-muted space-y-1 list-disc pl-4">
            <li>方案 D 工业极简融合风详情页</li>
            <li>搜索框 searched 标志位</li>
            <li>登录页本地映射验证</li>
            <li>CSS 变量主题系统</li>
          </ul>
        </div>
        <div class="hairline p-3">
          <div class="font-medium mb-2 text-accent">建议优化 (下阶段)</div>
          <ul class="text-muted space-y-1 list-disc pl-4">
            <li>OEM 查询改为模糊匹配 (支持 045090 → P00050000)</li>
            <li>登录页接后端 API (多用户/多 token 轮转)</li>
            <li>详情页加图片灯箱 (lightbox)</li>
            <li>移动端导航抽屉化</li>
          </ul>
        </div>
        <div class="hairline p-3">
          <div class="font-medium mb-2 text-accent">可选方案 (按需)</div>
          <ul class="text-muted space-y-1 list-disc pl-4">
            <li>方案 A 卡片网格 (备选)</li>
            <li>方案 B 时间线 (长内容)</li>
            <li>方案 C 左右分栏 (快速浏览)</li>
            <li>暗色模式自动跟随系统</li>
            <li>多语言切换 (i18n)</li>
          </ul>
        </div>
      </div>
    </div>
  </div>
</template>
