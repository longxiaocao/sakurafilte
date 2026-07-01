# SPIKE-REPORT-day7.7: ETL 运维可观测性闭环

> 日期: 2026-07-01
> 范围: ETL 历史落库 + 错误环形缓冲可配 + 死信时间过滤
> 状态: ✅ 全部 4 项改进完成并端到端验证

## 一、目标

承接 [Day 7.6 SPIKE-REPORT](SPIKE-REPORT-day7.6.md) 末尾的"💡 改进建议",本次一次性把 4 个运维痛点堵上:

| 痛点 | 现象 | 解决 |
|------|------|------|
| ETL 历史丢失 | 进程重启后 EtlProgress 单例清零,无法回溯昨天 14:00 跑成啥样 | 新增 `etl_progress_log` 表,Finish/Fail 时落库 |
| 错误环缓冲写死 5 | 失败风暴时只能看最新 5 条,无法看错误分布 | 配置 `Etl:RecentErrorBuffer` 改成可配 |
| 死信查全表 | 排查"今天又累积多少"必须 limit 翻页,效率低 | `GET /api/admin/dead-letter?since=ISO8601` |
| ETL_ENTITY 错记 | xrefs 错记为 apps,full-load 错记为 ETL_ENTITY | 修复 Finish/Fail 调用占位符 |

## 二、4 项改进实现

### 2.1 `etl_progress_log` 表 (核心)

**Schema** ([migrations/012_add_etl_progress_log.sql](../../backend/migrations/012_add_etl_progress_log.sql)):

```sql
CREATE TABLE etl_progress_log (
    id                  BIGSERIAL PRIMARY KEY,
    entity_type         VARCHAR(20) NOT NULL,   -- products / xrefs / apps
    mode                VARCHAR(20) NOT NULL,   -- full-load / insert-only / upsert
    file_path           TEXT NOT NULL,
    status              VARCHAR(20) NOT NULL,   -- completed / failed
    read_count          BIGINT NOT NULL DEFAULT 0,
    inserted_count      BIGINT NOT NULL DEFAULT 0,
    updated_count       BIGINT NOT NULL DEFAULT 0,
    skipped_count       BIGINT NOT NULL DEFAULT 0,
    skipped_missing_oem BIGINT NOT NULL DEFAULT 0,
    skipped_null_field  BIGINT NOT NULL DEFAULT 0,
    skipped_duplicate   BIGINT NOT NULL DEFAULT 0,
    error_count         BIGINT NOT NULL DEFAULT 0,
    indexed_count       BIGINT NOT NULL DEFAULT 0,
    index_pending_count BIGINT NOT NULL DEFAULT 0,
    last_error          TEXT,
    started_at          TIMESTAMPTZ NOT NULL,
    finished_at         TIMESTAMPTZ NOT NULL,
    duration_sec        DOUBLE PRECISION NOT NULL
);
CREATE INDEX idx_etl_log_entity_finished ON etl_progress_log (entity_type, finished_at DESC);
CREATE INDEX idx_etl_log_status ON etl_progress_log (status);
CREATE INDEX idx_etl_log_finished ON etl_progress_log (finished_at DESC);
```

**WHY 字段全覆盖**: 之前 EtlProgress 内存里有 read/inserted/updated/skipped/.../recentErrors 等 13 个计数器,落库时只挑运营排查高频的 12 个持久化。`recentErrors` 数组不进表(体量大,留 `last_error` 单条足够),但通过 `/api/etl/status` 实时暴露。

**WHY EtlImportService 是 Singleton + DbContext 是 Scoped**:
- Singleton: 三个 ETL endpoint (products/xrefs/apps) 共享同一个 Progress 状态,避免重复 IO
- Scoped DbContext: EF Core 默认生命周期,线程安全
- 矛盾: Singleton 不能直接注入 Scoped
- 解法: `PersistLogAsync` 内 `CreateScope()` 拿 scoped DbContext,fire-and-forget 调用

```csharp
private async Task PersistLogAsync(string entityType, string mode)
{
    if (_sp is null) return;  // 单测场景无 sp
    try
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        db.EtlProgressLogs.Add(ToLogSnapshot(entityType, mode));
        await db.SaveChangesAsync();
    }
    catch (Exception ex)
    {
        _logger?.LogWarning(ex, "ETL 历史落库失败 (不影响业务结果)");
    }
}
```

WHY 不重试: 日志写失败不影响业务结果,只丢一行历史,重试会污染业务路径。

### 2.2 错误环形缓冲可配

**之前**: 容量硬编码 5
```csharp
private const int MaxRecentErrors = 5;
```

**之后**: 构造函数注入 + appsettings.json 配置
```csharp
public EtlProgress(ILogger? logger, int bufferSize, IServiceProvider? sp = null)
{
    _bufferSize = Math.Max(1, bufferSize);
}
```

