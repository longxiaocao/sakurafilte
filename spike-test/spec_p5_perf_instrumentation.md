# P5.5 前后端性能埋点 — 验收规格

> 目标: 给 1M 产品 / 5-20M 交叉引用场景提供可观测性, 定位慢请求/慢端点
> 日期: 2026-07-03
> 优先级: 高 (无监控的优化是盲人摸象)

---

## 1. 背景

### 1.1 现状
- 后端无统一 ResponseTime 埋点 (仅个别 Service 内部 log)
- 前端无 API 耗时统计, 用户反馈"卡"只能靠猜
- 健康检查只有 `/` (返回 running) 和 `/api/search/health`, 无完整健康指标
- 日志全文本格式 (`ILogger<T>`, 无结构化字段)
- 慢请求排查靠 log 抓 `Duration` 字段, 无 P50/P95/P99 概览

### 1.2 目标
- 后端: ResponseTimeMiddleware 自动记录每请求耗时 → 输出 P50/P95/P99 到 `/api/admin/metrics/response-time`
- 前端: axios/fetch 拦截器统计请求耗时 → 批量上报后端 `/api/admin/metrics/frontend-perf`
- 健康检查分级: `/health/live` (liveness) + `/health/ready` (readiness, 含 DB/Meili 状态)
- 告警阈值: 后端 P95 > 1000ms 或前端页面加载 > 3000ms 时输出 warning 日志

---

## 2. 架构决策

### 2.1 后端埋点范围
- **所有端点** (含 /api/*, /scalar, /openapi), **排除**:
  - `/health/live` (健康检查自身, 否则被自己拖慢统计)
  - `/api/admin/metrics/*` (指标端点自身, 避免递归)
  - OPTIONS 预检请求 (CORS preflight)
- 统计维度: path template (`/api/admin/products/{id:long}` 而非具体 URL) + HTTP method
- 存储: 进程内 ring buffer (最近 1000 条), 内存零负担
- 输出: `/api/admin/metrics/response-time` 返回 P50/P95/P99 + count + 慢请求 top 10

### 2.2 前端批量上报策略
- **不逐条发请求** (反噬性能) — 收集到 10 条 或 30s 触发一次
- 失败重试: 上报失败放 localStorage, 下次启动重试
- 字段: pageUrl, apiPath, durationMs, statusCode, timestamp, userAgent
- 后端存 PG: `frontend_perf_log` 表, 7 天 TTL (后台服务清理)

### 2.3 健康检查分级
- `/health/live` → 200 永远 (liveness, K8s livenessProbe 用)
- `/health/ready` → 200/503 (readiness, K8s readinessProbe 用, 检查 PG + Meili)
- `/api/search/health` → 保留 (Day 4 已有, 给前端监控用)
- `/api/admin/metrics/response-time` → 新增 (本任务核心)

### 2.4 决策记录
| 备选方案 | 选择 | 理由 |
|---|---|---|
| Prometheus + Grafana | **不引入** | MVP 不需要, 进程内 ring buffer 够用; 后续 P8 监控统一规划 |
| OpenTelemetry | **不引入** | NuGet 包大 (~5MB); 现有 ILogger 够用 |
| Serilog JSON 输出 | **不引入** | NuGet 包大; MVP 阶段纯文本 OK; 后续 P8 监控统一规划 |
| 前端逐条上报 | **批量** | 1M 用户 × 每秒 1 请求 = 100 万 QPS 上报, 任何存储都炸; 批量降 100x |
| 前端 Web Vitals 集成 | **仅 API 耗时** | FCP/LCP 等浏览器 API 后续 P5.6 单独做, 本任务只关注 API |

---

## 3. 接口设计

### 3.1 后端: ResponseTimeMiddleware
```csharp
// backend/src/SakuraFilter.Api/Middleware/ResponseTimeMiddleware.cs
public class ResponseTimeMiddleware
{
    // 排除路径
    private static readonly HashSet<string> ExcludePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health/live",
        "/api/admin/metrics/response-time",
        "/api/admin/metrics/frontend-perf",
        "/api/search/health"
    };

    public async Task InvokeAsync(HttpContext ctx, IResponseTimeMetrics metrics)
    {
        var path = ctx.Request.Path.Value ?? "/";
        if (ExcludePaths.Contains(path) || HttpMethods.IsOptions(ctx.Request.Method))
        {
            await _next(ctx);
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            await _next(ctx);
        }
        finally
        {
            sw.Stop();
            var pathTemplate = ctx.GetEndpoint()?.DisplayName ?? path;
            metrics.Record(ctx.Request.Method, pathTemplate, sw.ElapsedMilliseconds, ctx.Response.StatusCode);
            ctx.Response.Headers["X-Response-Time-Ms"] = sw.ElapsedMilliseconds.ToString();
        }
    }
}
```

### 3.2 后端: IResponseTimeMetrics + RingBuffer 实现
```csharp
// backend/src/SakuraFilter.Api/Services/ResponseTimeMetrics.cs
public interface IResponseTimeMetrics
{
    void Record(string method, string pathTemplate, long durationMs, int statusCode);
    ResponseTimeSnapshot GetSnapshot();
}

public record ResponseTimeRecord(
    DateTime At, string Method, string PathTemplate, long DurationMs, int StatusCode);

public class ResponseTimeMetrics : IResponseTimeMetrics
{
    private readonly ConcurrentQueue<ResponseTimeRecord> _buffer = new();
    private const int Capacity = 1000;

    public void Record(string method, string pathTemplate, long durationMs, int statusCode)
    {
        _buffer.Enqueue(new ResponseTimeRecord(DateTime.UtcNow, method, pathTemplate, durationMs, statusCode));
        while (_buffer.Count > Capacity && _buffer.TryDequeue(out _)) { /* 丢最老 */ }
    }

    public ResponseTimeSnapshot GetSnapshot()
    {
        var list = _buffer.ToArray();
        return new ResponseTimeSnapshot(
            TotalCount: list.Length,
            P50Ms: Percentile(list, 50),
            P95Ms: Percentile(list, 95),
            P99Ms: Percentile(list, 99),
            Slowest10: list.OrderByDescending(r => r.DurationMs).Take(10).ToList()
        );
    }
}
```

### 3.3 后端: 指标端点
```csharp
// GET /api/admin/metrics/response-time
// 返回:
{
  "totalCount": 842,
  "p50Ms": 23,
  "p95Ms": 187,
  "p99Ms": 456,
  "slowest": [
    { "method": "POST", "path": "POST /api/admin/products/search", "durationMs": 892, "statusCode": 200, "at": "2026-07-03T..." },
    ...
  ]
}
```

### 3.4 前端: PerfInterceptor
```typescript
// frontend/src/utils/perf.ts
type PerfRecord = { path: string; durationMs: number; status: number; at: number };
const buffer: PerfRecord[] = [];
let flushTimer: number | null = null;

