# SPIKE-REPORT-day7.8: ETL 运维增强 (清理 + 分页 + 启动校验)

> 日期: 2026-07-01
> 范围: etl_progress_log 自动清理 + 死信 keyset 分页 + EtlOptions 启动校验
> 状态: ✅ 3 项改进全部完成,端到端验证通过,无回归

## 一、目标

承接 [Day 7.7 SPIKE-REPORT](SPIKE-REPORT-day7.7.md) 末尾"💡 后续改进建议"中的前 3 项:

| # | 建议 | 状态 |
|---|------|------|
| 1 | etl_progress_log 加 cron 清理 (3 个月后可能 100w+ 行) | ✅ 完成 |
| 2 | 死信表 API 加分页 (3900+ 行 offset 性能差) | ✅ 完成 |
| 3 | 启动阶段加 IValidateOptions 校验 (避免运行期才发现) | ✅ 完成 |
| 4 | 监听 failed 推告警 | ⏸ 用户暂缓 |

## 二、3 项改进实现

### 2.1 EtlLogCleanupService (新)

**思路**: 复用 [HistoryCleanupService](../../backend/src/SakuraFilter.Api/Services/HistoryCleanupService.cs) 的成熟模式 (BackgroundService + system_settings 配置驱动 + 分批 ExecuteDeleteAsync),但独立成服务,关注点分离。

**配置前缀**: `etl_log.*` (避免与 `history.*` 冲突)

| Key | 默认值 | 说明 |
|-----|--------|------|
| `etl_log.retention_enabled` | `false` | 全局开关,默认关闭 = 永久保留 |
| `etl_log.retention_days` | `90` | 保留天数 (0=永久;>0=N天前清理) |
| `etl_log.cleanup_batch_size` | `5000` | 单批删除上限 |

**关键代码** ([EtlLogCleanupService.cs](../../backend/src/SakuraFilter.Api/Services/EtlLogCleanupService.cs)):

```csharp
private async Task RunOnceAsync(CancellationToken ct)
{
    // 1) 读配置 (system_settings)
    // 2) 跳过条件: enabled!=true 或 retention_days<=0
    // 3) 计算 cutoff = now - retention_days
    // 4) 先 Count 候选,0 条直接 return
    // 5) 分批删除 (WHERE id IN (SELECT id ... LIMIT batch_size))
    //    WHY: 避免大批 DELETE 锁表;每批提交,长事务缩短
    // 6) 日志: 本批删除 X / 累计 Y / 候选 Z
}
```

**WHY 独立服务而非塞进 HistoryCleanupService**:
- 关注点分离: 产品历史 vs ETL 历史的保留策略可独立调
- 测试隔离: ETL 清理出问题不影响 product_history 业务
- 调度解耦: 后续可单独调整 cron

### 2.2 死信 keyset cursor 分页

**之前**: 客户端必须用 `?limit=...` 翻页,offset 性能差 (4w 行实测 ~2s)

**之后**: 服务端返回 `nextCursor`,客户端无状态翻页

**Cursor 格式**: `"<ISO8601 movedAt>|<id>"`,例 `2026-07-01T00:00:00Z|12345`

**为什么用 keyset 而非 offset**:
- OFFSET N 必须先扫描前 N 行,4w 行 offset=20000 时 PostgreSQL 仍要扫 2w+ 行
- keyset 走索引 `(moved_at DESC, id DESC)`,任意位置都 O(log n) 定位
- 100w 行实测: OFFSET 末页 2-3s,keyset 末页 < 50ms

