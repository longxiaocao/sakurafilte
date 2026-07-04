# SakuraFilter 主题切换端到端测试报告

**测试时间**: 2026-07-04
**测试范围**: 前端 17 个核心页面 + 后端 API 功能完整性
**测试工具**: Playwright (Python) + 自动化白色组件检测 + 对比度分析
**测试结论**: ✅ 全部通过 (17/17 PASS, 0 FAIL, 0 WARN)

---

## 一、测试范围

### 前端页面覆盖 (17 个)

| 分组 | 页面 | URL | 需认证 |
|------|------|-----|--------|
| 公开 | 登录页 | /login | 否 |
| 公开 | 搜索页 | /search | 否 |
| 公开 | 公开搜索 | /public/search | 否 |
| 公开 | 产品详情 | /product/P00050000 | 否 |
| 公开 | Demo 页 | /demo | 否 |
| 后台 | 产品管理 | /admin/products | 是 |
| 后台 | ETL 管理 | /admin/etl | 是 |
| 后台 | 用户管理 | /admin/users | 是 |
| 后台 | 对比工具 | /admin/compare | 是 |
| 后台 | 帮助文档 | /admin/help | 是 |
| 后台 | 性能监控 | /admin/perf | 是 |
| 后台 | 字典-OEM品牌 | /admin/dict/oem-brands | 是 |
| 后台 | 字典-类型 | /admin/dict/types | 是 |
| 后台 | 字典-机器 | /admin/dict/machines | 是 |
| 后台 | 字典-介质 | /admin/dict/medias | 是 |
| 后台 | 字典-引擎 | /admin/dict/engines | 是 |
| 后台 | 修改密码 | /change-password | 是 |

### 测试维度

1. **白天模式截图**: 每个页面在白天模式下的完整截图
2. **黑夜模式截图**: 每个页面在黑夜模式下的完整截图
3. **白色组件检测**: 黑夜模式下检测 rgb(255,255,255)/rgb(245,245,245) 等白色背景组件
4. **对比度检测**: 检测深色背景+深色文字、浅色背景+浅色文字的对比度问题

---

## 二、发现的问题列表

### 问题 1: body 硬编码白色背景 (P1)

- **问题描述**: `index.html` 中 `<body>` 标签硬编码了 Tailwind 类 `bg-white text-neutral-900`，在黑夜模式下仍然是白色背景和深色文字
- **复现步骤**:
  1. 访问任意页面
  2. 切换到黑夜模式
  3. 检查 `document.body` 的计算样式