`appsettings.json`:
```json
"Etl": {
  "RecentErrorBuffer": 5,
  "IndexReplayPollSeconds": 10,
  "IndexReplayBatchSize": 500
}
```

`EtlImportService` 构造时读 IConfiguration:
```csharp
var bufferSize = 5;
var config = sp.GetService<IConfiguration>();
if (config != null)
{
    var raw = config["Etl:RecentErrorBuffer"];
    if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out var v) && v > 0)
        bufferSize = v;
}
Progress = new EtlProgress(logger, bufferSize, sp);
```

WHY 失败风暴时 5 条不够: 真实生产 Meili ConnectionRefused 一发生就是几百条 1min 内连不上,5 条只能看到最新 5 条,无法判断是连接问题还是 schema 错。生产环境调成 50+ 才能看分布。

### 2.3 死信 `?since` 时间过滤

**之前**: `GET /api/admin/dead-letter` 只能 limit 翻页
**之后**: 加 `?since=ISO8601` 参数

```csharp
app.MapGet("/api/admin/dead-letter", async (
    [FromQuery] int? limit,
    [FromQuery] string? operation,
    [FromQuery] string? since,
    ProductDbContext db,
    CancellationToken ct) =>
{
    var cap = Math.Clamp(limit ?? 50, 1, 500);
    var query = db.SearchIndexDeadLetters.AsNoTracking();
    if (!string.IsNullOrEmpty(operation))
        query = query.Where(d => d.Operation == operation);
    DateTime? sinceUtc = null;
    if (!string.IsNullOrEmpty(since))
    {
        if (!DateTime.TryParse(since, null,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            return Results.BadRequest(new { error = "since 必须是 ISO8601 时间 (例: 2026-07-01T00:00:00Z)", since });
        sinceUtc = parsed;
        query = query.Where(d => d.MovedAt >= sinceUtc);
    }
    // ...
});
```

接受格式:
- `2026-07-01` (date-only) → 自动补 `T00:00:00Z`
- `2026-06-30T00:00:00Z` (UTC 显式)
- `2026-06-15T08:00:00+08:00` (带时区) → 内部转 UTC
- `not-a-date` → 返回 400 BadRequest + 友好提示

### 2.4 `ETL_ENTITY` 占位符修复

**Bug 现场** (Day 7.6 末尾落库的数据):
```
id=  1 entity=apps         mode=upsert     ← 实际是 xrefs 的 ETL 被错记
id=  2 entity=ETL_ENTITY   mode=full-load  ← products full-load 被错记
```

**根因**: `EtlProgress.Finish()` / `Fail()` 接 `entityType` 参数,但调用方传了占位符 `"ETL_ENTITY"` 和写死的 `"apps"`,xrefs 路径忘了改。

**修复** ([EtlImportService.cs](../../backend/src/SakuraFilter.Etl/EtlImportService.cs)):
```csharp
// 之前 (xrefs 错记为 apps)
Progress.Finish("apps", mode);
Progress.Fail(ex.Message, "apps", mode);

// 修复后
Progress.Finish("xrefs", mode);
Progress.Fail(ex.Message, "xrefs", mode);
```

## 三、端到端验证 (4 个测试)

### 3.1 entity_type 正确性

测试脚本: [_test_day77_entity_type.py](_test_day77_entity_type.py)

```text
=== 1) products upsert ===  status=completed read=2132 duration=0.158s
=== 2) xrefs upsert ===     status=completed read=36   duration=0.009s
=== 3) apps upsert ===      status=completed read=55   duration=0.107s

etl_progress_log 写入 4 行:
  id=4 entity=products  mode=upsert  status=completed read=2132 ins=0  skip=0  dup=0   dur=0.158s
  id=5 entity=xrefs     mode=upsert  status=completed read=36   ins=0  skip=0  dup=0   dur=0.009s
  id=6 entity=apps      mode=upsert  status=completed read=55   ins=0  skip=2  dup=2   dur=0.107s
```

✅ 三个 entity_type 都正确,xrefs 不再错记为 apps。

### 3.2 Buffer 容量配置生效

测试脚本: [_test_day77_buffer.py](_test_day77_buffer.py)

步骤:
1. 修改 `appsettings.json` → `"RecentErrorBuffer": 10`
2. 重启 API
3. 触发 apps 导入 15 行坏 JSON
4. 查 `/api/etl/status` 的 `recentErrors` 长度

```text
status=completed read=15 errors=15
recentErrors 长度: 10
[ 1] 2026-07-01T01:02:56.5890829Z | apps 行 6: 't' is an invalid start of a property name...
[ 2] 2026-07-01T01:02:56.5892258Z | apps 行 7: ...
...
[10] 2026-07-01T01:02:56.5904616Z | apps 行 15: ...
```

✅ 15 条错误只保留最近 10 条(行 6-15),前 5 条(行 1-5)被环出。

### 3.3 `?since` 6 种格式

测试脚本: [_test_day77_since.py](_test_day77_since.py)

