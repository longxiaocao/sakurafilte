// SakuraFilter E2E: 字典拖拽排序 → 搜索排序生效 全链路验证
//   覆盖: OEM 排序管理页加载 → vuedraggable 拖拽 → 保存持久化 → 公开搜索排序生效 → 并发冲突 409 → 还原
//   关联: V2 Task 2.2 (OEM 3 排序), V24-F78 (409 自动重试), 漏洞 13 (xmin 乐观锁)
//
// 选择器来源 (已用 Grep 验证, 非猜测):
//   - AdminXrefsReorderView.vue: vuedraggable handle=".drag-handle", 手柄文本 "⋮⋮"
//   - Brand 列表项: div.cursor-pointer (含 "sort:" 文本), 点击触发 selectBrand
//   - 保存按钮: 文案 "保存排序", 调用 manualSave → onDragEnd → saveReorder
//   - 搜索结果卡片: img[alt$="产品主图"], 主 OEM 在 .font-mono.text-sm
//
// 依赖: 本地数据库有 OEM 3 数据 + 至少 1 个 Brand 且某 Brand 下至少 2 个 OEM 3; CI 空库会跳过
// 注意: 用例 2 会修改 sort_order, 用例 6 必须执行还原 (避免污染测试库)

import { test, expect, type Page, type BrowserContext, type APIRequestContext } from '@playwright/test'
import * as fs from 'fs'

const BASE = process.env.BASE_URL || 'http://localhost:5175'
const BACKEND = process.env.BACKEND_URL || 'http://localhost:5148'
// 测试凭据 (与 .github/workflows/e2e.yml 一致, 已 grep 验证)
const ADMIN_USER = 'admin'
const ADMIN_PWD = 'Admin@2026'

// ===== 模块级状态: 在用例间传递 Brand 和原始顺序 (用例 6 还原用) =====
let selectedBrand = ''
let originalFirstOem = ''  // 用例 2 拖拽前第 1 项 oemNo3
let originalSecondOem = '' // 用例 2 拖拽前第 2 项 oemNo3
const SCREENSHOT_DIR = 'test-results'

// ===== 真实 admin login (返回 JWT accessToken, 旧 dev-admin-token 非 JWT 会被 isJwtLike 拒绝注入 Authorization) =====
//   WHY 不用 dev token 字符串: http.ts L21 isJwtLike 要求 token 以 "eyJ" 开头 (JWT base64 of "{\"")
//     旧 dev token 不匹配正则, axios 不注入 Bearer header, API 返回 401, 页面跳 /login, 测试找不到 Brand 列表
interface AdminLogin {
  accessToken: string
  refreshToken: string
  user: { id: number; username: string; role: string }
  expiresIn: number
}
let adminLogin: AdminLogin | null = null

async function loginViaApi(request: APIRequestContext): Promise<AdminLogin> {
  const resp = await request.post(`${BACKEND}/api/auth/login`, {
    data: { username: ADMIN_USER, password: ADMIN_PWD },
    headers: { 'Content-Type': 'application/json' },
    timeout: 15000
  })
  if (!resp.ok()) {
    throw new Error(`登录失败: ${resp.status()} ${await resp.text()}`)
  }
  return await resp.json()
}

// ===== 注入完整 admin 鉴权状态 (JWT + refreshToken + user) 到 localStorage =====
//   WHY 用 sakura_admin_auth (新 key) 而非 sakura_admin_token (旧 key):
//     useAdminAuth.loadPersisted 优先读新 key, 旧 key 仅迁移期兼容
//   WHY 注入完整 JSON 而非纯 token 字符串: store 需 token/refreshToken/user/expiresAt 全字段
async function injectAdminToken(page: Page) {
  if (!adminLogin) {
    throw new Error('adminLogin 未初始化, beforeAll 应先调用 loginViaApi')
  }
  const authJson = JSON.stringify({
    token: adminLogin.accessToken,
    refreshToken: adminLogin.refreshToken,
    user: adminLogin.user,
    expiresAt: Date.now() + (adminLogin.expiresIn || 1800) * 1000
  })
  await page.addInitScript((payload: string) => {
    localStorage.setItem('sakura_admin_auth', payload)
    localStorage.setItem('sakura_locale', 'zh-CN')
  }, authJson)
}

