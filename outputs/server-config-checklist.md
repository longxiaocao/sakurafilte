# 生产服务器配置清单（SakuraFilter Prod）

> 基于 `docker-compose.prod.yml`（commit `4a546bb`）落地参数整理。
> 部署命令：**单文件** `docker compose -f docker-compose.prod.yml --env-file .env.prod up -d`（不叠加 base）。

## 1. 主机规格建议（含资源核算）

容器硬性内存上限累加（来自各服务 `deploy.resources.limits.memory`）：

| 服务 | 内存上限 | 说明 |
|---|---|---|
| postgres | 4G | + `shm_size: 4gb`（并行排序/哈希临时内存，独立于内存上限） |
| meilisearch | 16G | 全量索引常驻 RAM |
| api | 2G | ASP.NET Core 运行时 |
| web | 1G（约） | nginx + 静态资源 |
| minio | 1G | 对象存储 |
| prometheus | 512M | 30d 指标留存 |
| grafana | 512M | 看板 |

- **硬性上限合计**：约 24G（不含 PG 的 4G shm）。
- **推荐主机物理内存 ≥ 32GB**（留 8G+ 给 OS、Docker 开销与突发）。若主机仅 16GB，**Meili 16G 上限会挤爆整机** —— 此时需把 Meili 降到 8G 或按索引体积实测值下调。
- **磁盘**：索引体积≈磁盘体积（百万级约数 GB）；建议系统盘 + 独立数据盘 ≥ 100GB SSD，named volume 持久化 `postgres-data`/`meili-data`/`minio-data`。
- **CPU**：PG/api 各 2 核、Meili 1 核、其余 0.5 核 —— 4 核起步，推荐 8 核。

## 2. 三个上线硬约束（本次修复，已验证）

| 项目 | 修复前 | 修复后 | 风险 |
|---|---|---|---|
| PG `/dev/shm` | 默认 64MB | `shm_size: "4gb"` | 百万级并发并行排序/哈希 → `No space left on device` → 查询失败、P95 飙升 |
| PG 连接数 | 默认 100 = Npgsql 池 | `max_connections=200` | c100 并发顶满槽位排队雪崩 |
| Meili 内存 | 1G 上限 | 16G 上限 + `--max-indexing-memory 8192` | 1G 必 OOMKill |
| Meili 启动命令 | base 写法 `--max-indexing-memory 8192`（无二进制路径，tini → exit 127） | `/bin/meilisearch --max-indexing-memory 8192` | 容器起不来 |
| api 连接池 | Npgsql 默认 100 | `Maximum Pool Size=120; Connection Idle Lifetime=60` | 与 PG 200 配对，防雪崩 |

> Meili 镜像实测 ENTRYPOINT 为 `["tini","--"]`，因此 `command` 必须带 `/bin/meilisearch` 前缀，否则 tini 把参数当可执行文件 → exit 127。**base（`docker-compose.yml`）同款已同步修复。**

## 3. `.env.prod` 必填环境变量（部署前核对）

- `POSTGRES_DB` / `POSTGRES_USER` / `POSTGRES_PASSWORD`
- `MEILI_MASTER_KEY`（注意：带引号写入，直连 Meili 需手动去引号；编排内通过 `${}` 注入自动去引号）
- `JWT_SIGNING_KEY` / `JWT_ISSUER` / `JWT_AUDIENCE`
- `INITIAL_ADMIN_PASSWORD` / `INITIAL_OPERATOR_PASSWORD`（空库初始化）
- `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD`
- `GRAFANA_USER` / `GRAFANA_PASSWORD`（避免回退 admin/admin）
- 可选：`TURNSTILE_*`、`R2_*`（对象存储切换）、`VITE_*` 前端文案

## 4. 部署 & 验证步骤

1. 确认 `.env.prod` 已填齐上方变量。
2. `docker compose -f docker-compose.prod.yml --env-file .env.prod up -d`
3. 健康检查：
   - PG：`docker exec sakura-postgres pg_isready -U $POSTGRES_USER`
   - Meili：`curl http://localhost:7700/health`（免鉴权）
   - api：`curl http://localhost:8080/health/live`
4. 压测复核：搜索 P95 < 200ms（中国红涨绿跌约定不适用，此处纯延迟）。

## 5. 待确认风险

- **Meili 16G 是上限而非保证**：实际按生产机物理内存与索引体积再调（建议 ≥32GB 主机）。
- 本次仅本地提交，**尚未 push / 开 PR** —— 见下方推送状态。
- 连接池 120 < PG 200，单 api 实例安全；若后续扩 api 副本需同步评估 PG 连接数。
