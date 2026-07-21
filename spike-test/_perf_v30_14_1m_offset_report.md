# v30-14 1M OFFSET 深分页专项压测报告 (ADR #5 验证闭环)

> 生成时间: 2026-07-21 20:47:59 +0800
> 数据库: sakurafilter_perf_tests@localhost:5432
> 数据规模: 950,209 products / 4,751,045 xrefs / 4,751,045 apps

## 1. 摘要

- 测试目的: 验证 v28-2 CTE UNION SQL 在 1M 数据下 OFFSET 深度退化比, 补 v27-3 报告 §4.5 空缺, 闭环 ADR #5 keyset 暂缓决策
- OFFSET 深度档: 0, 10000, 100000, 500000, 900000
- 重复次数: 5 (warmup 2)
- 场景: baseline, q_oil
- keyset 对比: 启用 (last_id 来自 OFFSET=500000)

## 2. 数据规模

| 表 | 行数 |
|---|---|
| products | 950,209 |
| cross_references | 4,751,045 |
| machine_applications | 4,751,045 |

## 3. OFFSET 深度 vs 性能曲线

### 3.1 场景: baseline

**描述**: 无关键词过滤 (仅 is_published + EXISTS xref), 测纯 OFFSET 深度影响
**q_tokens**: (无)
**total**: 948,803 (COUNT 耗时 2408.5ms)

| OFFSET | rows | min(ms) | median(ms) | p95(ms) | p99(ms) | max(ms) |
|---:|---:|---:|---:|---:|---:|---:|
| 0 | 20 | 4893.11 | 5067.12 | 5087.02 | 5090.54 | 5091.43 |
| 10000 | 20 | 4889.9 | 4897.3 | 5004.26 | 5020.72 | 5024.84 |
| 100000 | 20 | 4885.41 | 5044.11 | 5162.06 | 5183.04 | 5188.28 |
| 500000 | 20 | 5130.18 | 5243.54 | 5255.24 | 5257.39 | 5257.93 |
| 900000 | 20 | 4953.56 | 5054.57 | 5190.1 | 5200.32 | 5202.87 |

**keyset 对比** (last_id 来自 OFFSET=500000):

| mode | last_id | rows | median(ms) | p95(ms) | p99(ms) |
|---|---:|---:|---:|---:|---:|
| keyset | 500001 | 20 | 570.36 | 591.79 | 591.8 |

### 3.2 场景: q_oil

**描述**: q='oil' (高频词, 1M 数据 25% 命中, v28-3 验证), 测有 q_match CTE 时 OFFSET 深度影响
**q_tokens**: ['oil']
**total**: 236,778 (COUNT 耗时 4561.93ms)

| OFFSET | rows | min(ms) | median(ms) | p95(ms) | p99(ms) | max(ms) |
|---:|---:|---:|---:|---:|---:|---:|
| 0 | 20 | 2357.54 | 2421.68 | 2522.35 | 2542.29 | 2547.28 |
| 10000 | 20 | 2344.96 | 2371.09 | 2381.71 | 2383.04 | 2383.37 |
| 100000 | 20 | 2412.6 | 2437.56 | 2479.7 | 2486.55 | 2488.26 |
| 500000 | - | SKIP | - | - | - | - |
| 900000 | - | SKIP | - | - | - | - |

## 4. ADR #5 决策建议

### 4.1 OFFSET 深度退化分析 (控制变量法)

对比同一场景下深档 P95 vs 浅档 P95 (OFFSET=0), 衡量 OFFSET 深度本身的影响:

| 场景 | 浅档 (OFFSET=0) P95 | 最深档 P95 | 深浅比 | 主要瓶颈 |
|---|---:|---:|---:|---|
| baseline | 5087.0ms (OFFSET=0) | 5190.1ms (OFFSET=900000) | 1.02x | q_match CTE + ILIKE (与 OFFSET 无关) |
| q_oil | 2522.3ms (OFFSET=0) | 2479.7ms (OFFSET=100000) | 0.98x | q_match CTE + ILIKE (与 OFFSET 无关) |

**最大深度退化比: 1.02x**

### 4.2 keyset 改造潜力

keyset 简化版 (WHERE p.id > :last_id, last_id 来自 OFFSET=500000) vs OFFSET 等价深度:

| 场景 | OFFSET P95 | keyset P95 | 性能提升 |
|---|---:|---:|---:|
| baseline | 5255.2ms | 591.8ms | 8.9x |

### 4.3 综合决策

- ✅ OFFSET 深度退化比 ≤ 1.5x (1.02x): **维持 ADR #5 暂缓决策**
  - 1M 数据下 OFFSET 深度本身不是主要瓶颈, 浅档/深档性能差异不显著
  - v28-3 已验证 v28-2 CTE UNION 加速比 6.82x, 多 token 退化 1.49x
  - 真实用户行为: 99% 在前 5 页内, 深分页罕见
  - 生产环境 Meili 主路径兜底, PostgreSQL 仅 fallback

### 4.4 与 v27-3 50K 数据对比

| 数据规模 | 场景 | 浅档 P95 | 最深档 P95 | 深浅比 |
|---|---|---:|---:|---:|
| 50K (spike_test_v3) | baseline | 552.7ms | 529.4ms | 0.96x |
| 50K (spike_test_v3) | type_oil | 305.3ms | 315.9ms | 1.03x |
| 50K (spike_test_v3) | q_filter | 1879.1ms | 1897.5ms | 1.01x |
| 50K (spike_test_v3) | size_d1_100 | 264.9ms | 267.8ms | 1.01x |
| 1M (sakurafilter_perf_tests) | baseline | 5087.0ms | 5190.1ms | 1.02x |
| 1M (sakurafilter_perf_tests) | q_oil | 2522.3ms | 2479.7ms | 0.98x |


## 5. 文件清单

- `_perf_v30_14_1m_offset_verify.py` — 主压测脚本
- `_perf_v30_14_1m_offset_results.json` — 原始结果 (本报告的数据源)
- `_perf_v30_14_1m_offset_report.md` — 本报告
- `_v30_14_perf.log` — 执行日志
