namespace SakuraFilter.Core.DTOs;

/// <summary>
/// Day 8.4: 产品变更历史响应
/// 用途: 后台详情页"变更记录"tab 显示
/// 设计:
///   - ChangedFields 是 JSON 字符串 (entity 存的就是 JSON)
///   - ChangeType: create / update / discontinue / restore
///   - 倒序返回 (最新变更在前)
/// </summary>
public record ProductHistoryItemDto(
    long Id,
    long ProductId,
    string ChangeType,
    string? ChangedBy,
    DateTime ChangedAt,
    string? ChangedFields
);

/// <summary>Day 9.3: 历史分页响应
///   Day 9.4: 加 NextCursor, keyset 分页用 (ChangedAt + Id 编码 base64url)
///   nextCursor == null 表示无下一页 (已翻到末尾)
/// </summary>
public record ProductHistoryPageDto(
    int Total,
    int Limit,
    string? ChangeType,
    DateTime? Since,
    DateTime? Until,
    List<ProductHistoryItemDto> Items,
    string? NextCursor = null
);

/// <summary>
/// 后台手动 ETL 触发请求
/// 用途: 后台 ETL 页面点"立即导入"按钮调用
/// 字段:
///   - JsonlPath: 服务器上的 JSONL 绝对路径 (前端无法直接传文件给后端, 由管理端上传到共享盘)
///   - Mode: full-load | insert-only | upsert (Day 7 设计, 三态统一)
///   - DryRun: true 时只校验不写库 (Day 8.4 增强)
/// </summary>
public record EtlTriggerRequest(string JsonlPath, string? Mode, bool DryRun = false, string? EntityType = null);

/// <summary>
/// ETL 进度响应 (含手动触发任务)
/// 用途: 后台 ETL 页面轮询 (3 秒一次)
/// 字段:
///   - InProgress: 是否有 ETL 在跑
///   - Last: 上一次完成的任务
///   - RecentErrors: 最近 10 条错误
///   - ActiveTask: 当前手动触发的任务 (含 ID + 进度百分比)
/// </summary>
public record EtlProgressDto(
    bool InProgress,
    EtlProgressItem? Last,
    List<EtlRecentError> RecentErrors,
    EtlActiveTask? ActiveTask
);

public record EtlProgressItem(
    long Id,
    string EntityType,    // products / xrefs / apps
    string Mode,          // full-load / insert-only / upsert
    string Status,        // running / success / failed
    long? RowsStaged,
    long? RowsInserted,
    long? RowsUpdated,
    long? RowsSkipped,
    DateTime StartedAt,
    DateTime? FinishedAt,
    long? DurationMs,
    string? ErrorMessage
);

public record EtlRecentError(
    long Id,
    string EntityType,
    string Mode,
    string Status,
    long? RowsStaged,
    long? RowsInserted,
    string? ErrorMessage,
    DateTime StartedAt,
    DateTime? FinishedAt
);

public record EtlActiveTask(
    long Id,
    string EntityType,
    string Mode,
    DateTime StartedAt,
    string Stage,         // reading / staging / commit / meili-sync
    long? RowsProcessed,
    long? RowsTotal,
    int? ProgressPct
);
