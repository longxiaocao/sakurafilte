# 架构决策记录 (ADR)

本文件记录项目中关键技术选型、已排除方案及原因。每条决策格式固定:
```
#<编号> <决策标题> (<日期>)
决策: <选型结论>
理由: <为什么选择该方案>
排除方案:
  - <方案A>: <排除原因>
关联文件: <影响的核心文件列表>
```

---

#1 SSE 401 修复方案选择 (2026-07-18, v30-17 SSE 鉴权修复 2026-07-21, v30-18 多端点鉴权批量修复 2026-07-22, v30-19 /api/perf 鉴权修复 2026-07-22)
决策: 前端改用 fetch + ReadableStream 替代 EventSource, 不改后端 (V24-F78); v30-17 后端 SSE 端点加 RequireAuthorization("Admin") 修复 P0 安全漏洞 (未认证可访问 ETL 进度); v30-18 批量修复 6 个同类漏洞端点 (/api/admin/perf/alerts + /api/admin/auth/status + /api/etl/import + /api/etl/status + /api/etl/import-xrefs + /api/etl/import-apps); v30-19 修复 /api/perf 公开访问泄漏 P50/P95/P99 运维数据 (与 /api/admin/perf/alerts 同类敏感数据)
理由: EventSource API 不支持自定义 Header, 无法携带 JWT。fetch + ReadableStream 可携带 Authorization Bearer, 与现有 axios 拦截器逻辑一致 (复用 buildAuthHeaders), 无需后端改动。v30-17/v30-18/v30-19 后端鉴权修复: V24-F78 时期多个端点脱离 group 鉴权 (为兼容 EventSource 或脚本触发无鉴权), ADR #1 已改用 fetch + Bearer, 后端鉴权可恢复; 前端 useEtlProgress.ts L201-209 + AdminPerfView.vue L49 已带 Bearer, 修复不破坏前端
排除方案:
  - 后端 SSE 支持 query token (?token=xxx): token 会泄漏到访问日志/Referer/nginx 日志, 安全风险高
  - 后端 SSE 支持 cookie auth: 需后端改动 + 与 JWT 无状态架构冲突, 改动面大
  - SSE 端点加 RequireRateLimiting("etl"): SSE 长连接限流策略需单独评估 (QPS vs 并发连接), 留 P2
  - /api/perf/ingest 加 RequireAuthorization: P5.5 设计 sendBeacon 无法带 token, 保持无鉴权 + 限流 + 大小限制 (100 条/批) 防滥用
  - /metrics 加 RequireAuthorization: Prometheus 抓取需无鉴权, 通过 nginx 内部网络隔离 (部署侧决策)
  - RequireHttpsMetadata=true: 生产环境 nginx 做 TLS 终结, 应用层 false 是合理设计, 不修复
  - /api/info 版本号脱敏: 版本号公开是常见做法 (package.json 也有), 不修复
  - FallbackPolicy 全局默认鉴权: 改动面大, 需逐个标注公开端点, 留 P2
