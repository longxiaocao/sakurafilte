# SakuraFilter 运维手册

> 面向运维人员的部署、监控、告警响应、故障排查指南。

## 1. 部署架构

```
┌─────────────┐    ┌─────────────────────────────────┐
│  Frontend   │───▶│  Backend (ASP.NET Core 8)       │
│  Vue 3 + TS │    │  Port: 5148                     │
│  Nginx/CDN  │    │  Singleton: PerfMetrics         │
└─────────────┘    │              PerfAlertService   │
                   │              EtlAlertService    │
                   │              AuthTokenStore     │
                   └────────┬───────────┬───────────┘
                            │           │
                   ┌────────▼───┐  ┌───▼──────────┐
                   │ PostgreSQL │  │ Meilisearch  │
                   │  16+       │  │  1.6+        │
                   │  (主存储)  │  │  (主搜索引擎) │
                   └────────────┘  └──────────────┘
                            │
                   ┌────────▼───────────┐
                   │  Object Storage    │
                   │  MinIO / Aliyun OSS│
                   │  (产品图片存储)    │
                   └────────────────────┘
```

## 2. 启动 / 停止

### 启动后端

```bash
cd backend/src/SakuraFilter.Api
dotnet run --configuration Release
# 或编译后运行
dotnet publish -c Release -o ./publish
./publish/SakuraFilter.Api
```

**启动顺序**：
1. 读 `appsettings.json` + 环境变量覆盖
2. EF Core Migration 自动应用（`Database.Migrate()`）
3. `AuthTokenStore.InitAsync` - 建 `auth_token_state` 表 + 从 DB 加载 token
4. `EtlAlertService.EnsureDefaultSettingsAsync` - 插入默认告警配置
5. `PerfAlertService.EnsureDefaultSettingsAsync` - 插入性能告警配置
6. `EtlProgressBroadcaster.InitAsync` - 启动 PG LISTEN
7. `AuthTokenBroadcaster.StartAsync` - 启动 PG LISTEN
8. Kestrel 监听 5148 端口

### 启动前端

```bash
cd frontend
npm install
npm run build    # 生产构建
# 或开发模式
npm run dev
```

### 停止

```bash
# 优雅停止 (SIGTERM)
kill -TERM <pid>
# 或 Docker
docker compose down
```

**停止顺序**：
1. 收到 SIGTERM → `IHostApplicationLifetime.ApplicationStopping` 触发
2. `BackgroundService` 收到 CancellationToken.Cancel
3. `EtlProgressBroadcaster` 关闭 LISTEN 连接 + 释放 dataSource
4. `AuthTokenBroadcaster` 关闭 LISTEN 连接
5. EF Core `DbContext` 释放连接池
6. Kestrel 停止接受新连接，等待进行中请求完成

## 3. 配置说明

### appsettings.json 关键配置

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=...;Port=5432;Database=...;Username=...;Password=..."
  },
  "Auth": {
    "Enabled": true,
    "DevStaticToken": "<≥32 字符 token>",
    "DevStaticTokenPrevious": "<过渡期旧 token, 可选>",
    "AdminPaths": ["/api/admin", "/api/etl"],
    "ExemptPaths": ["/api/perf/ingest"]
  },
  "Meili": {
    "Url": "http://localhost:7700",
    "ApiKey": "<master key>"
  },
  "Storage": {
    "Provider": "minio",
    "Minio": {
      "Endpoint": "localhost:9000",
      "AccessKey": "...",
      "SecretKey": "...",
      "Bucket": "sakurafilter"
    }
  },
  "Etl": {
    "BatchSize": 5000,
    "MaxConcurrent": 1
  }
}
```

### 环境变量覆盖（推荐生产环境使用）

```bash
# PostgreSQL (P0-3: 严禁硬编码密码)
export ConnectionStrings__Postgres="Host=...;Password=..."

# Auth Token
export Auth__DevStaticToken="<新 token>"

# Meilisearch
export Meili__Url="http://meili:7700"
export Meili__ApiKey="<master key>"

