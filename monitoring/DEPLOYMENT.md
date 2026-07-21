# SakuraFilter 部署说明

## 单机部署（docker compose）

```bash
docker compose up -d --no-deps --build backend
```

注意：单机模式下 `deploy.replicas` 和 `update_config` 不生效，需短暂停机。

## Swarm 模式部署（零停机滚动更新）

```bash
# 初始化 Swarm
docker swarm init

# 部署 stack
docker compose -f docker-compose.yml config | docker stack deploy -c - sakurafilter

# 滚动更新
docker service update --image sakurafilter-backend:latest sakurafilter_backend

# 查看更新状态
docker service ls
docker service ps sakurafilter_backend
```

## 健康检查依赖

P3-10.4 滚动更新依赖 P1-10.2 的健康检查配置：
- backend healthcheck 必须正常工作
- frontend depends_on backend: condition: service_healthy
- update_config.failure_action: rollback 依赖 healthcheck 检测故障

## Grafana 监控（P3-10.3）

首次启动后自动加载（无需手动导入）：
- 访问 `http://localhost:3000`
- 默认账号: `${GRAFANA_ADMIN_USER:-admin}` / `${GRAFANA_ADMIN_PASSWORD:-admin}`（生产环境务必通过 `.env` 覆盖）
- 自动加载数据源：Prometheus（backend `/metrics`）、PostgreSQL（ETL 面板查询）
- 自动加载面板：
  - SakuraFilter ETL 运维面板（uid: `sakurafilter-etl-day710`）— 死信队列 + ETL 进度 + 恢复轨迹
  - SakuraFilter Meili 主路径监控（uid: `sakurafilter-meili-v30-21`，v30-21）— Meili P50/P95/P99/降级率 + 样本数

Provisioning 配置目录：
- `monitoring/provisioning/datasources/` — 数据源自动配置
- `monitoring/provisioning/dashboards/` — 面板自动发现配置
- `monitoring/dashboards/` — 面板 JSON 文件

### Meili 主路径监控面板（v30-21）

**面板布局**（见 `monitoring/dashboards/grafana-dashboard-meili.json`）：

| 区域 | 面板 | 数据源 | 说明 |
|---|---|---|---|
| 顶部 4 个 stat | P50 / P95 / P99 / 降级率 | Prometheus | 核心指标快照 (颜色阈值告警) |
| 中部 timeseries (大) | Meili 延迟趋势 (P50/P95/P99/Max) | Prometheus | 16 格宽, 4 条曲线, P99 红色加粗 |
| 中部 timeseries (小) | 降级率 & 错误率趋势 | Prometheus | 8 格宽, 双曲线对比 |
| 底部 4 个 stat | 样本数 / 成功次数 / 降级次数 / Max | Prometheus | 状态概览 |
| 底部 timeseries | 调用量趋势 (成功/降级/样本总数) | Prometheus | 24 格宽, 三色区分 |

**Grafana → AlertManager 告警规则建议**（在 Grafana Alert Rules 配置）：

```yaml
# Meili P99 ERROR (P0)
- alert: meili_p99_error
  expr: sakura_meili_p99_ms >= 1500
  for: 5m
  labels: { severity: P0 }
  annotations:
    summary: "Meili P99 = {{ $value }}ms (阈值 1500ms)"
    description: "Meili 主路径严重慢, 影响所有搜索. 查 /api/admin/perf/meili/snapshot"

# Meili 降级率超阈值 (P0)
- alert: meili_fallback_rate_error
  expr: avg_over_time(sakura_meili_fallback_rate_pct[5m]) >= 20
  for: 5m
  labels: { severity: P0 }
  annotations:
    summary: "Meili 降级率 = {{ $value }}% (阈值 20%)"
    description: "Meili 频繁不可用, 系统依赖 PG fallback 兜底"

# Meili 样本数不足 (P2, 排查用)
- alert: meili_low_traffic
  expr: sakura_meili_sample_count < 10
  for: 30m
  labels: { severity: P2 }
  annotations:
    summary: "Meili 样本数 = {{ $value }} (流量低, 可能误判)"
```

**注意**：
- 后端 `PerfAlertService` 已通过 `AlertCenter` 推送上述告警到 webhook 渠道 (dingtalk/wechat/webhook)，Grafana Alert 规则是补充而非替代
- AlertCenter 与 Grafana Alert 都会触发时，先收到的渠道处理，另一渠道的 5min 抑制窗口会自动过滤重复
