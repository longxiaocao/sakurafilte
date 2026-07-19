# 未采纳的 P1/P2 改进建议归档

格式: `- [日期] [优先级] [问题描述] | 触发文件: [路径]`

---

- [2026-07-18] [P2] SSE 后端 cookie auth 方案 (V24-F78 采用前端 fetch 替代, 后端端点未显式 RequireAuthorization) | 触发文件: backend/src/SakuraFilter.Api/Endpoints/AdminEtlEndpoints.cs L208
- [2026-07-18] [P2] Codecov/Coveralls 上传需 secrets.CODECOV_TOKEN 配置 | 触发文件: .github/workflows/ci.yml
- [2026-07-18] [P2] 覆盖率门禁待核心 Service 覆盖率达 40%+ 后启用 reportgenerator -thresholds | 触发文件: .github/workflows/ci.yml coverage job
- [2026-07-19] [P2] E2E 测试选择器规范化: 现有 .el-input__inner + .first() 不稳定, 应改用 getByPlaceholder/getByLabel/getByRole (需逐个查前端源码确认 placeholder/label) | 触发文件: frontend/tests/e2e/public-search-flow.spec.ts L17/L51, frontend/tests/e2e/admin-products-flow.spec.ts L33
- [2026-07-19] [P2] SetWithSize 后续新增 IMemoryCache.Set 调用点应统一使用, 建议加 Roslyn analyzer 强制 (V24-F85 仅覆盖 5 处) | 触发文件: backend/src/SakuraFilter.Api/Extensions/MemoryCacheExtensions.cs
- [2026-07-19] [P2] 前端 v-for key 静态规则: 建议在 ESLint vue/require-v-for-key 基础上加自定义规则禁止 :key="i" (V24-F86 仅 3 处手工修复) | 触发文件: frontend/eslint.config.js (待新增规则)
- [2026-07-19] [P2-3] E2E 巡检 (_design_audit.py + _api_contract_test.py) 纳入 CI 需 Testcontainers PG, 与 ADR #4 冲突, 待用户决策反转 ADR #4 | 触发文件: .github/workflows/ci.yml
- [2026-07-19] [P2-10] ProductDbContext 拆分 Alert* 实体到 AlertDbContext, 长期重构, v27+ 重新评估 | 触发文件: backend/src/SakuraFilter.Infrastructure/Data/ProductDbContext.cs
- [2026-07-19] [v27-1] PostgresSearchProvider Phase 2 keyset 分页 — 50K 压测已完成 (OFFSET 深度退化比 1.03x, 维持暂缓决策, 见 ADR #5), 1M 扩容验证留后续独立库 sakurafilter_perf_tests | 触发文件: backend/src/SakuraFilter.Search/PostgresSearchProvider.cs L224
- [2026-07-19] [v27-3 后续] 1M 数据扩容压测: 50K 数据下 OFFSET 深度退化 ≤1.03x, 需在独立库 (sakurafilter_perf_tests) 验证 1M 规模下深分页 (OFFSET > 100000) 是否显著退化, 避免污染 spike_test_v3 | 触发文件: spike-test/_perf_offset_paging.py (--scale-up 参数已就绪)
- [2026-07-19] [v27-3 后续] GIN trgm 索引验证: ~~q_filter 场景 ILIKE 全表扫描 1879ms 是真实瓶颈, 加 GIN trgm 索引预计降到 50-200ms~~ (v28-1 验证完成, 见 ADR #5: GIN trgm 对当前 OR + EXISTS xref SQL 模式无收益, PG 优化器不选, 已弃用此方向; 真实优化方向是 SQL 拆分重写, 见 v28-2) | 触发文件: backend/src/SakuraFilter.Search/PostgresSearchProvider.cs (ILIKE 查询路径)
- [2026-07-19] [v28-1 验证完成] GIN trgm 索引验证脚本 (_perf_gin_trgm_verify_v3.py): baseline SQL (OR + EXISTS xref) P95=197ms, 简化 SQL (无 EXISTS) + GIN trgm P95=20ms (6x 提升), 但简化 SQL 需改业务逻辑, 留 v28-2 SQL 拆分重写 | 触发文件: spike-test/_perf_gin_trgm_verify_v3.py
- [2026-07-19] [v28-2 候选] PostgresSearchProvider SQL 拆分重写: 当前 OR + EXISTS xref 模式让 GIN trgm 索引失效 (PG 优化器不选), 改造方向是先 GIN trgm 扫 products 5 字段 → 候选 product_id 集合 → 半 JOIN cross_references 走 idx_xref_product B-tree。预期 P95 从 197ms → 20-50ms (4-10x), 改动面: PostgresSearchProvider.cs BuildWhereClause + SearchAsync 重写 + 加 GIN trgm migration | 触发文件: backend/src/SakuraFilter.Search/PostgresSearchProvider.cs L106-L137 (BuildWhereClause q 块)
- [2026-07-19] [v27-2 后续] CleanupOrphanImages CLI 当前需手动执行, 后续可加 cron (如每周一次) 自动清理, 或在 K8s CronJob 中部署 | 触发文件: backend/src/SakuraFilter.Cli/Program.cs
- [2026-07-19] [v27-2 后续] IObjectStorage.ListAsync 仅用于 CLI, 未对 AdminProductImageService 等业务层暴露, 后续若有 UI 列桶需求需补单元测试 | 触发文件: backend/src/SakuraFilter.Core/Interfaces/IObjectStorage.cs
- [2026-07-19] [v30-3 / P1-1] DictManagerLayout 通用组件提取: 8 字典页 (AdminEnginesView/AdminOemNo3sView/AdminMachinesView/AdminOemBrandsView/AdminMediasView/AdminProductName1sView/AdminProductName2sView/AdminTypesView) 代码 80% 逐字重复 (1477 行重复代码), 提取 useDictManager composable + DictManagerLayout.vue 后可降到 ~400 行 (减少 78%), 后续新增字典页从 1.5h → 20min。预估总成本 9h (composable 2h + layout 2.5h + slot/props 1h + 迁移 8 页 2.5h + 兜底统一 1h)。本次 V24-F102 选择在 8 文件独立加 loadError/el-alert/SkeletonCard (P0-2 + P1-2), 未走提取路线, 因 P1-1 超 15min 高价值阈值且非紧急 | 触发文件: frontend/src/views/admin/AdminEnginesView.vue 等 8 个字典页
- [2026-07-19] [v30-3 / P2-1] ✅ 已实施 (V24-F103 v30-4, commit 302d622): 字典页空状态文案不统一: 7 个字典页用 i18n key t('common.action.no_data_click_top_right'), AdminOemBrandsView L283 和 AdminProductName1sView L224 用手写中文。修复 ~5min, 但本次未修 (非阻断级, 已归档) | 触发文件: frontend/src/views/admin/AdminOemBrandsView.vue L283, frontend/src/views/admin/AdminProductName1sView.vue L224
- [2026-07-19] [v30-3 / P2-2] ✅ 已实施 (V24-F103 v30-4, commit 302d622): 字典页跨标签页 stale 数据感知: 当前无定时刷新也无 visibilitychange 监听, 跨标签页编辑后无感知。方案: 监听 document.visibilitychange 事件, 页面重新可见时调 load()。预估 ~30min (若走 P1-1 路线, 可内置到 useDictManager)。本次未实施, 字典页 stale 影响小 | 触发文件: frontend/src/views/admin/AdminEnginesView.vue 等 8 个字典页