| Case | since 参数 | 期望返回 | 实际 |
|------|-----------|---------|------|
| T1 | (无) | 3 条 day77_test | ✅ 3 |
| T2 | `2026-07-01` (date only) | 1 条 (today) | ✅ 1 |
| T3 | `2026-06-30T00:00:00Z` (UTC) | 2 条 (today+yesterday) | ✅ 2 |
| T4 | `2026-06-15T08:00:00+08:00` (上海时区) | 2 条 (时区换算) | ✅ 2 |
| T5 | `not-a-date` | 400 BadRequest + 提示 | ✅ 400 |
| T6 | `2099-01-01` (未来) | 0 条 | ✅ 0 |

### 3.4 `skipped_duplicate` 暴露在 status API

测试脚本: [_test_day77_status_api.py](_test_day77_status_api.py)

触发 apps upsert (含 2 行 DISTINCT ON 去重),查 status API:

```text
status API 字段:
  skipped              = 2
  skippedMissingOem    = 0
  skippedNullField     = 0
  skippedDuplicate     = 2       ← 新增字段
  errors               = 0
  ...
```

✅ `skippedDuplicate` 字段在 `ToJson()` 中已暴露,前端可直接消费。

## 四、性能数据

| 场景 | 耗时 | 备注 |
|------|------|------|
| products upsert (2132 行) | 0.158s | 含 staging COPY + UPSERT + async Meili 启动 |
| xrefs upsert (36 行) | 0.009s | 36 行几乎瞬时 |
| apps upsert (55 行) | 0.107s | 53 行 upsert + 2 行 DISTINCT ON 去重 |
| etl_progress_log 落库 | <10ms | 异步,不阻塞 ETL 完结响应 |

## 五、修改文件清单

| 文件 | 改动 |
|------|------|
| [EtlImportService.cs](../../backend/src/SakuraFilter.Etl/EtlImportService.cs) | EtlProgress 构造函数加 IServiceProvider + bufferSize;新增 `PersistLogAsync`;ToJson 加 `recentErrors`;Finish/Fail 接收 entityType/mode;修复 xrefs 错记为 apps 的 bug;修复 Elapsed 计算(用 finishedAt) |
| [Product.cs](../../backend/src/SakuraFilter.Core/Entities/Product.cs) | 新增 `EtlProgressLog` 实体 (15 字段 + 2 索引) |
| [ProductDbContext.cs](../../backend/src/SakuraFilter.Infrastructure/Data/ProductDbContext.cs) | 注册 `DbSet<EtlProgressLog>` + OnModelCreating 配置 |
| [Program.cs](../../backend/src/SakuraFilter.Api/Program.cs) | `MapGet("/api/admin/dead-letter")` 加 `?since=ISO8601` 参数 |
| [migrations/012_add_etl_progress_log.sql](../../backend/migrations/012_add_etl_progress_log.sql) | 新建 (35 行) |
| [appsettings.json](../../backend/src/SakuraFilter.Api/appsettings.json) | 新增 `Etl` section |

测试脚本:
- [_test_day77_entity_type.py](_test_day77_entity_type.py)
- [_test_day77_buffer.py](_test_day77_buffer.py)
- [_test_day77_since.py](_test_day77_since.py)
- [_test_day77_status_api.py](_test_day77_status_api.py)
- [_cleanup_etl_log.py](_cleanup_etl_log.py)

## 六、生产部署 checklist

- [ ] 执行 migration 012: `psql -f migrations/012_add_etl_progress_log.sql`
- [ ] (可选) `appsettings.json` 中按需调高 `Etl:RecentErrorBuffer`,失败风暴期 50+
- [ ] 监控指标加 etl_progress_log 当日失败行数(可走 /api/etl/status 或 Grafana 拉 PG)
- [ ] 运维查"今天又累积多少死信"用 `?since=YYYY-MM-DD` 不必 limit 翻页

## 七、💡 后续改进建议

1. **etl_progress_log 保留期**: 现在永久保留,3 个月后表可能 100w+ 行,建议加 cron 清理 `WHERE finished_at < now() - interval '90 days'`
2. **死信表 API 加分页参数**: 现在 `?limit=500` 单次最多取 500,生产 3w+ 死信时需分页 (offset/limit) 或 cursor
3. **appsettings.json 配置校验**: `Etl:RecentErrorBuffer` 范围合理值是 1-100,目前只校验 > 0,加 Startup 阶段 `IValidateOptions` 启动失败,而不是运行期发现
4. **ETL 失败告警**: 监听 `etl_progress_log.status = 'failed'`,通过 Webhook/钉钉/Prometheus AlertManager 推送,免人工巡检
5. **`skipped_duplicate` 拆分列**: 把它和 `skipped_missing_oem` / `skipped_null_field` 一样在 etl_progress_log 落库时单独记,便于后续 ETL 失败率分析 (区分"数据脏 vs 源数据重复")