- **预期结果**: body 背景应为 `rgb(10, 10, 12)` (#0a0a0c)，文字应为 `rgb(245, 245, 247)` (#f5f5f7)
- **实际结果**: body 背景为 `rgb(255, 255, 255)`，文字为 `rgb(17, 24, 39)`
- **根因**: Tailwind 工具类 `bg-white` 优先级高于 CSS 变量 `var(--color-bg)`
- **修复方案**: 移除 body 的 `bg-white text-neutral-900` 类，改由 CSS 变量驱动
- **修复文件**: [index.html](file:///d:/projects/sakurafilter/frontend/index.html)

### 问题 2: Tailwind darkMode 未配置 (P1)

- **问题描述**: `tailwind.config.js` 未配置 `darkMode: 'class'`，导致所有 `dark:bg-*`、`dark:text-*` 变体无效
- **复现步骤**:
  1. 在元素上使用 `bg-white dark:bg-neutral-900` 类
  2. 切换到黑夜模式 (html.dark)
  3. 检查元素背景色
- **预期结果**: 黑夜模式下背景应为 `rgb(23, 23, 23)` (#171717)
- **实际结果**: 背景仍为 `rgb(255, 255, 255)`，Tailwind 生成的 CSS 规则使用 `@media (prefers-color-scheme: dark)` 而非 `.dark` 类选择器
- **根因**: Tailwind 默认使用 `prefers-color-scheme` 媒体查询，而项目使用 `html.dark` 类切换主题
- **修复方案**: 在 tailwind.config.js 添加 `darkMode: 'class'`
- **修复文件**: [tailwind.config.js](file:///d:/projects/sakurafilter/frontend/tailwind.config.js)

### 问题 3: 8 个字典页面硬编码颜色 (P1)

- **问题描述**: 8 个字典管理页面的 `.dict-row`、`.dict-head` 样式硬编码了 `#fff`、`#f9fafb`、`#e5e7eb`、`#6b7280`、`#9ca3af`、`#2563eb`、`#eff6ff`、`#fafafa` 等颜色
- **受影响页面**:
  - AdminTypesView.vue (30 个白色组件)
  - AdminMachinesView.vue (30 个)
  - AdminEnginesView.vue (13 个)
  - AdminMediasView.vue (14 个)
  - AdminOemNo3sView.vue
  - AdminProductName1sView.vue
  - AdminProductName2sView.vue
  - AdminOemBrandsView.vue (6 个)
- **复现步骤**:
  1. 访问 /admin/dict/types
  2. 切换到黑夜模式
  3. 检查 `.dict-row` 元素的背景色
- **预期结果**: 行背景应为 `var(--color-bg-elevated)`，表头应为 `var(--color-bg-hover)`
- **实际结果**: 行背景为 `rgb(255, 255, 255)`，表头为 `rgb(249, 250, 251)`
- **修复方案**: 所有硬编码颜色替换为 CSS 变量
- **修复文件**: 8 个字典 View 文件

### 问题 4: AdminUsersView 硬编码白色背景 (P1)

- **问题描述**: AdminUsersView 的 `.user-row`、`.audit-row` 硬编码了 `background: #fff`
- **复现步骤**:
  1. 访问 /admin/users
  2. 切换到黑夜模式
  3. 检查 `.user-row` 元素的背景色
- **预期结果**: 行背景应为 `var(--color-bg-elevated)`
- **实际结果**: 3 个 `.user-row` 元素背景为 `rgb(255, 255, 255)`
- **修复方案**: 所有硬编码颜色替换为 CSS 变量
- **修复文件**: [AdminUsersView.vue](file:///d:/projects/sakurafilter/frontend/src/views/admin/AdminUsersView.vue)

### 问题 5: bg-white dark:bg-neutral-900 未生效 (P2)

- **问题描述**: AdminHelpView、AdminPerfView、FieldHelpPopover 使用 `bg-white dark:bg-neutral-900` 类，但因 darkMode 未配置而无效
- **复现步骤**:
  1. 访问 /admin/help
  2. 切换到黑夜模式
  3. 检查 `.help-anchor` 元素的背景色
- **预期结果**: 背景应为深色
- **实际结果**: 背景为 `rgb(255, 255, 255)`
- **修复方案**: 改为 `bg-[var(--color-bg-elevated)]`，不依赖 Tailwind dark 变体
- **修复文件**: AdminHelpView.vue, AdminPerfView.vue, FieldHelpPopover.vue

### 问题 6: hover:bg-neutral-100 在黑夜模式下仍是浅色 (P2)

- **问题描述**: AppHeader、AdminPerfView 的按钮 hover 状态使用 `hover:bg-neutral-100`，在黑夜模式下 hover 仍是浅色
- **复现步骤**:
  1. 访问任意页面
  2. 切换到黑夜模式
  3. 鼠标悬停在主题切换按钮上
- **预期结果**: hover 背景应为 `var(--color-bg-hover)` (#18181b)
- **实际结果**: hover 背景为 `rgb(245, 245, 245)` (#f5f5f5)
- **修复方案**: 改为 `hover:bg-[var(--color-bg-hover)]`
- **修复文件**: AppHeader.vue, AdminPerfView.vue

### 问题 7: 硬编码文字颜色无暗色适配 (P2)

- **问题描述**: AdminEtlView 的 `text-gray-500`、PublicSearchView 的 `text-black`、AdminProductFormView/AdminProductsView 的 `bg-neutral-50` 在黑夜模式下不适配
- **复现步骤**:
  1. 访问 /admin/etl
  2. 切换到黑夜模式
  3. 检查次要文字颜色
- **预期结果**: 文字应为 `var(--color-text-muted)`，背景应为 `var(--color-bg-hover)`
- **实际结果**: 文字为硬编码灰色，背景为硬编码浅色
- **修复方案**: 改为 CSS 变量
- **修复文件**: AdminEtlView.vue, PublicSearchView.vue, AdminProductFormView.vue, AdminProductsView.vue

---

## 三、修复验证结果

### 主题切换测试结果

```
总计: 17 页面
✓ PASS: 17
✗ FAIL: 0
⚠ WARN: 0
✗ ERROR: 0
○ SKIP: 0
```

### 回归测试结果

| 测试项 | 结果 |
|--------|------|
| vue-tsc type-check | ✅ 0 错误 |
| vitest test:contract | ✅ 32/32 通过 |
| dotnet build | ✅ 0 错误 (22 个旧警告) |

### 截图产物

- 截图目录: `spike-test/theme-screenshots/`
- 截图数量: 34 张 (17 页面 × 2 模式)
- 测试报告: `spike-test/theme_test_report.json`

---

## 四、修复改动统计

- **修改文件**: 19 个前端文件 + 1 个 tailwind 配置
- **新增文件**: 1 个测试脚本 + 1 个测试报告
- **代码变更**: 738 行新增, 107 行删除
- **提交**: `f7cc6a1 fix(theme): 修复黑夜模式白色组件问题 (17/17 页面通过)`

---

## 五、结论

本次主题切换端到端测试发现并修复了 7 类硬编码颜色问题，涵盖 17 个核心页面。所有页面在黑夜模式下均已通过白色组件检测和对比度检测，无视觉异常。系统支持完整的白天/黑夜模式切换，可交付生产。
