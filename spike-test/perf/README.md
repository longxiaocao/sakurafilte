# P2-3: k6 性能压测指南

## 工具准备

```bash
# Windows (Chocolatey)
choco install k6

# macOS
brew install k6

# Linux (Debian)
sudo apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys C5AD17C747E3415A3642D57D77C6C491D6AC1D69
echo "deb https://dl.k6.io/deb stable main" | sudo tee /etc/apt/sources.list.d/k6.list
sudo apt-get update && sudo apt-get install k6
```

## 压测脚本

| 脚本 | 场景 | 目标 QPS | 验收 P95 |
|------|------|----------|----------|
| `search.js` | 公开搜索 + 详情 + 对比 | 100 VU | 搜索 < 200ms / 详情 < 300ms / 对比 < 500ms |
| `etl.js` | ETL dry-run + status + history | 30 VU | dry-run < 1.5s / status < 50ms |
| `mixed.js` | 公开:80% + 管理:20% 混合 | 300 VU | P95 < 500ms, 错误率 < 1% |

## 运行

```bash
# 1. 单脚本压测
k6 run --duration 60s --vus 100 spike-test/perf/search.js

# 2. 导出 JSON 报告 (后续 Grafana 解析)
k6 run --out json=perf-search.json spike-test/perf/search.js

# 3. 自定义目标地址
k6 run -e BASE_URL=http://api.example.com spike-test/perf/search.js

# 4. 全套压测 (依次跑)
./run-all.sh  # 待补充
```

## 验收标准 (50k 产品数据)

| 指标 | 目标 | 实测 |
|------|------|------|
| 公开搜索 P95 | < 200ms | 待测 |
| 公开搜索 QPS | > 200 | 待测 |
| 公开产品详情 P95 | < 300ms | 待测 |
| 公开对比 P95 (3个) | < 500ms | 待测 |
| ETL dry-run P95 | < 1.5s | 待测 |
| 整体错误率 | < 0.1% | 待测 |

## 报告输出

每次压测生成 `perf-result.json` (k6 原生), 含:
- `metrics` — 所有指标的中位数/P95/P99/最大/最小
- `root_group` — 断言结果
- `options.thresholds` — 阈值检查结果

后续可接入 Grafana k6 data source 自动展示。

## 调优建议

1. **MeiliSearch 慢**: 调高 `MeiliSearch.TimeoutMs`,或加 Meili 实例
2. **PG 慢**: 启用 PG 连接池, 加 `MaxPoolSize=100`
3. **CPU 满**: 增加 API 实例, 配 nginx upstream
4. **内存高**: 调高 `MeiliSearch.MemoryLimit`, 或改用 SSD
