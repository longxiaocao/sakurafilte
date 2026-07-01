# Day 8.4 后端补完 — 8 项集成测试报告

> 日期: 2026-07-01
> 范围: 后台鉴权 / 错误统一 / 限流 / CORS / API 文档 / OpenAPI / 历史 API / ETL 触发

## 1. 改动概览

| 模块 | 改动 | 状态 |
| --- | --- | --- |
| 鉴权 | `DevTokenAuthMiddleware` (X-Admin-Token) + 白名单 | ✅ |
| 错误统一 | `ProblemDetailsFactory` (RFC 7807) | ✅ |
| 限流 | `.NET 8 RateLimiter` FixedWindow, 3 分区 (global/search/etl) | ✅ |
| CORS | 白名单 5173/5174/3000 | ✅ |
| API 文档 | Swashbuckle Swagger (Scalar 备选) | ✅ |
| OpenAPI | `/swagger/v1/swagger.json` + X-Admin-Token security scheme | ✅ |
| 历史 API | `GET /api/admin/products/{id}/history` | ✅ |
| ETL 触发 | `POST /api/admin/etl/trigger` + `GET /api/admin/etl/progress` | ✅ |

## 2. 关键设计决策

### 2.1 中间件顺序
```
1. UseDeveloperExceptionPage   开发期显示堆栈
2. UseCors                     CORS 必须在鉴权前 (preflight 不需要 token)
3. UseRateLimiter              限流必须在鉴权前 (防匿名 DoS)
4. DevTokenAuthMiddleware       /api/admin + /api/etl 强制 X-Admin-Token
5. UseSwagger                  /swagger + /swagger/v1/swagger.json
```

### 2.2 限流参数 (appsettings.json `RateLimit`)
| 分区 | 限制 | 用途 |
| --- | --- | --- |
| global | 600/分钟 | 后台 CRUD 默认 |
| search | 300/分钟 | 前台搜索防爬虫 |
| etl | 30/分钟 | ETL 触发防误操作 |

### 2.3 鉴权白名单
- `/` 健康检查
- `/api/search` 前台搜索
- `/api/search/health` 搜索健康检查
- `/api/products/{oem}` 前台详情
- `/swagger` API 文档
- `/openapi` OpenAPI 文档

### 2.4 ProblemDetails 错误映射
| 异常类型 | HTTP |
| --- | --- |
| `ArgumentException` | 400 Bad Request |
| `KeyNotFoundException` | 404 Not Found |
| `InvalidOperationException` | 409 Conflict |
| `UnauthorizedAccessException` | 403 Forbidden |
| `OperationCanceledException` | 499 Client Closed |
| 其他 | 500 Internal Server Error |

## 3. 新增端点

### 3.1 `GET /api/admin/products/{id}/history`
- 查询产品变更历史, 倒序
- 参数: `limit` (1-500, 默认 50)
- 鉴权: X-Admin-Token
- 限流: global

### 3.2 `POST /api/admin/etl/trigger`
- 手动触发 ETL
- Body: `{ jsonlPath, mode, dryRun }`
- mode: `full-load` | `insert-only` | `upsert`
- dryRun: true 时只校验不写库
- 鉴权: X-Admin-Token
- 限流: etl (30/分钟)

### 3.3 `GET /api/admin/etl/progress`
- 查询当前 ETL 任务 + 进度
- 鉴权: X-Admin-Token
- 限流: etl (30/分钟)

## 4. 测试结果

### 4.1 `_test_day84_integration.py` (全部 8 项)

```
[1] 鉴权 / 中间件           5/5 ✓
  - 无 token → 401 + ProblemDetails
  - 错误 token → 401
  - 正确 token → 200
  - /api/search 白名单免鉴权
  - /api/search/health 白名单免鉴权

[2] ProblemDetails 错误统一  2/2 ✓
  - 404 (产品 id=999999999 不存在)
  - 400 (cursor 篡改)

[3] API Rate Limiting       2/2 ✓
  - 35 次并发 ETL dry-run: ok=30 limited=5
  - 429 含 Retry-After=60 头

[4] CORS 5173/5174 白名单    3/3 ✓
  - 5173 → ACAO=http://localhost:5173
  - 5174 → ACAO=http://localhost:5174
  - evil.com → 拒绝 (CORS 阻止)

[5] API 文档 (Swagger)      2/2 ✓
  - swagger.json 200, paths=21 个
  - swagger UI 200

[6] OpenAPI Schema 导出     5/5 ✓
  - admin/etl 端点: 17 个
  - securityScheme X-Admin-Token
  - 3 个新端点都在

[7] 产品历史查询 API        4/4 ✓
  - 存在产品: total/items 返回
  - 不存在产品: 404 + ProblemDetails
  - limit 边界

[8] 后台手动 ETL 触发 + 进度  4/4 ✓
  - dryRun 返回 lines/sizeBytes
  - 进度查询
  - 文件不存在 → 404
  - 真实 ETL 触发 (50 行 JSONL) → status=completed
```

## 5. Bug 复盘

### 5.1 cursor 验证只走 cursor 模式
- **症状**: 测试 `cursor=invalid|format` (无 pagingMode) 返回 200
- **根因**: `NormalizePagingMode()` 默认 "offset", 旧测试漏了 pagingMode=cursor
- **修复**: 测试加 `pagingMode=cursor` 参数; cursor 解析在 cursor 模式下才生效

### 5.2 ProductHistory DTO 接受 null
- **症状**: 编译警告 `ChangedFields` 可能 null
- **修复**: DTO 改为 `string? ChangedFields`

### 5.3 端口 5000 被旧服务器占用
- **症状**: 5xx/500 反复, dev 页面写不出来
- **根因**: 之前 `dotnet run` 启动的服务器进程 4820 还在 5000 端口监听, 新代码没生效
- **修复**: `Stop-Process -Id 4820 -Force` 后重启

### 5.4 限流测试用大文件 dryRun 拖时间
- **症状**: 35 次串行 70s 超过限流窗口 60s, 全 200
- **修复**: 用空文件 + 并发 10 线程, 立即打满 30 个配额

### 5.5 限流窗口撑满后 section 8 立刻 429
- **症状**: section 3 用完 30 配额, section 8 立刻被限流
- **修复**: section 8 前 `time.sleep(65)` 等窗口重置

## 6. 部署 checklist

- [x] 编译: 0 错误 12 警告
- [x] 集成测试: 8/8 通过
- [x] appsettings.json: RateLimit/Auth 配置完整
- [ ] 生产部署: `Auth:DevStaticToken` 必须用环境变量覆盖
- [ ] 生产部署: `Search:CursorHmacKey` 同上
- [ ] 生产部署: 限流按用户/IP 区分 (当前按全局 RemoteIpAddress)

## 7. 改进建议

1. **限流按用户区分**: 当前按 IP 分区, 共享 IP (公司 NAT) 容易误伤. 建议解析 token 获取 user_id 做 partition key
2. **OpenAPI TypeScript 生成**: 前端用 `openapi-typescript-codegen` 或 `orval` 自动生成 TS 类型, 避免手写 DTO
3. **History API 限流**: 当前走 global (600/分钟), 可单独加 "history" 分区 (120/分钟) 防止恶意刷历史
4. **ETL 进度 SSE 推送**: 当前轮询 3s, 可升级为 Server-Sent Events 减少空轮询
5. **/api/admin/etl/trigger 幂等性**: 同一文件多次触发会产生重复数据, 可加请求签名或 dedup key
