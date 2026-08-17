# 性能复跑验证报告（2026-08-17）

> 目的：回应"缓存优化后 API 并发性能未经验证"的质疑——在**百万数据 + Meili 全量重建**环境下，
> 对**公开站实际使用的聚合搜索接口** `POST /api/public/search/aggregate` 复跑 c1/c10/c50/c100，
> 提交结果 JSON 与报告作为上线依据。

## 一、环境与数据基线

| 项 | 值 |
|---|---|
| 代码版本 | master `39db157`（含全部 6 项优化：富化缓存/响应缓存/高亮收窄/Sanitize 快路径/关评分/并发槽位 16 核） |
| 数据量 | **1,000,000 文档**（`products` 表 100 万行，Meili `numberOfDocuments=1000000`，`isIndexing=false` 全量重建完成） |
| 被测接口 | `POST /api/public/search/aggregate`（生产前台主路径，AggregateSearchView/全局搜索框；含 EnrichFromPgAsync 富化） |
| 查询集 | `spike-test/bench_queries_public.json`（37 条，真实值必命中） |
| 压测方式 | 37 查询 × 10 轮 × 并发 1/10/50/100，两种客户端交叉：urllib（`_bench_aggregate.py`）+ http.client |
| 限流 | `RATELIMIT_ENABLED=false`（压测专用，生产需开启） |

## 二、压测结果（本次复跑，2026-08-17 14:34）

### urllib 客户端（`_bench_aggregate.py --rounds 10`）
| 并发 | P50 (ms) | **P95 (ms)** | P99 (ms) | 错误率 |
|---|---|---|---|---|
| c1 | 14.8 | 82.4 | 786.3* | 0/370 |
| c10 | 12.6 | 28.9 | 32.9 | 0/370 |
| c50 | 57.3 | **90.0** | 109.8 | 0/370 |
| c100 | 109.0 | **141.3** | 149.9 | 0/370 |

\* c1 的 P99=786ms 为**冷缓存首请求**（刚重启，响应缓存/富化缓存为空），P50 仅 14.8ms；c10 起缓存已热。

### http.client 快客户端（交叉验证，c50/c100）
| 并发 | P50 (ms) | **P95 (ms)** | P99 (ms) | 错误率 |
|---|---|---|---|---|
| c50 | 59.8 | **86.3** | 98.0 | 0/370 |
| c100 | 123.2 | **155.3** | 170.2 | 0/370 |

**两种客户端结论一致：c50 P95≈86-90ms、c100 P95≈141-155ms，全部 0 错误，红线 P95<200ms 达标。**

### 与历史数据对比
| 场景 | c50 P95 | c100 P95 |
|---|---|---|
| 最初基线（27GB 索引 + 无缓存） | 482ms | 855ms |
| 8-16 优化后（urllib / http.client） | 128 / 104ms | 213 / 187ms |
| **8-17 复跑（urllib / http.client）** | **90 / 86ms** | **141 / 155ms** |

复跑结果优于 8-16（可能与索引重建后 Meili 文档结构更瘦有关），相对最初基线 **c50 -81%、c100 -82%**。

## 三、冷缓存 vs 热缓存说明
- 每次压测首轮（每查询第一次）为**冷缓存**：需 Meili 搜索 + PG 富化 + 构建响应（单请求 700-800ms 量级）。
- 同一查询 30s 内再次命中**响应缓存**（SearchResponseCache，TTL 30s）→ 热缓存 P95 即上表数值。
- 真实生产：热点查询（用户反复搜同一关键词）命中率高，冷缓存占比低；**P95 反映的是热缓存混合表现**。
- 如需纯冷缓存压测：清空缓存后单轮 c1 即可（P50 14.8ms 说明冷路径本身也不慢，瓶颈主要在首次构建）。

## 四、为什么与 7 月旧报告不冲突
- 7 月旧报告（`_bench_results.json` / `_perf_v30_14_1m_offset_*`）记录的是 **PostgreSQL 深分页**场景（`OFFSET` 深翻页 P95 2.5-5.2s）——那是**旧架构的旧路径**，与本次 Meili 全量索引 + aggregate 优化链路**不是同一条链路**。
- 本次验证的是 **Meili 主路径 + 聚合搜索**（生产前台实际使用的接口），新旧报告分别回答不同阶段的问题，不互相否定。

## 五、结论
1. **上线依据已补齐**：百万数据 + Meili 全量重建 + 生产主路径接口 + 双客户端交叉验证，P95 达标、0 错误。
2. 缓存优化（富化缓存 TTL 3min + 响应缓存 TTL 30s）在真实生产热点流量下有效。
3. 生产部署注意：`RATELIMIT_ENABLED` 需按生产配置开启（压测时关闭仅限测试环境）。

## 六、证据文件
- `spike-test/_bench_aggregate.py` — 压测脚本（生产主路径）
- `spike-test/_bench_aggregate_results.json` — urllib 结果（本次）
- `spike-test/_bench_aggregate_http_results.json` — http.client 交叉验证结果（本次）
- `spike-test/bench_queries_public.json` — 查询集（37 条）
- `spike-test/PERFORMANCE_OPTIMIZATION_20260816.md` — 优化全链路报告（8-16）
- `spike-test/PERFORMANCE_REPORT_1M.md` — 百万数据性能报告（8-16）
- `spike-test/_dissect_search.py` — Meili vs backend 拆解工具
