# SakuraFilter API 契约

> 后端 API 端点契约文档。所有端点基于 ASP.NET Core 8 Minimal API，路径前缀 `/api`。
> 完整 OpenAPI Schema 见 `/openapi/v1.json`（开发环境）或 Swagger UI `/swagger`（仅 Development）。

## 1. 鉴权机制

### X-Admin-Token

`DevTokenAuthMiddleware` 保护 `/api/admin/*` 和 `/api/etl/*` 路径，要求请求头携带：

```
X-Admin-Token: <token>
```

**双 Key 模式**（零停机轮转）：

- `CurrentKey`：主用 token
- `PreviousKey`：过渡期保留的旧 token（使用时记 WARN 日志）

**鉴权失败响应**：

```json
HTTP 401 Unauthorized
{
  "error": "X-Admin-Token 缺失或无效"
}
```

**Token 管理**：参考 [Token 轮转 SOP](./token-rotation-sop.md)。

### 公开端点

以下端点无需鉴权：

- `GET /` - 服务信息
- `GET /health/live` / `GET /health/ready` - 健康检查
- `GET /api/perf` - 性能指标快照
- `POST /api/perf/ingest` - 前端性能埋点上报（豁免）
- `POST /api/search` - 产品搜索
- `GET /api/search/health` - 搜索引擎健康
- `GET /api/products/{oem}` - 按 OEM 查询产品
- `GET /api/public/product/{slug}` - 公开产品详情
- `GET /api/public/by-type` - 按 type 分组聚合

## 2. 统一响应格式

### 成功响应

- `GET` / `PUT` / `DELETE`：`200 OK` + JSON body
- `POST` 创建资源：`201 Created` + Location header + JSON body
- `POST` 动作类（如 ETL 触发）：`200 OK` + JSON body
- `DELETE` 无内容：`204 No Content`

### 错误响应（RFC 7807 ProblemDetails）

```json
HTTP 400 Bad Request
{
  "type": "https://httpstatuses.io/400",
  "title": "Bad Request",
  "status": 400,
  "detail": "字段长度超限: Oem2 不能超过 50 字符 (当前 60)",
  "instance": "/api/admin/products"
}
```

**5xx 错误（P0-2 修复）**：不泄露 `ex.Message`，统一返回：

```json
HTTP 500 Internal Server Error
{
  "title": "Internal Server Error",
  "detail": "服务内部错误,请联系管理员",
  "status": 500,
  "instance": "/api/admin/products"
}
```

详细堆栈仅记入服务端日志（`ILogger.LogError`）。

### 分页响应

游标分页（基于 `(updated_at, id)` keyset，O(1) 深分页）：

```json
{
  "items": [...],
  "nextCursor": "<HMAC 签名的游标>",
  "hasMore": true
}
```

> 游标用 Ticks（非 ISO 字符串）+ HMAC 签名，防止客户端篡改。

## 3. 公开端点

### 3.1 产品搜索

```
POST /api/search
```

**请求体**：

```json
{
  "q": "机油滤芯",
  "type": "oil",
  "d1Mm": 80,
  "d1Tolerance": 5,
  "limit": 20
}
```

**响应**：

```json
{
  "items": [
    {
      "id": 1234,
      "oemNoDisplay": "ABC-123",
      "productName1": "机油滤芯",
      "d1Mm": 80.0,
      "d2Mm": 65.0,
      "h1Mm": 120.0,
      "imageKey": null,
      "imageStatus": "pending"
    }
  ],
  "totalEstimate": 156,
  "searchEngine": "meili"
}
```

> Meili 不可用时降级到 PG 全文搜索（`searchEngine: "postgres"`），熔断器模式。

### 3.2 公开产品详情

```
GET /api/public/product/{slug}
```

`slug` 格式：`{name1}-{name2}-{oemBrand}-{oemNo}`，取最后一段作为 oem 匹配。

**匹配优先级**（P3-2 修复：合并为 1 次 OR 查询）：

1. `OemNoDisplay == oem`（优先级 1）
2. `Oem2 == oem`（优先级 2）
3. `Mr1 == oem`（优先级 3）

**响应**：`ProductDetailDto`（同管理端详情，含 xref + machine applications + 图片 URL）。

### 3.3 按 type 分组

```
GET /api/public/by-type
```

返回 `dict_type` 定义的 5 类（oil/fuel/air/cabin/others），每类至多 50 个产品。

## 4. 管理端点（X-Admin-Token）

### 4.1 产品 CRUD

