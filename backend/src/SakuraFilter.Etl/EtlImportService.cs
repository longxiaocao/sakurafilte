using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;
using SakuraFilter.Search;

namespace SakuraFilter.Etl;

/// <summary>
/// 进度报告 (单例,线程安全)
/// skipped 拆分为 2 个原因 (Day 7.5):
/// - skipped_missing_oem: xrefs/apps 中 product_oem 在 products 找不到 (数据对齐问题)
/// - skipped_null_field:  必填字段 (oem_brand/oem_no_3/machine_brand/machine_model) 为空 (源数据脏)
///   这种拆分让 silent skip 一眼可见,避免 apps 列名 bug 那种浪费 30min 排查的事故
///
/// last 5 错误环形缓冲 (Day 7.6):
///   - 之前 lastError 只保留 1 条,失败风暴时只能看最后一条
///   - 环形缓冲保留 5 条 (时间+消息),便于诊断 Meili 失败根因 (ConnectionRefused / schema 错 / payload 损坏)
///   - 容量 5 是经验值:太多掩盖最新错误,太少无法看分布
/// </summary>
public class EtlProgress
{
    private const int MaxRecentErrors = 5;
    private readonly object _errorsLock = new();
    private readonly Queue<(DateTime At, string Message)> _recentErrors = new();
    private readonly ILogger? _logger;       // Day 7.7: 落库失败时记日志
    private readonly IServiceProvider? _sp;  // Day 7.7: 落库时取 Scoped DbContext
    private int _bufferSize;                 // Day 7.7: 环形缓冲容量可配

    public EtlProgress() { _bufferSize = MaxRecentErrors; }
    public EtlProgress(ILogger? logger, int bufferSize, IServiceProvider? sp = null)
    {
        _logger = logger;
        _sp = sp;
        _bufferSize = Math.Max(1, bufferSize);
    }
    private long _read;
    private long _inserted;
    private long _updated;
    private long _skipped;                   // 兼容旧字段,=missing_oem + null_field
    private long _skippedMissingOem;         // Day 7.5: OEM 在 products 中找不到
    private long _skippedNullField;          // Day 7.5: 必填字段为 null
    private long _skippedDuplicate;          // Day 7.6: DISTINCT ON 去重掉的行
    private long _errors;
    private long _indexed;       // 直接成功写入 Meili
    private long _indexPending;  // 失败入队待补偿
    private long _rowsTotal;     // Day 9.2: 文件总行数 (启动时估读,用于前端进度条)
    private string _stage = "idle";  // Day 9.2: idle/reading/staging/inserting/committing/meili-sync
    private string _status = "idle";  // idle/running/completed/failed/cancelled
    private DateTime? _startedAt;
    private DateTime? _finishedAt;
    private string? _currentFile;
    private string? _lastError;
    private string? _cancelReason;   // Day 9.4: 取消原因 (写到 etl_progress_log.cancel_reason)
    private DateTime? _cancelledAt;   // Day 9.4: 取消时间 (写到 etl_progress_log.cancelled_at)
    private string? _reasonCode;      // Day 9.5: 取消原因枚举码 (写到 etl_progress_log.reason_code)

    // Day 9.5: 取消原因枚举白名单
    //   WHY 固定: 运营审计按码聚合 (USER_REQUEST/TIMEOUT/SYSTEM_SHUTDOWN/ADMIN_OVERRIDE/OTHER)
    //   自由文本 reason 仅供人工阅读
    public static readonly string[] AllowedReasonCodes = new[] {
        "USER_REQUEST",       // 用户主动取消 (前端 prompt 输入)
        "TIMEOUT",            // 任务超时被系统取消
        "SYSTEM_SHUTDOWN",    // 系统关闭/重启
        "ADMIN_OVERRIDE",     // 管理员强制 (CLI/DBA 直接 CancelActiveTask)
        "OTHER"               // 兜底
    };
    public static string NormalizeReasonCode(string? code, string? fallbackReason = null)
    {
        if (string.IsNullOrWhiteSpace(code)) return "OTHER";
        var upper = code.Trim().ToUpperInvariant();
        return AllowedReasonCodes.Contains(upper) ? upper : "OTHER";
    }

    public long Read => Interlocked.Read(ref _read);
    public long Inserted => Interlocked.Read(ref _inserted);
    public long Updated => Interlocked.Read(ref _updated);
    public long Skipped => Interlocked.Read(ref _skipped);
    public long SkippedMissingOem => Interlocked.Read(ref _skippedMissingOem);
    public long SkippedNullField => Interlocked.Read(ref _skippedNullField);
    public long SkippedDuplicate => Interlocked.Read(ref _skippedDuplicate);
    public long Errors => Interlocked.Read(ref _errors);
    public long Indexed => Interlocked.Read(ref _indexed);
    public long IndexPending => Interlocked.Read(ref _indexPending);
    public long RowsTotal => Interlocked.Read(ref _rowsTotal);
    // Day 9.2: Stage 字段公开 - 用于前端精细化显示当前 ETL 阶段
    //   值: idle / reading / staging / inserting / committing / meili-sync
    //   WHY Interlocked.Exchange: 后台 Meili 同步在另一线程跑,需要线程安全
    public string Stage => Interlocked.CompareExchange(ref _stage, null, null) ?? "idle";
    public string Status => _status;
    public DateTime? StartedAt => _startedAt;
    public DateTime? FinishedAt => _finishedAt;
    public string? CurrentFile => _currentFile;
    public string? LastError => _lastError;
    // Day 9.4: 取消审计 getter (EtlImportService 写日志用)
    public string? CancelReason => _cancelReason;
    public DateTime? CancelledAt => _cancelledAt;
    public string? ReasonCode => _reasonCode;  // Day 9.5
    // Day 7.7 修复: 已完结 (completed/failed) 时用 finishedAt-startedAt,避免查询时继续计时
    // 之前 5s 后查 status 显示 5.05s 实际 ETL 0.15s,运维误判
    public TimeSpan? Elapsed => _startedAt.HasValue
        ? (_finishedAt ?? DateTime.UtcNow) - _startedAt.Value
        : null;

    /// <summary>Day 7.6: 最近 5 条错误快照 (线程安全快照,环形缓冲)</summary>
    public IReadOnlyList<(DateTime At, string Message)> RecentErrors
    {
        get { lock (_errorsLock) return _recentErrors.ToArray(); }
    }

    public void Start(string file) { _status = "running"; _currentFile = file; _startedAt = DateTime.UtcNow; }

    /// <summary>Day 9.2: 设置当前阶段 (用于前端精细化显示)
    ///   可选值: idle / reading / staging / inserting / committing / meili-sync
    ///   线程安全: 用 Interlocked.Exchange 写引用类型,后台 Meili 线程可并发调
    /// </summary>
    public void SetStage(string stage) { Interlocked.Exchange(ref _stage, stage ?? "idle"); }

    /// <summary>Day 9.2: 设置文件总行数 (前端进度条分母, 启动时调一次)</summary>
    public void SetRowsTotal(long rowsTotal) { Interlocked.Exchange(ref _rowsTotal, Math.Max(0, rowsTotal)); }

    /// <summary>Day 9.2: 估算文件总行数 (用于前端进度条)
    ///   WHY 不精确到字符: JSONL 每行一条,ByteCount 估读 100ms 内完成,1M 行误差 ±5% 可接受
    ///   WHY 不用 File.ReadLines().Count: 1M 行要 2s+,会阻塞 ETL 启动
    /// </summary>
    public static long EstimateFileLines(string filePath, int avgLineBytes = 200)
    {
        try
        {
            var size = new FileInfo(filePath).Length;
            return Math.Max(1, size / Math.Max(1, avgLineBytes));
        }
        catch { return 0; }
    }
    public void Finish(string entityType, string mode)
    {
        _status = "completed"; _finishedAt = DateTime.UtcNow;
        // Day 7.7: 落库历史快照 (异步,不阻塞 ETL 完结)
        _ = PersistLogAsync(entityType, mode);
    }
    public void Fail(string error, string entityType, string mode)
    {
        _status = "failed";
        _lastError = error;
        _finishedAt = DateTime.UtcNow;
        PushError(error);
        _ = PersistLogAsync(entityType, mode);
    }
    // 保留旧 Fail(error) 给 advisory lock 失败等无 entityType/mode 上下文场景
    public void Fail(string error)
    {
        _status = "failed";
        _lastError = error;
        _finishedAt = DateTime.UtcNow;
        PushError(error);
    }
    // Day 9.1: 标记任务被取消 (CancelActiveTask 触发 OperationCanceledException 后调用)
    //   WHY: Status/LastError 是只读 getter, 必须用此方法安全写
    // Day 9.4: 取消审计字段 (cancel_reason / cancelled_at) 写到 etl_progress_log
    // Day 9.5: 取消原因枚举码 (USER_REQUEST/TIMEOUT/SYSTEM_SHUTDOWN/ADMIN_OVERRIDE/OTHER) 写到 etl_progress_log.reason_code
    public void Cancel(string reason = "用户取消", string reasonCode = "OTHER")
    {
        _status = "cancelled";
        _lastError = reason;
        _cancelReason = reason;
        _cancelledAt = DateTime.UtcNow;
        _reasonCode = NormalizeReasonCode(reasonCode);  // Day 9.5: 归一化, 防客户端传任意字符串
        _finishedAt = _cancelledAt;
        PushError(reason);
    }

