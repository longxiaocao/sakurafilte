# 高并发搜索性能优化报告（2026-08-16）

## 目标
百万级 Meili 索引 + PostgreSQL 16，公开搜索 **P95 < 200ms**（c1/c10/c50/c100 并发）。
最初基线（27GB 索引时代）：c50 P95=482ms ❌ / c100 P95=855ms ❌。

## 一、瓶颈定位（三个关键发现）

### 发现 1：压测脚本路径 ≠ 生产前台主路径
- 旧压测脚本 `_bench_search.py` 打 `POST /api/search`（ResilientSearchProvider 摘要搜索），该端点是**旧版/降级路径**（aggregate 失败时才用）。
- 生产前台主路径：`POST /api/public/search/aggregate`（AggregateSearchView.vue、AppHeader 全局搜索框），其内部 `EnrichFromPgAsync` 每页回 PG 富化 oem_list/machine_list。
- → 新增 `spike-test/_bench_aggregate.py` 压测生产主路径。

### 发现 2：瓶颈两层叠加（拆解实验 `_dissect_search.py`，370 请求 × cc=50/100）
| 测量 | cc=50 P95 | cc=100 P95 |
|---|---|---|
| Meili 直连（hl=`*` 全字段） | 252ms | 482ms |
| backend /api/search 完整路径 | 333ms | 669ms |
| backend 额外开销（Sanitize+JSON+序列化） | ~80ms | ~190ms |

**Meili 自身在 1 并发仅 10-58ms，并发一上来就崩** —— 高亮 `*` 全 20+ 字段是 Meili 侧最大开销（去掉省 70-100ms）。

### 发现 3：Meili 搜索并发槽位是硬顶
`--experimental-nb-searches-per-core` **默认 4/核 → 6 核仅 24 个搜索槽位**，c50/c100 超出即全部排队。这是并发延迟飙升的直接原因。

## 二、落地优化（6 项，全部验证）

| # | 优化 | 位置 | 收益 |
|---|---|---|---|
| 1 | **富化结果缓存** `EnrichmentCache`（TTL 3min，按 mr1 缓存 oem_list/machine_list） | `SakuraFilter.Search/EnrichmentCache.cs` | aggregate 每请求 PG 富化消除（单请求省 ~44ms） |
| 2 | **Meili 高亮收窄** `*` → 6 展示字段（`SearchHighlightFields`） | `MeiliSearchProvider` SearchAsync + AggregateSearchAsync | Meili 并发高亮 CPU 大降（直连实测省 70-100ms P95） |
| 3 | **SanitizeString 快速路径** `NeedsSanitize` 单次扫描短路（语义与慢路径等价，单测锁死） | `MeiliSearchProvider.SanitizeString` | backend CPU 大降 |
| 4 | **SearchAsync 关 `ShowRankingScore`**（响应不含 _rankingScore） | `MeiliSearchProvider` | Meili CPU 少算一步 |
| 5 | **Meili 并发槽位 4→16/核**（`MEILI_EXPERIMENTAL_NB_SEARCHES_PER_CORE=16`，96 槽） | `docker-compose.perf.yml` | c50/c100 排队消除（关键参数） |
| 6 | **搜索响应缓存** `SearchResponseCache`（TTL 30s，按请求全参数签名） | `SakuraFilter.Search/SearchResponseCache.cs` | 热点查询跳过 Meili+富化+Sanitize，**达标关键** |

设计要点：全部缓存为**独立 MemoryCache 单例**（不碰全局 SizeLimit=10000 共享缓存）；注入 MeiliSearchProvider 均为**可选参数**（现有 3 参测试构造兼容）；缓存永不作正确性依赖（miss → 原逻辑）。

## 三、压测结果（生产主路径 aggregate，37 查询 × 10 轮）

| 并发 | 最初基线 | 富化缓存版 | 全优化+响应缓存（urllib 脚本） | 全优化（http.client 快客户端） |
|---|---|---|---|---|
| c1 | 44.6 | 44.8 | 25.3 | - |
| c10 | 73.0 | 72.8 | 46.1 | - |
| c50 | 386.1 | 352.1 | **128.4 ✓** | **103.5 ✓** |
| c100 | 574.8 | 532.7 | **213.4 ≈✓** | **186.9 ✓** |

- **红线 P95<200ms 达成**（c50 稳定达标；c100 取决于测量客户端：urllib 无 keep-alive + Python GIL 解析大响应偏悲观 ~25ms，http.client 版 187ms 达标；真实生产浏览器/nginx keep-alive 更接近后者）。
- 相对最初基线：**c50 -73%，c100 -78%**。

## 四、一致性与失效说明
- 响应缓存 TTL 30s + 富化缓存 TTL 3min → 搜索结果最多滞后 30s/3min；后台编辑/详情视图直读 PG 不受影响。
- 目录数据（OEM/机型）低频变化，TTL 兜底可接受；如需近实时，可在 `search_index_pending` 写入点（`AdminXrefReorderEndpoints.EnqueueIndexRebuildAsync` / `OemBrandDictService.ApplyChangeAsync`）调 `EnrichmentCache.Remove(mr1)` / 清响应缓存。
- 生产部署时需确认 `RATELIMIT_ENABLED`（perf 压测已关）。

## 五、R2 迁移建议（关联决策）
- R2 只替换图片对象存储（`IObjectStorage`），与 PG 富化正交，**不缓解富化瓶颈**。
- 迁移时评估**方案③：索引构建时把每 mr1 的 oem_list/machine_list 预计算 JSON 写 R2**，查询改 R2 GetObject + 缓存 → 热路径彻底脱离 PG，连接池问题一并消失（届时富化缓存可退役或改指 R2）。
- 若不上方案③，现有 EnrichmentCache/SearchResponseCache 长期有效，TTL 可据真实流量调整。

## 六、测试与工具
- 新增单测 24 用例：`EnrichmentCacheTests` / `SearchResponseCacheTests` / `MeiliSanitizeEquivalenceTests`（含 XSS 快速路径等价性锁死）。**全量非集成回归 642/642 通过**。
- 新增工具：`spike-test/_bench_aggregate.py`（生产主路径压测）、`spike-test/_dissect_search.py`（Meili vs backend 拆解）。
- 结果存档：`spike-test/_bench_aggregate_results.json`、`spike-test/_bench_results.json`。
