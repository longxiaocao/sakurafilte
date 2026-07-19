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
- [2026-07-19] [v27-1] PostgresSearchProvider Phase 2 keyset 分页 — 需先压测 OFFSET 深分页性能 (1M 数据 page>100), 决策是否破坏前端 Page 契约引入 cursor 参数 | 触发文件: backend/src/SakuraFilter.Search/PostgresSearchProvider.cs L224
- [2026-07-19] [v27-2 后续] CleanupOrphanImages CLI 当前需手动执行, 后续可加 cron (如每周一次) 自动清理, 或在 K8s CronJob 中部署 | 触发文件: backend/src/SakuraFilter.Cli/Program.cs
- [2026-07-19] [v27-2 后续] IObjectStorage.ListAsync 仅用于 CLI, 未对 AdminProductImageService 等业务层暴露, 后续若有 UI 列桶需求需补单元测试 | 触发文件: backend/src/SakuraFilter.Core/Interfaces/IObjectStorage.cs