    // P1.1 (Task 3): 标记任务暂停 (Pause API 触发时调用)
    //   与 Cancel 的区别:
    //     - Cancel: _status = "cancelled", _finishedAt = now(), 写 cancel_reason + cancelled_at
    //     - Pause:  _status = "paused", _finishedAt = now(), 不写 cancel_reason (不是取消)
    //   PersistPausedLogAsync 单独写一条 status='paused' 的日志记录 (含 checkpoint_id)
    public void Pause()
    {
        _status = "paused";
        _finishedAt = DateTime.UtcNow;
    }

    
    /// <summary>Day 9.4: 公开的日志落库入口, 给 EtlImportService.TriggerAsync catch 块调用
    ///   cancel 时 PersistLogAsync 私有不可见, 这里包一层
    /// </summary>
    public Task PersistLogAsync(string entityType, string mode) => PersistLogAsyncInternal(entityType, mode);
    private async Task PersistLogAsyncInternal(string entityType, string mode)
    {
        if (_sp is null) return;
        try
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            var log = ToLogSnapshot(entityType, mode);
            db.EtlProgressLogs.Add(log);
            await db.SaveChangesAsync();
            _logger?.LogInformation("ETL 历史落库: id={Id} {Entity}/{Mode} status={Status} read={Read}",
                log.Id, log.EntityType, log.Mode, log.Status, log.ReadCount);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ETL 历史落库失败 (不影响业务结果)");
        }
    }

    public void IncrRead() => Interlocked.Increment(ref _read);
    // P1.1 (Task 3): xrefs 批次循环累加 read (1 批 1000 行)
    public void IncrReadBy(long n) => Interlocked.Add(ref _read, n);
    public void IncrInserted() => Interlocked.Increment(ref _inserted);
    public void IncrUpdated() => Interlocked.Increment(ref _updated);
    public void IncrInsertedBy(long n) => Interlocked.Add(ref _inserted, n);
    public void IncrUpdatedBy(long n) => Interlocked.Add(ref _updated, n);
    public void IncrSkipped() => Interlocked.Increment(ref _skipped);
    public void IncrSkippedBy(long n) => Interlocked.Add(ref _skipped, n);
    public void IncrSkippedMissingOem() { Interlocked.Increment(ref _skipped); Interlocked.Increment(ref _skippedMissingOem); }
    public void IncrSkippedNullField() { Interlocked.Increment(ref _skipped); Interlocked.Increment(ref _skippedNullField); }
    public void IncrSkippedDuplicate() { Interlocked.Increment(ref _skipped); Interlocked.Increment(ref _skippedDuplicate); }
    public void IncrErrors()
    {
        Interlocked.Increment(ref _errors);
        // Day 7.6: 每次 IncrErrors 触发时,记录一条最近错误
        // 注意: IncrErrors 旧调用不传 message,在 ETL 层改用 IncrErrorsWith(message)
    }
    public void IncrErrorsWith(string message)
    {
        Interlocked.Increment(ref _errors);
        PushError(message);
    }
    public void IncrIndexed() => Interlocked.Increment(ref _indexed);
    public void IncrIndexPending() => Interlocked.Increment(ref _indexPending);
    public void IncrIndexedBy(long n) => Interlocked.Add(ref _indexed, n);
    public void IncrIndexPendingBy(long n) => Interlocked.Add(ref _indexPending, n);

    /// <summary>Day 7.6: 推入最近错误,超容量时出队最旧 (Day 7.7: 容量改成 _bufferSize 可配)</summary>
    private void PushError(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        var entry = (DateTime.UtcNow, message.Length > 300 ? message[..300] : message);
        lock (_errorsLock)
        {
            _recentErrors.Enqueue(entry);
            while (_recentErrors.Count > _bufferSize) _recentErrors.Dequeue();
        }
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _read, 0);
        Interlocked.Exchange(ref _inserted, 0);
        Interlocked.Exchange(ref _updated, 0);
        Interlocked.Exchange(ref _skipped, 0);
        Interlocked.Exchange(ref _skippedMissingOem, 0);
        Interlocked.Exchange(ref _skippedNullField, 0);
        Interlocked.Exchange(ref _skippedDuplicate, 0);
        Interlocked.Exchange(ref _errors, 0);
        Interlocked.Exchange(ref _indexed, 0);
        Interlocked.Exchange(ref _indexPending, 0);
        Interlocked.Exchange(ref _rowsTotal, 0);
        Interlocked.Exchange(ref _stage, "idle");
        _status = "idle"; _startedAt = null; _finishedAt = null; _currentFile = null; _lastError = null;
        lock (_errorsLock) _recentErrors.Clear();
    }

    public object ToJson() => new
    {
        status = Status,
        stage = Stage,                          // Day 9.2: 暴露 stage 字段给前端
        rowsTotal = RowsTotal,                  // Day 9.2: 暴露总行数 (前端进度条)
        currentFile = CurrentFile,
        read = Read,
        inserted = Inserted,
        updated = Updated,
        skipped = Skipped,
        skippedMissingOem = SkippedMissingOem,
        skippedNullField = SkippedNullField,
        skippedDuplicate = SkippedDuplicate,  // Day 7.6
        errors = Errors,
        indexed = Indexed,
        indexPending = IndexPending,
        elapsedSec = Elapsed?.TotalSeconds,
        startedAt = StartedAt,
        finishedAt = FinishedAt,
        lastError = LastError,
        recentErrors = RecentErrors.Select(e => new { at = e.At, message = e.Message }).ToArray()  // Day 7.6
    };

    /// <summary>Day 7.7: 生成持久化快照,用于落库 etl_progress_log
    /// 不含 recentErrors 数组 (体量大,留 last_error 一条即可)
    /// </summary>
    public EtlProgressLog ToLogSnapshot(string entityType, string mode)
    {
        return new EtlProgressLog
        {
            EntityType = entityType,
            Mode = mode,
            FilePath = CurrentFile ?? "",
            Status = Status,
            ReadCount = Read,
            InsertedCount = Inserted,
            UpdatedCount = Updated,
            SkippedCount = Skipped,
            SkippedMissingOem = SkippedMissingOem,
            SkippedNullField = SkippedNullField,
            SkippedDuplicate = SkippedDuplicate,
            ErrorCount = Errors,
            IndexedCount = Indexed,
            IndexPendingCount = IndexPending,
            LastError = LastError,
            StartedAt = StartedAt ?? DateTime.UtcNow,
            FinishedAt = FinishedAt ?? DateTime.UtcNow,
            DurationSec = Elapsed?.TotalSeconds ?? 0,
            // Day 9.4: 取消审计字段 (NULL 表示非取消)
            CancelReason = _cancelReason,
            CancelledAt = _cancelledAt,
            // Day 9.5: 取消原因枚举码 (仅 cancelled 时有值)
            ReasonCode = _reasonCode
        };
    }
}

/// <summary>
/// ETL 导入服务 (Day 5 骨架)
/// - 输入: Python 清洗后的 JSONL (products.jsonl)
/// - 流程: 流式读 -> COPY 入 staging -> UPSERT 到 products
/// - 不做清洗 (已在 Python 阶段完成)
/// - 进度通过单例 EtlProgress 暴露
/// </summary>
public class EtlImportService
{
    private readonly string _pgConn;
    private readonly ILogger<EtlImportService> _logger;
    private readonly IServiceProvider _sp;
    private readonly EtlOptions _options;
    public EtlProgress Progress { get; }

    // Day 9.1: 当前活跃任务的 CancellationTokenSource
    //   - 取消时让 CancellationToken 传播到 Import*Async 内部的 COPY/INSERT
    //   - 锁用 object, 因为 EtlImportService 是 Singleton, 可能多线程访问
    //   - 单任务: 同时只允许一个 ETL 任务运行, 新的 TriggerAsync 在已有任务时抛 InvalidOperationException
    private readonly object _ctsLock = new();
    private CancellationTokenSource? _activeCts;
    private string? _activeTaskEntity;  // products/xrefs/apps
    private string? _activeCancelReason;  // Day 9.4: 取消原因 (CancelActiveTask 写入, catch 块读出后落库)
    private string? _activeCancelReasonCode;  // Day 9.5: 取消原因枚举码 (USER_REQUEST/TIMEOUT/...)

    // P1.1 (Task 3): Pause/Resume 标志位
    //   - Pause 不释放 _activeCts, 与 Cancel 区别 (Cancel 走 cts.Cancel(), Pause 走 _pausedFlag)
    //   - Interlocked.Exchange 保证多线程可见 (AdminEtlView 调 API 时, ETL 内部循环每批次检查)
    //   - 值: 0=未暂停, 1=已请求暂停
    private int _pausedFlag;

    // Day 7.8: 改用 IOptions<EtlOptions> 注入 (替代手动 IConfiguration 读取)
    //   WHY: 配置校验集中在 EtlOptionsValidator,启动失败立即可见,不必运行期才发现
    // Day 9.6: 可选 IEtlProgressBroadcaster (跨实例 SSE 广播),缺省时单实例本地轮询
    //   - 用 default = null 而非强制注入: 减少对 spike-test 脚本的影响 (它们不通过 DI 构造)
    public EtlImportService(
        string connectionString,
        ILogger<EtlImportService> logger,
        IServiceProvider sp,
        IOptions<EtlOptions> etlOptions,
        IEtlProgressBroadcaster? broadcaster = null)
    {
        _pgConn = connectionString;
        _logger = logger;
        _sp = sp;
        _options = etlOptions.Value;
        _broadcaster = broadcaster;
        Progress = new EtlProgress(logger, _options.RecentErrorBuffer, sp);
    }

    private readonly IEtlProgressBroadcaster? _broadcaster;

    // ========== Day 8.4 手动触发 + 进度查询 ==========

