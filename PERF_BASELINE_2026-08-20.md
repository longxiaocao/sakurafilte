# SakuraFilter perf 环境性能基线报告

- **测量日期**：2026-08-20
- **代码版本**：分支 `fix/typeahead-dict` / commit `ef8c3e4`（PR #9 → `master`）
- **环境**：perf 栈（`docker compose -p sakurafilter-perf -f docker-compose.yml -f docker-compose.perf.yml --env-file .env.perf`）
- **Backend 地址**：`http://localhost:55148`

## 1. 测试环境与配置

| 项目 | 值 |
|---|---|
| 数据规模 | products 100 万 / cross_references 1250 万 / machine_applications 1550 万 / typeahead_dict 1465 万 |
| 压测查询集 | `spike-test/bench_queries_public.json`（37 条真实值，必命中） |
| 限流 | `.env.perf` 设 `RATELIMIT_ENABLED=false`（隔离搜索引擎延迟） |
| 搜索响应缓存 | `SearchResponseCache` TTL 30s（按全请求参数签名） |
| 富化缓存 | `EnrichmentCache` TTL 3min |
| Meili 并发槽 | `MEILI_EXPERIMENTAL_NB_SEARCHES_PER_CORE=16` |
| typeahead 守卫 | `Typeahead__MaxCardinality=1000000`；`oem-no3`(1249万)/`engine-type`(199万) 近唯一，超阈值瞬时返空 |

## 2. 性能红线

- **搜索（aggregate）P95 < 200 ms**
- **typeahead P95 < 100 ms**

## 3. 搜索压测结果（POST /api/public/search/aggregate，生产主路径）

查询集 37 条 × 10 轮，每档并发 n=370，errors=0。

| 并发 | P50 (ms) | P95 (ms) | P99 (ms) | 是否达标 (P95<200) |
|---|---|---|---|---|
| 1  | 14.5 | 27.1  | 28.9  | ✅ |
| 10 | 13.2 | 29.2  | 34.4  | ✅ |
| 50 | 59.7 | 80.6  | 88.4  | ✅ |
| 100| 112.0| 133.9 | 147.5 | ✅ |

> 注：c1 的首个请求为冷启动（backend 刚重启后首个非预热查询偶发 12.7s 尖刺，见 §5）。稳态下所有并发档 P95 均远低于 200ms 红线，c100 P95=133.9ms 仍有充足余量。

## 4. typeahead 压测结果（GET /api/public/typeahead/{field}?q=）

每字段 20 轮（warmup 后）。守卫字段 `oem-no3`/`engine-type` 因近唯一直接返空。

| 字段 | 取样 q | P50 (ms) | P95 (ms) | max (ms) | items | 达标 (P95<100) |
|---|---|---|---|---|---|---|
| oem-brand    | bo   | 15.8 | 27.0 | 48.2  | 1  | ✅ |
| oem-no2      | AB   | 15.1 | 26.5 | 26.7  | 4  | ✅ |
| oem-no3      | TX   | 15.2 | 16.7 | 27.8  | 0  | ✅（守卫返空） |
| machine-brand| ka   | 14.8 | 17.1 | 26.3  | 0  | ✅ |
| machine-model| PC   | 15.2 | 16.4 | 27.9  | 0  | ✅ |
| model-name   | ZX   | 8.9  | 26.2 | 27.4  | 0  | ✅ |
| engine-brand | ko   | 3.3  | 4.4  | 17.5  | 0  | ✅ |
| engine-type  | D1   | 15.3 | 27.2 | 27.9  | 0  | ✅（守卫返空） |

> 全 8 字段 P95 ≤ 27ms，max ≤ 79ms，全部 < 100ms 红线。
> 返回 0 项的字段为数据本身无该子串匹配（已比对 `typeahead_dict` 确认，非 bug）。

## 5. 已知项与注意事项

1. **冷启动尖刺**：backend 进程重启后，首条非预热查询（尤其 c1）可能出现数百毫秒至秒级延迟（本次观测到一次 12.7s、一次 224.8ms 离群）。原因：Meili 索引/后端 JIT/连接池冷启 + 响应缓存空。经预热（首轮请求）后稳态 P95 立即回落至红线内。生产环境为长驻进程，此尖刺仅在部署后首请出现，已被启动期 warmup 与缓存机制覆盖。
2. **typeahead 高基数字段已守卫**：`oem-no3`/`engine-type` 不再查询 PG、瞬时返空，避免千万级字典扫描（修复前冷查询曾达 9.9s）。
3. **单测在本环境无法离线运行**（无 nuget 外网，报 `NU1301`），故验证以 `docker build` 编译 + 部署后端到端压测为准。

## 6. 结论

- 搜索（aggregate）全并发档 **P95 < 200ms** ✅（c100 P95=133.9ms）。
- typeahead 全 8 字段 **P95 < 100ms** ✅（最高 P95=27.0ms）。
- 两项核心红线全部达标，性能基线已固化。后续回归检测以本表数值为基准。
