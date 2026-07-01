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
    public void IncrInserted() => Interlocked.Increment(ref _inserted);
    public void IncrUpdated() => Interlocked.Increment(ref _updated);
    public void IncrInsertedBy(long n) => Interlocked.Add(ref _inserted, n);
    public void IncrUpdatedBy(long n) => Interlocked.Add(ref _updated, n);
    public void IncrSkipped() => Interlocked.Increment(ref _skipped);
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

    // Day 7.8: 改用 IOptions<EtlOptions> 注入 (替代手动 IConfiguration 读取)
    //   WHY: 配置校验集中在 EtlOptionsValidator,启动失败立即可见,不必运行期才发现
    public EtlImportService(
        string connectionString,
        ILogger<EtlImportService> logger,
        IServiceProvider sp,
        IOptions<EtlOptions> etlOptions)
    {
        _pgConn = connectionString;
        _logger = logger;
        _sp = sp;
        _options = etlOptions.Value;
        Progress = new EtlProgress(logger, _options.RecentErrorBuffer, sp);
    }

    // ========== Day 8.4 手动触发 + 进度查询 ==========

    /// <summary>
    /// 手动触发 ETL (后台 ETL 页面 "立即导入" 按钮调用)
    /// entityType: products / xrefs / apps
    /// mode: full-load / insert-only / upsert
    /// </summary>
    public async Task<EtlProgress> TriggerAsync(string entityType, string jsonlPath, string mode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jsonlPath))
            throw new ArgumentException("jsonlPath 不能为空");
        if (!File.Exists(jsonlPath))
            throw new FileNotFoundException($"JSONL 文件不存在: {jsonlPath}");

        var normalizedMode = NormalizeMode(mode);
        var normalizedEntity = entityType?.Trim().ToLowerInvariant() ?? "";

        // Day 9.1: 抢占式单任务: 若已有 ETL 在跑, 抛 InvalidOperationException
        //   WHY: Import*Async 内部修改 Progress (单例状态), 并发跑会互相覆盖计数
        CancellationTokenSource cts;
        lock (_ctsLock)
        {
            if (_activeCts != null && !_activeCts.IsCancellationRequested)
                throw new InvalidOperationException($"已有 ETL 任务在运行 (entity={_activeTaskEntity}), 请先等待完成或调用 /api/admin/etl/task 取消");
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _activeCts = cts;
            _activeTaskEntity = normalizedEntity;
        }

        try
        {
            return normalizedEntity switch
            {
                "products" or "product" => await ImportProductsAsync(jsonlPath, normalizedMode, cts.Token),
                "xrefs" or "xref" or "cross_references" => await ImportXrefsAsync(jsonlPath, normalizedMode, cts.Token),
                "apps" or "machine_applications" => await ImportAppsAsync(jsonlPath, normalizedMode, cts.Token),
                _ => throw new ArgumentException($"未知 entityType={entityType}, 期望 products/xrefs/apps")
            };
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Day 9.4: ImportXxxAsync 内部 catch 已处理日志落库 + Progress.Cancel,
            //   这里仅上抛给调用方 (HTTP layer 看到 4xx 或 200 都行,触发链路已结束)
            //   WHY 不要再 PersistLogAsync: 写两条 cancelled 日志会让历史分析重复计数
            _logger.LogInformation("ETL 任务已取消, 调用方准备返回 entity={Entity}", normalizedEntity);
            throw;
        }
        finally
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
    }

    /// <summary>
    /// Day 9.1: 取消当前活跃 ETL 任务
    /// - 若没有活跃任务, 返回 false
    /// - 取消信号传播到 Import*Async 内部 COPY/INSERT, 任务会抛 OperationCanceledException
    /// - 已插入/更新的数据会保留 (不事务回滚, 因为 COPY 阶段无显式事务)
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
    /// </summary>
    public async Task<EtlProgress> ImportProductsAsync(string jsonlPath, string mode = "upsert", CancellationToken ct = default)
    {
        Progress.Reset();
        Progress.Start(jsonlPath);
        // Day 9.2: 估算文件总行数 (前端进度条分母, 启动 < 1ms)
        Progress.SetRowsTotal(EtlProgress.EstimateFileLines(jsonlPath));
        var importStartedAt = Progress.StartedAt ?? DateTime.UtcNow;

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
                COPY products_stage (oem_no_normalized, oem_no_display, type, product_name_3,
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

            // 4) 根据 mode 选择导入策略
            //    Day 9.2: 切换 stage = "inserting" 给前端精细化显示
            //    WHY 在这里 (而不是 ExecuteNonQueryAsync 之后): 用户感知 "INSERT 写库" 是从 cmd 启动开始
            Progress.SetStage("inserting");
            //    full-load: TRUNCATE + INSERT (首次全量, 5s 内完成 1M)
            //    insert-only: INSERT ON CONFLICT DO NOTHING (只插新行,不更新已有)
            //    upsert: 完整 INSERT ON CONFLICT DO UPDATE (默认,慢但最完整)
            string finalSql = mode switch
            {
                // WHY: RESTART IDENTITY 让 serial 列从 1 重新开始,首次全量场景下 id 连续
                //      CASCADE 防御性写法,即使未来加 FK 也不会破坏 ETL
                //      Day 7 修复: 同时清 cross_references/machine_applications 避免孤儿行 (无 FK 约束时不会失败)
                "full-load" => @"
                    TRUNCATE products, cross_references, machine_applications RESTART IDENTITY CASCADE;
                    INSERT INTO products (oem_no_normalized, oem_no_display, type, product_name_3,
                        remark, d1_mm, d2_mm, d3_mm, h1_mm, h2_mm, h3_mm,
                        d7_thread, d8_thread, media, sealing_material,
                        efficiency_1, efficiency_2, bypass_valve_lr, bypass_valve_hr,
                        collapse_pressure_bar, temp_range, bypass_pressure, updated_at)
                    SELECT DISTINCT ON (oem_no_normalized)
                        oem_no_normalized, oem_no_display, type, product_name_3,
                        remark, d1_mm, d2_mm, d3_mm, h1_mm, h2_mm, h3_mm,
                        d7_thread, d8_thread, media, sealing_material,
                        efficiency_1, efficiency_2, bypass_valve_lr, bypass_valve_hr,
                        collapse_pressure_bar, temp_range, bypass_pressure, now()
                    FROM products_stage
                    ORDER BY oem_no_normalized, ctid DESC;",
                "insert-only" => @"
                    INSERT INTO products (oem_no_normalized, oem_no_display, type, product_name_3,
                        remark, d1_mm, d2_mm, d3_mm, h1_mm, h2_mm, h3_mm,
                        d7_thread, d8_thread, media, sealing_material,
                        efficiency_1, efficiency_2, bypass_valve_lr, bypass_valve_hr,
                        collapse_pressure_bar, temp_range, bypass_pressure, updated_at)
                    SELECT DISTINCT ON (oem_no_normalized)
                        oem_no_normalized, oem_no_display, type, product_name_3,
                        remark, d1_mm, d2_mm, d3_mm, h1_mm, h2_mm, h3_mm,
                        d7_thread, d8_thread, media, sealing_material,
                        efficiency_1, efficiency_2, bypass_valve_lr, bypass_valve_hr,
                        collapse_pressure_bar, temp_range, bypass_pressure, now()
                    FROM products_stage
                    ORDER BY oem_no_normalized, ctid DESC
                    ON CONFLICT (oem_no_normalized) DO NOTHING;",
                _ => @"
                    INSERT INTO products (oem_no_normalized, oem_no_display, type, product_name_3,
                        remark, d1_mm, d2_mm, d3_mm, h1_mm, h2_mm, h3_mm,
                        d7_thread, d8_thread, media, sealing_material,
                        efficiency_1, efficiency_2, bypass_valve_lr, bypass_valve_hr,
                        collapse_pressure_bar, temp_range, bypass_pressure, updated_at)
                    SELECT DISTINCT ON (oem_no_normalized)
                        oem_no_normalized, oem_no_display, type, product_name_3,
                        remark, d1_mm, d2_mm, d3_mm, h1_mm, h2_mm, h3_mm,
                        d7_thread, d8_thread, media, sealing_material,
                        efficiency_1, efficiency_2, bypass_valve_lr, bypass_valve_hr,
                        collapse_pressure_bar, temp_range, bypass_pressure, now()
                    FROM products_stage
                    ORDER BY oem_no_normalized, ctid DESC
                    ON CONFLICT (oem_no_normalized) DO UPDATE SET
                        oem_no_display = EXCLUDED.oem_no_display,
                        type = EXCLUDED.type,
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
            Progress.PersistLogAsync("products", mode);  // 不 await: 写日志失败不阻塞取消信号
            _logger.LogInformation("ETL products 任务被用户取消, reason={Reason} code={Code}",
                _activeCancelReason, _activeCancelReasonCode);
        }
        catch (Exception ex)
        {
            Progress.Fail(ex.Message, "products", mode);
            _logger.LogError(ex, "ETL 导入失败");
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
                    new DateTimeOffset(p.UpdatedAt, TimeSpan.Zero).ToUnixTimeSeconds()
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
    /// </summary>
    public async Task<EtlProgress> ImportXrefsAsync(string jsonlPath, string mode = "upsert", CancellationToken ct = default)
    {
        Progress.Reset();
        Progress.Start(jsonlPath);
        // Day 9.2: 文件总行数估算 (前端进度条)
        Progress.SetRowsTotal(EtlProgress.EstimateFileLines(jsonlPath));
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

            await using (var begin = new NpgsqlCommand("BEGIN;", conn))
                await begin.ExecuteNonQueryAsync(ct);
            await using (var create = new NpgsqlCommand(@"
                CREATE TEMP TABLE xrefs_stage (
                    product_id BIGINT,
                    product_name_1 VARCHAR(100),
                    oem_brand VARCHAR(100),
                    oem_no_3 VARCHAR(100)
                ) ON COMMIT DROP;
            ", conn))
                await create.ExecuteNonQueryAsync(ct);

            var swCopy = System.Diagnostics.Stopwatch.StartNew();
            var lineNo = 0;
            long missing = 0;
            await using (var writer = await conn.BeginBinaryImportAsync(@"
                COPY xrefs_stage (product_id, product_name_1, oem_brand, oem_no_3) FROM STDIN (FORMAT BINARY)
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
                            Progress.IncrSkippedMissingOem();  // Day 7.5: 区分原因
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
                        Progress.IncrErrors();
                        _logger.LogWarning("xrefs 行 {LineNo} 解析失败: {Error}", lineNo, ex.Message);
                    }
                }
                await writer.CompleteAsync(ct);
            }
            swCopy.Stop();
            _logger.LogInformation("[TIMING] xrefs staging COPY: {Ms}ms ({Count} 行, skipped={Skipped})", swCopy.ElapsedMilliseconds, Progress.Read, missing);

            // Day 7.6: 计算 DISTINCT ON 去重掉的行数
            // WHY: silent 去重无信号,运维无法判断"为什么 read=36 但 inserted=0"
            //      公式: raw_count - distinct_count = 重复行数
            await using (var dupCmd = new NpgsqlCommand(@"
                SELECT count(*) - count(DISTINCT (product_id, oem_brand, oem_no_3))
                FROM xrefs_stage
                WHERE oem_brand IS NOT NULL AND oem_no_3 IS NOT NULL", conn))
            {
                var dup = (long)(await dupCmd.ExecuteScalarAsync(ct) ?? 0L);
                if (dup > 0)
                {
                    for (long i = 0; i < dup; i++) Progress.IncrSkippedDuplicate();
                    _logger.LogInformation("xrefs 去重: {Dup} 行 (DISTINCT ON)", dup);
                }
            }

            // Day 7: 加 mode + DISTINCT ON + ON CONFLICT
            // WHY: 真实 Excel 同 (product_id, oem_brand, oem_no_3) 多行,UNIQUE 索引会触发冲突
            //      DISTINCT ON (product_id, oem_brand, oem_no_3) 按 ctid DESC 取最后一行 (最新数据)
            string finalSql = mode switch
            {
                "full-load" => @"
                    TRUNCATE cross_references;
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

            var swInsert = System.Diagnostics.Stopwatch.StartNew();
            await using (var insert = new NpgsqlCommand(finalSql, conn))
            {
                insert.CommandTimeout = 0;
                var affected = await insert.ExecuteNonQueryAsync(ct);
                swInsert.Stop();
                _logger.LogInformation("[TIMING] xrefs {Mode}: {Ms}ms ({Affected} 行)", mode, swInsert.ElapsedMilliseconds, affected);
                if (mode == "full-load" || mode == "insert-only")
                    Progress.IncrInsertedBy(affected);
                else
                    Progress.IncrUpdatedBy(affected);
            }

            await using (var commit = new NpgsqlCommand("COMMIT;", conn))
                await commit.ExecuteNonQueryAsync(ct);
            Progress.Finish("xrefs", mode);
        }
        // Day 9.4: 区分取消与失败 (同 products, 见 ImportProductsAsync 注释)
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Progress.Cancel(_activeCancelReason ?? "用户取消", _activeCancelReasonCode ?? "OTHER");
            Progress.PersistLogAsync("xrefs", mode);
            _logger.LogInformation("ETL xrefs 任务被用户取消, reason={Reason} code={Code}",
                _activeCancelReason, _activeCancelReasonCode);
        }
        catch (Exception ex)
        {
            Progress.Fail(ex.Message, "xrefs", mode);
            _logger.LogError(ex, "xrefs 导入失败");
        }
        return Progress;
    }

    /// <summary>
    /// 导入机型适配 (apps.jsonl) -> machine_applications
    /// mode: "upsert" (默认) | "full-load" | "insert-only"
    /// </summary>
    public async Task<EtlProgress> ImportAppsAsync(string jsonlPath, string mode = "upsert", CancellationToken ct = default)
    {
        Progress.Reset();
        Progress.Start(jsonlPath);
        // Day 9.2: 文件总行数估算 (前端进度条)
        Progress.SetRowsTotal(EtlProgress.EstimateFileLines(jsonlPath));
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

            // Day 7.6: 计算 DISTINCT ON 去重掉的行数
            await using (var dupCmd = new NpgsqlCommand(@"
                SELECT count(*) - count(DISTINCT (product_id, machine_brand, machine_model))
                FROM apps_stage
                WHERE machine_brand IS NOT NULL AND machine_model IS NOT NULL", conn))
            {
                var dup = (long)(await dupCmd.ExecuteScalarAsync(ct) ?? 0L);
                if (dup > 0)
                {
                    for (long i = 0; i < dup; i++) Progress.IncrSkippedDuplicate();
                    _logger.LogInformation("apps 去重: {Dup} 行 (DISTINCT ON)", dup);
                }
            }

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

            await using (var commit = new NpgsqlCommand("COMMIT;", conn))
                await commit.ExecuteNonQueryAsync(ct);
            Progress.Finish("apps", mode);
        }
        // Day 9.4: 区分取消与失败 (同 products, 见 ImportProductsAsync 注释)
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Progress.Cancel(_activeCancelReason ?? "用户取消", _activeCancelReasonCode ?? "OTHER");
            Progress.PersistLogAsync("apps", mode);
            _logger.LogInformation("ETL apps 任务被用户取消, reason={Reason} code={Code}",
                _activeCancelReason, _activeCancelReasonCode);
        }
        catch (Exception ex)
        {
            Progress.Fail(ex.Message, "apps", mode);
            _logger.LogError(ex, "apps 导入失败");
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