| 方法 | 路径 | 用途 |
|---|---|---|
| `POST` | `/api/admin/products` | 新增产品 |
| `GET` | `/api/admin/products` | 产品列表（分页） |
| `GET` | `/api/admin/products/search` | 后台搜索（含 ILike + EscapeLikePattern） |
| `POST` | `/api/admin/products/compare` | 产品对比 |
| `GET` | `/api/admin/products/{id}` | 产品详情 |
| `PUT` | `/api/admin/products/{id}` | 更新产品 |
| `DELETE` | `/api/admin/products/{id}` | 软删除（`is_discontinued = true`） |
| `POST` | `/api/admin/products/{id}/restore` | 恢复下架产品 |

**ProductFormDto 请求体**（13 字段长度校验，P2-2 修复）：

```json
{
  "oem2": "ABC-123",
  "productName1": "机油滤芯",
  "productName2": "高性能",
  "type": "oil",
  "mr1": "MR-001",
  "d1Mm": 80.0,
  "d2Mm": 65.0,
  "h1Mm": 120.0,
  "media": "纸质",
  "isPublished": true,
  "crossReferences": [
    { "oemBrand": "Toyota", "oemNo3": "90915-10004" }
  ],
  "machineApplications": [
    { "machineBrand": "Toyota", "machineModel": "Camry 2.5" }
  ]
}
```

### 4.2 产品图片

| 方法 | 路径 | 用途 |
|---|---|---|
| `POST` | `/api/admin/products/{id}/images/{slot}` | 上传图片（slot 1-6） |
| `DELETE` | `/api/admin/products/{id}/images/{slot}` | 删除图片 |
| `GET` | `/api/admin/products/{id}/images` | 查询所有图片 |
| `GET` | `/api/admin/products/{id}/history` | 变更历史 |

### 4.3 ETL 任务管理

| 方法 | 路径 | 用途 |
|---|---|---|
| `POST` | `/api/admin/etl/trigger` | 触发 ETL（拖拽 XLSX） |
| `DELETE` | `/api/admin/etl/task` | 取消 ETL 任务 |
| `POST` | `/api/admin/etl/pause` | 暂停 ETL |
| `POST` | `/api/admin/etl/resume` | 恢复 ETL |
| `GET` | `/api/admin/etl/progress` | 查询进度快照 |
| `GET` | `/api/admin/etl/progress/stream` | 实时 SSE 流（跨实例广播） |
| `GET` | `/api/admin/etl/history` | ETL 历史记录 |
| `GET` | `/api/admin/etl/history/aggregate` | 历史聚合统计 |

### 4.4 字典管理

8 个字典复用同一套 7 操作（`BaseDictService<TItem>` 抽象）：

| 字典 | 路径前缀 | xrefCount 来源 |
|---|---|---|
| OEM 品牌 | `/api/admin/dict/oem-brands` | `cross_references.oem_brand` |
| Product Name 1 | `/api/admin/dict/product-name1s` | `products.product_name_1` |
| Product Name 2 | `/api/admin/dict/product-name2s` | `products.product_name_2` |
| Type | `/api/admin/dict/types` | `products.type` |
| OEM No3 | `/api/admin/dict/oem-no3s` | `cross_references.oem_no_3` |
| Media | `/api/admin/dict/medias` | `products.media` |
| Machine | `/api/admin/dict/machines` | `machine_applications.machine_brand` |
| Engine | `/api/admin/dict/engines` | `machine_applications.engine_brand` |

**统一操作**：

| 方法 | 路径 | 用途 |
|---|---|---|
| `GET` | `/{prefix}` | 列表（keyword + includeDeleted + limit，默认 200） |
| `GET` | `/{prefix}/typeahead` | 自动补全（limit 1-50） |
| `POST` | `/{prefix}` | 新增 |
| `PUT` | `/{prefix}/{id}` | 更新 |
| `DELETE` | `/{prefix}/{id}` | 软删除（`deleted_at = now()`） |
| `POST` | `/{prefix}/{id}/restore` | 恢复 |
| `POST` | `/{prefix}/reorder` | 批量重排序 |

**字典元数据**：

```
GET /api/admin/dict/_schema
```

返回所有字典的字段定义，前端动态生成表单。

### 4.5 死信队列

| 方法 | 路径 | 用途 |
|---|---|---|
| `GET` | `/api/admin/dead-letter` | 死信列表（分页） |
| `POST` | `/api/admin/dead-letter/{id}/recover` | 手动重试单条 |
| `POST` | `/api/admin/dead-letter/recover-batch` | 批量重试 |