    /// <summary>
    /// 手动触发 ETL (后台 ETL 页面 "立即导入" 按钮调用)
    /// entityType: products / xrefs / apps
    /// mode: full-load / insert-only / upsert
    /// P1.1 (Task 3): startLineNo = 0 走全新 ETL, > 0 走续读模式 (从第 N+1 行开始读 JSONL)
    /// </summary>
    // Day 11 改进 2: 增加 cascade 参数 (仅 products full-load 生效)
    //   - cascade=true (默认, 兼容旧行为): TRUNCATE products CASCADE 清空 xrefs/apps
    //   - cascade=false: 仅 TRUNCATE products, 保留 xrefs/apps (用于单独刷新产品主表)
    public async Task<EtlProgress> TriggerAsync(string entityType, string jsonlPath, string mode, long startLineNo = 0, CancellationToken ct = default, bool cascade = true)
    {
        if (string.IsNullOrWhiteSpace(jsonlPath))
            throw new ArgumentException("jsonlPath 不能为空");
        if (!File.Exists(jsonlPath))
            throw new FileNotFoundException($"JSONL 文件不存在: {jsonlPath}");

        // P0-3.3: defense in depth — 即使绕过 HTTP 端点校验直接调 TriggerAsync, 也校验白名单
        //   - _options.AllowedImportDirs 为空时不拦截 (dev 兼容); 非空时严格校验
        //   - WHY 二次校验: HTTP 端点校验只覆盖 4 个端点, CLI/脚本/未来新端点直接调本方法时仍受保护
        var allowedDirs = _options.AllowedImportDirs;
        if (allowedDirs is { Length: > 0 })
        {
            string normalized;
            try { normalized = Path.GetFullPath(jsonlPath); }
            catch (Exception) { throw new ArgumentException($"jsonlPath 路径非法: {jsonlPath}"); }
            var inWhitelist = false;
            foreach (var dir in allowedDirs)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string normalizedDir;
                try { normalizedDir = Path.GetFullPath(dir); }
                catch (Exception) { continue; }
                if (normalized.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase) &&
                    (normalized.Length == normalizedDir.Length ||
                     normalized[normalizedDir.Length] == Path.DirectorySeparatorChar ||
                     normalized[normalizedDir.Length] == Path.AltDirectorySeparatorChar))
                {
                    inWhitelist = true;
                    break;
                }
            }
            if (!inWhitelist)
                throw new ArgumentException($"jsonlPath 不在允许目录内: {jsonlPath}");
        }

        var normalizedMode = NormalizeMode(mode);
        var normalizedEntity = entityType?.Trim().ToLowerInvariant() ?? "";

        // P1.1: Resume 触发时重置 _pausedFlag, 避免继承上一次 ETL 的暂停状态
        if (startLineNo > 0) ClearPausedFlag();

        // Day 9.9: _activeCts 下沉到 Import*Async 入口, TriggerAsync 只做路由
        return normalizedEntity switch
        {
            "products" or "product" => await ImportProductsAsync(jsonlPath, normalizedMode, startLineNo, ct, cascade),
            "xrefs" or "xref" or "cross_references" => await ImportXrefsAsync(jsonlPath, normalizedMode, startLineNo, ct),
            "apps" or "machine_applications" => await ImportAppsAsync(jsonlPath, normalizedMode, startLineNo, ct),
            _ => throw new ArgumentException($"未知 entityType={entityType}, 期望 products/xrefs/apps")
        };
    }

    /// <summary>
    /// P1.1 (Task 3): 读 etl_progress_log 最近一条 status='paused' 的 checkpoint_id,
    ///   触发新 ETL 任务从该 checkpoint_id+1 行续读
    ///   - 用于 admin 点 "恢复" 按钮
    ///   - 找不到 paused 记录时抛 InvalidOperationException (前端弹窗提示)
    /// </summary>
    public async Task<(long checkpointId, string entity, string mode, string filePath)> GetLastPausedCheckpointAsync()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var last = await db.EtlProgressLogs
            .AsNoTracking()
            .Where(l => l.Status == "paused" && l.CheckpointId != null)
            .OrderByDescending(l => l.Id)
            .FirstOrDefaultAsync();
        if (last is null || last.CheckpointId is null)
        {
            throw new InvalidOperationException("找不到 paused 状态的 ETL 记录, 无法 Resume");
        }
        return (last.CheckpointId.Value, last.EntityType, last.Mode, last.FilePath);
    }

    /// <summary>
    /// Day 9.1: 取消当前活跃 ETL 任务
    /// - 若没有活跃任务, 返回 false
    /// - 取消信号传播到 Import*Async 内部 COPY/INSERT, 任务会抛 OperationCanceledException
    /// - 已插入/更新的数据会保留 (COPY+INSERT 在同一事务内, 但 cancel 时 Npgsql 连接 dispose
    ///   会自动 ROLLBACK 未提交事务 → TRUNCATE 也回滚, 数据恢复到导入前状态)
    ///   Day 9.9 复核: BEGIN(CreateStagingTableAsync) → COPY → INSERT → COMMIT, 配对完整
    ///   异常时 await using conn 自动 dispose → Npgsql 自动 ROLLBACK → 临时表随会话结束清理
    /// Day 9.4: reason 写入 _activeCancelReason, catch 块读出后写到 etl_progress_log.cancel_reason
    /// Day 9.5: reasonCode 写入 _activeCancelReasonCode, 枚举白名单校验 (兜底 OTHER)
    /// </summary>
    public bool CancelActiveTask(string? reason = null, string? reasonCode = null)
    {
        lock (_ctsLock)
        {
            if (_activeCts == null || _activeCts.IsCancellationRequested)
                return false;
            _activeCancelReason = string.IsNullOrWhiteSpace(reason) ? "用户取消" : reason.Trim();
            // Day 9.5: 枚举码白名单, 兜底 OTHER (NormalizeReasonCode 内已校验)
            _activeCancelReasonCode = EtlProgress.NormalizeReasonCode(reasonCode);
            _activeCts.Cancel();
            _logger.LogInformation("ETL 任务取消信号已发送 entity={Entity} reason={Reason} code={Code}",
                _activeTaskEntity, _activeCancelReason, _activeCancelReasonCode);
            return true;
        }
    }

    // P1.1 (Task 3): Pause/Resume 框架
    //   设计要点:
    //     - Pause 不释放 _activeCts, 不抛 OperationCanceledException
    //       (区别于 Cancel, 避免触发现有 catch 块的 Cancel 分支走错路径)
    //     - 当前批次跑完后, ETL 内部循环 Interlocked.Exchange(ref _pausedFlag, 0) 命中则 break
    //     - checkpoint_id 写到 etl_progress_log, 用于 Resume 时定位
    //     - Resume 复用 _activeCts 不可行 (旧 ETL 已退出, 调 Pause 之前的 _activeCts 已被 ReleaseActiveCts 释放)
    //       → Resume 走新 TriggerAsync 路径, 传 startLineNo 跳过已读行
    //   生产部署前: 5 百万 xref 真实测试需在 staging 跑 (本机 10 万行 1 批次约 0.1s × 100 = 10s)

    /// <summary>设置 _pausedFlag=1, 请求当前 ETL 暂停 (admin 调 Pause API 时)</summary>
    public bool PauseActiveTask()
    {
        if (_activeCts == null || _activeCts.IsCancellationRequested)
            return false;
        Interlocked.Exchange(ref _pausedFlag, 1);
        _logger.LogInformation("ETL 任务暂停信号已发送 entity={Entity}", _activeTaskEntity);
        return true;
    }

    /// <summary>内部: 批次循环检测 _pausedFlag, 命中则请求暂停</summary>
    public bool IsPausedRequested() => Interlocked.CompareExchange(ref _pausedFlag, 0, 0) == 1;

    /// <summary>重置暂停标志 (Resume 触发新 ETL 之前调)</summary>
    public void ClearPausedFlag() => Interlocked.Exchange(ref _pausedFlag, 0);

    // Day 9.9: _activeCts 下沉到 Import*Async 入口
    //   WHY: 之前 _activeCts 仅在 TriggerAsync 设置, HTTP 端点直接调 Import*Async 会绕过,
    //        导致 CancelActiveTask 永远 cancelled=False (xrefs/apps 端点均有此 BUG)
    //   方案: Import*Async 入口调 AcquireActiveCts, finally 调 ReleaseActiveCts
    //        TriggerAsync 简化为纯路由, 不再管 _activeCts
    private CancellationTokenSource AcquireActiveCts(string entityType, CancellationToken externalCt)
    {
        lock (_ctsLock)
        {
            if (_activeCts != null && !_activeCts.IsCancellationRequested)
                throw new InvalidOperationException($"已有 ETL 任务在运行 (entity={_activeTaskEntity}), 请先等待完成或调用 /api/admin/etl/task 取消");
            var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            _activeCts = cts;
            _activeTaskEntity = entityType;
            return cts;
        }
    }

    private void ReleaseActiveCts(CancellationTokenSource cts)
    {
        lock (_ctsLock)
        {
            if (ReferenceEquals(_activeCts, cts))
            {
                _activeCts = null;
                _activeTaskEntity = null;
            }
            cts.Dispose();
        }
    }

    // Day 9.9: full-load 性能优化 — DROP 非约束索引, INSERT 后 CREATE
    //   WHY: products 表 15 个索引, INSERT 1M 行 = 1500 万次索引更新, 占 55s/62s
    //   DROP+CREATE 后 INSERT 只需更新 PK+UNIQUE 索引, 速度提升 5-10 倍
    private async Task<List<(string Name, string Definition)>> DropNonConstraintIndexesAsync(
        NpgsqlConnection conn, string table, CancellationToken ct)
    {
        var indexes = new List<(string Name, string Definition)>();
        // 查询非约束索引 (排除 PRIMARY KEY 和 UNIQUE)
        var sql = $"SELECT indexname, indexdef FROM pg_indexes WHERE tablename = '{table}' " +
                  "AND indexdef NOT LIKE '%UNIQUE%' AND indexdef NOT LIKE '%PRIMARY KEY%'";
        // WHY 显式 {} 作用域: reader 必须在 DROP INDEX 前释放,
        //   否则 Npgsql 抛 "A command is already in progress" (reader 仍占用连接)
        {
            await using var queryCmd = new NpgsqlCommand(sql, conn);
            await using var reader = await queryCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                indexes.Add((reader.GetString(0), reader.GetString(1)));
        } // reader 在此 Dispose, 连接释放给后续 DROP INDEX 使用

        foreach (var (name, _) in indexes)
        {
            await using var dropCmd = new NpgsqlCommand($"DROP INDEX IF EXISTS {name}", conn);
            await dropCmd.ExecuteNonQueryAsync(ct);
        }
        _logger.LogInformation("Dropped {Count} non-constraint indexes on {Table}", indexes.Count, table);
        return indexes;
    }

    private async Task RecreateIndexesAsync(
        NpgsqlConnection conn, List<(string Name, string Definition)> indexes, CancellationToken ct)
    {
        foreach (var (_, def) in indexes)
        {
            await using var createCmd = new NpgsqlCommand(def, conn);
            createCmd.CommandTimeout = 0;  // 大索引可能超 30s
            await createCmd.ExecuteNonQueryAsync(ct);
        }
        _logger.LogInformation("Recreated {Count} indexes", indexes.Count);
    }

    private static string NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return "upsert";
        var m = mode.Trim().ToLowerInvariant();
        return m switch
        {
            "full" or "full-load" or "fullload" or "truncate" => "full-load",
            "insert" or "insert-only" or "insertonly" => "insert-only",
            "upsert" or "update" => "upsert",
            _ => "upsert"
        };
    }

    /// <summary>
    /// 获取当前活跃任务 + 进度信息 (后台 ETL 页面 3s 轮询)
    /// Day 9.2: stage 直接读 Progress.Stage (精确反映 reading/staging/inserting/committing/meili-sync)
    ///   之前用 p.IndexPending > 0 ? "meili-sync" : "commit" 简化推断,无法识别 staging/inserting
    /// </summary>

    /// <summary>
    /// Day 9.7: 跨实例广播上下文 (timer + 终态对比基准)
    ///   WHY 改用闭包内部 lastJson: 避免外层 ctx 字段被 timer 异步修改
    /// </summary>
    private sealed class BroadcastCtx : IDisposable
    {
        public Timer? Timer;
        public string LastJson;
        public bool IsActive => Timer != null;
        public BroadcastCtx(Timer? timer, string lastJson) { Timer = timer; LastJson = lastJson; }
        public void Dispose() { try { Timer?.Dispose(); } catch { } }
    }

    /// <summary>
    /// Day 9.7: 启动 snapshot timer (Import*Async 入口处调,覆盖所有调用路径)
    ///   - 立即推一帧 (让其他实例 SSE 立即看到)
    ///   - 500ms 拍一次 Progress.ToJson() 推给 broadcaster
    ///   - broadcaster 为 null (单测/离线脚本) 时直接返回 inactive ctx
    /// </summary>
    private BroadcastCtx StartSnapshotTimerIfNeeded()
    {
        if (_broadcaster == null) return new BroadcastCtx(null, "");
        // 拍首帧
        var firstJson = System.Text.Json.JsonSerializer.Serialize(Progress.ToJson());
        try { _broadcaster.Publish(firstJson); } catch { }
        // 闭包内维护 lastJson,避免 ctx 字段被 timer 异步读写冲突
        var lastJsonRef = new[] { firstJson };
        var timer = new Timer(_ =>
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(Progress.ToJson());
                if (json != lastJsonRef[0])
                {
                    lastJsonRef[0] = json;
                    _broadcaster.Publish(json);
                }
            }
            catch { /* 静默, 不影响 ETL */ }
        }, null, 500, 500);
        return new BroadcastCtx(timer, firstJson);
    }

    /// <summary>
    /// Day 9.7: 停止 snapshot timer + 推终态帧 (Import*Async finally 块调)
    /// </summary>
    private void StopSnapshotTimer(BroadcastCtx? ctx)
    {
        if (ctx == null || ctx.Timer == null) return;
        ctx.Dispose();
        // 推终态 (completed/failed/cancelled)
        try
        {
            var finalJson = System.Text.Json.JsonSerializer.Serialize(Progress.ToJson());
            if (finalJson != ctx.LastJson) _broadcaster?.Publish(finalJson);
        }
        catch { }
    }

    public object GetActiveTaskInfo()
    {
        var p = Progress;
        var inProgress = p.Status == "running";
        // Day 9.2: stage 优先取 Progress.Stage (精确), running 状态下 fallback 推断
        string stage = p.Status switch
        {
            "running" => string.IsNullOrEmpty(p.Stage) || p.Stage == "idle"
                ? (p.IndexPending > 0 ? "meili-sync" : "commit")
                : p.Stage,
            _ => p.Stage == "idle" ? p.Status : p.Stage
        };
        // Day 9.2: rowsTotal 从 Progress.RowsTotal 取 (启动时估算)
        long? rowsTotal = p.RowsTotal > 0 ? p.RowsTotal : (long?)null;
        // Day 9.2: progressPct 按 stage 计算,staging/read 阶段 = read/total
        int? pct = null;
        if (rowsTotal.HasValue && rowsTotal.Value > 0)
        {
            // staging: 用 read 数 (已读取行数)
            // inserting: 用 read 数 (几乎在 INSERT 完就 close,所以读数 ≈ 总数)
            // committing: 视为 99%
            // meili-sync: 用 indexed/(estimated)
            var denom = rowsTotal.Value;
            long numer = stage switch
            {
                "staging" or "inserting" => p.Read,
                "committing" => (long)(denom * 0.99),
                "meili-sync" => p.Indexed,
                _ => p.Read
            };
            pct = (int)Math.Clamp(numer * 100 / denom, 0, 100);
        }
        return new
        {
            inProgress,
            activeTask = inProgress ? new
            {
                status = p.Status,
                stage,
                currentFile = p.CurrentFile,
                read = p.Read,
                inserted = p.Inserted,
                updated = p.Updated,
                skipped = p.Skipped,
                errors = p.Errors,
                indexed = p.Indexed,
                indexPending = p.IndexPending,
                rowsProcessed = p.Inserted + p.Updated,
                rowsTotal,
                progressPct = pct,
                startedAt = p.StartedAt,
                elapsedSec = p.Elapsed?.TotalSeconds,
                lastError = p.LastError
            } : null
        };
    }

    /// <summary>
    /// 主入口: 导入 products JSONL
    /// mode: "upsert" (默认,完整 INSERT ON CONFLICT DO UPDATE)
    ///       "full-load" (TRUNCATE + INSERT,适合 1M 首次全量, 5s 内)
    ///       "insert-only" (INSERT ON CONFLICT DO NOTHING,只插新行)
    /// P1.1 (Task 3): startLineNo 参数与 xrefs 对齐 — products 暂不实现批次, 传 0 即可
    /// </summary>
    // Day 11 改进 2: cascade 参数控制 full-load TRUNCATE 行为
    //   - cascade=true (默认): TRUNCATE products, cross_references, machine_applications (兼容旧行为)
    //   - cascade=false: 仅 TRUNCATE products, 保留 xrefs/apps (用于单独刷新主表)
    public async Task<EtlProgress> ImportProductsAsync(string jsonlPath, string mode = "upsert", long startLineNo = 0, CancellationToken ct = default, bool cascade = true)
    {
        // Day 9.9: _activeCts 下沉, 确保任何入口 (HTTP 端点/TriggerAsync/直接调用) 都能被 cancel
        var cts = AcquireActiveCts("products", ct);
        ct = cts.Token;
        Progress.Reset();
        Progress.Start(jsonlPath);
        // Day 9.2: 估算文件总行数 (前端进度条分母, 启动 < 1ms)
        Progress.SetRowsTotal(EtlProgress.EstimateFileLines(jsonlPath));
        var importStartedAt = Progress.StartedAt ?? DateTime.UtcNow;

        // Day 9.7: 启动跨实例广播 snapshot timer (在 Import*Async 内部,所有入口都覆盖)
        var broadcastCtx = StartSnapshotTimerIfNeeded();

        try
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);

            // Day 7: 事务级 advisory lock 防多实例并发跑同一 ETL
            // 锁 key 7740001 固定 (跨重启稳定)
            if (!await TryAcquireAdvisoryLockAsync(conn, 7740001L, ct))
            {
                Progress.Fail("另一 ETL 任务正在跑 (advisory lock 7740001 被占用)");
                _logger.LogWarning("ImportProductsAsync advisory lock 获取失败");
                return Progress;
            }

            // 1) 准备 staging 表 (跳过 LoadExistingOemMapAsync:1M 规模下 Dictionary 太重,
            //    inserted/updated 统计改为 UPSERT RETURNING (xmax=0))
            Progress.SetStage("staging");
            var swStaging = System.Diagnostics.Stopwatch.StartNew();
            await CreateStagingTableAsync(conn, ct);
            swStaging.Stop();
            _logger.LogInformation("[TIMING] 创建 staging: {Ms}ms", swStaging.ElapsedMilliseconds);

            // 3) 流式读 JSONL + COPY 入 staging
            Progress.SetStage("staging");  // 显式再次设置,前端可能错过 start 后的状态变更
            var swCopy = System.Diagnostics.Stopwatch.StartNew();
            var lineNo = 0;
            // Day 9.2: COPY 阶段每 1000 行检查一次取消信号
            //   WHY: 单行处理 1-2ms, 1000 行 ~1s, 取消时最大延迟 1s 内生效
            //   之前: 100K 行取消要等 100s 才能停
            const int CancelCheckInterval = 1000;
            await using (var writer = await conn.BeginBinaryImportAsync(@"
                COPY products_stage (oem_no_normalized, oem_no_display, type,
                    product_name_1, product_name_2, product_name_3,
                    remark, d1_mm, d2_mm, d3_mm, h1_mm, h2_mm, h3_mm,
                    d7_thread, d8_thread, media, sealing_material,
                    efficiency_1, efficiency_2, bypass_valve_lr, bypass_valve_hr,
                    collapse_pressure_bar, temp_range, bypass_pressure)
                FROM STDIN (FORMAT BINARY)
            ", ct))
            {
                using var reader = new StreamReader(jsonlPath);
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    lineNo++;
                    if (lineNo % CancelCheckInterval == 0) ct.ThrowIfCancellationRequested();
                    Progress.IncrRead();
                    try
                    {
                        var doc = JsonSerializer.Deserialize<JsonElement>(line);
                        var oemNorm = doc.GetProperty("oem_no_normalized").GetString()!;

                        await writer.StartRowAsync(ct);
                        await writer.WriteAsync(oemNorm, NpgsqlDbType.Varchar, ct);
                        await writer.WriteAsync(doc.GetProperty("oem_no_display").GetString() ?? "", NpgsqlDbType.Varchar, ct);
                        await writer.WriteAsync(doc.GetProperty("type").GetString() ?? "UNKNOWN", NpgsqlDbType.Varchar, ct);
                        // WHY 新增: product_name_1/2 写入 stage (分区 1 主信息区, 之前 COPY 漏读导致 products 表这两列全 NULL)
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "product_name_1"), ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "product_name_2"), ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "product_name_3"), ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "remark"), ct);
                        await WriteNullableDecimalAsync(writer, GetDecimalOrNull(doc, "d1_mm"), ct);
                        await WriteNullableDecimalAsync(writer, GetDecimalOrNull(doc, "d2_mm"), ct);
                        await WriteNullableDecimalAsync(writer, GetDecimalOrNull(doc, "d3_mm"), ct);
                        await WriteNullableDecimalAsync(writer, GetDecimalOrNull(doc, "h1_mm"), ct);
                        await WriteNullableDecimalAsync(writer, GetDecimalOrNull(doc, "h2_mm"), ct);
                        await WriteNullableDecimalAsync(writer, GetDecimalOrNull(doc, "h3_mm"), ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "d7_thread"), ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "d8_thread"), ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "media"), ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "sealing_material"), ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "efficiency_1"), ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "efficiency_2"), ct);
                        await WriteNullableDecimalAsync(writer, GetDecimalOrNull(doc, "bypass_valve_lr"), ct);
                        await WriteNullableDecimalAsync(writer, GetDecimalOrNull(doc, "bypass_valve_hr"), ct);
                        await WriteNullableDecimalAsync(writer, GetDecimalOrNull(doc, "collapse_pressure_bar"), ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "temp_range"), ct);
                        await WriteNullableDecimalAsync(writer, GetDecimalOrNull(doc, "bypass_pressure"), ct);
                        // inserted/updated 改在 UPSERT 后通过 RETURNING (xmax=0) 统计
                    }
                    catch (Exception ex)
                    {
                        Progress.IncrErrorsWith($"products 行 {lineNo}: {ex.Message}");  // Day 7.6
                        _logger.LogWarning("行 {LineNo} 解析失败: {Error}", lineNo, ex.Message);
                    }
                }
                await writer.CompleteAsync(ct);
            }
            swCopy.Stop();
            _logger.LogInformation("[TIMING] staging COPY: {Ms}ms ({Count} 行)", swCopy.ElapsedMilliseconds, Progress.Read);

            // Day 9.9: 数据完整性校验 — COPY 后查 stage 行数, 防止静默丢行
            //   WHY: 100万行 COPY 可能因连接中断/内存压力丢行, 之前无对账
            //   对账 1: stage_count + errors == read (每行要么进 stage, 要么 error)
            long stageCount;
            await using (var countCmd = new NpgsqlCommand("SELECT count(*) FROM products_stage", conn))
                stageCount = (long)(await countCmd.ExecuteScalarAsync(ct))!;
            _logger.LogInformation("[AUDIT] products_stage: read={Read} stage={Stage} errors={Errors}", Progress.Read, stageCount, Progress.Errors);
            if (stageCount + Progress.Errors != Progress.Read)
            {
                var msg = $"数据完整性校验失败: read={Progress.Read} stage={stageCount} errors={Progress.Errors} (期望 stage+errors=read)";
                _logger.LogError(msg);
                Progress.Fail(msg);
                _ = Progress.PersistLogAsync("products", mode);
                return Progress;
            }

            // 4) 根据 mode 选择导入策略
            //    Day 9.2: 切换 stage = "inserting" 给前端精细化显示
            //    WHY 在这里 (而不是 ExecuteNonQueryAsync 之后): 用户感知 "INSERT 写库" 是从 cmd 启动开始
            Progress.SetStage("inserting");

            // Day 9.9: full-load 性能优化 — DROP 非约束索引, INSERT 后 CREATE
            //   WHY: 15 个索引 × 1M 行 = 1500 万次索引更新, 占 INSERT 55s 的 88%
            //   DROP+CREATE 后 INSERT 只需更新 PK+UNIQUE 索引, 速度提升 5-10 倍
            List<(string Name, string Definition)> savedIndexes = new();
            if (mode == "full-load")
            {
                var swDrop = System.Diagnostics.Stopwatch.StartNew();
                savedIndexes = await DropNonConstraintIndexesAsync(conn, "products", ct);
                swDrop.Stop();
                _logger.LogInformation("[TIMING] DROP indexes: {Ms}ms ({Count} indexes)", swDrop.ElapsedMilliseconds, savedIndexes.Count);
            }

            //    full-load: TRUNCATE + INSERT (首次全量, 5s 内完成 1M)
            //    insert-only: INSERT ON CONFLICT DO NOTHING (只插新行,不更新已有)
            //    upsert: 完整 INSERT ON CONFLICT DO UPDATE (默认,慢但最完整)
            // Day 11 改进 2: cascade 参数控制 TRUNCATE 范围
            //   - cascade=true (默认): TRUNCATE products + xrefs + apps (旧行为, 首次全量场景)
            //   - cascade=false: 仅 TRUNCATE products (单独刷新产品主表, 保留 xrefs/apps)
            //     场景: 修复 products 字段 (如 product_name_1/2) 不想破坏 xrefs/apps 关联
            string truncateClause = cascade
                ? "TRUNCATE products, cross_references, machine_applications RESTART IDENTITY CASCADE;"
                : "TRUNCATE products RESTART IDENTITY CASCADE;";
            string finalSql = mode switch
            {
                // WHY: RESTART IDENTITY 让 serial 列从 1 重新开始,首次全量场景下 id 连续
                //      CASCADE 防御性写法,即使未来加 FK 也不会破坏 ETL
                //      Day 7 修复: 同时清 cross_references/machine_applications 避免孤儿行 (无 FK 约束时不会失败)
                "full-load" => $@"
                    {truncateClause}
                    INSERT INTO products (oem_no_normalized, oem_no_display, type,
                        product_name_1, product_name_2, product_name_3,
                        remark, d1_mm, d2_mm, d3_mm, h1_mm, h2_mm, h3_mm,
                        d7_thread, d8_thread, media, sealing_material,
                        efficiency_1, efficiency_2, bypass_valve_lr, bypass_valve_hr,
                        collapse_pressure_bar, temp_range, bypass_pressure, updated_at)
                    SELECT DISTINCT ON (oem_no_normalized)
                        oem_no_normalized, oem_no_display, type,
                        product_name_1, product_name_2, product_name_3,
                        remark, d1_mm, d2_mm, d3_mm, h1_mm, h2_mm, h3_mm,
                        d7_thread, d8_thread, media, sealing_material,
                        efficiency_1, efficiency_2, bypass_valve_lr, bypass_valve_hr,
                        collapse_pressure_bar, temp_range, bypass_pressure, now()
                    FROM products_stage
                    ORDER BY oem_no_normalized, ctid DESC;",
                "insert-only" => @"
                    INSERT INTO products (oem_no_normalized, oem_no_display, type,
                        product_name_1, product_name_2, product_name_3,
                        remark, d1_mm, d2_mm, d3_mm, h1_mm, h2_mm, h3_mm,
                        d7_thread, d8_thread, media, sealing_material,
                        efficiency_1, efficiency_2, bypass_valve_lr, bypass_valve_hr,
                        collapse_pressure_bar, temp_range, bypass_pressure, updated_at)
                    SELECT DISTINCT ON (oem_no_normalized)
                        oem_no_normalized, oem_no_display, type,
                        product_name_1, product_name_2, product_name_3,
                        remark, d1_mm, d2_mm, d3_mm, h1_mm, h2_mm, h3_mm,
                        d7_thread, d8_thread, media, sealing_material,
                        efficiency_1, efficiency_2, bypass_valve_lr, bypass_valve_hr,
                        collapse_pressure_bar, temp_range, bypass_pressure, now()
                    FROM products_stage
                    ORDER BY oem_no_normalized, ctid DESC
                    ON CONFLICT (oem_no_normalized) DO NOTHING;",
                _ => @"
                    INSERT INTO products (oem_no_normalized, oem_no_display, type,
                        product_name_1, product_name_2, product_name_3,
                        remark, d1_mm, d2_mm, d3_mm, h1_mm, h2_mm, h3_mm,
                        d7_thread, d8_thread, media, sealing_material,
                        efficiency_1, efficiency_2, bypass_valve_lr, bypass_valve_hr,
                        collapse_pressure_bar, temp_range, bypass_pressure, updated_at)
                    SELECT DISTINCT ON (oem_no_normalized)
                        oem_no_normalized, oem_no_display, type,
                        product_name_1, product_name_2, product_name_3,
                        remark, d1_mm, d2_mm, d3_mm, h1_mm, h2_mm, h3_mm,
                        d7_thread, d8_thread, media, sealing_material,
                        efficiency_1, efficiency_2, bypass_valve_lr, bypass_valve_hr,
                        collapse_pressure_bar, temp_range, bypass_pressure, now()
                    FROM products_stage
                    ORDER BY oem_no_normalized, ctid DESC
                    ON CONFLICT (oem_no_normalized) DO UPDATE SET
                        oem_no_display = EXCLUDED.oem_no_display,
                        type = EXCLUDED.type,
                        product_name_1 = EXCLUDED.product_name_1,
                        product_name_2 = EXCLUDED.product_name_2,
                        product_name_3 = EXCLUDED.product_name_3,
                        remark = EXCLUDED.remark,
                        d1_mm = EXCLUDED.d1_mm,
                        d2_mm = EXCLUDED.d2_mm,
                        d3_mm = EXCLUDED.d3_mm,
                        h1_mm = EXCLUDED.h1_mm,
                        h2_mm = EXCLUDED.h2_mm,
                        h3_mm = EXCLUDED.h3_mm,
                        d7_thread = EXCLUDED.d7_thread,
                        d8_thread = EXCLUDED.d8_thread,
                        media = EXCLUDED.media,
                        sealing_material = EXCLUDED.sealing_material,
                        efficiency_1 = EXCLUDED.efficiency_1,
                        efficiency_2 = EXCLUDED.efficiency_2,
                        bypass_valve_lr = EXCLUDED.bypass_valve_lr,
                        bypass_valve_hr = EXCLUDED.bypass_valve_hr,
                        collapse_pressure_bar = EXCLUDED.collapse_pressure_bar,
                        temp_range = EXCLUDED.temp_range,
                        bypass_pressure = EXCLUDED.bypass_pressure,
                        updated_at = now();"
            };

            await using (var cmd = new NpgsqlCommand(finalSql, conn))
            {
                cmd.CommandTimeout = 0;  // 1M 可能超 30s
                var swUpsert = System.Diagnostics.Stopwatch.StartNew();
                var affected = await cmd.ExecuteNonQueryAsync(ct);
                swUpsert.Stop();
                _logger.LogInformation("[TIMING] {Mode}: {Ms}ms ({Affected} 行)", mode, swUpsert.ElapsedMilliseconds, affected);
                if (mode == "full-load" || mode == "insert-only")
                {
                    Progress.IncrInsertedBy(affected);
                }
                else
                {
                    // upsert 模式: 简化为"全部计为 updated"(避免 RETURNING 慢)
                    Progress.IncrUpdatedBy(affected);
                }
            }

            // Day 9.9: full-load 性能优化 — CREATE 索引 (INSERT 后, COMMIT 前)
            if (mode == "full-load" && savedIndexes.Count > 0)
            {
                var swRecreate = System.Diagnostics.Stopwatch.StartNew();
                await RecreateIndexesAsync(conn, savedIndexes, ct);
                swRecreate.Stop();
                _logger.LogInformation("[TIMING] CREATE indexes: {Ms}ms ({Count} indexes)", swRecreate.ElapsedMilliseconds, savedIndexes.Count);
            }

            // Day 9.9: 对账 2 — INSERT 影响行数 <= stage 行数 (去重/冲突后应少于等于)
            //   WHY: DISTINCT ON 去重 + ON CONFLICT 会让 affected <= stage_count
            //   skipped = stage_count - affected (被去重/冲突跳过的行)
            var actualAffected = mode == "upsert" ? Progress.Updated : Progress.Inserted;
            if (actualAffected > stageCount)
            {
                var msg = $"数据完整性校验失败: affected={actualAffected} > stage={stageCount} (INSERT 影响行数不应超过 stage 行数)";
                _logger.LogError(msg);
                Progress.Fail(msg);
                _ = Progress.PersistLogAsync("products", mode);
                return Progress;
            }
            var skippedCount = stageCount - actualAffected;
            if (skippedCount > 0)
            {
                Progress.IncrSkippedBy(skippedCount);
                _logger.LogInformation("[AUDIT] products: skipped={Skipped} (去重/冲突, stage={Stage} affected={Affected})", skippedCount, stageCount, actualAffected);
            }

            // 5) 提交事务
            //   Day 9.2: 切换 stage = "committing"
            Progress.SetStage("committing");
            var swCommit = System.Diagnostics.Stopwatch.StartNew();
            await using (var commit = new NpgsqlCommand("COMMIT;", conn))
                await commit.ExecuteNonQueryAsync(ct);
            swCommit.Stop();
            _logger.LogInformation("[TIMING] COMMIT: {Ms}ms", swCommit.ElapsedMilliseconds);

            // 6) 异步推 Meili 索引 (失败入队待补偿,不阻塞导入成功返回)
            //   Day 9.2: 后台任务启动前 stage = "meili-sync" (后续在 SyncSearchIndexAsync 内重置回 idle)
            Progress.SetStage("meili-sync");
            _ = Task.Run(async () =>
            {
                try { await SyncSearchIndexAsync(importStartedAt, CancellationToken.None); }
                catch (Exception ex) { _logger.LogError(ex, "后台 Meili 同步失败"); }
                finally { Progress.SetStage("idle"); }  // Day 9.2: Meili 同步完结,stage 归零
            });

            Progress.Finish("products", mode);
        }
        // Day 9.4: 区分 "用户主动取消" 与 "真异常失败"
        //   Bug: 之前 catch (Exception) 全部走 Fail,导致 CancelActiveTask 后 Npgsql 抛
        //        OperationCanceledException 也会被吞进 "failed" 桶, cancel_reason 落空
        //   Fix: ct.IsCancellationRequested 时调 Cancel (落 cancel_reason + cancelled_at)
        //        其他异常才走 Fail
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Progress.Cancel(_activeCancelReason ?? "用户取消", _activeCancelReasonCode ?? "OTHER");
            _ = Progress.PersistLogAsync("products", mode);  // 不 await: 写日志失败不阻塞取消信号
            _logger.LogInformation("ETL products 任务被用户取消, reason={Reason} code={Code}",
                _activeCancelReason, _activeCancelReasonCode);
        }
        catch (Exception ex)
        {
            Progress.Fail(ex.Message, "products", mode);
            _logger.LogError(ex, "ETL 导入失败");
        }
        finally
        {
            StopSnapshotTimer(broadcastCtx);
            ReleaseActiveCts(cts);
        }

        return Progress;
    }

    /// <summary>
    /// 同步本次导入的 products 到 Meili
    /// 1) 从 products 表查 updated_at >= import_started_at 的所有产品
    /// 2) 构建 ProductIndexDoc 列表
    /// 3) 尝试直接 IndexAsync;失败/超时则批量入队 search_index_pending
    /// 流式分批 (每批 1000) 避免 1M 规模 OOM
    /// </summary>
    private async Task SyncSearchIndexAsync(DateTime importStartedAt, CancellationToken ct)
    {
        _logger.LogInformation("Meili 同步开始 (updated_at >= {Time})", importStartedAt);

        // 1) 创建 scope 拿 scoped 服务
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var meili = scope.ServiceProvider.GetRequiredService<MeiliSearchProvider>();

        // 2) 流式分批:每批 1000 个 OEM -> 从 products 查 (用 updated_at 时间窗)
        //    不再依赖 _affectedOems,避免 COPY 阶段加 lock
        const int batchSize = 1000;
        int directOk = 0, queuedFail = 0;
        long? lastId = null;  // keyset 分页 (updated_at + id 组合保证稳定)
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var query = db.Products.AsNoTracking()
                    .Where(p => p.UpdatedAt >= importStartedAt);
                if (lastId.HasValue) query = query.Where(p => p.Id > lastId.Value);
                var batch = await query.OrderBy(p => p.Id).Take(batchSize)
                    .Select(p => new
                    {
                        p.Id, p.OemNoNormalized, p.OemNoDisplay, p.Remark, p.Type,
                        p.D1Mm, p.D2Mm, p.H1Mm, p.H3Mm, p.Media, p.IsDiscontinued, p.UpdatedAt
                    })
                    .ToListAsync(ct);
                if (batch.Count == 0) break;
                lastId = batch[^1].Id;
                var docs = batch.Select(p => new ProductIndexDoc(
                    p.Id, p.OemNoNormalized, p.OemNoDisplay ?? "", p.Remark, p.Type ?? "UNKNOWN",
                    p.D1Mm, p.D2Mm, p.H3Mm, p.H1Mm, p.Media, p.IsDiscontinued,
                    // Day 9.9: 修复 EnableLegacyTimestampBehavior 下 Kind=Local 导致 DateTimeOffset 构造异常
                    //   WHY: Npgsql.EnableLegacyTimestampBehavior 读 timestamptz 返回 Kind=Local,
                    //        new DateTimeOffset(dt, TimeSpan.Zero) 要求 Kind==Utc, 否则抛 ArgumentException
                    //   修复: SpecifyKind 改为 Utc (不改时间值, 只改 Kind 标记)
                    new DateTimeOffset(DateTime.SpecifyKind(p.UpdatedAt, DateTimeKind.Utc), TimeSpan.Zero).ToUnixTimeSeconds()
                )).ToList();
                try
                {
                    await meili.IndexAsync(docs, ct);
                    Progress.IncrIndexedBy(docs.Count);
                    directOk += docs.Count;
                }
                catch (Exception batchEx)
                {
                    _logger.LogWarning(batchEx, "Meili 批次 (lastId={LastId}) 失败,入队", lastId);
                    await EnqueuePendingBatchAsync(db, docs, ct);
                    Progress.IncrIndexPendingBy(docs.Count);
                    queuedFail += docs.Count;
                }
            }
            _logger.LogInformation("Meili 同步完成: 直接={Direct}, 入队待补偿={Queued}", directOk, queuedFail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Meili 同步整体异常");
        }
    }

    /// <summary>
    /// Meili 不可用时,批量入队 search_index_pending (1000/批,避免单次 SaveChanges 过大)
    /// </summary>
    private static async Task EnqueuePendingBatchAsync(ProductDbContext db, List<ProductIndexDoc> docs, CancellationToken ct)
    {
        const int chunkSize = 1000;
        for (int i = 0; i < docs.Count; i += chunkSize)
        {
            var now = DateTime.UtcNow;
            var chunk = docs.Skip(i).Take(chunkSize).Select(d => new SearchIndexPending
            {
                Operation = "index",
                Payload = JsonSerializer.Serialize(d),
                CreatedAt = now,
                NextRetryAt = now,
                RetryCount = 0
            }).ToList();
            db.SearchIndexPending.AddRange(chunk);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task<Dictionary<string, long>> LoadExistingOemMapAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var map = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var cmd = new NpgsqlCommand("SELECT id, oem_no_normalized FROM products", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            map[reader.GetString(1)] = reader.GetInt64(0);
        }
        return map;
    }

    /// <summary>
    /// 获取 PostgreSQL 事务级 advisory lock,防止多实例并发 ETL
    /// 锁 key = 固定 bigint (每个 ETL 类型不同)
    /// WHY: pg_try_advisory_xact_lock 在事务 commit/rollback 时自动释放,无需手动 unlock
    ///      失败 (锁被另一实例持有) 抛 409 由 API 层转 Conflict
    /// </summary>
    private static async Task<bool> TryAcquireAdvisoryLockAsync(NpgsqlConnection conn, long lockKey, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@k)", conn);
        cmd.Parameters.AddWithValue("k", lockKey);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is bool b && b;
    }

    /// <summary>
    /// 导入交叉引用 (xrefs.jsonl) -> cross_references
    /// JSONL 中 product_oem 是 OEM 字符串,先映射到 product_id
    /// mode: "upsert" (默认, ON CONFLICT DO UPDATE) | "full-load" (TRUNCATE + INSERT)
    ///       | "insert-only" (ON CONFLICT DO NOTHING)
    /// P1.1 (Task 3): startLineNo > 0 时为续读模式, 跳过前 startLineNo 行, 从 startLineNo+1 行开始处理
    ///   - 批次大小: BatchSize (默认 1000)
    ///   - 每批 1 个事务 (BEGIN/COPY/INSERT/COMMIT)
    ///   - 批次间检查 _pausedFlag, 命中则写 checkpoint_id 到 etl_progress_log 后退出
    /// </summary>
    public async Task<EtlProgress> ImportXrefsAsync(string jsonlPath, string mode = "upsert", long startLineNo = 0, CancellationToken ct = default)
    {
        // Day 9.9: _activeCts 下沉
        var cts = AcquireActiveCts("xrefs", ct);
        ct = cts.Token;
        Progress.Reset();
        Progress.Start(jsonlPath);
        // Day 9.2: 文件总行数估算 (前端进度条)
        Progress.SetRowsTotal(EtlProgress.EstimateFileLines(jsonlPath));
        // Day 9.7: 启动跨实例广播 snapshot timer
        var broadcastCtx = StartSnapshotTimerIfNeeded();

        // P1.1 (Task 3): 批次大小, 1000 行/批, 1 批 1 事务, 暂停粒度精确到 1k 行
        const int BatchSize = 1000;
        long lastCommittedBatchId = startLineNo;  // 累计已成功 COMMIT 的总行数 (= checkpoint_id)
        bool isFirstBatch = startLineNo == 0;      // 续读时跳过 full-load 的 TRUNCATE 等特殊处理
        try
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            // Day 7: xrefs 锁 key 7740002
            if (!await TryAcquireAdvisoryLockAsync(conn, 7740002L, ct))
            {
                Progress.Fail("另一 ETL 任务正在跑 (advisory lock 7740002 被占用)");
                _logger.LogWarning("ImportXrefsAsync advisory lock 获取失败");
                return Progress;
            }
            var swMap = System.Diagnostics.Stopwatch.StartNew();
            var oemMap = await LoadExistingOemMapAsync(conn, ct);
            swMap.Stop();
            _logger.LogInformation("[TIMING] xrefs 加载 OEM map: {Ms}ms ({Count} 条)", swMap.ElapsedMilliseconds, oemMap.Count);

            // P1.1: 批次循环 — 每 BatchSize 行 1 个事务, 暂停检查点
            Progress.SetStage("staging");
            using var reader = new StreamReader(jsonlPath);
            // P1.1: 跳过前 startLineNo 行 (resume 续读)
            if (startLineNo > 0)
            {
                _logger.LogInformation("ETL xrefs Resume: 跳过前 {StartLineNo} 行", startLineNo);
                for (long i = 0; i < startLineNo; i++)
                {
                    if (await reader.ReadLineAsync(ct) == null) break;
                }
            }
            // P1.1: 续读时 full-load 不能 TRUNCATE (会清掉首批已写入的数据), 强制转为 upsert
            if (!isFirstBatch && mode == "full-load")
            {
                _logger.LogWarning("ETL xrefs Resume 时 full-load 模式不安全 (会清掉已写入数据), 强制转为 upsert");
                mode = "upsert";
            }
            var batchLines = new List<string>(BatchSize);
            string? line;
            long lineNo = startLineNo;  // 全局行号 (含已跳过的)
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                batchLines.Add(line);
                lineNo++;
                if (batchLines.Count >= BatchSize)
                {
                    // P1.1 (Task 3) 修复: ProcessXrefBatchAsync 现在返回 per-batch affected (4-tuple)
                    //   此前 3-tuple + 后续用 cumulative Progress 算 dup, 公式错误 (dup 翻倍)
                    //   正确 dup = stageCount - affected (本批) — 与 ImportAppsAsync 一致
                    var (batchMissing, batchErrors, xrefStageCount, batchAffected) = await ProcessXrefBatchAsync(
                        conn, batchLines, oemMap, mode, isFirstBatch, ct);
                    lastCommittedBatchId += batchLines.Count;
                    Progress.IncrReadBy(batchLines.Count);
                    // dup = stageCount - affected (DISTINCT ON 去重 + ON CONFLICT 跳过)
                    var dup = xrefStageCount - batchAffected;
                    if (dup > 0) for (long i = 0; i < dup; i++) Progress.IncrSkippedDuplicate();
                    batchLines.Clear();
                    isFirstBatch = false;
                    _logger.LogDebug("[BATCH] xrefs 已 commit 累计 {Count} 行, checkpoint={Cp}, dup={Dup}", lastCommittedBatchId, lastCommittedBatchId, dup);
                    // P1.1: 批次间检查暂停
                    if (IsPausedRequested())
                    {
                        await PersistPausedLogAsync("xrefs", mode, lastCommittedBatchId, ct);
                        Progress.SetStage("paused");
                        Progress.Pause();  // P1.1: 同步 _status = "paused" 让前端 SSE 看到 paused 状态
                        _logger.LogInformation("ETL xrefs 已暂停, checkpoint_id={Cp}, 下次 Resume 从 {Next} 行开始", lastCommittedBatchId, lastCommittedBatchId + 1);
                        return Progress;
                    }
                }
            }
            // 处理剩余 (最后一批 < BatchSize)
            if (batchLines.Count > 0)
            {
                // P1.1 (Task 3) 修复: 同样 4-tuple
                var (batchMissing, batchErrors, xrefStageCount, batchAffected) = await ProcessXrefBatchAsync(
                    conn, batchLines, oemMap, mode, isFirstBatch, ct);
                lastCommittedBatchId += batchLines.Count;
                Progress.IncrReadBy(batchLines.Count);
                var dup = xrefStageCount - batchAffected;
                if (dup > 0) for (long i = 0; i < dup; i++) Progress.IncrSkippedDuplicate();
                isFirstBatch = false;
            }
            Progress.Finish("xrefs", mode);
        }
        // Day 9.4: 区分取消与失败 (同 products, 见 ImportProductsAsync 注释)
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Progress.Cancel(_activeCancelReason ?? "用户取消", _activeCancelReasonCode ?? "OTHER");
            _ = Progress.PersistLogAsync("xrefs", mode);
            _logger.LogInformation("ETL xrefs 任务被用户取消, reason={Reason} code={Code}",
                _activeCancelReason, _activeCancelReasonCode);
        }
        catch (Exception ex)
        {
            Progress.Fail(ex.Message, "xrefs", mode);
            _logger.LogError(ex, "xrefs 导入失败");
        }
        finally
        {
            StopSnapshotTimer(broadcastCtx);
            ReleaseActiveCts(cts);
        }
        return Progress;
    }

    /// <summary>
    /// P1.1 (Task 3): 处理 xrefs 1 批 (BatchSize 行): BEGIN → COPY → INSERT → COMMIT
    ///   - 每批 1 个独立事务, 暂停粒度 = 1 批
    ///   - staging 改用 regular table (非 TEMP), TRUNCATE 每批开头清空
    ///   - 续读模式: isFirstBatch=false 时跳过 full-load 的 TRUNCATE cross_references
    /// </summary>
    private async Task<(long missing, long errors, long stageCount, long affected)> ProcessXrefBatchAsync(
        NpgsqlConnection conn,
        List<string> batchLines,
        Dictionary<string, long> oemMap,
        string mode,
        bool isFirstBatch,
        CancellationToken ct)
    {
        long missing = 0;
        long errors = 0;
        long stageCount = 0;
        long batchAffected = 0;  // P1.1 (Task 3): 本批 INSERT 实际影响行数, 用于 dup 计算

        await using (var begin = new NpgsqlCommand("BEGIN;", conn))
            await begin.ExecuteNonQueryAsync(ct);
        try
        {
            // P1.1: 用 regular table 而非 TEMP, 跨批持久 (每批 TRUNCATE 即可, 不用 CREATE/DROP)
            await using (var ensureStage = new NpgsqlCommand(@"
                CREATE TABLE IF NOT EXISTS xrefs_stage (
                    product_id BIGINT,
                    product_name_1 VARCHAR(100),
                    oem_brand VARCHAR(100),
                    oem_no_3 VARCHAR(100)
                );", conn))
                await ensureStage.ExecuteNonQueryAsync(ct);
            await using (var trunc = new NpgsqlCommand("TRUNCATE xrefs_stage", conn))
                await trunc.ExecuteNonQueryAsync(ct);

            // COPY 写入 staging
            await using (var writer = await conn.BeginBinaryImportAsync(@"
                COPY xrefs_stage (product_id, product_name_1, oem_brand, oem_no_3) FROM STDIN (FORMAT BINARY)
            ", ct))
            {
                foreach (var jsonLine in batchLines)
                {
                    try
                    {
                        var doc = JsonSerializer.Deserialize<JsonElement>(jsonLine);
                        var oem = doc.GetProperty("product_oem").GetString();
                        if (oem is null || !oemMap.TryGetValue(oem, out var pid))
                        {
                            missing++;
                            Progress.IncrSkippedMissingOem();
                            continue;
                        }
                        await writer.StartRowAsync(ct);
                        await writer.WriteAsync(pid, NpgsqlDbType.Bigint, ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "product_name_1"), ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "oem_brand"), ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "oem_no_3"), ct);
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        Progress.IncrErrors();
                        _logger.LogWarning("xrefs 批次内行解析失败: {Error}", ex.Message);
                    }
                }
                await writer.CompleteAsync(ct);
            }

            // 校验 stage 行数
            await using (var countCmd = new NpgsqlCommand("SELECT count(*) FROM xrefs_stage", conn))
                stageCount = (long)(await countCmd.ExecuteScalarAsync(ct))!;

            // full-load 仅在首批 TRUNCATE, 续读模式强制 upsert
            string truncClause = (isFirstBatch && mode == "full-load") ? "TRUNCATE cross_references;" : "";
            string finalSql = mode switch
            {
                "full-load" => $@"
                    {truncClause}
                    INSERT INTO cross_references (product_id, product_name_1, oem_brand, oem_no_3, created_at)
                    SELECT DISTINCT ON (product_id, oem_brand, oem_no_3)
                        product_id, product_name_1, oem_brand, oem_no_3, now()
                    FROM xrefs_stage
                    WHERE oem_brand IS NOT NULL AND oem_no_3 IS NOT NULL
                    ORDER BY product_id, oem_brand, oem_no_3, ctid DESC;",
                "insert-only" => @"
                    INSERT INTO cross_references (product_id, product_name_1, oem_brand, oem_no_3, created_at)
                    SELECT DISTINCT ON (product_id, oem_brand, oem_no_3)
                        product_id, product_name_1, oem_brand, oem_no_3, now()
                    FROM xrefs_stage
                    WHERE oem_brand IS NOT NULL AND oem_no_3 IS NOT NULL
                    ORDER BY product_id, oem_brand, oem_no_3, ctid DESC
                    ON CONFLICT (product_id, oem_brand, oem_no_3) WHERE oem_brand IS NOT NULL AND oem_no_3 IS NOT NULL DO NOTHING;",
                _ => @"  -- upsert 默认
                    INSERT INTO cross_references (product_id, product_name_1, oem_brand, oem_no_3, created_at)
                    SELECT DISTINCT ON (product_id, oem_brand, oem_no_3)
                        product_id, product_name_1, oem_brand, oem_no_3, now()
                    FROM xrefs_stage
                    WHERE oem_brand IS NOT NULL AND oem_no_3 IS NOT NULL
                    ORDER BY product_id, oem_brand, oem_no_3, ctid DESC
                    ON CONFLICT (product_id, oem_brand, oem_no_3) WHERE oem_brand IS NOT NULL AND oem_no_3 IS NOT NULL DO UPDATE SET
                        product_name_1 = EXCLUDED.product_name_1,
                        created_at = now();"
            };

            await using (var insert = new NpgsqlCommand(finalSql, conn))
            {
                insert.CommandTimeout = 0;
                var affected = await insert.ExecuteNonQueryAsync(ct);
                if (mode == "full-load" || mode == "insert-only")
                    Progress.IncrInsertedBy(affected);
                else
                    Progress.IncrUpdatedBy(affected);
                // P1.1 (Task 3) 修复: 直接把 affected 传出,避免调用方用 cumulative Progress
                //   算 per-batch dup (此前公式 dup = 2*stageCount - xrefAffected - missing - errors 错误翻倍)
                //   正确公式: dup = stageCount - affected (DISTINCT ON 去重 + ON CONFLICT 跳过)
                batchAffected = affected;
            }

            await using (var commit = new NpgsqlCommand("COMMIT;", conn))
                await commit.ExecuteNonQueryAsync(ct);

            // 清理 staging (任务彻底完成时)
            //   不在每批清理, 保留 stageCount 校验后再删, 避免任务中断时残留
            //   ETL 正常完成由调用方负责清理
        }
        catch
        {
            // 任意异常 ROLLBACK 当前批, 不影响已 commit 的前批
            try
            {
                await using var rb = new NpgsqlCommand("ROLLBACK;", conn);
                await rb.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch { }
            throw;
        }
        return (missing, errors, stageCount, batchAffected);
    }

    /// <summary>
    /// P1.1 (Task 3): ETL 暂停时持久化日志到 etl_progress_log
    ///   - status = 'paused' 区别于 completed/failed/cancelled
    ///   - checkpoint_id = lastCommittedBatchId
    /// </summary>
    private async Task PersistPausedLogAsync(string entityType, string mode, long checkpointId, CancellationToken ct)
    {
        try
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            var log = new EtlProgressLog
            {
                EntityType = entityType,
                Mode = mode,
                FilePath = Progress.CurrentFile ?? "",
                Status = "paused",
                ReadCount = Progress.Read,
                InsertedCount = Progress.Inserted,
                UpdatedCount = Progress.Updated,
                SkippedCount = Progress.Skipped,
                SkippedMissingOem = Progress.SkippedMissingOem,
                SkippedNullField = Progress.SkippedNullField,
                SkippedDuplicate = Progress.SkippedDuplicate,
                ErrorCount = Progress.Errors,
                IndexedCount = Progress.Indexed,
                IndexPendingCount = Progress.IndexPending,
                LastError = null,
                StartedAt = Progress.StartedAt ?? DateTime.UtcNow,
                FinishedAt = DateTime.UtcNow,
                DurationSec = Progress.Elapsed?.TotalSeconds ?? 0,
                CancelReason = null,
                CancelledAt = null,
                ReasonCode = null,
                CheckpointId = checkpointId
            };
            db.EtlProgressLogs.Add(log);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("ETL {Entity} 暂停日志落库: id={Id} checkpoint_id={Cp}", entityType, log.Id, checkpointId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ETL {Entity} 暂停日志落库失败", entityType);
        }
    }

    /// <summary>
    /// P1.1 (Task 3): ETL 暂停后, 找到刚写的 completed 状态行 (Progress.Finish 触发), 改为 paused
    ///   - 简化设计: 不另外写 paused 行, 而是把 Progress.Finish 写的 completed 行改成 paused
    ///   - 这种实现减少了日志表双写
    /// </summary>
    private async Task UpdateLastLogToPausedAsync(long checkpointId, CancellationToken ct)
    {
        try
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            var last = await db.EtlProgressLogs
                .OrderByDescending(l => l.Id)
                .FirstOrDefaultAsync(ct);
            if (last != null && last.Status == "completed")
            {
                last.Status = "paused";
                last.CheckpointId = checkpointId;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ETL 暂停状态更新失败");
        }
    }

    /// <summary>
    /// 导入机型适配 (apps.jsonl) -> machine_applications
    /// mode: "upsert" (默认) | "full-load" | "insert-only"
    /// P1.1 (Task 3): startLineNo 参数与 xrefs 对齐 — apps 暂不实现批次, 传 0 即可
    /// </summary>
    public async Task<EtlProgress> ImportAppsAsync(string jsonlPath, string mode = "upsert", long startLineNo = 0, CancellationToken ct = default)
    {
        // Day 9.9: _activeCts 下沉
        var cts = AcquireActiveCts("apps", ct);
        ct = cts.Token;
        Progress.Reset();
        Progress.Start(jsonlPath);
        // Day 9.2: 文件总行数估算 (前端进度条)
        Progress.SetRowsTotal(EtlProgress.EstimateFileLines(jsonlPath));
        // Day 9.7: 启动跨实例广播 snapshot timer
        var broadcastCtx = StartSnapshotTimerIfNeeded();
        try
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            // Day 7: apps 锁 key 7740003
            if (!await TryAcquireAdvisoryLockAsync(conn, 7740003L, ct))
            {
                Progress.Fail("另一 ETL 任务正在跑 (advisory lock 7740003 被占用)", "apps", mode);
                _logger.LogWarning("ImportAppsAsync advisory lock 获取失败");
                return Progress;
            }
            var swMap = System.Diagnostics.Stopwatch.StartNew();
            var oemMap = await LoadExistingOemMapAsync(conn, ct);
            swMap.Stop();
            _logger.LogInformation("[TIMING] apps 加载 OEM map: {Ms}ms ({Count} 条)", swMap.ElapsedMilliseconds, oemMap.Count);

            await using (var begin = new NpgsqlCommand("BEGIN;", conn))
                await begin.ExecuteNonQueryAsync(ct);
            await using (var create = new NpgsqlCommand(@"
                CREATE TEMP TABLE apps_stage (
                    product_id BIGINT,
                    machine_brand VARCHAR(200),
                    machine_model VARCHAR(200),
                    model_name VARCHAR(100),
                    engine_brand VARCHAR(100),
                    engine_type VARCHAR(100),
                    engine_energy VARCHAR(50),
                    production_date_start DATE,
                    is_ongoing BOOLEAN
                ) ON COMMIT DROP;
            ", conn))
                await create.ExecuteNonQueryAsync(ct);

            var swCopy = System.Diagnostics.Stopwatch.StartNew();
            var lineNo = 0;
            long missing = 0;
            await using (var writer = await conn.BeginBinaryImportAsync(@"
                COPY apps_stage (product_id, machine_brand, machine_model, model_name,
                    engine_brand, engine_type, engine_energy, production_date_start, is_ongoing)
                FROM STDIN (FORMAT BINARY)
            ", ct))
            {
                using var reader = new StreamReader(jsonlPath);
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    lineNo++;
                    if (lineNo % 1000 == 0) ct.ThrowIfCancellationRequested();
                    Progress.IncrRead();
                    try
                    {
                        var doc = JsonSerializer.Deserialize<JsonElement>(line);
                        var oem = doc.GetProperty("product_oem").GetString();
                        if (oem is null || !oemMap.TryGetValue(oem, out var pid))
                        {
                            missing++;
                            Progress.IncrSkippedMissingOem();  // Day 7.5
                            continue;
                        }
                        // Day 7.5: 必填字段预检 (SQL 的 WHERE machine_brand IS NOT NULL 静默过滤在这里显式化)
                        // apps ETL 在 COPY 阶段就拦下,免得下游 SQL 默默丢行
                        var brand = GetStringOrNull(doc, "machine_brand");
                        var model = GetStringOrNull(doc, "machine_model");
                        if (brand is null || model is null)
                        {
                            Progress.IncrSkippedNullField();  // Day 7.5
                            _logger.LogDebug("apps 行 {LineNo} 必填字段空: brand={Brand}, model={Model}", lineNo, brand ?? "null", model ?? "null");
                            continue;
                        }
                        await writer.StartRowAsync(ct);
                        await writer.WriteAsync(pid, NpgsqlDbType.Bigint, ct);
                        await writer.WriteAsync(brand, NpgsqlDbType.Varchar, ct);
                        await writer.WriteAsync(model, NpgsqlDbType.Varchar, ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "model_name"), ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "engine_brand"), ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "engine_type"), ct);
                        await WriteNullableStringAsync(writer, GetStringOrNull(doc, "engine_energy"), ct);
                        await WriteNullableDateAsync(writer, GetDateOrNull(doc, "production_date_start"), ct);
                        await writer.WriteAsync(GetBoolOrFalse(doc, "is_ongoing"), NpgsqlDbType.Boolean, ct);
                    }
                    catch (Exception ex)
                    {
                        Progress.IncrErrorsWith($"apps 行 {lineNo}: {ex.Message}");  // Day 7.6
                        _logger.LogWarning("apps 行 {LineNo} 解析失败: {Error}", lineNo, ex.Message);
                    }
                }
                await writer.CompleteAsync(ct);
            }
            swCopy.Stop();
            _logger.LogInformation("[TIMING] apps staging COPY: {Ms}ms ({Count} 行, skipped={Skipped})", swCopy.ElapsedMilliseconds, Progress.Read, missing);

            // Day 9.9: 数据完整性校验 — COPY 后查 stage 行数, 防止静默丢行
            long appStageCount;
            await using (var appCountCmd = new NpgsqlCommand("SELECT count(*) FROM apps_stage", conn))
                appStageCount = (long)(await appCountCmd.ExecuteScalarAsync(ct))!;
            _logger.LogInformation("[AUDIT] apps_stage: read={Read} stage={Stage} errors={Errors} missingOem={Missing}", Progress.Read, appStageCount, Progress.Errors, Progress.SkippedMissingOem);
            if (appStageCount + Progress.Errors + Progress.SkippedMissingOem != Progress.Read)
            {
                var msg = $"数据完整性校验失败: read={Progress.Read} stage={appStageCount} errors={Progress.Errors} missingOem={Progress.SkippedMissingOem} (期望 stage+errors+missingOem=read)";
                _logger.LogError(msg);
                Progress.Fail(msg);
                _ = Progress.PersistLogAsync("apps", mode);
                return Progress;
            }

            // Day 9.10: 删除 pre-INSERT 的 COUNT(DISTINCT) 统计查询 (同 xrefs,改用 stage_count - affected 推算)

            // Day 7: 加 mode + DISTINCT ON + ON CONFLICT
            string finalSql = mode switch
            {
                "full-load" => @"
                    TRUNCATE machine_applications;
                    INSERT INTO machine_applications (product_id, machine_brand, machine_model, model_name,
                        engine_brand, engine_type, engine_energy, production_date_start, is_ongoing, created_at)
                    SELECT DISTINCT ON (product_id, machine_brand, machine_model)
                        product_id, machine_brand, machine_model, model_name,
                        engine_brand, engine_type, engine_energy, production_date_start, is_ongoing, now()
                    FROM apps_stage
                    WHERE machine_brand IS NOT NULL AND machine_model IS NOT NULL
                    ORDER BY product_id, machine_brand, machine_model, ctid DESC;",
                "insert-only" => @"
                    INSERT INTO machine_applications (product_id, machine_brand, machine_model, model_name,
                        engine_brand, engine_type, engine_energy, production_date_start, is_ongoing, created_at)
                    SELECT DISTINCT ON (product_id, machine_brand, machine_model)
                        product_id, machine_brand, machine_model, model_name,
                        engine_brand, engine_type, engine_energy, production_date_start, is_ongoing, now()
                    FROM apps_stage
                    WHERE machine_brand IS NOT NULL AND machine_model IS NOT NULL
                    ORDER BY product_id, machine_brand, machine_model, ctid DESC
                    ON CONFLICT (product_id, machine_brand, machine_model) WHERE machine_brand IS NOT NULL AND machine_model IS NOT NULL DO NOTHING;",
                _ => @"
                    INSERT INTO machine_applications (product_id, machine_brand, machine_model, model_name,
                        engine_brand, engine_type, engine_energy, production_date_start, is_ongoing, created_at)
                    SELECT DISTINCT ON (product_id, machine_brand, machine_model)
                        product_id, machine_brand, machine_model, model_name,
                        engine_brand, engine_type, engine_energy, production_date_start, is_ongoing, now()
                    FROM apps_stage
                    WHERE machine_brand IS NOT NULL AND machine_model IS NOT NULL
                    ORDER BY product_id, machine_brand, machine_model, ctid DESC
                    ON CONFLICT (product_id, machine_brand, machine_model) WHERE machine_brand IS NOT NULL AND machine_model IS NOT NULL DO UPDATE SET
                        model_name = EXCLUDED.model_name,
                        engine_brand = EXCLUDED.engine_brand,
                        engine_type = EXCLUDED.engine_type,
                        engine_energy = EXCLUDED.engine_energy,
                        production_date_start = EXCLUDED.production_date_start,
                        is_ongoing = EXCLUDED.is_ongoing,
                        created_at = now();"
            };

            var swInsert = System.Diagnostics.Stopwatch.StartNew();
            await using (var insert = new NpgsqlCommand(finalSql, conn))
            {
                insert.CommandTimeout = 0;
                var affected = await insert.ExecuteNonQueryAsync(ct);
                swInsert.Stop();
                _logger.LogInformation("[TIMING] apps {Mode}: {Ms}ms ({Affected} 行)", mode, swInsert.ElapsedMilliseconds, affected);
                if (mode == "full-load" || mode == "insert-only")
                    Progress.IncrInsertedBy(affected);
                else
                    Progress.IncrUpdatedBy(affected);
            }

            // Day 9.9: 对账 2 — INSERT 影响行数 <= stage 行数 (去重/冲突后应少于等于)
            var appAffected = mode == "upsert" ? Progress.Updated : Progress.Inserted;
            if (appAffected > appStageCount)
            {
                var msg = $"数据完整性校验失败: apps affected={appAffected} > stage={appStageCount}";
                _logger.LogError(msg);
                Progress.Fail(msg);
                _ = Progress.PersistLogAsync("apps", mode);
                return Progress;
            }

            // Day 9.10: 用 stage_count - affected 推算 skipped_duplicate (同 xrefs)
            //   注意: 不能用 IncrSkippedBy(dup) + IncrSkippedDuplicate(),后者内部已调 IncrSkipped,会翻倍
            var appDup = appStageCount - appAffected;
            if (appDup > 0)
            {
                for (long i = 0; i < appDup; i++) Progress.IncrSkippedDuplicate();
                _logger.LogInformation("apps 去重/冲突: {Dup} 行 (stage={Stage} affected={Affected})", appDup, appStageCount, appAffected);
            }

            await using (var commit = new NpgsqlCommand("COMMIT;", conn))
                await commit.ExecuteNonQueryAsync(ct);
            Progress.Finish("apps", mode);
        }
        // Day 9.4: 区分取消与失败 (同 products, 见 ImportProductsAsync 注释)
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Progress.Cancel(_activeCancelReason ?? "用户取消", _activeCancelReasonCode ?? "OTHER");
            _ = Progress.PersistLogAsync("apps", mode);
            _logger.LogInformation("ETL apps 任务被用户取消, reason={Reason} code={Code}",
                _activeCancelReason, _activeCancelReasonCode);
        }
        catch (Exception ex)
        {
            Progress.Fail(ex.Message, "apps", mode);
            _logger.LogError(ex, "apps 导入失败");
        }
        finally
        {
            StopSnapshotTimer(broadcastCtx);
            ReleaseActiveCts(cts);
        }
        return Progress;
    }

    private static async Task CreateStagingTableAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var begin = new NpgsqlCommand("BEGIN;", conn);
        await begin.ExecuteNonQueryAsync(ct);

        await using var create = new NpgsqlCommand(@"
            CREATE TEMP TABLE products_stage (
                oem_no_normalized VARCHAR(50),
                oem_no_display VARCHAR(50),
                type VARCHAR(50),
                product_name_1 VARCHAR(100),
                product_name_2 VARCHAR(100),
                product_name_3 VARCHAR(100),
                remark TEXT,
                d1_mm NUMERIC(8,2), d2_mm NUMERIC(8,2), d3_mm NUMERIC(8,2),
                h1_mm NUMERIC(8,2), h2_mm NUMERIC(8,2), h3_mm NUMERIC(8,2),
                d7_thread VARCHAR(50), d8_thread VARCHAR(50),
                media VARCHAR(100), sealing_material VARCHAR(100),
                efficiency_1 VARCHAR(100), efficiency_2 VARCHAR(100),
                bypass_valve_lr NUMERIC, bypass_valve_hr NUMERIC,
                collapse_pressure_bar NUMERIC, temp_range VARCHAR(50),
                bypass_pressure NUMERIC
            ) ON COMMIT DROP;
        ", conn);
        await create.ExecuteNonQueryAsync(ct);
    }

    private static string? GetStringOrNull(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static decimal? GetDecimalOrNull(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        return v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;
    }

    private static DateTime? GetDateOrNull(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        var s = v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParse(s, out var dt) ? dt : null;
    }

    private static bool GetBoolOrFalse(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null) return false;
        return v.ValueKind == JsonValueKind.True
            || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b);
    }

    private static async Task WriteNullableStringAsync(NpgsqlBinaryImporter w, string? s, CancellationToken ct)
    {
        if (s is null) await w.WriteNullAsync(ct);
        else await w.WriteAsync(s, NpgsqlDbType.Varchar, ct);
    }

    private static async Task WriteNullableDecimalAsync(NpgsqlBinaryImporter w, decimal? d, CancellationToken ct)
    {
        if (d is null) await w.WriteNullAsync(ct);
        else await w.WriteAsync(d.Value, NpgsqlDbType.Numeric, ct);
    }

    private static async Task WriteNullableDateAsync(NpgsqlBinaryImporter w, DateTime? d, CancellationToken ct)
    {
        if (d is null) await w.WriteNullAsync(ct);
        else await w.WriteAsync(d.Value, NpgsqlDbType.Date, ct);
    }
}
