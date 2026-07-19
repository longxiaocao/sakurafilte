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

#5 PostgresSearchProvider Phase 2 keyset 分页暂缓 (2026-07-19)
决策: v27-1 暂不实施 keyset 分页改造, 保留 OFFSET 分页, 待 v27-3 压测验证后再决策
理由:
  - 当前 SearchRequest DTO 用 Page/PageSize 页式分页, 前端依赖 Page 契约
  - 改 keyset 需破坏前端 Page 契约或引入 cursor 参数, 改动面大
  - 真实用户行为: 搜索结果 99% 在前 5 页内 (典型电商行为), 深分页场景罕见
  - V24-F80 Phase 1 原生 SQL + CTE + LATERAL JOIN 已优化首屏性能, 深分页性能问题需压测数据支撑
排除方案:
  - 立即改 keyset: 工作量大 (前后端契约改造) 且缺乏压测数据支撑收益
  - 加 covering index: 涉及 DB schema 变更, 需 migration, 不适合 v27 阶段
关联文件:
  - backend/src/SakuraFilter.Search/PostgresSearchProvider.cs L20/L224 (TODO 标注)
  - backend/src/SakuraFilter.Core/DTOs/SearchRequest.cs (Page/PageSize 契约)

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
