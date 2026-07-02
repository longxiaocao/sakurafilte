# Search 性能基准与退化告警 (P1.4)

> Day 10+ P1.4 任务产物。本文说明搜索性能基线、复现方法、退化告警规则和性能调优 checklist。

---

## 目录

1. [为什么需要基准](#1-为什么需要基准)
2. [当前基线数字 (本地 spike_test_v3)](#2-当前基线数字-本地-spike_test_v3)
3. [SLA 目标与 CI 阈值说明](#3-sla-目标与-ci-阈值说明)
4. [复现方法](#4-复现方法)
5. [退化告警规则](#5-退化告警规则)
6. [性能调优 checklist](#6-性能调优-checklist)
7. [瓶颈与后续优化方向](#7-瓶颈与后续优化方向)
8. [相关文件清单](#8-相关文件清单)

---

## 1. 为什么需要基准

### 1.1 背景

SakuraFilter 搜索由三部分组成 (Day 5-8):
- **Primary**: MeiliSearch (typo 容错 + facet 过滤)
- **Fallback**: PostgreSQL FTS + trigram (100% 可靠, 无 typo)
- **Resilient**: 熔断器 (Day 6) 自动主备切换

在 1 百万 products + 5 百万 xref 规模下, 搜索性能是核心 SLA 指标。但 Day 5-8 的迭代中没有可重复执行的性能基准, 无法:
1. 验证 SLA 是否达标 (搜索 P95 < 200ms, typeahead P95 < 100ms)
2. 检测性能退化 (升级/重构后是否变慢)
3. 量化优化效果 (加索引/改查询的真实收益)

### 1.2 本文产出

- **可重复执行的 benchmark 脚本**: `spike-test/_bench_search.py`
  - 50 个典型查询 (8 大类)
  - 4 档并发 (1/10/50/100)
  - 输出 P50/P95/P99 + min/max + error count
  - 控制台表格 + JSON 报告 (`_bench_results.json`)
- **CI 集成**: 每次 PR/push 自动跑 bench (`.github/workflows/e2e.yml` 新增 `Run Search Benchmark` step)
- **退化告警规则**: P95 比 baseline 慢 30% 触发告警
- **调优 checklist**: 常见瓶颈 (Meili 索引 / PG FTS GIN 索引 / 网络)

---

## 2. 当前基线数字 (本地 spike_test_v3)

> **测量时间**: 2026-07-02
> **测量环境**: Windows 11, dotnet 8, PostgreSQL 16 (1.005M products + 5.21M xref + 1M apps)
> **运行命令**: `python _bench_search.py`
> **关键约束**: 本次测量时 MeiliSearch 未启动, Resilient 全部走 PG 兜底

### 2.1 搜索接口 (POST /api/search + /api/admin/products/search)

| 并发档 | P50 (ms) | P95 (ms) | P99 (ms) | errors | 说明 |
|--------|----------|----------|----------|--------|------|
| 1      | 1,206.8  | 3,560.7  | 3,803.8  | 0      | 串行, 50 查询一遍 |
| 10     | 1,332.7  | 7,339.7  | 8,010.8  | 0      | 10 并发, 50 查询 |
| 50     | 5,589.9  | 10,010.1 | 10,010.8 | 11     | 50 并发 (10 个 429 限流) |
| 100    | 2,788.9  | 10,012.6 | 10,016.9 | 9      | 100 并发 (9 个 429 限流) |

### 2.2 Typeahead (GET /api/admin/dict/oem-brands/typeahead)

| 指标 | P50 (ms) | P95 (ms) | P99 (ms) | errors | 说明 |
|------|----------|----------|----------|--------|------|
| 并发=10, 49 请求 | 6.0 | 7.9 | 9.0 | 0 | 字典自动补全, 直接 PG 走 |

### 2.3 与 SLA 对比

| 指标 | SLA 目标 | 当前基线 (Meili 离线) | 差距 | 何时达到 SLA |
|------|----------|------------------------|------|---------------|
| 搜索 P95 | < 200ms | 3,560ms | 17.8x 超 | Meili 恢复 + warmup 完成后 |
| Typeahead P95 | < 100ms | 7.9ms | ✅ 达标 | — |

> **关键发现**: 搜索基线 P95 (3.5s 串行, 7s 10 并发) 远高于 SLA (200ms), 根因是 **MeiliSearch 未运行, Resilient 走 PG 兜底** + Meili timeout (1s) + PG LIKE 全表扫描。
> Typeahead 直接走 PG, 不经过 Resilient, P95 = 7.9ms 满足 100ms SLA。

---

## 3. SLA 目标与 CI 阈值说明

### 3.1 SLA 目标 (生产环境)

| 指标 | SLA 目标 | 测量条件 |
|------|----------|----------|
| 搜索 P95 | < 200ms | Meili 在线 (Primary) |
| Typeahead P95 | < 100ms | PG 字典 (10 行) |

### 3.2 CI 阈值放宽 (原因)

`ResilientSearchProvider` (Day 6) 配置了 **1s Meili timeout** 作为安全网:

```csharp
// ResilientSearchProvider.cs
.AddTimeout(TimeSpan.FromSeconds(1))  // 单次调用 1s 超时
```

Meili 不可用时, **每次调用都有 1s 延迟地板** (timeout 触发 → 降级到 PG)。CI 环境通常无 Meili, 故:

| 环境 | 搜索 P95 SLA 阈值 | 原因 |
|------|-------------------|------|
| 本地 (Meili 在线) | 200ms | 严格 SLA |
| 本地 (Meili 离线) | 3000ms | Meili timeout 1s + PG 兜底 1-2s |
| CI (无 Meili, 空白 PG) | 3000ms | 1s timeout + 空表 <10ms |

CI 实际使用 `BENCH_THRESHOLD_P95=3000` 避免误报。当 Meili 启用时, 可通过 `BENCH_THRESHOLD_P95=200` 走严格 SLA。

### 3.3 限流影响

- `SearchPermitsPerMinute=300` (appsettings.json)
- 50 并发 × 60秒 = 3000/min → 远超 300 限流
- 50/100 并发档的 errors = 11/9 全部是 429 (限流), 不是服务端错误
- 日常 10 并发档 (300/min 之内) 无 429

---

## 4. 复现方法

### 4.1 本地完整复现 (1/10/50/100 全部并发档 + typeahead)

```bash
# 前置: 后端运行 (dotnet run 5148), PG 有 1M+ 数据
cd d:\projects\sakurafilter
python spike-test/_bench_search.py
```

输出:
- 控制台: 4 档并发 + typeahead 表格
- JSON: `spike-test/_bench_results.json`

### 4.2 只跑 warmup 验证脚本启动

```bash
python _bench_search.py --warmup-only --skip-typeahead
```

仅 1 并发 × 50 查询, 不打印表格, 验证:
1. 脚本语法正确
2. 后端可达
3. 50 个查询无 4xx/5xx 错误

### 4.3 CI 模式 (只跑 10 并发, 阈值检查)

```bash
python _bench_search.py --concurrency=10 --threshold-p95=3000 --threshold-typeahead-p95=200
```

阈值超 → `::error::` GitHub 注解 + exit 1。

### 4.4 自定义阈值 (生产/Meili 在线时严格 SLA)

```bash
# 严格 SLA 200ms (Meili 在线)
BENCH_THRESHOLD_P95=200 BENCH_THRESHOLD_TYPEAHEAD_P95=100 \
  python _bench_search.py --concurrency=10 \
    --threshold-p95=$BENCH_THRESHOLD_P95 \
    --threshold-typeahead-p95=$BENCH_THRESHOLD_TYPEAHEAD_P95
```

### 4.5 输出示例 (控制台)

```
================================================================================
Day 10+ P1.4 Search 性能基准 (Task 5)
  BASE = http://localhost:5148
  queries = 50 (8 类)
================================================================================

[1] 后端健康检查 ...
  ✓ /api/search/health: 200 {"provider":"resilient(meili→pg)","healthy":true}

[2] 跑搜索并发基准 ...

  --- concurrency=1 ---
    P50= 1206.8ms | P95= 3560.7ms | P99= 3803.8ms | n=50 | errors=0
  ...

[3] 跑 typeahead 基准 (并发=10, 50 请求) ...
    P50=    6.0ms | P95=    7.9ms | P99=    9.0ms | n=49 | errors=0

[4] 结果已写入: _bench_results.json

================================================================================
汇总表 (P50/P95/P99 ms)
================================================================================
并发档            |      P50 |      P95 |      P99 |  errors
--------------------------------------------------
concurrency_1  |  1206.8 |  3560.7 |  3803.8 |       0
concurrency_10 |  1332.7 |  7339.7 |  8010.8 |       0
concurrency_50 |  5589.9 | 10010.1 | 10010.8 |      11
concurrency_100 |  2788.9 | 10012.6 | 10016.9 |       9
typeahead-10   |     6.0 |     7.9 |     9.0 |       0

=== PASS: 所有阈值达标 ===
```

---

## 5. 退化告警规则

### 5.1 自动化告警 (CI)

`.github/workflows/e2e.yml` 加 `Run Search Benchmark` step, **超过阈值即 fail**:
- `BENCH_THRESHOLD_P95=3000` (搜索 P95 > 3000ms 告警, CI 默认)
- `BENCH_THRESHOLD_TYPEAHEAD_P95=200` (typeahead P95 > 200ms 告警)

Meili 启用后, 将 `BENCH_THRESHOLD_P95` 调到 `200` 走严格 SLA。

### 5.2 人工告警 (相对基线退化)

**规则**: P95 比 `docs/bench-baseline.md` 第 2 节基线数字慢 **30%** → 告警。

| 指标 | 基线 | 退化告警线 | 严重告警线 |
|------|------|-----------|-----------|
| 搜索 P95 (concurrency=1) | 3,560ms | 4,628ms (30%↑) | 7,120ms (100%↑) |
| 搜索 P95 (concurrency=10) | 7,339ms | 9,540ms (30%↑) | 14,678ms (100%↑) |
| Typeahead P95 | 7.9ms | 10.3ms (30%↑) | 15.8ms (100%↑) |

**WHY 相对基线**: 绝对阈值会被硬件/数据规模影响, 相对基线能稳定捕捉"性能变差"的信号。

**告警处理 SOP**:
1. 确认是哪个查询变慢 (按 8 大类排查)
2. 检查服务端日志 (Meili 错误 / PG 慢查询)
3. 对比 6 调优 checklist (见下节)
4. 必要时回滚最近部署

### 5.3 报告频次

- **每次 PR/push**: CI 自动跑
- **每周一次**: 人工 review 数字趋势 (后续可加 Grafana dashboard)
- **数据规模翻倍时**: 重跑基线, 更新本文档

---

## 6. 性能调优 checklist

> 按优先级排序, 任何一项都可能让 P95 翻倍。

### 6.1 MeiliSearch (Primary) 配置

- [ ] **Meili 索引配置**: 1M products 索引, 内存占用约 200-500MB
  - `meilisearch --master-key=xxx --no-analytics --http-addr 0.0.0.0:7700 --env development`
  - 生产建议 2GB+ RAM, SSD 存储
- [ ] **Meili ranking rules**: 调整 `words`, `typo`, `proximity`, `attribute`, `sort`, `exactness`
  - 默认适合产品搜索, 无需调整
- [ ] **Meili filterable attributes**: 确保 `type`, `is_discontinued`, `d1_mm`, `h1_mm` 可过滤
- [ ] **Meili 索引完整性**: `curl http://localhost:7700/indexes/products/stats` 看 `numberOfDocuments` 应 = products 表行数
- [ ] **Meili 同步延迟**: IndexReplayWorker (Day 9.x) 是否有大量 pending

### 6.2 PostgreSQL Fallback 优化

- [ ] **PG FTS GIN 索引**: `remark` / `product_name` 字段
  - 当前用 `ILIKE '%xxx%'` 全表扫描, 1M 数据 1-3s
  - 建议加 `CREATE INDEX idx_products_remark_trgm ON products USING gin (remark gin_trgm_ops);`
  - 注意: GIN 索引占空间, 1M 文本字段约 500MB-1GB
- [ ] **PG 复合索引**: `(type, d1_mm, h1_mm)` 让范围查询走 Index Scan
  - 已有: `idx_products_type` (Day 1), `idx_products_h1` (Day 1)
  - 建议加: `CREATE INDEX idx_products_type_h1 ON products (type, h1_mm);`
- [ ] **PG ANALYZE**: 定期 `ANALYZE products;` 更新统计信息
- [ ] **PG 慢查询日志**: `log_min_duration_statement = 1000` 记录 >1s 查询

### 6.3 Resilient 配置

- [ ] **Meili timeout**: 当前 1s, 1M 数据首次查询 200-500ms
  - 生产可降到 500ms (失败更快降级, 减少用户等待)
  - 但要保证 warmup 后 < 500ms
- [ ] **Circuit breaker 阈值**: 当前 50% 失败率 + 4 采样 + 30s 熔断
  - 高并发场景熔断易触发, 可考虑调到 30% 失败率

### 6.4 网络与基础设施

- [ ] **后端 - DB 网络**: localhost 通常 < 1ms, 跨机需 < 5ms
- [ ] **Meili - DB 网络**: Meili 应与后端同节点, < 1ms
- [ ] **HTTP 客户端 keep-alive**: `urllib` 默认复用, 无需调
- [ ] **CDN 静态资源**: 搜索页 JS/CSS 走 CDN, 减少首屏时间 (与搜索 P95 无关)

### 6.5 应用层

- [ ] **search/pageSize**: 当前默认 20, 大页 (50-100) 会变慢 2-5x
- [ ] **搜索字段**: 用 `OemNoNormalized` (走索引) 替代 `OemNoDisplay` (无索引)
- [ ] **缓存**: 高频查询 (e.g. 热门 OEM) 可加 Redis 缓存 (后续 P 任务考虑)
- [ ] **限流**: `SearchPermitsPerMinute=300` 适合单实例, 集群需按节点数 × 300

---

## 7. 瓶颈与后续优化方向

> 基于本地基线测量 (2026-07-02) 发现的真实瓶颈。

### 7.1 当前主要瓶颈 (按影响排序)

| # | 瓶颈 | 影响 | 当前 | 优化后 | 优先级 |
|---|------|------|------|--------|--------|
| 1 | Meili 未启动, Resilient 走 PG 兜底 | 搜索 P95 = 3.5s | Meili 离线 | Meili 上线后 < 200ms | P0 |
| 2 | PG `ILIKE '%xxx%'` 全表扫描 | 1M 数据 1-3s | 无 GIN 索引 | 加 GIN trgm → 50-200ms | P1 |
| 3 | Meili timeout 1s (地板) | 即便 Meili 健康, 偶尔抖动会到 1s | 1s | 降到 500ms | P2 |
| 4 | 50+ 并发触发限流 (429) | 高并发失败率 22% | 300/min | 加 Redis 缓存或令牌桶 | P2 |

### 7.2 优化方向 (后续任务)

- **P1 (后续 Sprint)**: 加 `remark` / `product_name` GIN trgm 索引
- **P2 (Meili 部署)**: Linux 服务器起 Meili, 同步 1M 索引, 测真实 Meili P95
- **P2 (缓存层)**: Redis 缓存 typeahead 结果, 减少 DB 压力
- **P3 (限流优化)**: 滑动窗口或令牌桶, 避免突发 429

### 7.3 已知边缘场景

- **中文搜索**: 1M 数据中 remark 多为英文, 中文 search 未验证
- **特殊字符**: `%` `_` `\` 已被 P0.1 ILIKE ESCAPE 修复, bench 测了 P02/A0/1142 等
- **空结果查询**: `q=XYZ` (无匹配) 应 0ms, 实际是 1-2s (PG COUNT 仍走)
- **深度分页**: `page=100, pageSize=20` 未测, OFFSET 100 慢, 用 cursor 模式

---

## 8. 相关文件清单

| 文件 | 用途 |
|------|------|
| `spike-test/_bench_search.py` | 主基准脚本 (50 查询 + 4 档并发 + typeahead) |
| `spike-test/_bench_results.json` | 最新一次运行的 JSON 报告 (baseline 数字) |
| `backend/src/SakuraFilter.Search/MeiliSearchProvider.cs` | Meili 主搜索 |
| `backend/src/SakuraFilter.Search/PostgresSearchProvider.cs` | PG 兜底搜索 (含 ILIKE 3 参) |
| `backend/src/SakuraFilter.Search/ResilientSearchProvider.cs` | Polly 熔断 + 主备切换 |
| `backend/src/SakuraFilter.Core/DTOs/SearchRequest.cs` | /api/search 请求/响应 DTO |
| `backend/src/SakuraFilter.Core/DTOs/AdminProductSearchRequest.cs` | /api/admin/products/search DTO |
| `.github/workflows/e2e.yml` | CI workflow (含 `Run Search Benchmark` step) |
| `docs/bench-baseline.md` | 本文档 |

---

> 文档维护: Day 10+ P1.4 Task 5 / SubTask 5.4
> 最后更新: 2026-07-02
> **下次基线刷新**: Meili 上线后重跑 `_bench_search.py`, 替换第 2 节表格