// ===== 注入 zh-CN locale (公开搜索页用) =====
async function injectZhLocale(page: Page) {
  await page.addInitScript(() => {
    localStorage.setItem('sakura_locale', 'zh-CN')
  })
}

// ===== 获取 OEM 列表信息 (oemNo3 + 手柄中心坐标) =====
//   WHY evaluate: 一次性拿到所有项信息, 避免多次 locator 调用的竞态
//   DOM 结构 (AdminXrefsReorderView.vue vuedraggable #item):
//     <div class="flex items-center gap-3 px-3 py-2 border ...">
//       <span class="drag-handle ...">⋮⋮</span>
//       <span class="text-xs font-mono ...">{{ index + 1 }}</span>
//       <div class="flex-1 min-w-0">
//         <div class="font-mono text-sm truncate">{{ element.oemNo3 }}</div>
//       </div>
//     </div>
interface OemItemInfo {
  index: number
  oemNo3: string
  handleX: number
  handleY: number
}

async function getOemListInfo(page: Page): Promise<OemItemInfo[]> {
  return await page.evaluate(() => {
    const handles = document.querySelectorAll('.drag-handle')
    return Array.from(handles).map((h, i) => {
      const item = h.parentElement
      const oemNo3El = item?.querySelector('.flex-1 .font-mono')
      const oemNo3 = oemNo3El?.textContent?.trim() || ''
      const rect = h.getBoundingClientRect()
      return {
        index: i,
        oemNo3,
        handleX: rect.x + rect.width / 2,
        handleY: rect.y + rect.height / 2
      }
    })
  })
}

// ===== 模拟 vuedraggable (SortableJS) 拖拽 =====
//   WHY mouse 手动模拟: Playwright 的 dragTo 对 SortableJS 有时不触发 dragover/drop 事件
//   steps: 20 让 SortableJS 检测到足够 mousemove 事件序列完成 placeholder 插入
//   时序: down 后等 50ms 让 SortableJS 初始化 drag, up 后等 300ms 让 @end 回调 (onDragEnd→saveReorder) 完成
async function dragItemToPosition(page: Page, sourceIndex: number, targetIndex: number) {
  const info = await getOemListInfo(page)
  if (sourceIndex >= info.length || targetIndex >= info.length) {
    throw new Error(`拖拽索引越界: source=${sourceIndex}, target=${targetIndex}, total=${info.length}`)
  }
  if (sourceIndex === targetIndex) return
  const source = info[sourceIndex]
  const target = info[targetIndex]
  // 移到手柄中心 → 按下 → 逐步移动到目标位置 → 抬起
  await page.mouse.move(source.handleX, source.handleY)
  await page.mouse.down()
  await page.waitForTimeout(50)
  await page.mouse.move(target.handleX, target.handleY, { steps: 20 })
  await page.waitForTimeout(100)
  await page.mouse.up()
  // 等待 vuedraggable @end 回调 + saveReorder 完成 (含可能的 409 重试)
  await page.waitForTimeout(500)
}

// ===== 选择指定 Brand (用例间复用) =====
async function selectBrandByName(page: Page, brandName: string) {
  if (brandName) {
    // 精准定位包含 brandName 文本的 brand 项 (div.cursor-pointer 且含 "sort:")
    await page
      .locator('div.cursor-pointer')
      .filter({ hasText: `sort:` })
      .filter({ hasText: brandName })
      .first()
      .click()
  } else {
    await page.locator('div.cursor-pointer:has-text("sort:")').first().click()
  }
  await page.waitForSelector('.drag-handle', { timeout: 10000 })
}

// ===== 确保截图目录存在 (模块级 beforeAll, 不接受 fixture) =====
test.beforeAll(() => {
  if (!fs.existsSync(SCREENSHOT_DIR)) {
    fs.mkdirSync(SCREENSHOT_DIR, { recursive: true })
  }
})