export function recordPerf(path: string, durationMs: number, status: number) {
  buffer.push({ path, durationMs, status, at: Date.now() });
  if (buffer.length >= 10) flushPerf();
  else if (!flushTimer) flushTimer = window.setTimeout(flushPerf, 30_000);
}

function flushPerf() {
  if (flushTimer) { clearTimeout(flushTimer); flushTimer = null; }
  if (buffer.length === 0) return;
  const batch = buffer.splice(0, buffer.length);
  navigator.sendBeacon('/api/admin/metrics/frontend-perf', JSON.stringify(batch));
}
```

### 3.5 健康检查端点
```csharp
// GET /health/live → 200 { "status": "alive" } (永远 200, K8s livenessProbe)
// GET /health/ready → 200/503
//   200: { "status": "ready", "checks": { "postgres": "ok", "meili": "ok" } }
//   503: { "status": "not_ready", "checks": { "postgres": "ok", "meili": "down" } }
```

---

## 4. 实施清单 (P5.5.1 → P5.5.5)

### P5.5.1 后端 ResponseTimeMiddleware + Metrics (3 文件)
- `backend/src/SakuraFilter.Api/Middleware/ResponseTimeMiddleware.cs` (新)
- `backend/src/SakuraFilter.Api/Services/ResponseTimeMetrics.cs` (新, 含 IResponseTimeMetrics + 实现 + Snapshot)
- `backend/src/SakuraFilter.Api/Program.cs` (改) — 注册 middleware + DI

### P5.5.2 后端指标端点 (1 文件, 增量)
- `backend/src/SakuraFilter.Api/Program.cs` (改) — 加 `/api/admin/metrics/response-time` 和 `/api/admin/metrics/frontend-perf`
- `backend/src/SakuraFilter.Infrastructure/Data/ProductDbContext.cs` (改) — 加 `FrontendPerfLog` entity
- `backend/src/SakuraFilter.Infrastructure/Data/Migrations/<timestamp>_AddFrontendPerfLog.cs` (新 migration)

### P5.5.3 健康检查端点 (1 文件, 增量)
- `backend/src/SakuraFilter.Api/Program.cs` (改) — 加 `/health/live` 和 `/health/ready`
- **排除 DevTokenAuthMiddleware** (健康检查要免鉴权, 加到 ExemptPaths)

### P5.5.4 前端 PerfInterceptor (2 文件)
- `frontend/src/utils/perf.ts` (新)
- `frontend/src/utils/api.ts` (改) — 在 fetch 拦截器里调用 recordPerf

### P5.5.5 后台 perf log 清理服务 (1 文件, 新)
- `backend/src/SakuraFilter.Api/Services/FrontendPerfLogCleanupService.cs` (新 IHostedService)
- 默认 7 天 TTL, 可配置 (与现有 EtlLogCleanupService 风格一致)

### P5.5.6 E2E 验证脚本 (1 文件)
- `spike-test/_test_p55_perf.py`
  - 场景 1: 触发 100 个 GET /api/search, 验证 /api/admin/metrics/response-time P50 存在
  - 场景 2: 验证 /health/live 返回 200
  - 场景 3: 验证 /health/ready 返回 200/503 (PG/Meili 状态)
  - 场景 4: 前端 perf 上报 (模拟 10 条 batch)
  - 场景 5: 慢请求 warning 日志验证 (P95 > 1000ms)

---

## 5. 验收标准

### 5.1 功能验收 (E2E 5 场景全过)
- [ ] 触发 100 请求后, 指标端点返回 totalCount >= 100
- [ ] P50/P95/P99 数值非 null, P50 < P95 < P99 (单峰分布)
- [ ] /health/live 永远 200
- [ ] /health/ready 200 (PG OK) 或 503 (PG 停)
- [ ] 前端 10 条 batch 上报, 后端 PG 收到 10 条记录

### 5.2 性能验收
- [ ] ResponseTimeMiddleware 自身开销 < 1ms (Stopwatch 启停)
- [ ] Ring buffer 1000 条容量下, 内存 < 200KB
- [ ] 指标端点响应 < 50ms (纯内存读)
- [ ] 前端 flushPerf 在页面 unload 时 100% 触发 (用 sendBeacon 保活)

### 5.3 兼容性验收
- [ ] 现有 _test_p5_polish.py 11 测试全过 (不改 P5 既有功能)
- [ ] 现有 _bench_search.py 跑出 P95 < 3000ms 阈值
- [ ] 不引入新 NuGet 依赖 (只用 Stopwatch + ConcurrentQueue + IHostedService)

### 5.4 健壮性验收
- [ ] 指标端点被攻击时不挂: 60s 1000 次请求仍能 200 返回
- [ ] 前端上报失败不阻塞页面 (sendBeacon 失败静默)
- [ ] PG 停时 /health/ready 503 不挂 (有 try/catch)

---

## 6. 不在范围内 (Out of Scope)
- OpenTelemetry / Prometheus 集成 — 留给 P8 监控告警
- 慢请求自动告警 (webhook) — 留给 P8.3
- 前端 Web Vitals (FCP/LCP/CLS) — 留给 P5.6
- 分布式 tracing (TraceId 串联) — 留给 P8.4
- Meili 索引健康细分 — 当前 /api/search/health 够用

---

## 7. 风险评估

| 风险 | 概率 | 影响 | 缓解 |
|---|---|---|---|
| Ring buffer 频繁 Dequeue 性能差 | 低 | 低 | Capacity=1000 + ConcurrentQueue, 实测 1M 操作 < 100ms |
| 前端 sendBeacon 浏览器兼容 | 低 | 低 | 主流浏览器都支持, 失败 fallback 丢弃 |
| /health/ready 把 DB 探活搞挂 | 中 | 高 | 设 1s 超时, 用现有连接池, 不创建新连接 |
| 慢请求 warning 误报 | 中 | 低 | 阈值 1000ms 偏宽松, 调低到 500ms 留后续 P5.5.7 |
| 前端 perf 表无限增长 | 低 | 中 | IHostedService 7 天 TTL 清理 + size 限制 |

---

## 8. 依赖关系

- **前置**: CI 拆分 3 job 全绿 (CI run #47 验证中)
- **后置**: P5.6 (前端 Web Vitals) / P8 (结构化监控告警)
- **并行**: P7.1 (X-Admin-Token 轮转 CLI) — 无依赖

---

## 9. 时间估算

- P5.5.1 后端 middleware + metrics: 1.5h
- P5.5.2 指标端点 + migration: 1h
- P5.5.3 健康检查端点: 0.5h
- P5.5.4 前端 interceptor: 1h
- P5.5.5 perf log 清理服务: 0.5h
- P5.5.6 E2E 验证: 1.5h
- **总计**: ~6h (1 个工作日)

---

## 10. 参考实现

- `backend/src/SakuraFilter.Api/Middleware/` (目录, 当前为空, 新建)
- `backend/src/SakuraFilter.Api/Services/EtlLogCleanupService.cs` (IHostedService 清理模式参考)
- `backend/src/SakuraFilter.Api/Controllers/PublicSearchController.cs` (现 search 端点, 路径模板来源)
- `frontend/src/utils/api.ts` (现 fetch 拦截器位置)