# 存储切换 (minio → aliyun-oss)
export Storage__Provider="aliyun-oss"
```

### system_settings 表（运行时可调，无需重启）

| Key | 默认值 | 说明 |
|---|---|---|
| `alert.enabled` | `false` | ETL 失败告警全局开关 |
| `alert.webhook_url` | `""` | ETL 告警 webhook URL |
| `alert.poll_seconds` | `60` | ETL 告警轮询周期 |
| `perf.alert.enabled` | `true` | 性能告警全局开关 |
| `perf.alert.poll_seconds` | `60` | 性能告警扫描周期 |
| `perf.alert.p95_warn_ms` | `1000` | P95 WARN 阈值 |
| `perf.alert.p95_error_ms` | `3000` | P95 ERROR 阈值 |
| `perf.alert.error_rate_pct` | `5` | 错误率 ERROR 阈值 |
| `perf.alert.max_ms` | `10000` | 单请求最大耗时 ERROR 阈值 |
| `retention.*` | - | 各类历史数据保留期 |

**修改示例**：

```sql
UPDATE system_settings SET value = '500', updated_at = now()
WHERE key = 'perf.alert.p95_warn_ms';
-- 下次扫描周期 (60s) 自动生效, 无需重启
```

## 4. 监控端点

### 健康检查

| 端点 | 用途 | 检查内容 |
|---|---|---|
| `GET /health/live` | K8s liveness probe | 进程存活（永远 200） |
| `GET /health/ready` | K8s readiness probe | PG 连接 + Meili 连接 |
| `GET /api/search/health` | 搜索引擎健康 | Meili 健康状态 + 任务队列 |

**响应示例**：

```json
// GET /health/ready
{
  "status": "healthy",
  "checks": [
    { "name": "postgres", "ok": true, "latency_ms": 2.3 },
    { "name": "meilisearch", "ok": true, "latency_ms": 8.1 }
  ]
}
```

### 性能指标

| 端点 | 鉴权 | 用途 |
|---|---|---|
| `GET /api/perf` | 无 | P50/P95/P99/错误率快照（最近 1000 条请求） |
| `GET /api/admin/perf/alerts?limit=50` | X-Admin-Token | 最近性能告警列表 |
| `POST /api/perf/ingest` | 无（豁免） | 前端性能埋点批量上报 |

**`/api/perf` 响应**：

```json
{
  "sampleCount": 1000,
  "totalRequests": 15234,
  "errorRequests": 23,
  "errorRate": 0.15,
  "p50Ms": 12.3,
  "p95Ms": 145.7,
  "p99Ms": 892.1,
  "maxMs": 2341.5,
  "generatedAt": "2026-07-04T16:30:00Z"
}
```

## 5. 告警响应

### 性能告警（PerfAlertService）

**告警规则**：

| 规则 | 级别 | 阈值 | 触发条件 |
|---|---|---|---|
| `p95_warn` | WARN | 1000ms | P95 ≥ 1000ms 且 < 3000ms |
| `p95_error` | ERROR | 3000ms | P95 ≥ 3000ms |
| `error_rate` | ERROR | 5% | 错误率 ≥ 5% |
| `max_ms` | ERROR | 10000ms | 单请求耗时 ≥ 10s |

**抑制**：5min 窗口内同 (level+rule) 不重发，防止日志刷屏。

**响应流程**：

1. **发现告警**：
   - 日志：`[PERF-ALERT] p95_error: P95 = 3500ms (阈值 3000ms)`
   - API：`curl -H "X-Admin-Token: <token>" http://<api>/api/admin/perf/alerts`

2. **定位慢请求**：
   - 查 `/api/perf` 看当前 P95/P99
   - 查后端日志过滤慢请求：`grep "elapsed" /var/log/sakurafilter/api.log | sort -t'=' -k2 -rn | head -20`
   - PG 慢查询：`SELECT query, mean_exec_time FROM pg_stat_statements ORDER BY mean_exec_time DESC LIMIT 10;`

3. **临时缓解**：
   - 调整告警阈值（避免误报）：`UPDATE system_settings SET value='5000' WHERE key='perf.alert.p95_error_ms';`
   - 关闭告警：`UPDATE system_settings SET value='false' WHERE key='perf.alert.enabled';`

4. **根因修复**：
   - DB 慢查询：加索引 / 优化 SQL
   - Meili 慢：检查 Meili 任务队列，必要时重建索引
   - 网络抖动：检查 PG/Meili 连接延迟

### ETL 告警（EtlAlertService）

**默认关闭**，需配置 webhook：

```sql
UPDATE system_settings SET value = 'true' WHERE key = 'alert.enabled';
UPDATE system_settings SET value = 'https://hooks.slack.com/...' WHERE key = 'alert.webhook_url_p0';
```

