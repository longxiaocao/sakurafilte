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
- 自动加载面板：SakuraFilter ETL 运维面板（uid: `sakurafilter-etl-day710`）

Provisioning 配置目录：
- `monitoring/provisioning/datasources/` — 数据源自动配置
- `monitoring/provisioning/dashboards/` — 面板自动发现配置
- `monitoring/dashboards/` — 面板 JSON 文件
