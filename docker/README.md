# P1-2: Docker 部署快速指南

## 文件结构

```
docker/
├── Dockerfile.api           # API 镜像 (Alpine .NET 8, 多阶段构建)
├── Dockerfile.web           # 前端镜像 (Nginx Alpine 1.27)
├── nginx.conf               # 反代 + 静态托管 + SSE 长连接
├── prometheus.yml           # 指标抓取配置
└── grafana/
    ├── provisioning/
    │   └── datasources/
    │       └── prometheus.yml     # 自动加载数据源
    └── dashboards/
        └── sakurafilter-main.json # 业务仪表盘
```

## 一键部署

```bash
# 1. 准备环境变量
cp .env.prod.example .env.prod
# 编辑 .env.prod, 修改所有 [CHANGE-ME] 字段
# 建议: openssl rand -base64 48 生成随机密钥

# 2. 构建并启动
docker-compose -f docker-compose.prod.yml --env-file .env.prod up -d --build

# 3. 查看状态
docker-compose -f docker-compose.prod.yml ps
curl http://localhost/health       # Web
curl http://localhost/api/health/live  # API
curl http://localhost:9090/         # Prometheus
open http://localhost:3000          # Grafana (admin / .env.prod 中设置的密码)
```

## 端口规划

| 端口 | 服务 | 说明 |
|------|------|------|
| 80 | Nginx (Web) | 对外服务, 反代 API |
| 9090 | Prometheus | 内部, 监控用 |
| 3000 | Grafana | 内部, 仪表盘 |
| 5432 | Postgres | **不对外** (bridge 网络隔离) |
| 7700 | MeiliSearch | **不对外** |
| 9000 | MinIO | **不对外** |

## 反代 / SSE 说明

`/api/etl/progress` 走 SSE 长连接 (24h timeout), 已单独配置 `proxy_buffering off` + `proxy_read_timeout 86400s`, 否则会被 nginx 缓冲掉。

## 升级 / 回滚

```bash
# 拉取新镜像
docker-compose -f docker-compose.prod.yml pull

# 滚动重启 (保留数据卷)
docker-compose -f docker-compose.prod.yml up -d --no-deps api

# 回滚
docker-compose -f docker-compose.prod.yml down
docker tag sakurafilter-api:1.0.0 bak:sakurafilter-api:1.0.0
docker load < sakurafilter-api-1.0.0.tar
docker-compose -f docker-compose.prod.yml up -d
```

## 备份

```bash
# Postgres
docker exec sakura-postgres pg_dump -U sakura sakurafilter | gzip > backup-$(date +%F).sql.gz

# MinIO
docker exec sakura-minio mc mirror /data /backup/

# 数据卷 (慎用, 文件级)
docker run --rm -v sakura_postgres-data:/data -v $(pwd):/backup alpine tar czf /backup/postgres-data.tar.gz /data
```

## 监控

Grafana 预置仪表盘 (uid=`sakurafilter-main`) 包含:
- HTTP 请求率 / 延迟 P95
- ETL 处理速率
- 死信队列深度
- 后台服务陈旧数
- Meili 熔断器状态
- 鉴权失败 / 限流命中计数

## 安全要点

1. **必须** 修改 `.env.prod` 所有 `[CHANGE-ME]` 字段
2. 定期轮转 `ADMIN_DEV_TOKEN` (建议季度)
3. 启用 HTTPS (在 Nginx 前加 Cloudflare / Caddy / Traefik)
4. 启用 PostgreSQL 备份自动化 (cron + S3)
5. 启用 Prometheus 告警 (AlertManager)

## 已知限制

- 单机部署 (无 K8s 编排), 不支持水平扩展
- 监控仅 1 个 Prometheus 实例, 无 HA
- AlertManager 未包含, 告警仅 Grafana 内部通知