**告警级别**：
- P0：Meili 连接失败 / 500 错误 / timeout（必配 webhook）
- P1：schema/列名/字段错（数据问题）
- P2：其他一般告警

## 6. ETL 触发流程

### 触发产品 ETL

```bash
curl -X POST http://localhost:5148/api/etl/import \
  -H "X-Admin-Token: <token>" \
  -H "Content-Type: application/json" \
  -d '{"filePath": "/data/products.xlsx", "mode": "upsert"}'
```

### 监控 ETL 进度

```bash
# 查询当前任务状态
curl -H "X-Admin-Token: <token>" http://localhost:5148/api/etl/status

# 实时 SSE 流 (跨实例广播)
curl -N -H "X-Admin-Token: <token>" http://localhost:5148/api/admin/etl/progress/stream

# 查询历史
curl -H "X-Admin-Token: <token>" "http://localhost:5148/api/admin/etl/history?limit=20"
```

### 取消 ETL 任务

```bash
curl -X DELETE -H "X-Admin-Token: <token>" \
  -H "Content-Type: application/json" \
  -d '{"reason": "USER_REQUEST"}' \
  http://localhost:5148/api/admin/etl/task
```

## 7. 常见问题排查

### Q1: 启动报错 `ConnectionStrings:Postgres 未配置`

**根因**：环境变量 `ConnectionStrings__Postgres` 未设置，且 `appsettings.json` 无配置

**处理**：
```bash
export ConnectionStrings__Postgres="Host=...;Password=..."
```

### Q2: `/api/admin/*` 全部 401

**根因**：token 不正确，或 `auth_token_state` 表与 `appsettings.json` 不一致

**处理**：
1. 查 DB：`SELECT current_key, previous_key FROM auth_token_state WHERE id=1;`
2. 用 `current_key` 调用 `/api/admin/auth/status` 验证
3. 参考 [Token 轮转 SOP](./token-rotation-sop.md) 修正

### Q3: dict_oem_no3 接口慢（>1s）

**根因**：可能索引未应用，或 ETL 后统计信息过期

**处理**：
```sql
-- 验证索引存在
\d+ dict_oem_no3
-- 应包含: idx_dict_oem_no3_sort (sort_order, oem_no_3) WHERE deleted_at IS NULL

-- 更新统计信息
ANALYZE dict_oem_no3;
```

### Q4: Meilisearch 健康检查失败

**根因**：Meili 进程挂掉，或 API Key 错误

**处理**：
1. `curl http://<meili>/health` 验证 Meili 存活
2. 检查 `Meili__ApiKey` 环境变量
3. Meili 不可用时系统自动降级到 PG 全文搜索（熔断器模式）

### Q5: 性能告警频繁触发

**根因**：阈值过低，或真实性能问题

**处理**：
1. 查 `/api/perf` 看实际 P95 值
2. 临时调高阈值：`UPDATE system_settings SET value='2000' WHERE key='perf.alert.p95_warn_ms';`
3. 持续触发时定位慢请求根因（参考第 5 节）

## 8. 备份与恢复

### 数据库备份

```bash
# 全量备份
pg_dump -U postgres -d sakurafilter > backup_$(date +%Y%m%d).sql

# 仅字典表 (频繁变更)
pg_dump -U postgres -d sakurafilter -t 'dict_*' -t 'system_settings' > dict_backup.sql
```

### 恢复

```bash
psql -U postgres -d sakurafilter < backup_20260704.sql
```

## 9. CI/CD 流程

GitHub Actions 工作流 `.github/workflows/e2e.yml` 包含：

| Job | 用途 | 触发条件 |
|---|---|---|
| `setup` | 环境准备 + 构建产物 | push/PR |
| `e2e` | 端到端测试 (9 用例) + P0 防回归 + 安全门禁 + P1/P2/P3 防回归 | push/PR |
| `frontend-contract` | 前端契约测试 (vitest + zod) | push/PR |

**防回归脚本**：
- `spike-test/_test_p0_fixes.py` - P0 修复点 (API 验证 + grep 扫描)
- `spike-test/_test_regression.py` - P1/P2/P3 修复点 (grep 模式扫描)

**安全门禁**：扫描 `.cs` 文件中的硬编码密码（`Password=xxx` 模式）

## 10. 相关文档

- [API 契约](./api-contract.md) - 端点列表 + 请求/响应格式
- [Token 轮转 SOP](./token-rotation-sop.md) - 零停机 token 切换流程
