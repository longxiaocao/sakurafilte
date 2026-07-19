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

#1 SSE 401 修复方案选择 (2026-07-18)
决策: 前端改用 fetch + ReadableStream 替代 EventSource, 不改后端
理由: EventSource API 不支持自定义 Header, 无法携带 JWT。fetch + ReadableStream 可携带 Authorization Bearer, 与现有 axios 拦截器逻辑一致 (复用 buildAuthHeaders), 无需后端改动
排除方案:
  - 后端 SSE 支持 query token (?token=xxx): token 会泄漏到访问日志/Referer/nginx 日志, 安全风险高
  - 后端 SSE 支持 cookie auth: 需后端改动 + 与 JWT 无状态架构冲突, 改动面大
关联文件:
  - frontend/src/composables/useEtlProgress.ts
  - frontend/src/utils/http.ts (新增 buildAuthHeaders 导出)
  - backend/src/SakuraFilter.Api/Endpoints/AdminEtlEndpoints.cs (未改动)

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

#5 PostgresSearchProvider Phase 2 keyset 分页暂缓 (2026-07-19, 50K 压测验证 2026-07-19, v28-1 GIN trgm 验证 2026-07-19, v28-2 CTE UNION 拆分验证 2026-07-19)
决策: v27-1 暂不实施 keyset 分页改造, 保留 OFFSET 分页; v27-3 50K 压测后维持暂缓决策; v28-1 GIN trgm 索引对 baseline SQL 无收益; v28-2 CTE UNION 拆分 + 三表 GIN trgm 索引 P95 1827ms → 305ms (6.0x), 达 4x 目标, 已落地
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
排除方案:
  - 立即改 keyset: 工作量大 (前后端契约改造) 且 50K 压测显示 OFFSET 深度非主要瓶颈
  - 加 GIN trgm 索引 (v28-1 验证): 对当前 SQL 模式无收益, PG 优化器不选, 不应加索引 (50MB 索引浪费)
  - v28-2 CTE 拆分 v1 (仅 products 5 字段 GIN trgm): 2.56x 未达 4x 目标, EXISTS xref (623K 行) + EXISTS machine (775K 行) 仍拖累
  - v28-5 限制最大 token 数量 (如 8 个): 5 token 610ms 仍可接受, 退化曲线趋于平缓, 防御性兜底留 P2 候选 (1M 数据下退化情况留 v28-3 验证)
  - 加 covering index: 涉及 DB schema 变更, 需 migration, 不适合 v27 阶段
  - 1M 扩容压测: 50K 数据下退化比 ≤1.03x (OFFSET) / ≤2.64x (多 token), 1M 留后续独立库 (sakurafilter_perf_tests) 验证, 避免污染 spike_test_v3
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
  - .trae/specs/v2-architecture-migration/spec.md chapter 27.8 (v27-3 实施记录) + chapter 28.1 (v28-1 验证记录) + chapter 28.2 (v28-2 实施记录) + chapter 28.5 (v28-5 验证记录)

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
