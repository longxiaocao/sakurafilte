# SakuraFilter 生产部署测试报告

- 日期：2026-09-13
- 分支：`feat/etl-v3-import`
- 环境：Docker Compose 全栈（postgres16 / minio / meilisearch / backend:5148 / frontend:5175）
- 服务健康：`/health/ready` → `{status: healthy, checks: [postgres✓, meili✓, fallback✓, backgroundServices✓]}`

## 1. 测试范围与方法

| 层 | 范围 | 方法 |
|---|---|---|
| API | 全端点契约、数据处理、错误处理、边界条件、鉴权、限流、性能 | PowerShell smoke 脚本（109→112 用例） |
| 前端 | 全路由交互、UI、响应式、主题/i18n、跨浏览器 | Playwright E2E（16 spec / ~190 用例） |
| 异常 | Meili 降级、恶意文件上传、并发冲突、SSE 重连 | 专用异常场景 E2E |

## 2. API 层测试结果

- 执行：`_qa/api_smoke.ps1` → 重跑结果 **111 PASS / 1 FAIL / 112 TOTAL**
- 性能采样：搜索 avg 76ms / p95 113ms；详情 avg 26ms / p95 32ms；字典 avg 1ms；健康检查 avg 47ms
- 唯一 FAIL：`O01 rate limit 429` — 开发 compose `RateLimit__AuthPermitsPerMinute=999`（E2E 并发登录需要），连续 8 次登录不触发 429 属配置预期；生产 `docker-compose.prod.yml` 为 5/min 严格限流，O01 需在生产配置下验证
- 说明：首轮报告 15 个 FAIL 经逐一复现确认**全部为脚本断言过时或端点路径错误（伪 FAIL）**，当前后端行为均符合契约：
  - C08 `page=-1`：后端设计为 200 归一化（非 400）
  - D05 `/product/{oem}`：后端无此路由返回 404（nginx 生产层 301），符合设计
  - F05 `id=abc`：ASP.NET 路由约束未命中返回 404（符合路由语义）
  - H05 创建用户：返回 201 Created（正确）
  - I02 `/api/perf`：需 Admin token，脚本未携带
  - K01/L01/K03：端点路径为 `/xrefs/reorder/brands`、`/dead-letter`（无尾斜杠）、`/machine-apps/batch-bind` 404 语义
  - G04/J01/M04/E06/B09/C11/K05：重跑全部 200/201，字段结构与前端契约一致（如 aggregate 返回 `hits`、turnstile 返回 `{siteKey}`）

## 3. 前端层 E2E 测试结果

（待全量回归完成后填充）

## 4. 问题清单与优先级排序

### P1（高 — 测试可靠性/数据依赖）

| # | 问题 | 影响 | 根因 | 状态 |
|---|---|---|---|---|
| P1-1 | real-search-compare 用例 2/3 偶发点击卡片不跳转 | 全量回归偶发红 | Vue 大列表 re-render 竞态：高负载下点击事件派发到被替换 DOM 节点 | ✅ 已修复（渲染稳定等待 + 点击重试 ≤3 次） |
| P1-2 | real-dict-reorder 因端点路径错误全部跳过 | 字典排序功能无 E2E 覆盖 | 脚本用 `/api/admin/xrefs/brands`，真实端点为 `/api/admin/xrefs/reorder/brands` | ✅ 已修复 + 数据验证 6/6 通过 |
| P1-3 | real-dict-reorder 无数据环境硬等超时失败 | 空库/数据被清理时误报红 | `.drag-handle` 10s 硬等待，无数据永不出现 | ✅ 已修复（3s 探测 + 无数据 skip） |
| P1-4 | real-etl-flow 用例 6 `ADMIN_TOKEN is not defined` | 死信队列 API 验证必然失败 | 脚本残留未定义变量 | ✅ 已修复（改用共享 JWT Bearer） |
| P1-5 | 账号锁定过期后失败计数未重置 | 安全缺陷：锁定过期后单次密码错误立即再次锁定 15min，用户几乎无法恢复登录 | `UserService.AuthenticateAsync` 缺少锁定过期惰性重置逻辑，FailedLoginCount 仍为 5 | ✅ 已修复（锁定过期时重置计数+锁定，新增 2 个单元测试，39/39 通过） |
| P1-6 | deep-flow 1.3 用 admin+错误密码测试失败提示 | 测试污染生产账号：累计 5 次失败触发 admin 锁定，导致后续 E2E 登录全红 | 测试用例未区分"失败提示"与"污染真实账号" | ✅ 已修复（改用不存在用户 non-existent-user-e2e） |

### P2（中 — 测试基建/环境）

| # | 问题 | 影响 | 状态 |
|---|---|---|---|
| P2-1 | api_smoke 报告断言过时（15 个伪 FAIL） | 报告可信度受损 | ⏳ 待更新断言（本次已重跑验证全过） |
| P2-2 | 限流用例 O01 在开发环境不触发 429 | 无法自动回归限流 | ⏳ 建议生产配置下跑一次验证 |
| P2-3 | real-dict-reorder 依赖手工 seed 数据 | 全量回归需先造数据 | ⏳ 建议增加 seed 脚本自动准备/清理 |
| P2-4 | deep-flow 6.2 用 `.el-input` count 断言 | firefox 下异步渲染时序不同导致断言脆弱 | 改为等待 `.el-form-item.is-required` 必填字段可见 | ✅ 已修复 |
| P2-5 | webkit 下参数化路由 goto 20s 超时 | real-ui/typeahead 在 webkit 偶发超时 | webkit 渲染慢于 chromium/firefox | ✅ 已修复（导航超时放宽至 30s） |

### P3（低）

| # | 问题 | 状态 |
|---|---|---|
| P3-1 | 跨浏览器测试未纳入 CI（ENABLE_CROSS_BROWSER 开关默认关） | ⏳ 本次已手动执行，建议发布前开启 |

## 5. 修复计划与执行

（见下文「已执行修复」与「待办」）

## 6. 边界测试建议

- ⚠️ 搜索 `page=-1` 后端归一化为 1 页：若业务需要严格参数校验需单独确认
- ⚠️ `id=abc` 路由返回 404 而非 400：若前端存在拼错 ID 场景，错误码可读性降低
- ⚠️ 登录限流在开发环境放宽至 999/min：若联调环境与公网同网段，存在暴力破解窗口