### 4.6 性能告警

```
GET /api/admin/perf/alerts?limit=50
```

返回最近 100 条性能告警（FIFO，按时间倒序）：

```json
[
  {
    "atUtc": "2026-07-04T16:30:00Z",
    "level": "ERROR",
    "rule": "p95_error",
    "message": "P95 = 3500ms (阈值 3000ms)",
    "p50Ms": 12.3,
    "p95Ms": 3500.0,
    "p99Ms": 4200.5,
    "maxMs": 5100.0,
    "errorRate": 0.5,
    "sampleCount": 1000
  }
]
```

### 4.7 Auth 状态

```
GET /api/admin/auth/status
```

返回当前 token 状态（用于验证轮转是否生效）：

```json
{
  "loadedFromDb": true,
  "currentKeyPrefix": "dev-admin...",
  "previousKeyPrefix": "old-token...",
  "lastRotatedAt": "2026-07-04T10:00:00Z",
  "lastRotatedBy": "ops-alice@host01"
}
```

## 5. ETL 公开端点（X-Admin-Token）

| 方法 | 路径 | 用途 |
|---|---|---|
| `POST` | `/api/etl/import` | 触发产品 ETL |
| `POST` | `/api/etl/import-xrefs` | 触发 cross_reference ETL |
| `POST` | `/api/etl/import-apps` | 触发 machine_application ETL |
| `GET` | `/api/etl/status` | 当前 ETL 任务状态 |

**ETL 三种模式**：

| 模式 | 语义 | 实现 |
|---|---|---|
| `full-load` | 全量加载 | `TRUNCATE + INSERT`（事务原子） |
| `insert-only` | 仅新增 | `ON CONFLICT DO NOTHING` |
| `upsert` | 更新或插入 | `ON CONFLICT DO UPDATE` |

> 幂等性通过 UNIQUE 索引保证。多实例并发用 PG advisory lock（`pg_try_advisory_xact_lock`，不同 ETL 类型用不同 lock key）。

## 6. 限流

| 端点类别 | 限流策略 |
|---|---|
| `/api/etl/*` | 30 req/min（`etl` 策略） |
| `/api/admin/*` | 60 req/min（`admin` 策略） |
| 公开搜索 | 120 req/min（`public` 策略） |
| `/api/perf/ingest` | 60 req/min（豁免鉴权但不豁免限流） |

**限流响应**：

```json
HTTP 429 Too Many Requests
Retry-After: 60
{
  "error": "请求过于频繁,请稍后重试"
}
```

> 限流基于 `X-Forwarded-For`（P1-3 修复：`UseForwardedHeaders` 中间件解析真实客户端 IP）。

## 7. CORS

允许的来源（开发环境）：

- `http://localhost:5173`（Vite dev server）
- `http://localhost:3000`（Node dev server）

生产环境通过环境变量 `Cors__AllowedOrigins` 配置：

```bash
export Cors__AllowedOrigins="https://sakurafilter.example.com,https://admin.sakurafilter.example.com"
```

## 8. 数据库 Schema 概览

主要表：

| 表名 | 用途 | 索引 |
|---|---|---|
| `products` | 产品主表 | `uq_products_oem_normalized` + `ix_products_oem_no_display` + `ix_products_oem_2` + `ix_products_mr_1` + 复合索引 `(type, dX_mm)` |
| `cross_references` | 交叉引用 | `(oem_brand, oem_no_3)` + `product_id` |
| `machine_applications` | 车型应用 | `(machine_brand, machine_model)` + `product_id` |
| `product_history` | 变更历史 | `(product_id, changed_at)` |
| `product_images` | 产品图片 | `uq (product_id, slot)` |
| `dict_*` | 8 个字典表 | `(sort_order, value) WHERE deleted_at IS NULL` |
| `system_settings` | 运行时配置 | PK `key` |
| `auth_token_state` | Token 状态 | PK `id=1`（单行） |
| `etl_progress_log` | ETL 日志 | `(entity_type, finished_at)` + `status` |
| `search_index_dead_letters` | 死信队列 | `(status, recovery_count, last_recovery_at) WHERE status='active'` |
| `search_index_recovered` | 死信恢复 | `(operation)` + `moved_at` |

## 9. 相关文档

- [运维手册](./ops-manual.md) - 部署/监控/告警响应
- [Token 轮转 SOP](./token-rotation-sop.md) - 零停机 token 切换