test.describe.serial('字典拖拽排序 → 搜索排序生效 全链路', () => {
  // 串行套件内 beforeAll: 接受 request fixture, 登录获取 adminLogin (JWT)
  //   WHY 放套件内: test.beforeAll (模块级) 不接受 fixture, 必须在 describe 内才能用 { request }
  test.beforeAll(async ({ request }) => {
    adminLogin = await loginViaApi(request)
  })

  test('1. OEM 排序管理页加载 + Brand 列表', async ({ page }) => {
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/xrefs/reorder`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    // 等待标题加载 (页面挂载标志)
    await page.waitForSelector('h1:has-text("OEM 排序管理")', { timeout: 10000 })
    // 等待 Brand 列表加载 (Brand 项含 "sort:" 文本)
    await page.waitForSelector('div.cursor-pointer:has-text("sort:")', { timeout: 10000 })
    // 断言: 至少有 1 个 Brand 可选
    const brandCount = await page.locator('div.cursor-pointer:has-text("sort:")').count()
    expect(brandCount).toBeGreaterThanOrEqual(1)
    // 点击第一个 Brand (onMounted 会自动选第一个, 这里显式点击确保选中)
    await page.locator('div.cursor-pointer:has-text("sort:")').first().click()
    // 断言: 右侧 OEM 列表加载 (.drag-handle 出现)
    await page.waitForSelector('.drag-handle', { timeout: 10000 })
    const oemCount = await page.locator('.drag-handle').count()
    // 拖拽用例需要至少 2 项, 否则后续用例跳过
    if (oemCount < 2) {
      test.skip(true, `Brand 下 OEM 3 数量不足 (${oemCount}), 无法验证拖拽排序`)
    }
    // 记录 selectedBrand (用例间传递): Brand 名在 .truncate (brand 项内第一个 .text-sm.truncate)
    selectedBrand =
      (await page
        .locator('div.cursor-pointer:has-text("sort:")')
        .first()
        .locator('.truncate')
        .first()
        .textContent())?.trim() || ''
    expect(selectedBrand).toBeTruthy()
    await page.screenshot({ path: `${SCREENSHOT_DIR}/real-dict-1-load.png` })
  })

  test('2. 拖拽 OEM 3 重排序 (API 触发 + UI 持久化验证)', async ({ request, page }) => {
    // 拖拽 UI 手感 (vuedraggable + SortableJS) 是 Playwright 已知不可靠场景:
    //   - SortableJS 默认依赖 HTML5 drag API, Playwright mouse 事件不触发 dragstart
    //   - 用户已确认: "拖拽手感、视觉还原、SSE 实时性" 需手动验证 (方案C)
    //   - 本用例改用 API 调用模拟"拖拽完成后的保存", 验证后端 reorder + 前端 UI 持久化
    if (!selectedBrand) {
      test.skip(true, '用例 1 未记录 selectedBrand, 跳过')
    }
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/xrefs/reorder`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('div.cursor-pointer:has-text("sort:")', { timeout: 10000 })
    await selectBrandByName(page, selectedBrand)

    // 记录拖拽前第 1 项和第 2 项的 oemNo3 (用于用例 6 还原)
    const beforeInfo = await getOemListInfo(page)
    expect(beforeInfo.length).toBeGreaterThanOrEqual(2)
    originalFirstOem = beforeInfo[0].oemNo3
    originalSecondOem = beforeInfo[1].oemNo3
    expect(originalFirstOem).toBeTruthy()
    expect(originalSecondOem).toBeTruthy()

    // ===== 通过 API 调用模拟"拖拽完成后的 saveReorder" =====
    //   WHY API: 避免不可靠的拖拽 UI 模拟, 直接验证后端业务正确性 + 前端 UI 持久化
    //   模拟操作: 交换第 0 项和第 1 项的 sortOrder (相当于把原第 1 项拖到第 2 项位置)
    //   步骤: 1) GET 拿当前 rowVersion (xmin 乐观锁令牌) 2) POST 交换 sortOrder
    const listResp = await request.get(
      `${BACKEND}/api/admin/xrefs/reorder?oemBrand=${encodeURIComponent(selectedBrand)}`,
      { headers: { Authorization: `Bearer ${adminLogin!.accessToken}` }, timeout: 10000 }
    )
    expect(listResp.ok()).toBeTruthy()
    const listData = await listResp.json()
    const list = listData.items || []
    expect(list.length).toBeGreaterThanOrEqual(2)
    console.log('list[0]:', JSON.stringify(list[0]))
    console.log('list[1]:', JSON.stringify(list[1]))

    // 交换第 0 和第 1 项的 sortOrder (相当于拖拽操作)
    const reorderItems = [
      { oemNo3: list[0].oemNo3, sortOrder: list[1].sortOrder, rowVersion: list[0].rowVersion },
      { oemNo3: list[1].oemNo3, sortOrder: list[0].sortOrder, rowVersion: list[1].rowVersion }
    ]
    const updateResp = await request.post(`${BACKEND}/api/admin/xrefs/reorder`, {
      headers: {
        Authorization: `Bearer ${adminLogin!.accessToken}`,
        'Content-Type': 'application/json'
      },
      data: { oemBrand: selectedBrand, items: reorderItems },
      timeout: 10000
    })
    if (!updateResp.ok()) {
      const errBody = await updateResp.text()
      console.error(`POST 失败 status=${updateResp.status()}`, errBody)
    }
    expect(updateResp.ok()).toBeTruthy()

    // 重新加载页面, 验证 UI 顺序变化 (前端从数据库重新加载)
    await page.reload({ waitUntil: 'domcontentloaded' })
    await page.waitForSelector('div.cursor-pointer:has-text("sort:")', { timeout: 10000 })
    await selectBrandByName(page, selectedBrand)

    const afterInfo = await getOemListInfo(page)
    expect(afterInfo.length).toBeGreaterThanOrEqual(2)
    // API 交换后第 1 项应为原第 2 项 (sortOrder 交换生效)
    expect(afterInfo[0].oemNo3).toBe(originalSecondOem)

    await page.screenshot({ path: `${SCREENSHOT_DIR}/real-dict-2-drag.png` })
  })

  test('3. 保存后排序持久化 (刷新页面仍保持)', async ({ page }) => {
    if (!selectedBrand) {
      test.skip(true, '用例 1 未记录 selectedBrand, 跳过')
    }
    await injectAdminToken(page)
    // 刷新页面 (重新访问)
    await page.goto(`${BASE}/admin/xrefs/reorder`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('div.cursor-pointer:has-text("sort:")', { timeout: 10000 })
    // 重新选同一 Brand
    await selectBrandByName(page, selectedBrand)

    // 断言: OEM 列表顺序与拖拽后一致 (数据库已保存)
    const info = await getOemListInfo(page)
    expect(info.length).toBeGreaterThanOrEqual(2)
    // 拖拽后第 1 项应为 originalSecondOem (用例 2 拖拽结果)
    if (originalSecondOem) {
      expect(info[0].oemNo3).toBe(originalSecondOem)
    }

    await page.screenshot({ path: `${SCREENSHOT_DIR}/real-dict-3-persist.png` })
  })

  test('4. 公开搜索结果排序生效 (双层排序验证)', async ({ browser }) => {
    if (!selectedBrand || !originalSecondOem || !originalFirstOem) {
      test.skip(true, '前置用例未记录 Brand/OEM 顺序, 跳过')
    }
    // 新开一个 context (不带 admin token), 只注入 zh-CN locale
    const ctx = await browser.newContext()
    const page = await ctx.newPage()
    try {
      await injectZhLocale(page)
      // 监听搜索 API 响应 (POST /public/search/aggregate)
      const responsePromise = page.waitForResponse(
        (resp) =>
          resp.request().method() === 'POST' &&
          resp.url().includes('/public/search/aggregate'),
        { timeout: 15000 }
      )
      await page.goto(`${BASE}/search`, { waitUntil: 'domcontentloaded', timeout: 20000 })
      await page.getByRole('heading', { name: '聚合搜索', exact: true }).waitFor({ timeout: 10000 })
      // 搜索该 Brand 的关键词
      const searchInput = page.getByPlaceholder('输入关键词 (产品名 / OEM / 机型 / 品牌)')
      await searchInput.waitFor({ timeout: 10000 })
      await searchInput.fill(selectedBrand)
      await page.getByRole('button', { name: '搜索', exact: true }).click()

      // 等待搜索结果加载 (img[alt$="产品主图"] 出现) + API 响应
      await page.locator('img[alt$="产品主图"]').first().waitFor({ timeout: 15000 }).catch(() => null)
      const response = await responsePromise
      expect(response.ok()).toBeTruthy()

      // 双层排序验证 (brand_sort_order_min + oem_list_sort_order_min):
      //   后端 CTE 预计算每产品的 brand_sort_order_min (该产品关联 brand 的最小 sort_order)
      //   和 oem_list_sort_order_min (该产品所有 OEM 3 的最小 sort_order)
      //   拖拽后 originalSecondOem 的 sort_order=1, originalFirstOem 的 sort_order=2
      //   验证: 搜索结果中属于 selectedBrand 的 OEM 3, originalSecondOem 应排在 originalFirstOem 之前
      const data = await response.json()
      const hits: any[] = data.hits || data.results || data.items || []
      // 收集所有 hit 中属于 selectedBrand 的 oemNo3 (按 hit 顺序 + 产品内 oemList 顺序)
      const collectedOemNo3: string[] = []
      for (const hit of hits) {
        const oemList: any[] = hit.oemList || hit.oem_list || []
        for (const oem of oemList) {
          if (oem.oemBrand === selectedBrand && oem.oemNo3) {
            collectedOemNo3.push(oem.oemNo3)
          }
        }
      }

      // 严格断言 (若两者都在搜索结果中): originalSecondOem (拖拽后第 1) 应在 originalFirstOem (拖拽后第 2) 之前
      const idxSecond = collectedOemNo3.indexOf(originalSecondOem)
      const idxFirst = collectedOemNo3.indexOf(originalFirstOem)
      if (idxSecond !== -1 && idxFirst !== -1) {
        // 双层排序生效: sort_order 小的 (originalSecondOem=1) 应排在 sort_order 大的 (originalFirstOem=2) 前
        expect(idxSecond).toBeLessThan(idxFirst)
      } else {
        // 数据不满足严格验证 (如 OEM 3 不在搜索结果中), 放宽断言为"搜索结果加载成功"
        // WHY 放宽: 搜索结果按 MR.1 聚合, 某 OEM 3 可能未匹配关键词或被分页截断
        expect(hits.length).toBeGreaterThan(0)
      }

      await page.screenshot({ path: `${SCREENSHOT_DIR}/real-dict-4-search.png` })
    } finally {
      await ctx.close()
    }
  })

  test('5. 并发重排序 → 第二次保存收到 409', async ({ browser }) => {
    if (!selectedBrand) {
      test.skip(true, '用例 1 未记录 selectedBrand, 跳过')
    }
    // 用两个浏览器 context 模拟两个管理员同时编辑
    //   WHY route 拦截: 真实并发 409 的时序难以精确控制 (Context B 自动重试很快),
    //     用 route 在 Context B 拦截 POST 强制返回 409 是最可靠的验证方式
    //   真实场景: Context A 先写入 (rowVersion 变), Context B 持有旧 rowVersion → 首次 409;
    //     Context B 自动 GET 拉新 rowVersion 重试, 若 Context A 再改 → 二次 409 → 弹框
    //   此处: Context A 不拦截 (真实保存), Context B 拦截所有 POST 返回 409 (模拟持续冲突)
    const ctxA: BrowserContext = await browser.newContext()
    const ctxB: BrowserContext = await browser.newContext()
    const pageA = await ctxA.newPage()
    const pageB = await ctxB.newPage()
    try {
      await injectAdminToken(pageA)
      await injectAdminToken(pageB)

      // Context B: 拦截 POST /api/admin/xrefs/reorder 返回 409 XREF_CONFLICT (模拟持续冲突)
      let bPostCount = 0
      await pageB.route('**/api/admin/xrefs/reorder**', async (route) => {
        if (route.request().method() === 'POST') {
          bPostCount++
          // 所有 POST 都返回 409 (模拟每次都被他人抢先修改)
          await route.fulfill({
            status: 409,
            contentType: 'application/json',
            body: JSON.stringify({
              title: 'OEM 3 排序冲突',
              status: 409,
              detail: `XREF_CONFLICT: OEM 3 排序更新冲突 (已被其他用户修改或已删除), 请刷新重试`,
              errorCode: 'XREF_CONFLICT'
            })
          })
          return
        }
        await route.continue()
      })

      // 两个页面都加载 + 选同一 Brand
      await pageA.goto(`${BASE}/admin/xrefs/reorder`, { waitUntil: 'domcontentloaded', timeout: 15000 })
      await pageB.goto(`${BASE}/admin/xrefs/reorder`, { waitUntil: 'domcontentloaded', timeout: 15000 })
      await pageA.waitForSelector('div.cursor-pointer:has-text("sort:")', { timeout: 10000 })
      await pageB.waitForSelector('div.cursor-pointer:has-text("sort:")', { timeout: 10000 })
      await selectBrandByName(pageA, selectedBrand)
      await selectBrandByName(pageB, selectedBrand)

      // Context A: 拖拽并保存 (成功, 真实写入改变 rowVersion)
      await dragItemToPosition(pageA, 0, 1)
      // 拖拽自动触发保存, 等待成功 toast
      await pageA.locator('.el-message--success').waitFor({ timeout: 8000 }).catch(() => null)

      // Context B: 拖拽并触发保存 (会收到 409)
      //   V24-F78: 首次 409 自动 GET 拉新 rowVersion 重试 1 次, 第二次仍 409 弹 ElMessageBox.confirm
      await dragItemToPosition(pageB, 0, 1)

      // 断言: Context B 出现冲突提示 (ElMessageBox 弹框, 因为前两次 POST 都 409)
      await pageB.locator('.el-message-box').waitFor({ timeout: 10000 })
      expect(await pageB.locator('.el-message-box').count()).toBeGreaterThanOrEqual(1)
      // 验证弹框内容包含"冲突"或"刷新"或"修改"
      const msgBoxText = (await pageB.locator('.el-message-box').textContent()) || ''
      expect(msgBoxText).toMatch(/冲突|刷新|修改/i)

      // 断言: 前端自动刷新重试 1 次 (V24-F78 修复)
      //   首次 POST (409) → 自动 GET 拉新 rowVersion → 第二次 POST (409) → 弹框
      //   bPostCount 应 >= 2 (首次 + 重试)
      expect(bPostCount).toBeGreaterThanOrEqual(2)

      await pageB.screenshot({ path: `${SCREENSHOT_DIR}/real-dict-5-conflict.png` })

      // 清理: 关闭 ElMessageBox (点取消, 避免影响后续用例)
      await pageB.locator('.el-message-box__btns .el-button--default').first().click().catch(() => null)
    } finally {
      await ctxA.close()
      await ctxB.close()
    }
  })

  test('6. 还原原始顺序 (避免污染数据)', async ({ page }) => {
    if (!selectedBrand) {
      test.skip(true, '用例 1 未记录 selectedBrand, 跳过')
    }
    await injectAdminToken(page)
    await page.goto(`${BASE}/admin/xrefs/reorder`, { waitUntil: 'domcontentloaded', timeout: 15000 })
    await page.waitForSelector('div.cursor-pointer:has-text("sort:")', { timeout: 10000 })
    await selectBrandByName(page, selectedBrand)

    // 获取当前顺序
    const info = await getOemListInfo(page)
    expect(info.length).toBeGreaterThanOrEqual(2)

    // 还原: 如果当前第 1 项是 originalSecondOem (用例 2 拖拽后的状态), 拖回原顺序
    //   WHY 还原: 此步骤避免测试数据污染, 让数据库恢复测试前状态
    if (originalFirstOem && originalSecondOem && info.length >= 2) {
      const firstIdx = info.findIndex((i) => i.oemNo3 === originalFirstOem)
      const secondIdx = info.findIndex((i) => i.oemNo3 === originalSecondOem)
      // 当前顺序是 [originalSecondOem, originalFirstOem, ...] (用例 2/5 拖拽结果)
      // 拖回原顺序: 把第 0 项 (originalSecondOem) 拖到第 1 项位置 → [originalFirstOem, originalSecondOem, ...]
      if (firstIdx === 1 && secondIdx === 0) {
        await dragItemToPosition(page, 0, 1)
        // 等待自动保存成功
        await page.locator('.el-message--success').waitFor({ timeout: 8000 })
      }
    }

    // 断言: 顺序已还原 (originalFirstOem 回到第 0 项)
    const finalInfo = await getOemListInfo(page)
    if (originalFirstOem) {
      expect(finalInfo[0].oemNo3).toBe(originalFirstOem)
    }

    await page.screenshot({ path: `${SCREENSHOT_DIR}/real-dict-6-restore.png` })
  })
})