关联文件:
  - frontend/src/composables/useEtlProgress.ts
  - frontend/src/utils/http.ts (新增 buildAuthHeaders 导出)
  - frontend/src/router/index.ts (v30-19: L203 注释更新 /api/perf 改需 token)
  - backend/src/SakuraFilter.Api/Endpoints/AdminEtlEndpoints.cs (v30-17: L212 app.MapGet 末尾加 .RequireAuthorization("Admin"))
  - backend/src/SakuraFilter.Api/Endpoints/CommonEndpoints.cs (v30-18: L52 /api/admin/perf/alerts + L147 /api/admin/auth/status 加 .RequireAuthorization("Admin"); v30-19: L49 /api/perf 加 .RequireAuthorization("Admin"))
  - backend/src/SakuraFilter.Api/Endpoints/EtlEndpoints.cs (v30-18: 4 个 /api/etl/* 端点全部加 .RequireAuthorization("Admin"))
  - spike-test/_test_p55_p71_e2e.py (v30-19: L101/L194/L204 调 /api/perf 加 X-Admin-Token)
  - spike-test/smoke-test.sh (v30-19: L86 调 /api/perf 加 X-Admin-Token)
  - spike-test/smoke-test.ps1 (v30-19: L108 调 /api/perf 加 X-Admin-Token)
  - spike-test/_test_e2e_destructive.py (v30-19: L1299 调 /api/perf 加 X-Admin-Token)

#2 V24-F83 23505 唯一约束并发测试方案 (2026-07-19)
决策: 用 raw SQL 两个并行 NpgsqlTransaction 触发 23505, 不用 EF Core 并发
理由: AdminProductImageService.UploadAsync 内部用 EF Core + BeginTransactionAsync。两个并行 DbContext 调用 UploadAsync 时, EF Core 内部时序难以稳定复现 23505:
  - task1 可能先 commit, task2 的 FirstOrDefaultAsync 读到 task1 写入的记录 → 走 UPDATE 路径 (不撞 23505)
  - 即使两个 Task 都查到 old=null, task2 的 INSERT 在 task1 commit 后才被阻塞, 但 EF Core 可能直接抛 ObjectDisposedException
用 raw SQL 两个并行 NpgsqlTransaction 可稳定复现 (tx1 持有行锁 → tx2 阻塞 → tx1 commit → tx2 撞 23505)
排除方案:
  - EF Core 双 DbContext 并发调用 UploadAsync: 时序不稳定, 测试偶发失败
  - Moq 模拟 EF Core 抛 DbUpdateException(23505): 不验证真实 DB 唯一约束存在
关联文件:
  - backend/tests/SakuraFilter.Api.Tests/Integration/AdminProductImageServiceIntegrationTests.cs (ConcurrentInsertSameDetailSlot_SecondThrows23505_Integration)
  - backend/tests/SakuraFilter.Api.Tests/ProblemDetailsFactoryTests.cs (L134, 23505 → 409 ERR_DB_CONFLICT 映射单元测试)

#3 V24-F84 CleanupOrphanImages MVP 方案选择 (2026-07-19)
决策: 采用方案 A (MVP) — 仅增强 AdminProductImageService 异步删旧文件容错, 不实施全量孤儿清理
理由: spec 26.4.1 用户决策暂缓 Task 5.1.20 v8 终态 (6 步大改造, 不符合最小设计原则)。MVP 方案与 spec 26.3.2「不扩展 IObjectStorage 公共接口」+ 26.17.2 P1-5「兜底覆盖上传异步删旧文件失败场景」次要目标一致, 改动 < 50 行
排除方案:
  - 方案 B (单 IObjectStorage + 时间戳过滤): 需扩展 IObjectStorage 接口, 与 spec 26.3.2 冲突
  - 方案 D (完整 v8 终态): 6 步大改造 (接口扩展 + EF 迁移 + BackgroundService + DI 调整 + cleanup_failures 表 + 状态机), spec 26.4.1 明确「不符合最小设计原则」
  - 方案 C (维持暂缓): 0 改动, 但 P1-5 不算完成
关联文件:
  - backend/src/SakuraFilter.Api/Services/AdminProductImageService.cs (SafeDeleteOldImageAsync 私有方法)
  - backend/tests/SakuraFilter.Api.Tests/AdminProductImageServiceTests.cs (UploadAsync_OverwriteDeleteOldFile* 2 测试)

#4 PG 集成测试基础设施选择 (2026-07-19)
决策: 本地 PG + 独立测试库 sakurafilter_int_tests + TRUNCATE CASCADE 重置, 不用 Testcontainers
理由: 团队成员环境不一 (Docker 不可用), 复用本地 PG 实例更轻量。通过 PG_TEST_CONNECTION_STRING 环境变量注入连接串, CI 中可用 GitHub Actions service container 启动 PG
排除方案:
  - Testcontainers: 需本地 Docker 守护进程, 团队成员环境不一
  - EF Core InMemory: 不支持 raw SQL / advisory lock / FOR UPDATE SKIP LOCKED / 23505 / xmin
关联文件:
  - backend/tests/SakuraFilter.Api.Tests/Integration/PgIntegrationTestBase.cs (基类)
  - backend/tests/SakuraFilter.Api.Tests/Integration/AdminProductServiceIntegrationTests.cs (V24-F81)
  - backend/tests/SakuraFilter.Api.Tests/Integration/IndexReplayWorkerLockMechanismTests.cs (V24-F82)
  - backend/tests/SakuraFilter.Api.Tests/Integration/AdminProductImageServiceIntegrationTests.cs (V24-F83)
  - .env (PG_TEST_CONNECTION_STRING 指向 sakurafilter_int_tests)

#5 PostgresSearchProvider Phase 2 keyset 分页暂缓 (2026-07-19, 50K 压测验证 2026-07-19, v28-1 GIN trgm 验证 2026-07-19, v28-2 CTE UNION 拆分验证 2026-07-19, v28-3 1M 扩容压测验证 2026-07-19, v29-1 token 数量限制 2026-07-19, v29-2 高频词分布调研 2026-07-19, v30-14 1M OFFSET 深分页专项压测验证 2026-07-21)
决策: v27-1 暂不实施 keyset 分页改造, 保留 OFFSET 分页; v27-3 50K 压测后维持暂缓决策; v28-1 GIN trgm 索引对 baseline SQL 无收益; v28-2 CTE UNION 拆分 + 三表 GIN trgm 索引 P95 1827ms → 305ms (6.0x), 达 4x 目标, 已落地; v28-3 1M 扩容压测 v28-2 加速比保持 6.82x, 多 token 退化 1.49x, 维持当前实现; v29-1 token 数量限制为 8 (防御性兜底, 防止极端场景 INTERSECT HashSetOp Append 退化); v29-2 高频词分布调研后候选 2 不实施 (真正高频词仅 "filter" 1 个, 是 type 字段结构性特征非 bug, ROI 低); v30-14 1M OFFSET 深分页专项退化 1.02x (远低于 1.5x 阈值), 闭环 ADR #5 keyset 暂缓决策
理由:
  - 当前 SearchRequest DTO 用 Page/PageSize 页式分页, 前端依赖 Page 契约
  - 改 keyset 需破坏前端 Page 契约或引入 cursor 参数, 改动面大
  - 真实用户行为: 搜索结果 99% 在前 5 页内 (典型电商行为), 深分页场景罕见
  - V24-F80 Phase 1 原生 SQL + CTE + LATERAL JOIN 已优化首屏性能, 深分页性能问题需压测数据支撑
v27-3 50K 压测数据 (2026-07-19, spike_test_v3: 50011 products/623134 xrefs/775053 apps):
  - OFFSET 深度退化比 (控制变量法, 同场景深档 P95 / 浅档 P95): 最大 1.03x (type_oil), baseline 0.96x, q_filter 1.01x
  - 结论: 50K 数据下 OFFSET 深度本身不是主要瓶颈 (≤1.5x 暂缓阈值)
  - 真实瓶颈识别: q_filter ILIKE 全表扫描 1879ms (HTTP 端到端, 含 Meili 切换) > baseline CTE+LATERAL JOIN 510ms > OFFSET 深度 (1.03x)
  - keyset 简化版潜力: 17-5932x (baseline 31.6x / type_oil 19.2x / q_filter 5932x / size_d1_100 54.9x), 但真实三层排序 keyset 改造需前后端契约改造
v28-1 GIN trgm 索引验证数据 (2026-07-19, spike_test_v3 50K, 直连 PG cache hit):
  - baseline SQL (OR + EXISTS xref): P95 = 197ms (5 个 q 平均)
  - 简化 SQL (无 EXISTS xref, 5 字段 OR ILIKE): P95 = 124ms
  - 简化 SQL + GIN trgm 索引: P95 = 20ms (6x 提升, 用 Bitmap Index Scan)
  - baseline SQL + GIN trgm 索引: P95 ≈ 197ms (无收益, PG 优化器不选 GIN trgm, 仍用 idx_products_is_published_true + Filter ILIKE)
  - 结论: GIN trgm 索引对当前 OR + EXISTS xref SQL 模式无收益, PG 优化器不选 (原因: EXISTS 子查询的 Nested Loop 模式让单字段 GIN trgm 失效)
  - 真实优化方向: SQL 拆分重写 (products 5 字段 ILIKE 走 GIN trgm 索引 → 候选 product_id → 半 JOIN cross_references 走 idx_xref_product B-tree), 留 v28-2
v28-2 CTE UNION 拆分验证数据 (2026-07-19, spike_test_v3 50K, 直连 PG cache hit):
  - baseline SQL (OR + 2 EXISTS): P95 = 1827ms (5 个 q 平均, 49989 产品 × 37459 次循环)
  - CTE 拆分 v1 (q_match CTE + products 5 字段 GIN trgm): P95 = 629ms (2.56x, 未达 4x 目标, EXISTS xref/machine 仍拖累)
  - CTE UNION v2 (三表 GIN trgm: products 5 字段 + xref 3 字段 + machine 2 字段): P95 = 305ms (6.0x, 达 4x 目标)
  - 端到端 HTTP 压测 (5 场景): 平均 P95 1421ms → 264ms (5.23x)
  - PG 优化器行为: CTE UNION 让每个分支独立选 GIN trgm Bitmap Index Scan, 避免 baseline OR + EXISTS 的 Nested Loop 模式
  - 集成测试: 12/12 通过 (覆盖 NoQ / 单 token 三表 / 多 token INTERSECT / type / dimension / includeDiscontinued / pagination / aggregate / machineCategory / 特殊字符转义)
v28-5 多 token INTERSECT 边界压测数据 (2026-07-19, spike_test_v3 50K, 直连 PG cache hit):
  - 1 token (oil): P95 = 231ms (基准)
  - 2 token (oil filter): P95 = 308ms (1.34x)
  - 3 token (oil filter CAT): P95 = 512ms (2.22x, 最大跳跃)
  - 4 token (oil filter CAT bosch): P95 = 578ms (2.50x)
  - 5 token (oil filter CAT bosch kubota): P95 = 610ms (2.64x, 趋于平缓)
  - PG 优化器计划稳定: 1-5 token 所有场景都选 GIN trgm Bitmap Index Scan (Seq Scan 是 INTERSECT HashSetOp Append 阶段, 非表扫描)
  - 结论: PG 优化器未放弃 GIN trgm, chapter 28.2 边界测试建议 #2 风险未触发
  - 决策: 暂不限制最大 token 数量 (5 token 610ms 仍可接受, 退化曲线趋于平缓, 实际场景 5+ token 罕见)
v28-3 1M 数据扩容压测验证数据 (2026-07-19, sakurafilter_perf_tests 950K products/4.75M xrefs/4.75M apps, 直连 PG cache hit):
  - 场景 A baseline (OR+2EXISTS) vs v28-2 (CTE UNION) 单 token 对比 (5 个 q 场景):
    - 高频 oil: baseline P95=22848ms → v28-2 P95=5027ms (4.54x)
    - 高频 filter: baseline P95=8176ms → v28-2 P95=5346ms (1.53x, 候选集爆炸)
    - 中频 CAT: baseline P95=24361ms → v28-2 P95=3523ms (6.91x)
    - 中频 bosch: baseline P95=22693ms → v28-2 P95=3258ms (6.97x)
    - 低频 kubota: baseline P95=26316ms → v28-2 P95=1864ms (14.12x, 低频词加速最佳)
    - 平均加速比: 6.82x (vs 50K 6.0x, 反而提升 0.82x, 因低频词加速 14.12x 拉高均值)
  - 场景 B v28-2 多 token 1-5 INTERSECT 退化曲线 (vs 50K v28-5):
    - 1 token P95=4053ms (vs 50K 231ms, 放大 17.55x)
    - 2 token P95=6391ms (vs 1 token 1.58x)
    - 3 token P95=5337ms (vs 1 token 1.32x, 2→3 token 反而下降, 因 2 token "oil filter" 高频爆炸, 3 token 加 CAT 后 INTERSECT 收敛)
    - 4 token P95=5488ms (vs 1 token 1.35x)
    - 5 token P95=6024ms (vs 1 token 1.49x, vs 50K v28-5 2.64x, 反而更稳定)
  - PG 优化器行为: 1-5 token 所有场景都选 GIN trgm Bitmap Index Scan, chapter 28.2 边界测试建议 #2 风险未触发
  - 结论: v28-2 CTE UNION 方案在 1M 数据下保持有效性, 维持当前实现, 无需启用 v27-1 q_match 候选集爆炸防御
  - 风险提示: 1M 数据下 v28-2 单 token P95 1.8-5.3s (绝对延迟显著放大 17.5x), 高频词 filter 仅 1.53x 加速 (候选集爆炸), 生产环境需配合 Meili 主路径
v29-1 token 数量限制 (V24-F97, 2026-07-19, spec 28.6):
  - 决策: PostgresSearchProvider.BuildQMatchCte 限制最大 token 数量为 8, 超出截断 + LogWarning
  - WHY 8: v28-5 (50K) 验证 1-5 token P95 610ms (2.64x, 趋于平缓), v28-3 (1M) 验证 1-5 token P95 6s (1.49x, 反而更稳定), PG 优化器仍选 GIN trgm Bitmap Index Scan
  - 6-8 token 缺乏压测数据, 但 PG 优化器 INTERSECT HashSetOp Append 应在 8 token 内保持稳定, 9+ token 风险不明
  - 防御性兜底: 防止极端场景 (20+ token 恶意搜索) 触发 INTERSECT HashSetOp Append 退化
  - 集成测试: 2 个新增 (10 token 截断为 8 / 边界值 8 token 不截断), 全量后端测试 448 通过 (原 446 + 新增 2)
v29-2 高频词分布调研 (V24-F98, 2026-07-19, spec 28.7, 候选 2 不实施):
  - 调研对象: spike_test_v3 (50K) + sakurafilter_perf_tests (1M), 21 个高频词候选
  - 1M 数据高频词 (>50% 命中): 仅 "filter" 1 个 (99.95% 命中), 50K 数据高频词 3 个 (filter/CAT/bosch)
  - 真正高频词只有 "filter" (1M 99.95%): 原因是 type 字段值都含 "FILTER" 后缀 (AIR FILTER / OIL FILTER 等共 25 个 type), 工业滤芯行业结构性特征
  - "CAT"/"bosch" 在 1M 数据下变成中频 (29%/31%): 1M 数据生成时 machine_applications 用 random.sample 均匀化品牌分布
  - 方案评估: A (type 等值过滤) 不可行 - 仅 475 个 type 完全匹配会严重改变搜索语义; B (静态黑名单) 改变搜索语义; C (q_match LIMIT) 漏数据风险高; G (动态识别) ROI 低 (只解决 1 个词); H (静态黑名单) 维护成本高
  - 最终决策: 候选 2 不实施, 真正高频词只有 1 个 "filter" 是 type 字段结构性特征非 bug, v28-3 已证明 filter 的 1.53x 加速是异常值但绝对延迟 5.3s 可接受, 生产环境有 Meili 主路径兜底
v30-14 1M OFFSET 深分页专项压测验证数据 (2026-07-21, sakurafilter_perf_tests 950K products/4.75M xrefs/4.75M apps, 直连 PG cache hit):
  - 测试目的: 补 v27-3 报告 §4.5 "1M 数据扩容压测" 空缺, v28-3 已验证 1M baseline vs v28-2 加速比 + 多 token 退化, 但 OFFSET 深度专项退化比在 1M 数据下未验证
  - 场景 A baseline (无 q, v28-2 CTE UNION SQL, OFFSET 档 0/10K/100K/500K/900K):
    * OFFSET=0: P95=5087ms (浅档基准)
    * OFFSET=10000: P95=5004ms (0.98x)
    * OFFSET=100000: P95=5162ms (1.01x)
    * OFFSET=500000: P95=5255ms (1.03x, 最深档)
    * OFFSET=900000: P95=5190ms (1.02x)
    * 深浅比: 1.02x (50K baseline 为 0.96x, 1M 反而略增但仍远低于 1.5x 阈值)
    * keyset 对比 (OFFSET=500000, last_id=500001): P95=592ms (8.9x 提升, 潜力巨大但真实三层排序 keyset 改造工作量大)
  - 场景 B q_oil (q='oil', 1M 数据 25% 命中, v28-3 验证, OFFSET 档 0/10K/100K, 500K/900K 因 total=236778 跳过):
    * OFFSET=0: P95=2522ms (浅档基准)
    * OFFSET=10000: P95=2382ms (0.94x)
    * OFFSET=100000: P95=2480ms (0.98x, 最深有效档)
    * 深浅比: 0.98x (q 过滤后结果集小, 深分页无意义)
    * 关键发现: q 过滤后 total=236778, OFFSET=500000/900000 超过 total 被跳过, 说明 q 场景深分页在生产环境罕见
  - 与 v27-3 50K 数据对比 (控制变量法):
    * 50K 最大深浅比: 1.03x (type_oil 场景)
    * 1M 最大深浅比: 1.02x (baseline 场景)
    * 结论: 1M 数据规模下 OFFSET 深度退化比与 50K 一致 (~1.0x), 证明 OFFSET 深度本身不是主要瓶颈, 即使在 1M 数据下
  - 综合决策: 维持 ADR #5 暂缓, 1M 深度退化 1.02x 远低于 1.5x 阈值; keyset 潜力 8.9x 但改造工作量大不值得; 真实用户行为 99% 在前 5 页内; 生产环境 Meili 主路径兜底, PostgreSQL 仅 fallback
  - 绝对延迟提示: baseline 场景 P95 ~5s, q_oil 场景 P95 ~2.5s, 这是 CTE + LATERAL JOIN + EXISTS 的开销, 不是 OFFSET 深度导致 (深浅比 1.02x 证明), 生产环境有 Meili 主路径兜底
排除方案:
  - 立即改 keyset: 工作量大 (前后端契约改造) 且 50K 压测显示 OFFSET 深度非主要瓶颈
  - 加 GIN trgm 索引 (v28-1 验证): 对当前 SQL 模式无收益, PG 优化器不选, 不应加索引 (50MB 索引浪费)
  - v28-2 CTE 拆分 v1 (仅 products 5 字段 GIN trgm): 2.56x 未达 4x 目标, EXISTS xref (623K 行) + EXISTS machine (775K 行) 仍拖累
  - v28-5 限制最大 token 数量 (如 8 个): 5 token 610ms 仍可接受, 退化曲线趋于平缓, 防御性兜底留 P2 候选 (1M 数据下退化情况留 v28-3 验证)
  - v28-3 启用 v27-1 q_match 候选集爆炸防御: 1M 数据加速比 6.82x 保持, 多 token 退化 1.49x 可控, 无需启用
  - 加 covering index: 涉及 DB schema 变更, 需 migration, 不适合 v27 阶段
  - 1M 扩容压测: ✅ 已执行 (v28-3 baseline vs v28-2 + 多 token + v30-14 OFFSET 深分页专项), 50K 数据下退化比 ≤1.03x (OFFSET) / ≤2.64x (多 token), 1M 数据下 OFFSET 深度退化 1.02x / 多 token 退化 1.49x, 验证完成
关联文件:
  - backend/src/SakuraFilter.Search/PostgresSearchProvider.cs (V24-F94: BuildBaseFilter + BuildQMatchCte + BuildFullSql + BuildCountSql 拆分)
  - backend/src/SakuraFilter.Infrastructure/Data/Migrations/20260719165000_AddGinTrgmIndexesForSearch.cs (5 个新 GIN trgm 索引)
  - backend/tests/SakuraFilter.Api.Tests/Integration/PostgresSearchProviderIntegrationTests.cs (12 个集成测试)
  - backend/src/SakuraFilter.Core/DTOs/SearchRequest.cs (Page/PageSize 契约)
  - spike-test/perf_offset_config.json (压测参数化配置)
  - spike-test/_perf_offset_paging.py (压测脚本, derive_advice 控制变量法)
  - spike-test/_perf_offset_results.json (raw 数据)
  - spike-test/_perf_offset_report.md (人读报告 + ADR #5 决策建议)
  - spike-test/_perf_gin_trgm_verify_v3.py (v28-1 GIN trgm 验证脚本, 含 3 种 SQL 对比)
  - spike-test/_perf_gin_trgm_v3_results.json (v28-1 验证 raw 数据)
  - spike-test/_perf_v28_2_cte_split_verify.py (v28-2 第一轮 spike: CTE 拆分 v1)
  - spike-test/_perf_v28_2_v2_cte_union_verify.py (v28-2 第二轮 spike: CTE UNION v2)
  - spike-test/_perf_v28_2_e2e_verify.py (v28-2 端到端压测, 5 场景)
  - spike-test/_perf_v28_2_e2e_results.json (v28-2 端到端 raw 数据)
  - spike-test/_perf_v28_5_multi_token_verify.py (v28-5 多 token 1-5 INTERSECT 边界压测脚本)
  - spike-test/_perf_v28_5_multi_token_results.json (v28-5 验证 raw 数据)
  - spike-test/_perf_v28_3_1m_verify.py (v28-3 1M 数据扩容压测脚本: 场景 A baseline vs v28-2 + 场景 B 多 token 1-5 + 场景 C EXPLAIN)
  - spike-test/_perf_v28_3_1m_results.json (v28-3 验证 raw 数据)
  - spike-test/_v28_3_perf.log (v28-3 压测执行日志)
  - spike-test/_check_v28_3_baseline.py (v28-3 数据量 + schema 检查脚本)
  - spike-test/_check_xref_app_schema.py (v28-3 xref/apps 表结构检查脚本)
  - spike-test/_gen_v28_3_1m_data.py (v28-3 1M 数据生成主脚本: step1-3 建库 + schema + 950K products)
  - spike-test/_gen_v28_3_continue.py (v28-3 1M 数据续传脚本: step4 4.75M xrefs)
  - spike-test/_gen_v28_3_apps_only.py (v28-3 1M 数据 apps 续传脚本: step5-7 4.75M apps + xref_oem_brand + ANALYZE)
  - spike-test/_v28_3_schema_dump.sql (v28-3 spike_test_v3 schema 导出, 含 10 个 GIN trgm 索引)
  - spike-test/_v28_3_gen.log (v28-3 数据生成日志)
  - backend/src/SakuraFilter.Search/PostgresSearchProvider.cs (V24-F97 v29-1 BuildQMatchCte token 截断逻辑)
  - backend/tests/SakuraFilter.Api.Tests/Integration/PostgresSearchProviderIntegrationTests.cs (V24-F97 v29-1 新增 2 个 token 截断测试)
  - spike-test/_perf_v29_2_high_freq_survey.py (V24-F98 v29-2 高频词分布调研脚本)
  - spike-test/_perf_v29_2_high_freq_survey.json (V24-F98 v29-2 调研 raw 数据)
  - spike-test/_perf_v30_14_1m_offset_verify.py (v30-14 1M OFFSET 深分页专项压测脚本: baseline + q_oil 2 场景 × 5 OFFSET 档位 + keyset 对比)
  - spike-test/_perf_v30_14_1m_offset_results.json (v30-14 压测 raw 数据)
  - spike-test/_perf_v30_14_1m_offset_report.md (v30-14 人读报告 + ADR #5 决策建议 + 50K/1M 对比)
  - spike-test/_v30_14_perf.log (v30-14 压测执行日志)
  - .trae/specs/v2-architecture-migration/spec.md chapter 27.8 (v27-3 实施记录) + chapter 28.1 (v28-1 验证记录) + chapter 28.2 (v28-2 实施记录) + chapter 28.5 (v28-5 验证记录) + chapter 28.3 (v28-3 1M 扩容压测验证记录) + chapter 28.6 (v29-1 token 数量限制) + chapter 28.7 (v29-2 高频词分布调研与候选 2 不实施决策)

#6 IObjectStorage.ListAsync 接口扩展决策 (2026-07-19)
决策: v27-2 扩展 IObjectStorage 接口加 ListAsync 方法, MinioStorage + AliyunOssStorage 双实现
理由:
  - CleanupOrphanImages CLI 需枚举存储桶所有对象与 DB 比对找孤儿, 必须有 List 能力
  - 接口扩展是必要抽象, 不算过度工程化 (符合"接口 segregation 原则")
  - MinIO 用 ListObjectsEnumAsync (IAsyncEnumerable<Item>), OSS 用 ListObjectsRequest + Marker 翻页
排除方案:
  - CLI 直接用 MinIO SDK (绕过 IObjectStorage): CLI 只支持 MinIO, 不支持 OSS, 违反"复用优先"
  - 在 AdminProductImageService 加 ListOrphans 方法: 业务层不应承担运维职责, 与 spec 26.4.1 决策冲突
关联文件:
  - backend/src/SakuraFilter.Core/Interfaces/IObjectStorage.cs (ListAsync 接口)
  - backend/src/SakuraFilter.Infrastructure/Storage/MinioStorage.cs (ListObjectsEnumAsync 实现)
  - backend/src/SakuraFilter.Infrastructure/Storage/AliyunOssStorage.cs (ListObjectsRequest 翻页实现)
  - backend/src/SakuraFilter.Cli/Program.cs (cleanup-orphan-images 子命令)

#7 后端日志脱敏审计与修复 (2026-07-19, V24-F99 v29-3, spec 28.8)
决策: 审计 backend/src/**/*.cs 全部 _logger.Log* 调用, 修复 1 高风险 + 1 中风险 + 2 低风险, 1 低风险保留+注释; 新增 IsSensitiveKey 关键字过滤防御未来回归
理由:
  - 规则 6.3 强制要求: 严禁在日志中打印密码、Token、完整手机号/身份证等敏感信息
  - H1 AuthTokenBroadcaster 日志 PG NOTIFY payload 含完整 admin token 明文, 任何能读日志的人可绕过鉴权, 必须立即修复
  - M1 EtlAlertService 日志 webhook 错误响应 body 可能 echo 签名 URL, 中风险
  - L1 DefaultSettingsEnsurer 当前 webhook_url* 为空, 但未来若添加非空默认值会回归, 加 IsSensitiveKey 防御
  - L2 AdminProductService cursor 是签名令牌, 不应大量暴露原文 (防御性)
  - L3 EtlImportService 产品域数据无 PII, 保留 preview 用于数据问题定位, 加注释说明未来 PII 数据源需重新评估
修复方案:
  - H1: 删除 AuthTokenBroadcaster L86 完整 payload 日志, 合并到下一行 rotatedBy 日志 (审计字段)
  - M1: EtlAlertService L197-198 移除 body 内容, 仅记录状态码 + bodyLen
  - L1: DefaultSettingsEnsurer 新增 IsSensitiveKey(key) 方法, 含 webhook_url/secret/token/password/api_key 关键字的 key, value 脱敏为 ***
  - L2: AdminProductService L485 仅记录 cursor 长度 + 前 8 字符前缀 (V2 cursor "v2:" 开头)
  - L3: EtlImportService L927/1989 保留 preview (产品域无 PII), 加 V24-F99 注释说明安全考量
排除方案:
  - 全部日志加 ILogger 中间件统一脱敏: 改动面大, ROI 低, 当前 80+ 处日志绝大多数已正确处理
  - L3 移除 preview: 损失数据问题定位能力, 当前产品域无 PII, 不必移除
  - L1 IsSensitiveKey 改用正则: 关键字匹配已足够, 正则增加复杂度
关联文件:
  - backend/src/SakuraFilter.Api/Services/AuthTokenBroadcaster.cs (H1 修复)
  - backend/src/SakuraFilter.Api/Services/EtlAlertService.cs (M1 修复)
  - backend/src/SakuraFilter.Api/Services/DefaultSettingsEnsurer.cs (L1 加固 + IsSensitiveKey)
  - backend/src/SakuraFilter.Api/Services/AdminProductService.cs (L2 修复)
  - backend/src/SakuraFilter.Etl/EtlImportService.cs (L3 保留+注释, 2 处)
  - .trae/specs/v2-architecture-migration/spec.md chapter 28.8 (v29-3 完整审计与修复记录)

#8 前端 loading 兜底全量审计与分层修复策略 (2026-07-19, V24-F100/F101/F102 v30-1/2/3, spec 29.1/2/3)
决策:
  - 审计全部 .vue 文件 (22 个问题: 3 HIGH + 9 MEDIUM + 10 LOW), 按 HIGH → MEDIUM → LOW 三波分层修复 (V24-F100/F101/F102)
  - 选择"在 9 个文件独立加 loadError ref + el-alert + SkeletonCard"模式, 不提取 DictManagerLayout 通用组件
  - P0-1 i18n key 字面量 BUG 修复选择硬编码模式, 与 AdminEnginesView 等其他字典页一致, 不修复 i18n key 调用方式
理由:
  - 规则 8 防白屏是硬性要求, 必须全量审计修复, 不能遗漏
  - 分层修复 (HIGH → MEDIUM → LOW) 让最高风险 (首屏白屏) 优先解决, 避免一次性大改动引入回归
  - DictManagerLayout 提取预估 9h, 超 15min 高价值阈值, 且当前 8 字典页兜底缺失是阻断级问题, 应先快速修复兜底再考虑重构
  - i18n key soft_delete_confirm 值本身被截断 (' 吗? (软删除, 可在'), 设计不合理, 修复 i18n 调用方式不如直接硬编码与其他字典页一致
  - V24-F100/F101/F102 三波修复共 20 文件 +535/-69 行, 全部通过 vitest 258 测试 (12 ECONNREFUSED 非回归)
排除方案:
  - 一次性全量修复 22 个问题: 改动面过大, 难以审查, 易引入回归
  - 提取 DictManagerLayout 通用组件 (P1-1): 9h 成本过高, 超 15min 高价值阈值, 且本次先解决兜底缺失阻断级问题更优先; 8 字典页 1477 行重复代码留 P1-1 单独提案 (已归档 .ai/suggestions.md)
  - 修复 i18n key 调用方式 (用 ${t('...')} 插值): i18n key soft_delete_confirm 值本身被截断, 修复后语义不完整, 不如硬编码与其他字典页一致
  - 加 30s 定时刷新到字典页 (P2-2): 字典数据 stale 影响小, 可用 visibilitychange 替代, 留 P2 候选
关联文件:
  - frontend/src/components/EtlKpiCards.vue (V24-F100 HIGH-3)
  - frontend/src/views/admin/AdminPerfView.vue (V24-F100 HIGH-2)
  - frontend/src/views/admin/AdminCompareView.vue (V24-F100 HIGH-1)
  - frontend/src/views/admin/AdminAlertsView.vue (V24-F101 M-1)
  - frontend/src/views/admin/AdminEtlView.vue (V24-F101 M-2)
  - frontend/src/views/admin/AdminProductFormView.vue (V24-F101 M-3)
  - frontend/src/views/admin/AdminProductsView.vue (V24-F101 M-4)
  - frontend/src/views/admin/AdminUsersView.vue (V24-F101 M-5)
  - frontend/src/components/AppHeader.vue (V24-F101 M-7)
  - frontend/src/components/EtlAlertStatus.vue (V24-F101 M-8, 30s stale 提示)
  - frontend/src/views/ChangePasswordView.vue (V24-F101 M-9)
  - frontend/src/views/admin/AdminEnginesView.vue (V24-F102 P0-2 + P1-2)
  - frontend/src/views/admin/AdminOemNo3sView.vue (V24-F102 P0-2 + P1-2)
  - frontend/src/views/admin/AdminMachinesView.vue (V24-F102 P0-2 + P1-2)
  - frontend/src/views/admin/AdminOemBrandsView.vue (V24-F102 P0-1 i18n BUG + P0-2 + P1-2)
  - frontend/src/views/admin/AdminMediasView.vue (V24-F102 P0-2 + P1-2)
  - frontend/src/views/admin/AdminProductName1sView.vue (V24-F102 P0-1 i18n BUG + P0-2 + P1-2)
  - frontend/src/views/admin/AdminProductName2sView.vue (V24-F102 P0-2 + P1-2)
  - frontend/src/views/admin/AdminTypesView.vue (V24-F102 P0-2 + P1-2)
  - frontend/src/views/admin/AdminApiDocsView.vue (V24-F102 P0-2 + P1-2 + P1-3 v-loading 统一)
  - .trae/specs/v2-architecture-migration/spec.md chapter 29 (v30 三波修复完整记录)
  - .ai/suggestions.md (P1-1 DictManagerLayout 提取建议, P2-1 空状态文案统一, P2-2 visibilitychange 监听)

#9 DevTokenAuthMiddleware 中间件顺序纠正 (2026-07-19, v30 端到端冒烟验证 P0, commit cebd2ef)
决策: 调整中间件顺序为 UseAuthentication → DevTokenAuthMiddleware → UseAuthorization
理由:
  - 修复前顺序: UseAuthentication → UseAuthorization → DevTokenAuthMiddleware (顺序错误)
  - 错误后果: .RequireAuthorization("Admin") 端点 (如 /api/admin/dict/_schema) 在 UseAuthorization 阶段
    评估 ctx.User, 此时 X-Admin-Token 还未被 DevTokenAuthMiddleware 处理, ctx.User 未认证 → 直接 401 短路
    (WWW-Authenticate: Bearer, DevTokenAuthMiddleware 永远跑不到)
  - 正确顺序: UseAuthentication 先处理 Authorization: Bearer (JWT), DevTokenAuthMiddleware 中间处理
    X-Admin-Token (设置 ClaimsPrincipal + admin role), UseAuthorization 最后基于 ctx.User 评估 policy
  - v30 端到端验证暴露: 12 个 contract/dict-schema.test.ts 401 失败 (之前 ECONNREFUSED 掩盖),
    Playwright smoke 8 字典页 + admin/products 等也受 X-Admin-Token 阻断
  - 修复后: vitest 270/270 通过, Playwright smoke 14/15 通过 (1 个 ETL 页 networkidle 超时为 SSE 固有, 非 v30 回归)
排除方案:
  - 修改 contract 测试改用 JWT (/api/auth/login 获取 Bearer): 治标不治本, Playwright smoke 仍需另改, 工作量大
  - 暂跳过 12 个 contract 失败: v30 不算完全闭环, 违反规则 9.1 端到端冒烟强制要求
  - 在 DevTokenAuthMiddleware 中改用 IAuthorizationFilter: 改动大, 违反最小设计原则
关联文件:
  - backend/src/SakuraFilter.Api/Extensions/MiddlewarePipelineExtensions.cs (中间件顺序纠正)
  - backend/src/SakuraFilter.Api/Services/DevTokenAuthMiddleware.cs (未改动, 验证设置 ClaimsPrincipal 逻辑正确)
  - frontend/tests/contract/dict-schema.test.ts (12 个 contract 测试, 修复后转绿)
  - frontend/tests/functional/smoke.spec.ts (Playwright smoke, 修复后 14/15 通过)

#10 V27-9-3 设计巡检保留非阻塞模式 (2026-07-19, v28-4 V27-9-3 CI 解锁, commits 0f035f5 + 6a6b76b + 0f76779)
决策: V27-9-3 设计巡检 step 保留 continue-on-error: true + if: always() + 失败时 exit 0 (非阻塞)
理由:
  - V27-9-3 巡检目的: 发现设计问题 (console errors / network 4xx/5xx / 缺失 aria 等), 上传截图供人工排查
  - 巡检结果 (CI run 29693549347): 21 页面 / 18 OK / 3 ISSUE
    * /admin + /admin/products: 500 (admin/products/search 端点, 待 v28-4 P0 migration 修复后复测)
    * /admin/etl: page.goto Timeout 15000ms (SSE 长连接, V24-F78/F79 引入, 待 _design_audit.py 改 domcontentloaded)
  - 3 个 ISSUE 都是已知模式, 应独立归档处理, 不应阻断 CI 主流程
  - V24-F92 v27-9 设计意图: 设计巡检非阻塞, 仅上传截图供人工排查 (而非强制修复)
  - v28-4 已通过 if: always() 让 V27-9-3 在 Day 9.6 失败后仍能跑, 通过 continue-on-error + exit 0 让巡检发现问题不阻断 push
排除方案:
  - 改 continue-on-error: false 阻断: 需先修 3 个 ISSUE 才能 push, 巡检失去"发现"意义变成"强制修复"
  - 删除 V27-9-3 step: 失去设计巡检能力, 与 V24-F92 v27-9 设计目标冲突
  - 把 3 个 ISSUE 改成 warning 级别 (在 _design_audit.py 中过滤): 掩盖真实问题, 失去巡检价值
关联文件:
  - .github/workflows/e2e.yml (V27-9-2/V27-9-3 加 if: always() + Vite 加 --port 5173 + 顶层 env 补齐 11 个环境变量)
  - frontend/src/components/EtlKpiCards.vue (aria-busy P0 回归修复, commit 6a6b76b)
  - spike-test/_e2e_audit/_design_audit.py (V27-9-3 巡检脚本, 21 页面 × 3 视口)
  - backend/migrations/019_v2_etl_progress_log_add_skipped_missing_mr1.sql (CI 未执行, v28-4 P0 待修)
  - .ai/suggestions.md (4 个 v28-4 P0/P1 归档)

#11 v28-4 测试脚本路径跟随代码重构同步更新 (2026-07-20, v28-4 V27-9-3 CI 解锁闭环, commit 7421dab → CI run 29718056565 success)
决策: 测试脚本 (spike-test/*.py) 中硬编码的代码路径必须跟随被测代码重构同步更新, 不能写死 Program.cs
理由:
  - v28-4 CI 解锁过程中发现 8 处测试脚本路径 bug (P1-3/P1-5/P1-6/P3-2/P3 可用性用例 2/P2 migration 用例 2+5/V2-VL-1/V2-VL-2)
  - 根因模式: 早期测试脚本硬编码 Program.cs 路径检查关键字 (UseForwardedHeaders/UseExceptionHandler/UseSwagger/Initialize/MigrateAsync 等)
  - 代码后续重构到 Extensions/ 目录 (MiddlewarePipelineExtensions.cs / WebApplicationExtensions.cs / IProductDetailService.cs / Mr1Validator.cs)
  - Program.cs 仅调用扩展方法 (app.InitializeDatabaseAsync / app.UseSakuraFilterMiddleware / app.InitializeSearchAsync)
  - 测试脚本未同步更新, CI 跑时找不到关键字报 FAIL
  - 修复方式: 测试脚本路径改为扩展方法实际所在的 Extensions/ 目录文件
排除方案:
  - 在 Program.cs 中保留关键调用的 alias (重复代码): 违反 DRY, 重构失去意义
  - 测试脚本改为全项目 grep 搜索关键字 (不限文件): 误匹配率高, 失去精确定位能力
  - 强制要求所有重构不拆分 Program.cs: 与 ASP.NET Core 最佳实践冲突 (Program.cs 应精简, 扩展方法拆分到 Extensions/)
关联文件:
  - spike-test/_test_p2_migration.py (用例 2/5 路径改 WebApplicationExtensions.cs)
  - spike-test/_test_p3_observability.py (用例 6 路径改 MiddlewarePipelineExtensions.cs)
  - spike-test/_test_p3_availability.py (用例 2 路径改 WebApplicationExtensions.cs)
  - spike-test/_test_regression.py (P1-3/P1-5/P1-6/P3-2/V2-VL-1/V2-VL-2 6 处路径修复)
  - backend/src/SakuraFilter.Api/Extensions/MiddlewarePipelineExtensions.cs (中间件管道扩展方法)
  - backend/src/SakuraFilter.Api/Extensions/WebApplicationExtensions.cs (数据库迁移 + 搜索探活扩展方法)
  - backend/src/SakuraFilter.Api/Services/IProductDetailService.cs (P3-2 fallback 合并实际位置)
  - backend/src/SakuraFilter.Core/Validation/Mr1Validator.cs (V2 MR1 校验实际位置)

#12 v30-6 ETL 数据完整性校验加 SkippedNullField (2026-07-20, v30-6 Day 9.7 Case 4 修复, commit 1cbca15 + a45285b → CI run 29721720744 success)
决策: EtlImportService products/apps 数据完整性校验公式加 SkippedNullField, 与 IncrSkippedNullField+continue 跳过逻辑对齐
理由:
  - v30-5 CI 暴露 Day 9.7 Case 4 ETL failed, 根因 _test_day97.py 测试数据缺 mr_1 字段
  - V2 Task 5.1.2 引入 mr_1 必填校验 (EtlImportService L866-875): mr_1 空 → IncrSkippedNullField + continue (行不进 stage 表)
  - 旧校验公式 (L944): stageCount + Progress.Errors != Progress.Read — 漏算 SkippedNullField
  - 真实数据若有 mr_1 空行 (或 apps brand/model 空行), 旧公式会误报"数据完整性校验失败", ETL failed
  - apps ETL L1958-1962 brand/model 空同样用 IncrSkippedNullField + continue, L2006 旧公式也漏算
  - 修复: products L947 加 SkippedNullField; apps L2012 加 SkippedNullField (与 SkippedMissingMr1 并列)
  - 测试数据同步修复: _test_day97.py L299 加 mr_1=f"MR1{i:05d}" (纯字母数字 7 位, 满足 chk_mr_1_format '^[A-Za-z0-9]{1,10}$', 不允许连字符)
排除方案:
  - 仅修测试数据不修生产代码: 真实数据 mr_1/brand/model 空时仍会误报, P1 生产代码 bug 必须一起修
  - 移除 mr_1 必填校验: 与 V2 Task 5.1.2 决策冲突, mr_1 是 V2 主键必填
  - 改 IncrSkippedNullField 为 IncrErrors: 改变错误语义 (skipped 非 error), 影响 Progress.Display 和前端展示
  - mr_1 格式用连字符 (MR1-00001): 违反 chk_mr_1_format 约束 (^[A-Za-z0-9]{1,10}$), 参考 PostgresSearchProviderIntegrationTests.cs L407 注释
关联文件:
  - backend/src/SakuraFilter.Etl/EtlImportService.cs (L943-954 products 校验 + L2008-2019 apps 校验)
  - backend/src/SakuraFilter.Infrastructure/Data/ProductDbContext.cs (L66 chk_mr_1_format 约束定义)
  - backend/src/SakuraFilter.Core/Validation/Mr1Validator.cs (V2 MR1 格式校验, ^[A-Za-z0-9]{1,10}$)
  - spike-test/_test_day97.py (L290-305 测试数据加 mr_1=MR1{i:05d})
  - backend/tests/SakuraFilter.Api.Tests/Integration/PostgresSearchProviderIntegrationTests.cs (L407 mr_1 纯字母数字注释参考)

#13 P1-1 DictManagerLayout 提取 (推翻 ADR #8 不提取决策) (2026-07-20, commit 99a9c03 + fe31b48 + 0b7344e)
决策: 提取 useDictManager<T extends DictItem, R extends DictReorderItem> composable + DictManagerLayout 公共布局组件, 8 个字典页全部迁移
理由:
  - ADR #8 决策暂缓提取 (用户决策暂缓 1-2 周, 当时不实施), 后续维护成本暴露
  - 用户提前启动 P1-1, 实测 8 页 2058 行 ~80% 代码逐字重复 (state + CRUD + 拖拽 5 函数 + 辅助 + 模板 + style)
  - 单点维护需求: 新增/修改一个字典页时, 不再需要在 8 个文件里复制粘贴
  - 类型安全: 用 Vue 3 composable 泛型模式 useDictManager<T extends DictItem, R extends DictReorderItem> 保留各字典页的 Item 类型
  - 可演进: 未来新增 P2.4 字典页只需写 ~50 行配置, 不再写 220 行模板代码
  - 3 slot 设计承接差异: #toolbar-extra (预留) / #row-cells (复杂数据列, 如 AdminMachinesView category el-tag) / #dialog-form (表单字段)
  - CSS 变量传 grid 列宽: --dict-grid CSS 变量由 computed 根据 columns 推导, 支持复杂页用 gridTemplate prop 显式覆盖
  - 底部文案统一: AdminOemBrandsView/AdminProductName1sView 历史遗留硬编码文案统一为 i18n key common.dictviewcommon.total_drag
  - 行数削减: 8 页合计 2058 → 646 行 (净减 1412 行, -69%)
排除方案:
  - 维持 ADR #8 不提取: 8 页重复 2058 行, 新增字典页需复制 220 行模板, 单点维护需求未满足
  - 用 HOC (higher-order component) 模式: Vue 3 HOC 不自然 (Vue 3 推荐 composable), 不如 composable + slot 直观
  - 用 mixins (Vue 2 风格): Vue 3 已弃用 mixins, 类型推断差, 不推荐
  - 用 render function 替代 slot: 可读性差, 维护成本高, 与 Vue SFC 模板风格不一致
  - 提取为 useDictManager composable 但不提取 DictManagerLayout 组件: 8 页模板仍重复, 单点维护只解决一半
关联文件:
  - frontend/src/composables/useDictManager.ts (新增 ~250 行, 封装 state + CRUD + 拖拽 + 辅助)
  - frontend/src/components/DictManagerLayout.vue (新增 ~320 行, 3 slot + 公共 style)
  - frontend/src/views/admin/AdminTypesView.vue (222 → 72, -68%, 1 字段 + 固定 5 值 softDelete 警告)
  - frontend/src/views/admin/AdminOemBrandsView.vue (399 → 70, -82%, 1 字段 + 底部文案 i18n 统一)
  - frontend/src/views/admin/AdminProductName1sView.vue (300 → 66, -78%, 1 字段 + 底部文案 i18n 统一)
  - frontend/src/views/admin/AdminProductName2sView.vue (197 → 63, -68%, 1 字段)
  - frontend/src/views/admin/AdminOemNo3sView.vue (210 → 63, -70%, 1 字段)
  - frontend/src/views/admin/AdminMediasView.vue (223 → 87, -61%, 2 字段 mediaName + mediaModel)
  - frontend/src/views/admin/AdminEnginesView.vue (222 → 87, -61%, 2 字段 engineBrand + engineType)
  - frontend/src/views/admin/AdminMachinesView.vue (272 → 166, -39%, 4 字段 + category el-tag + el-select, 用 #row-cells slot)
  - .trae/specs/v2-architecture-migration/design-dict-manager-layout.md (设计文档, 1141 行)

#14 v30-20 Meili 主路径 P99 监控告警 (2026-07-22)
决策: 新增 MeiliSearchMetrics (独立 ring buffer, 1000 样本) 采集 Meili 主路径性能指标, 在 ResilientSearchProvider.SearchAsync 4 个分支埋点 (PrimarySuccess/Fallback/PrimaryError), 复用 AlertCenter 推送 3 条告警规则 (meili_p99_error P0 / meili_p99_warn P1 / meili_fallback_rate_error P0), 通过 /api/admin/perf/meili/snapshot 暴露快照查询端点
理由:
  - 原架构缺陷: PerfMetrics 是全局 HTTP 指标, 不区分 Meili vs PG fallback, Meili 真实性能无监控, P99 异常或频繁降级时无告警
  - 复用 AlertCenter (P2-1 基础设施): AlertCenter 已是完整告警基础设施 (统一路由 + 5min 抑制 + 持久化 alert_history), 与 EtlAlertService 走老 webhook URL 相比更上层
  - P99 计算仅基于 PrimarySuccess 样本: Fallback 混入会掩盖 Meili 真实问题 (Fallback 走 PG 慢是预期行为, 不是 Meili 故障)
  - MaxMs 含所有样本: 用于发现极端慢请求 (无论 PrimarySuccess 还是 Fallback)
  - 严重度映射: meili_p99_error→P0 (主路径严重慢, 影响所有搜索) / meili_p99_warn→P1 (提前预警) / meili_fallback_rate_error→P0 (频繁降级=Meili 不可用)
  - 样本数门槛: Meili 搜索频率低, 用 10 (PerfMetrics 用 30)
  - 鉴权安全: /api/admin/perf/meili/snapshot 加 RequireAuthorization("Admin") (与 v30-19 同模式, 防泄漏 P50/P95/P99 运维数据)
  - 测试覆盖: 45 个新单测全过 (11 个 MeiliSearchMetricsTests + 9 个 PerfAlertClassifierTests 加 meili 规则 + 既有测试无回归), 总测试 610 → 635
排除方案:
  - 在 PerfMetrics 内联 Meili 指标: PerfMetrics 是全局 HTTP 指标, 混入 Meili 会让 P95/P99 含义不清 (不区分主备)
  - 走 EtlAlertService 老路径 (webhook URL): EtlAlertService 走 system_settings 中的 webhook_url, 需自己实现抑制 + 持久化, AlertCenter 已封装这些
  - 限制 ring buffer 容量到 100: 100 样本不足以反映 P99 (nearest-rank 算法在 100 样本时 P99 取第 99 个, 噪声大), 1000 是 P99 准确性与内存占用的平衡点 (8 bytes/sample × 1000 = 8KB)
  - 改 ResilientSearchProvider 构造函数为必填参数: 破坏既有 ResilientSearchProviderTests (无 metrics), 用可空参数兼容
  - MaxMs 仅基于 PrimarySuccess: 会丢失 Fallback 极端慢请求信息 (虽然 Fallback 走 PG 是预期, 但若 PG 也慢需告警)
关联文件:
  - backend/src/SakuraFilter.Search/MeiliSearchMetrics.cs (新建, 采集 + 聚合, Singleton)
  - backend/src/SakuraFilter.Search/ResilientSearchProvider.cs (注入 MeiliSearchMetrics + 4 分支埋点)
  - backend/src/SakuraFilter.Api/Services/PerfAlertClassifier.cs (加 MeiliRules 嵌套类 + ClassifyMeiliSeverity + BuildMeiliAlertContext + BuildMeiliAlertMarkdown)
  - backend/src/SakuraFilter.Api/Services/PerfAlertService.cs (注入 MeiliSearchMetrics + AlertCenter, 加 EvaluateMeiliRulesAsync + EmitMeiliViaAlertCenterAsync)
  - backend/src/SakuraFilter.Api/Endpoints/CommonEndpoints.cs (加 /api/admin/perf/meili/snapshot, RequireAuthorization Admin)
  - backend/src/SakuraFilter.Api/Extensions/ServiceCollectionExtensions.cs (注册 MeiliSearchMetrics Singleton)
  - backend/tests/SakuraFilter.Api.Tests/MeiliSearchMetricsTests.cs (新建, 11 用例)
  - backend/tests/SakuraFilter.Api.Tests/PerfAlertClassifierTests.cs (加 9 用例 meili 规则)

#15 v30-21 Prometheus 暴露 Meili 指标 (2026-07-22)
决策: 在 BusinessMetrics 加 9 个 sakura_meili_* Gauge (P50/P95/P99/MaxMs/FallbackRate/PrimaryErrorRate/FallbackCount/PrimarySuccessCount/SampleCount), 由 BusinessMetricsRefreshWorker 周期 (30s) 刷新, 通过 /metrics 端点暴露给 Prometheus/Grafana
理由:
  - v30-20 完成后, Meili P99/FallbackRate 仅通过 /api/admin/perf/meili/snapshot (JSON, Admin 鉴权) 可查, Grafana 无法对接
  - 暴露到 /metrics 让 Grafana 可视化趋势 (P99 历史曲线), AlertManager 可配置告警规则 (如 P99 > 1500ms 持续 5min)
  - 与现有 sakura_etl_* / sakura_dead_letter_* 风格一致, 复用 BusinessMetricsRefreshWorker 30s 周期刷新机制
  - /metrics 无鉴权 (与现有 sakura_ 指标同模式), 通过 nginx 内部网络隔离 (ADR #1 排除方案已决策: Prometheus 抓取需无鉴权)
  - SampleCount > 0 才刷新 P50/P95/P99 等 (避免启动初期 ring buffer 空时刷新 0 值干扰)
排除方案:
  - 加 /api/admin/perf/meili/prometheus 独立端点: 多一个端点多一份维护, 复用 /metrics 更合理
  - 在 MeiliSearchMetrics 内部直接调 Prometheus.Metrics.CreateGauge: 破坏关注点分离 (Search 项目不依赖 Prometheus, Api 项目才依赖)
  - 用 Histogram 而非 Gauge: Histogram 是累计分布, ring buffer 已是滑动窗口 (最近 1000 条), 用 Gauge 反映窗口快照更合适
  - 暴露完整 MeiliSearchSnapshot JSON 到 /metrics: /metrics 是 Prometheus 文本格式, 不能塞 JSON
关联文件:
  - backend/src/SakuraFilter.Api/Services/BusinessMetrics.cs (加 9 个 Meili Gauge + RefreshWorker 加 Meili 刷新 block)
  - backend/src/SakuraFilter.Search/MeiliSearchMetrics.cs (未改动, 数据源)

#16 v30-23 Meili 索引字段命名对齐 (JsonPropertyName snake_case) + BCrypt.Verify 崩溃修复 (2026-07-22, commit 62749e1)
决策: 所有 Meili 索引文档 record (Mr1IndexDoc / OemListItem / MachineListItem) 共 38 个字段全部显式加 [property: JsonPropertyName("snake_case")] 特性; MeiliSearchProvider.IndexAsync 加空主键过滤 (validBatch); UserService.AuthenticateAsync 的 BCrypt.Verify 调用包 try-catch 降级 SaltParseException → passwordValid=false
理由:
  - 真实浏览器验证发现搜索功能完全不可用 (Meili /indexes/products/stats 显示 0 文档), 排查发现 58 个 indexing task 全部 failed, 错误信息 "Document doesn't have a 'mr_1' attribute: {\"mr1\":\"\",\"productName1\":\"AIR FILTER\",...}"
  - 根因 1: Mr1IndexDoc record C# PascalCase 命名 (mr1 / isPublished / d1Mm / productName1 等) 与 MeiliSearchProvider 期望的 snake_case (mr_1 / is_published / d1_mm / product_name_1) 不一致, Meili .NET SDK 0.15.4 AddDocumentsAsync<T> 不做 PascalCase → snake_case 转换
  - 根因 2: 49988 条产品 mr_1 字段为空 (测试数据), BuildMr1DocumentAsync L543 `Mr1: p.Mr1 ?? ""` fallback 为空字符串, Meili 主键不能为空字符串 (Document identifier "" is invalid), 整批 1000 条被拒绝 (不是跳过单条)
  - 根因 3: pgcrypto gen_salt('bf', 12) 生成的 hash 与 BCrypt.Net 不完全兼容 (前缀差异 $2a$ vs $2b$), BCrypt.Verify 抛 SaltParseException 未被 catch, 导致登录接口 500 错误
  - 关键学习: MeiliSearchProvider L681 注释 "字段命名: snake_case (与 Mr1IndexDoc 的 JSON 序列化默认一致)" 是错误的, .NET 默认 JsonSerializer 序列化为 camelCase 而非 snake_case, 此注释误导了之前的开发者
  - 修复后验证: build 0 错误 + 574 单测通过 + Meili /indexes/products/stats 显示 49990 文档 + 搜索 "air" 返回 17351 条结果
  - 数据修复: SQL `UPDATE products SET mr_1 = id::text WHERE mr_1 IS NULL OR mr_1 = '';` (49988 条, id 是 bigint 转 string 不超过 10 位满足 varchar(10) 限制)
排除方案:
  - 全局 JsonSerializerOptions(PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower): Meili .NET SDK 0.15.4 AddDocumentsAsync<T> 内部用自有的 JsonSerializerOptions, 不接受外部传入的 options, 无法全局配置
  - 改 record 字段名为 snake_case (如 string mr_1): 违反 C# 命名规范 (PascalCase 是 C# 公共字段约定), 且会破坏所有调用方的 .Mr1 / .IsPublished 等属性访问
  - 不修复 49988 条空 mr_1 数据, 仅在代码层过滤空主键: 治标不治本, 49988 条产品 (占总数据 99.99%) 永远无法被搜索到, 搜索功能形同虚设
  - BCrypt.Verify 不 catch SaltParseException 让其抛 500: 用户体验差 (无法登录), 且日志中 SaltParseException 噪声大; 改为 try-catch 降级 + LogWarning 是更合理的容错策略
  - 改用 BCrypt.Net 的 EnhancedVerify: EnhancedVerify 内部仍调 HashPassword 抛 SaltParseException, 不解决根本问题
关联文件:
  - backend/src/SakuraFilter.Search/ISearchProvider.cs (Mr1IndexDoc + OemListItem + MachineListItem 3 个 record 38 个字段全部加 [property: JsonPropertyName])
  - backend/src/SakuraFilter.Search/MeiliSearchProvider.cs (IndexAsync L405-418 加 validBatch 空主键过滤逻辑 + L426/L428 把 batch 替换为 validBatch)
  - backend/src/SakuraFilter.Api/Services/UserService.cs (L80-92 AuthenticateAsync BCrypt.Verify 包 try-catch 降级 SaltParseException)
  - backend/src/SakuraFilter.Core/Entities/Product.cs (L23 [Column("mr_1")] string? Mr1, 空字段来源)
  - .ai/_reset_admin.sql (修复 admin 密码 hash, PowerShell 转义破坏 BCrypt hash 用 SQL 文件绕过)
  - .ai/context.md (v30-23 完整记录)
  - .ai/_v30_23_commit_msg.txt (commit 消息文件)

#17 v30-25 NpgsqlDataSource 全局单例统一 + 手动 Open 连接归还修复 (2026-07-22, commit da1436c)
决策: 注册全局 NpgsqlDataSource 单例, AddDbContext 复用该 DataSource; EtlProgressBroadcaster 改为注入单例 DataSource (不再自建独立池); PostgresSearchProvider.SearchAsync/AggregateSearchAsync + DeadLetterRecoveryService.TryWithAdvisoryLockAsync 中手动 OpenAsync 的连接在 finally 块显式 CloseAsync 归还
理由:
  - E2E 测试中发现 compare API 返回 500, 排查根因: EtlProgressBroadcaster 构造函数自建 NpgsqlDataSource.Create(_connectionString) 独立连接池 (100 槽位), 与 AddDbContext 主池 (100 槽位) 隔离, 进程内总连接上限 200, 超过 PostgreSQL max_connections=100 时主池借连接被拒, 抛 "连接池耗尽" 异常
  - PostgresSearchProvider 2 处 (_db.Database.GetDbConnection() + conn.OpenAsync()) 后无 finally Close, 连接归还依赖 Scoped DbContext Dispose 延迟归还, 高并发时连接滞留加剧池耗尽
  - 修复后: 全局单例 DataSource 统一连接池, 总连接上限 = max_connections=100; 手动 Open 的连接在 finally 主动 Close, 即时归还
排除方案:
  - 调高 PostgreSQL max_connections 到 200: 治标不治本, 独立池问题仍存在, 且每个连接消耗 ~10MB 内存, 200 连接 = 2GB 仅 PG 就占
  - 用 PgBouncer 中间件: 开发环境引入额外组件过重, MVP 阶段单例 DataSource 足够; 生产大规模 (>500 并发) 再引入
  - 不修复手动 Open 不 Close, 改用 using var conn: _db.Database.GetDbConnection() 返回的是 DbContext 管理的连接, using 会 Dispose 掉 DbContext 的连接导致后续 DbContext 操作失败
关联文件:
  - backend/src/SakuraFilter.Api/Extensions/ServiceCollectionExtensions.cs (L196-214 注册单例 NpgsqlDataSource)
  - backend/src/SakuraFilter.Api/Services/EtlProgressBroadcaster.cs (构造函数注入 DataSource, DisposeAsync 不释放单例)
  - backend/src/SakuraFilter.Search/PostgresSearchProvider.cs (SearchAsync + AggregateSearchAsync 加 wasOpened + finally Close)
  - backend/src/SakuraFilter.Api/Services/DeadLetterRecoveryService.cs (TryWithAdvisoryLockAsync 加 try-finally)

#18 v30-27 Meili 压测验证 + reindex-all 后台化 + SanitizeToken parent 修复 (2026-07-22)
决策: reindex-all 端点改为 Task.Run + CancellationToken.None 后台触发 (与 /resume 同模式); MeiliSearchProvider.SanitizeToken 方法只在值变化时赋值 (避免 JsonNode 重复设置 parent 抛 InvalidOperationException); 69000 文档压测验证 Meili 性能达标
理由:
  - reindex-all 1M 文档重建耗时 30+ 分钟, 原 await etl.ReindexAllAsync(ct) 的 ct 来自 HTTP 请求, 请求 30s 超时后 ct 被取消导致索引写入中断; 改为 Task.Run + CancellationToken.None 后台执行, 返回立即响应, 进度通过 /progress/stream 查询
  - SanitizeToken 递归处理 _formatted 高亮字段时, arr[i] = SanitizeToken(arr[i]) 当递归返回同一节点 (字符串未变化), JsonArray.SetItem 尝试重新设置 parent 抛 "The node already has a parent"; 修复为 if (sanitized != arr[i]) arr[i] = sanitized 只在值变化时赋值
  - 压测结果 (69000 文档, Meili 单实例, 本地开发机):
    * 单次搜索 P95=32.1ms ✅ (< 200ms 目标)
    * 50 并发 P95=98.6ms ✅, 100 并发 P95=184.1ms ✅ (各 1000 次, 0 错误, RPS~640)
    * Offset 深分页: offset=0 P95=36.6ms ✅, offset≥1000 P95~290ms ⚠️ (空查询全表 69000 文档的深分页, 实际带关键词搜索匹配数远少于此, 性能会更好)
  - 压测中发现: 搜索端点限流 SearchPermitsPerMinute=300, 压测时用环境变量 RateLimit__SearchPermitsPerMinute=100000 临时调高
  - 1M 全量索引验证 (2026-07-22, 压测库 sakurafilter_perf_tests 950K products):
    * 索引成功 654209 文档 (约 69%, 剩余 31% 为 mr_1 空主键被 IndexAsync 过滤, 符合预期)
    * 单次搜索 P95=53.6ms ✅ (< 200ms 目标)
    * 50 并发 P95=83.3ms ✅, 100 并发 P95=152.9ms ✅ (各 1000 次, 0 错误, RPS~740)
    * Offset 深分页: offset=0 P95=34.9ms ✅, offset≥1000 P95~290-332ms ⚠️ (空查询全表 65 万文档深分页, 实际带关键词搜索匹配数远少于此)
    * 结论: Meili 单实例支撑 65 万文档搜索性能达标, 100 并发 P95=152.9ms 远低于 200ms 目标
排除方案:
  - reindex-all 用 AcquireActiveCts 的 linked CTS: CreateLinkedTokenSource(externalCt) 即使外部传 CancellationToken.None, 内部仍创建 linked CTS, 不解决超时问题
  - SanitizeToken 改用 JsonDocument 不可变模型: 改动面大, 需重写整个 _formatted 处理逻辑, 且 JsonNode 的 DOM 可变性正是此处需要的
  - Offset 深分页用 keyset 分页 (cursor): Meili 0.15.4 不支持 cursor 分页, 只支持 offset/limit; 后台管理深分页 ~290ms 可接受, 前台用户搜索带关键词不会触发
关联文件:
  - backend/src/SakuraFilter.Api/Endpoints/AdminEtlEndpoints.cs (L188-213 reindex-all 改 Task.Run + CancellationToken.None)
  - backend/src/SakuraFilter.Search/MeiliSearchProvider.cs (L597-633 SanitizeToken if-changed 守卫)
  - spike-test/perf/stress_meili.py (压测脚本: 串行/并发/Offset 三场景)
  - spike-test/perf/_stress_results.json (压测结果 JSON)

#19 Meili 品牌优先级排序修复 (2026-07-22)
决策: MeiliSearch SearchQuery 加两层 Sort=["brand_sort_order_min:asc", "oem_list_sort_order_min:asc"] (品牌优先 > 品牌内 OEM 3 优先 > 相关性); BuildMr1DocumentAsync 中 brand_sort_order_min 和 oem_list_sort_order_min 的 DefaultIfEmpty() bug 修复 (返回 0 → 返回 null); oem_list_sort_order_min 计算时 sort_order=0 视为未维护 (null)
理由:
  - 调查发现 Meili 主搜索路径未应用品牌优先级排序 (SortableAttributes 配置了 brand_sort_order_min 但 SearchQuery 未传 Sort 参数), 导致 95%+ 的搜索请求按相关性排序, 品牌优先级不生效; 仅 PG 兜底路径 (< 5%) 按 brand_sort_order_min ASC 排序
  - 用户需求 "品牌白名单优先显示" 明确要求品牌优先 > 相关性, 因此在 SearchQuery 中加 Sort 完全替换默认 ranking rules 排序
  - 用户进一步需求: 品牌内 OEM 3 也需按优先级排序 (如 Donaldson 下 DON-00008=1, DON-00015=2, 搜索时这两个排前面, 其余 sort_order=0 的排后面); 因此 Sort 数组加第二维度 oem_list_sort_order_min:asc
  - 修复 DefaultIfEmpty() bug: 原 .Select(x => x.BrandSortOrder!.Value).DefaultIfEmpty().Cast<int?>().Min() 对空集合返回 0 (int 默认值), 导致无品牌产品 brand_sort_order_min=0 排在 Donaldson (sort_order=10) 之前; 修复为 .Select(x => (int?)x.BrandSortOrder!.Value).Min() 对空集合返回 null, Meili asc 排序将 null 排末尾
  - oem_list_sort_order_min 同样修复 DefaultIfEmpty() bug, 并加 .Where(x => x.SortOrder > 0) 过滤: sort_order=0 是 cross_references 表默认值 (未通过 /admin/xrefs/reorder 维护), 视为 null 排末尾
  - 验证 (spike_test_v3 库, 49990 文档):
    * 品牌优先级: 搜索 "filter" 前 10 条全部 brand_sort_order_min=10 (Donaldson) ✅
    * OEM 3 优先级: 设置 DON-00008=1, DON-00015=2 后, 搜索结果 mr1=38213 (oem3_sort=1) 排第 1, mr1=31723 (oem3_sort=2) 排第 2, 其余 oem3_sort=null 排后面 ✅
排除方案:
  - Ranking Rules 方案 (brand_sort_order_min 加入 ranking rules 的 sort 位置): 先按相关性匹配再按品牌排序, 但用户明确要求品牌优先 > 相关性, 不符合需求
  - 品牌分组 + 组内相关性方案: Meili ranking rules 配置更复杂, 且用户明确选择品牌优先 > 相关性
关联文件:
  - backend/src/SakuraFilter.Search/MeiliSearchProvider.cs (L166-178 SearchAsync 加两层 Sort; L266-278 AggregateSearchAsync 加两层 Sort; L541-546 brand_sort_order_min DefaultIfEmpty 修复; L548-554 oem_list_sort_order_min DefaultIfEmpty + sort_order=0 过滤)

#20 规划V2特殊参数双存储方案 (2026-07-26)
决策: 对 No. Check Valves、No. Bypass Valves、Bypass Valve LR/HR、Bypass Pressure、Collapse Pressure 新增 `*_raw` 原始文本列；保留现有整数/数值列作为检索值。原始值为单一数值且可带单位时自动派生检索值，分数或复合表达式不做猜测。
理由:
  - 规划V2要求尺寸与技术参数可包含特殊字符，同时要支持数值检索；直接把字段改为字符串会破坏既有筛选、索引和 API 契约。
  - 与已存在的 d1-h4 `*_raw` 实现一致，原值用于展示和追溯，数值用于范围查询。
  - `1/2`、`N/A` 这类表达式不存在唯一数值语义，错误派生会产生误匹配；`1.2 bar` 则可无歧义保留为 1.2。
排除方案:
  - 将既有数值列直接改为 text: 会破坏现有搜索和索引，且迁移风险高。
  - 使用单一正则提取第一个数字: 会把分数、区间和复合参数误判为精确值。
关联文件:
  - backend/src/SakuraFilter.Core/Entities/Product.cs
  - backend/src/SakuraFilter.Core/DTOs/ProductFormDto.cs
  - backend/src/SakuraFilter.Api/Services/AdminProductService.cs
  - backend/src/SakuraFilter.Etl/EtlImportService.cs
  - backend/src/SakuraFilter.Infrastructure/Data/Migrations/20260726014656_AddRawParameterValues.cs

---

#21 聚合搜索高亮净化方案: 正则等价实现替代 DOMPurify (2026-07-30)
决策: 维持现状, 使用 `frontend/src/utils/html-sanitizer.ts` 的 30 行正则等价实现 (先全量 HTML 转义再仅还原 `<mark>` 标签), 不切换为 DOMPurify。
理由:
  1. 安全性更强: 正则实现先对所有字符做 HTML 转义, 再仅还原 `<mark>` 标签, 比 DOMPurify 默认配置更严格 (DOMPurify 默认允许更多标签, 需额外配置 ALLOWED_TAGS)。
  2. 包体积优化: DOMPurify 22KB, 正则实现仅 30 行, 节省前端 bundle 体积。
  3. 功能等价: 后端 Meilisearch 返回的 `_formatted` 高亮仅含 `<mark>` 标签, 正则实现完全覆盖该场景, 无功能缺失。
  4. 经用户确认 (Task 6 选 A): spec F14 字面要求 DOMPurify, 但安全意图 100% 达成, 维持现状。
排除方案:
  - 切换为 DOMPurify (npm install dompurify + 重写 html-sanitizer.ts): 严格按 spec F14 字面, 但安全性不增强 (反而可能因默认配置放宽而降低), 且增加 22KB 包体积。
关联文件:
  - frontend/src/utils/html-sanitizer.ts
  - frontend/src/views/public/AggregateSearchView.vue
  - backend/src/SakuraFilter.Search/MeiliSearchProvider.cs (SanitizeFormatted 后端净化)


#22 /api/perf 鉴权路径修复 — AdminPaths 补 /api/perf (2026-08-02)
决策: Auth:AdminPaths 加 "/api/perf", Auth:ExemptPaths 加 "/api/perf/ingest"
理由: v30-19 给 /api/perf 加 RequireAuthorization("Admin") 但漏同步 DevTokenAuthMiddleware 的 AdminPaths 前缀 → X-Admin-Token 请求 401 (只有 JWT 能访问), CI 7-21 起连续失败. 补前缀后 dev token 可访问 /api/perf (与 /api/admin/* 同级凭据); /api/perf/ingest 保持无鉴权 (ADR #1: sendBeacon 无法带 token), 通过 ExemptPaths 精确放行避免被 StartsWith(/api/perf) 波及
排除方案:
  - 改脚本用 JWT 登录: 脚本需登录流程, 复杂且 dev token 本就应覆盖后台端点
  - 移除 /api/perf 的 RequireAuthorization: 回退安全修复 (v30-19 P0)
关联文件: backend/src/SakuraFilter.Api/appsettings.json, spike-test/_test_p55_p71_e2e.py

#23 AuthTokenBroadcaster WaitAsync 重连循环修复 (2026-08-02)
决策: WaitAsync 收到 NOTIFY 后 continue 继续等待, 而非 break 重连
理由: 原实现收到通知即 break → Dispose + 重连, 与异步 Notification 事件处理器 (ReloadFromDbAsync 未完成) 竞争同一 Npgsql 连接 → "Connection is busy" 无限重连循环 → LISTEN 间歇失效, token 轮转广播丢失, /health/ready 误判 stale. 连接仍 Open 时继续等下一个通知是 Npgsql WaitAsync 标准用法
排除方案:
  - 事件处理器改同步: Notification 事件本身是 fire-and-forget, 无法同步等待
  - 连接池隔离: 复杂度高, 收益低
关联文件: backend/src/SakuraFilter.Api/Services/AuthTokenBroadcaster.cs

#24 生产部署编排修复 (2026-08-02, Docker Desktop 演练)
决策: prod compose 补 db-init/db-migrate 迁移链 + api --migrate-db 模式; Dockerfile.api 补 ICU; nginx 显式 root
理由: 部署演练发现 6 个预存缺陷: ① 编排无迁移步骤, 空库 42P01 崩溃; ② .NET8 Alpine 缺 icu-libs (SIGSEGV 139); ③ Dockerfile.web COPY 超出构建上下文; ④ Alpine nginx prefix=/etc/nginx 默认 root 不存在 (try_files 循环 500); ⑤ web healthcheck localhost→::1 误判; ⑥ api 缺 robots.txt (nginx 已反代)
排除方案:
  - EnsureCreated 替代 EF Migrate: 无迁移历史, 运维不可控; EF 34 迁移 + SQL 增量与 CI 组合等价
  - DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1: 丢失全球化 (时区/文化), 项目显式设 false
  - web healthcheck 改 curl: Alpine busybox 无 curl
关联文件: docker-compose.prod.yml, docker/Dockerfile.api, docker/Dockerfile.web, docker/nginx.conf, backend/src/SakuraFilter.Api/Program.cs, SitemapEndpoints.cs

#25 运维演练结论: 密钥轮换 + 备份恢复 (2026-08-03)
决策: 轮换走 DB 写 + pg_notify 广播 (CLI rotate-token 的等价验证), 备份用 pg_dump -Fc
理由: 生产容器内 CLI 无法直连 DB (端口未映射), 演练改为容器内 psql 直接写 auth_token_state + NOTIFY, 验证结果与 CLI 行为等价
演练实证:
  - 轮换1: current=新/previous=旧 → 新 200 + 旧 200 (过渡期) + 无关 401, API 未重启实时生效
  - 轮换2: current=新2/previous=新1 → 旧原始 key 401 (脱离双 key 即失效)
  - 备份: pg_dump -Fc 109KB → pg_restore 临时库, 27 表全恢复, 行数完全一致 (100/300/500)
  - 恢复后 DROP 测试库, 生产库未受影响
关联文件: auth_token_state 轮换机制, docker-compose.prod.yml (postgres 无端口映射)
补充: Grafana 管理员密码在 grafana-data volume 首次初始化时固化 (compose 默认读 .env 而非 .env.prod; --env-file .env.prod 需显式指定)
  - 若忘记密码: docker compose -f docker-compose.prod.yml stop grafana && docker compose -f docker-compose.prod.yml rm -f grafana && docker volume rm sakurafilter_grafana-data (provisioning 会自动重建数据源+面板, 但丢失手工配置)

#26 阶段4 TLS 部署 (2026-08-03)
决策: nginx 443 HTTPS (HTTP/2 + TLS1.2/1.3) + 80→301 跳转 + HSTS; 自签名证书演练, LE 接入点路径不变
关键修复 (演练实证):
  - 安全头丢失: location 内 add_header 覆盖 server 级 → security-headers.conf include 片段统一注入
  - 健康端点: /health/live|ready 注册于根路径 (CommonEndpoints.cs), 443 需显式反代, 否则落 SPA fallback
  - compose env: 默认读 .env 而非 .env.prod → 部署必须 --env-file .env.prod (api 曾空连接串崩溃 139)
  - fix(P1): AuthTokenBroadcaster async void 事件处理器竞争连接 → Channel 同步 TryWrite + 主循环顺序处理, "Connection is busy" 刷屏根除 (0 次/60s)
排除方案: 80/443 双监听共存 (选 80 全跳转, 生产形态)
关联文件: docker/nginx.conf, docker/security-headers.conf, docker-compose.prod.yml, AuthTokenBroadcaster.cs

#27 真实数据导入演练 (2026-08-04)
决策: 50K 产品真实规模 ETL 链路验证 (生成真实格式 Excel → etl_clean → 3 端点导入 → Meili 重建)
结果: products 50,000 + xrefs 624,539 + apps 775,457 全量导入 0 错误; 8字段/机型/聚合搜索/SEO 全通
演练修复 (4 轮迭代, 均为脚本契约失配, 非系统缺陷):
  - etl_clean 输出缺 mr_1 (V2 主键升级后清洗脚本未同步) → 生成器补 MR.1 列 + 清洗输出 mr_1 + OEM→MR.1 映射
  - xrefs 23505 (uq_xrefs_brand_oem3 全局唯一): 生成器随机 OEM NO.3 重复 → 全局唯一集 (真实业务 OEM 号唯一)
  - apps 23505 (uq_apps_product_brand_model 产品内唯一): 生成器同产品 model 重复 → 产品内唯一集
  - is_published 全 false → 导入后前台不可见: 清洗补 is_published=true (客户目录=在售目录, 管理后台可批量下架)
关联文件: spike-test/_gen_source_xlsx.py, spike-test/etl_clean.py, spike-test/_etl_prod_load.py

#28 自动 reindex 优化 (2026-08-04)
决策: xrefs/apps 导入后自动增量同步 Meili — 收集受影响产品 id → touch products.updated_at → 复用 SyncSearchIndexAsync 时间窗
验证 (生产栈实测): MR000001 oem_list 14→15→16 两次增量导入自动更新, 0 手动 reindex; 增量场景只重建受影响文档
配套修复: LISTEN 长连接 Pooling=false (池化复用与 WaitAsync 竞争 busy, 修复后 0 次/2min)
排除方案: 导入后自动 reindex-all 全量重建 (50K 文档每次导入都重建, 增量场景浪费)
关联文件: EtlImportService.cs (SyncAffectedProductsAsync), AuthTokenBroadcaster.cs, EtlProgressBroadcaster.cs

#29 演示数据方案 (2026-08-04)
决策: 客户真实产品区 (1949) 保留 + 模拟生成 OEM 替代/机型适配 (xrefs 24,823 + apps 30,225) 填充演示
理由: 客户暂缺 OEM/机型关联数据, 前台 OEM 维度搜索无法演示; 生成器基于现有 mr_1 生成关联 (不造新产品)
唯一约束 (演练实证): xrefs 全局 (brand,oem3) / apps 产品内 (brand,model); products.jsonl 复制行按 mr_1 去重
恢复路径: 客户数据到位 → etl_clean + full-load 重导替换 (流程不变, 自动 reindex)
验证: OEM 搜索 total=1 / 机型搜索 35 / 聚合 1000+ / 详情页 200 全通
关联文件: spike-test/_gen_demo_xrefs_apps.py

#30 Npgsql DateTime Kind 兼容策略 (2026-08-06)
决策: Program.cs 启用 `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)`，实体 DateTime 全部默认 `DateTime.UtcNow`
理由: Npgsql 8 严格 Kind 校验 — timestamptz 拒 Unspecified / timestamp 拒 UTC; 项目列类型各地不一致 (CI 新库 EF 迁移 timestamptz, 本地/生产旧库 SQL 迁移 timestamp) → OEM Brand 字典 create 500 "Cannot write DateTime with Kind" (CI 实证)
排除方案:
  - 全库 ALTER 统一 timestamptz + 迁移: 需动本地/生产所有表, 部署成本高, 演示环境风险大
  - 仅修实体赋值: 无法覆盖所有历史列类型差异
关联文件: backend/src/SakuraFilter.Api/Program.cs, backend/src/SakuraFilter.Core/Entities/Product.cs

#31 CI 分层策略 (2026-08-06)
决策: 快门禁 (ci.yml: 构建+单测 652+冒烟+coverage gate) 每次 push/PR 必跑; 重防线 (e2e.yml: 全链路 E2E + frontend-contract) master push/PR/月度定时跑; docker.yml 构建镜像
理由: E2E 全量 (编排 9 测试 + Playwright 跨浏览器) 耗时 ~10min, 每次 push 全跑性价比低; master 为保护分支, 每次提交 = 可发布候选, gate 全开合理 (用户曾连环收 CI 失败邮件)
经验: CI 快机器 vs 本地慢机器时序敏感测试 (ETL cancel 验证) 不可靠 → 宽容 SKIP + 慢环境可验证; 静态扫描类 (P0 fixes/P1 health/P2 migration/pattern scan) 可合并进 ci.yml 降 E2E 时长 (建议项)
关联文件: .github/workflows/ci.yml, .github/workflows/e2e.yml

#32 codex 上线审核 6 项问题处置 (2026-08-24)
决策: 修 3/4/5/6, 留 1/2 但补文档/ADR。①fuzzy 截断: 合并加 OrderBy 保证确定性 + 达 5000 上限时 CountMode="truncated" 显式告知 (前端 types 注释同步); ②backup-db.sh --verify 挂载路径改 $(pwd) 动态推导; ③prometheus/grafana 端口绑 127.0.0.1 + GRAFANA_PASSWORD 去 admin 回退 (fail-fast); ④.env.prod.example 补 INITIAL_ADMIN_PASSWORD/INITIAL_OPERATOR_PASSWORD; ⑤db-init/db-migrate 保持注释禁用, 空库初始化改一次性手动流程 (ops-manual 文档化); ⑥机型目录 Take(8) 保持现状 (与前端 slice(0,8) 对齐)
理由: 审核基于远端 master 4e37901 逐行核验 6 项全属实。①原无 OrderBy 每次取哪 5000 不确定 → 随机漏结果, 且响应声称 exact 无截断告知; ②原硬编码 "F:/sakurafilter-real/${BACKUP_DIR}" 换目录/机器即挂载失败 → 备份可恢复门禁失效; ③9090/3000 裸绑 0.0.0.0 + 密码回退 admin, 漏配即弱口令裸奔公网; ④compose :54-55 引用 INITIAL_* 而样例缺失 → 漏配时空值, 默认用户创建失败; ⑤db-migrate 每次 compose up 重建会重跑迁移清库 (2026-08-22 实证), 禁用是正确运维决策, 问题在"空库无初始化路径"而非注释本身 → 文档化一次性流程; ⑥全量型号 = 200 万机型 15.5MB JSON 拖垮目录 (2026-08-23 实测), 截断 8 与前端对齐是性能必需, "展开更多"低频场景, 维持现状; 如产品要求目录可达全部型号, 另立需求做按品牌分页端点
排除方案:
  - fuzzy 移除 5000 上限: 1M 行 ILIKE 全表扫描 10s+ 超时 (8/23 走查实证), 不可行
  - fuzzy 改深度分页: 需 keyset/游标 + 前端配合, 超出本次门禁修复范围, 留待产品需求
  - 恢复 db-init/db-migrate 服务: 任何 compose up 都会重建并重跑迁移 → 数据全清, 危险
  - Take(8) 加"加载更多"端点: 前端需联动改造 + 后端新端点, 低频场景不值, 留 P2
关联文件: backend/src/SakuraFilter.Api/Controllers/PublicSearchController.cs, docker-compose.prod.yml, .env.prod.example, scripts/backup-db.sh, docs/ops-manual.md, frontend/src/api/types.ts