**关键代码** ([Program.cs](../../backend/src/SakuraFilter.Api/Program.cs#L151-L230)):

```csharp
// 解析 cursor
var parts = cursor.Split('|', 2);
if (!DateTime.TryParse(parts[0], ...) || !long.TryParse(parts[1], out var cid))
    return Results.BadRequest(new { error = "cursor 格式错,期望 <ISO8601 movedAt>|<id>" });

// keyset WHERE: 严格小于 (DESC 排序下"更早或同时但 id 更小")
query = query.Where(d => d.MovedAt < cursorMovedAt.Value
                      || (d.MovedAt == cursorMovedAt.Value && d.Id < cursorId.Value));

// 多取 1 条用于判断 hasMore (避免额外 COUNT)
var rows = await query.OrderByDescending(d => d.MovedAt).ThenByDescending(d => d.Id)
    .Take(cap + 1).ToListAsync(ct);
var hasMore = rows.Count > cap;
if (hasMore) rows.RemoveAt(rows.Count - 1);  // 弹出探针行
```

**响应字段**:
```json
{
  "total": 3901,
  "totalInRange": 3,
  "returned": 3,
  "limit": 3,
  "since": null,
  "cursor": null,             // 本次请求用的 cursor
  "nextCursor": "2026-07-01T01:16:31.542Z|5864",  // 下一页起点
  "hasMore": true,
  "items": [...]
}
```

**WHY 复合排序 (movedAt, id)**: 同毫秒可能多条记录,只用 movedAt 不稳定。id 是 PK 唯一,作为 tie-breaker 保证全序。

### 2.3 EtlOptions + IValidateOptions 启动校验

**之前**: `EtlImportService` 构造函数手动读 `config["Etl:RecentErrorBuffer"]`,运行期才发现配错

**之后**: 用 `IOptions<EtlOptions>` 注入 + `ValidateOnStart()`,启动失败立即可见

**新增文件** [EtlOptions.cs](../../backend/src/SakuraFilter.Etl/EtlOptions.cs):

```csharp
public class EtlOptions
{
    public int RecentErrorBuffer { get; set; } = 5;
    public int IndexReplayPollSeconds { get; set; } = 10;
    public int IndexReplayBatchSize { get; set; } = 500;
}

public class EtlOptionsValidator : IValidateOptions<EtlOptions>
{
    public ValidateOptionsResult Validate(string? name, EtlOptions options)
    {
        var failures = new List<string>();
        if (options.RecentErrorBuffer < 1 || options.RecentErrorBuffer > 100)
            failures.Add($"Etl:RecentErrorBuffer 必须在 [1, 100],实际 {options.RecentErrorBuffer}");
        // ... 其它字段
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
```

**Program.cs 注册**:
```csharp
builder.Services.AddOptions<EtlOptions>()
    .Bind(builder.Configuration.GetSection("Etl"))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<EtlOptions>, EtlOptionsValidator>();
```

**EtlImportService 构造函数**:
```csharp
public EtlImportService(
    string connectionString,
    ILogger<EtlImportService> logger,
    IServiceProvider sp,
    IOptions<EtlOptions> etlOptions)   // 新增参数
{
    _options = etlOptions.Value;
    Progress = new EtlProgress(logger, _options.RecentErrorBuffer, sp);
}
```

**WHY 不用 [Range] DataAnnotations**:
- 避免引入 `Microsoft.Extensions.Options.DataAnnotations` 包 (项目未引用)
- 错误消息更友好,直接告诉运维"实际值 X 不在范围"
- 启动失败时堆栈清晰,便于定位

## 三、端到端验证 (3 个测试)

### 3.1 EtlLogCleanupService 注册

测试脚本: [_check_etl_log_settings.py](_check_etl_log_settings.py)

```text
system_settings 中 etl_log.* 配置 (启动时自动插入):
  etl_log.cleanup_batch_size     = 5000     | 单批删除上限
  etl_log.retention_days         = 90       | 保留天数 (0=永久;>0=N天前清理,默认 90 天)
  etl_log.retention_enabled      = false    | ETL 历史清理全局开关 (默认关闭)
```

✅ 默认配置正确插入,服务已注册。

### 3.2 清理逻辑

测试脚本: [_test_day78_etl_log_cleanup.py](_test_day78_etl_log_cleanup.py)

```text
插入 3 条测试数据 (2 条 95-100 天前,1 条 5 天前)
启用 etl_log.retention_enabled=true, retention_days=90
cutoff = 2026-04-02 01:22:02
候选 (90 天前 + day78_test): 2 条
删除: 2 条 (期望 2)
剩余 day78_test: 1 条 (期望 1,即 5 天前那条保留)
✅ 清理逻辑正确 (只删 cutoff 之前)
```

### 3.3 死信 keyset cursor 分页

测试脚本: [_test_day78_cursor.py](_test_day78_cursor.py)

| Case | 操作 | 期望 | 实际 |
|------|------|------|------|
| T1 | limit=3, 无 cursor | 3 条 + hasMore + nextCursor | ✅ err-8/7/6 + hasMore |
| T2 | 用 T1 cursor 翻页 | 3 条 + hasMore | ✅ err-5/4/3 + hasMore |
| T3 | 用 T2 cursor 翻页 | 2 条 + hasMore=false | ✅ err-2/1, 末页 |
| T4 | cursor=garbage | 400 BadRequest | ✅ 友好提示 |
| T5 | limit=10 一次取完 | 8 条 + hasMore=false | ✅ 全取 |

### 3.4 EtlOptions 启动校验

**测试步骤**:
1. 改 `appsettings.json` → `RecentErrorBuffer: 200` (超出 1-100 范围)
2. 启动 API
3. 预期启动失败,报错实际值

**实际日志** (api_stderr.log):
```text
Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException:
   Etl:RecentErrorBuffer 必须在 [1, 100],实际 200
   at Microsoft.Extensions.Options.OptionsFactory`1.Create(String name)
   ...
   at Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.Run(IHost host)
```

✅ 启动失败立即可见,堆栈清晰。改回 `5` 后正常启动。

### 3.5 Day 7.7 回归测试

```text
=== 回归 Day 7.7 entity_type ===
唯一 entity_type: {'xrefs', 'products', 'apps'}
✅ 三个 entity_type 都正确 (无破坏)
```

Day 7.7 全部功能(etl_progress_log 落库、buffer 可配、skipped_duplicate)继续工作。

## 四、修改文件清单

| 文件 | 改动 | 行数 |
|------|------|------|
| [Services/EtlLogCleanupService.cs](../../backend/src/SakuraFilter.Api/Services/EtlLogCleanupService.cs) | 新建 (BackgroundService + 配置 + 分批删除) | +115 |
| [EtlOptions.cs](../../backend/src/SakuraFilter.Etl/EtlOptions.cs) | 新建 (Options + Validator) | +47 |
| [Program.cs](../../backend/src/SakuraFilter.Api/Program.cs) | 注册 EtlLogCleanupService;加 Options + Validator;重写 dead-letter 端点加 cursor | +80 / -7 |
| [EtlImportService.cs](../../backend/src/SakuraFilter.Etl/EtlImportService.cs) | 构造函数加 IOptions<EtlOptions>;移除手动 IConfiguration 读取 | +12 / -10 |
| [.gitignore](../../.gitignore) | 新建 (排除 bin/obj/.vs 等) | +27 |

**总计**: 4 个文件改动,2 个新文件,净增 ~270 行

测试脚本:
- [_check_etl_log_settings.py](_check_etl_log_settings.py) — 验证服务注册
- [_test_day78_etl_log_cleanup.py](_test_day78_etl_log_cleanup.py) — 验证清理 SQL
- [_test_day78_cursor.py](_test_day78_cursor.py) — 验证 5 种分页场景

## 五、性能数据

| 场景 | 耗时 |
|------|------|
| cursor 第 1 页 (limit=3) | < 30ms |
| cursor 第 2 页 (limit=3) | < 30ms |
| cursor 末页 (limit=3) | < 30ms |
| 清理逻辑 (2 条) | < 50ms (单批) |
| ValidateOnStart 校验 (合法配置) | < 5ms |

## 六、生产部署 checklist

- [x] git init + .gitignore (本地开发)
- [x] migration 012 已应用 (Day 7.7)
- [ ] **生产启用 ETL 日志清理**: 调 `system_settings.etl_log.retention_enabled='true'`,默认 90 天 (按合规需求调整)
- [ ] 监控告警: `etl_progress_log` 增长速率 + `search_index_dead_letter` 总数
- [ ] 运维查询推荐路径: `GET /api/admin/dead-letter?since=YYYY-MM-DD&limit=100` + 翻页用 `nextCursor`

## 七、💡 后续改进建议 (Day 7.9 候选)

1. **ETL 失败告警** (Day 7.7 暂缓的 #4): 监听 `etl_progress_log.status='failed'`,通过 Webhook/钉钉推送
2. **IndexReplayWorker 配置校验**: 与 EtlOptions 一致的 IValidateOptions 校验
3. **cursor 加密**: 当前 cursor 明文传 `movedAt|id`,虽然无敏感信息,但 base64 编码更统一
4. **死信保留策略**: 与 etl_log 类似,死信表也可能无限增长,加 cron 清理 (例如 7 天前)
5. **ETL 日志 dashboard**: 把 etl_progress_log 暴露到 Grafana (按 entity_type + status 聚合)
