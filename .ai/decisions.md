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

#5 PostgresSearchProvider Phase 2 keyset 分页暂缓 (2026-07-19, 50K 压测验证 2026-07-19, v28-1 GIN trgm 验证 2026-07-19, v28-2 CTE UNION 拆分验证 2026-07-19, v28-3 1M 扩容压测验证 2026-07-19, v29-1 token 数量限制 2026-07-19, v29-2 高频词分布调研 2026-07-19)
决策: v27-1 暂不实施 keyset 分页改造, 保留 OFFSET 分页; v27-3 50K 压测后维持暂缓决策; v28-1 GIN trgm 索引对 baseline SQL 无收益; v28-2 CTE UNION 拆分 + 三表 GIN trgm 索引 P95 1827ms → 305ms (6.0x), 达 4x 目标, 已落地; v28-3 1M 扩容压测 v28-2 加速比保持 6.82x, 多 token 退化 1.49x, 维持当前实现; v29-1 token 数量限制为 8 (防御性兜底, 防止极端场景 INTERSECT HashSetOp Append 退化); v29-2 高频词分布调研后候选 2 不实施 (真正高频词仅 "filter" 1 个, 是 type 字段结构性特征非 bug, ROI 低)
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
排除方案:
  - 立即改 keyset: 工作量大 (前后端契约改造) 且 50K 压测显示 OFFSET 深度非主要瓶颈
  - 加 GIN trgm 索引 (v28-1 验证): 对当前 SQL 模式无收益, PG 优化器不选, 不应加索引 (50MB 索引浪费)
  - v28-2 CTE 拆分 v1 (仅 products 5 字段 GIN trgm): 2.56x 未达 4x 目标, EXISTS xref (623K 行) + EXISTS machine (775K 行) 仍拖累
  - v28-5 限制最大 token 数量 (如 8 个): 5 token 610ms 仍可接受, 退化曲线趋于平缓, 防御性兜底留 P2 候选 (1M 数据下退化情况留 v28-3 验证)
  - v28-3 启用 v27-1 q_match 候选集爆炸防御: 1M 数据加速比 6.82x 保持, 多 token 退化 1.49x 可控, 无需启用
  - 加 covering index: 涉及 DB schema 变更, 需 migration, 不适合 v27 阶段
  - 1M 扩容压测: ✅ 已执行 (v28-3), 50K 数据下退化比 ≤1.03x (OFFSET) / ≤2.64x (多 token), 1M 数据下退化 1.49x, 验证完成
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

