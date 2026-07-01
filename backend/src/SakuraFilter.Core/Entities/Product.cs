namespace SakuraFilter.Core.Entities;

using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// 滤芯产品主表 (100 万级,按 Type 分区)
/// </summary>
public class Product
{
    public long Id { get; set; }
    public string OemNoNormalized { get; set; } = "";
    public string OemNoDisplay { get; set; } = "";
    public string? Remark { get; set; }
    [Column("product_name_3")] public string? ProductName3 { get; set; }
    /// <summary>从 product_name_3 派生,ETL 强制注入</summary>
    public string Type { get; set; } = "";

    // 尺寸 (mm) - 显式列名(因 NamingConvention 不识别 D1Mm 中的 Mm 后缀)
    [Column("d1_mm")] public decimal? D1Mm { get; set; }
    [Column("d2_mm")] public decimal? D2Mm { get; set; }
    [Column("d3_mm")] public decimal? D3Mm { get; set; }
    [Column("h1_mm")] public decimal? H1Mm { get; set; }
    [Column("h2_mm")] public decimal? H2Mm { get; set; }
    [Column("h3_mm")] public decimal? H3Mm { get; set; }
    [Column("d7_thread")] public string? D7Thread { get; set; }
    [Column("d8_thread")] public string? D8Thread { get; set; }
    [Column("media")] public string? Media { get; set; }

    // 技术参数
    [Column("sealing_material")] public string? SealingMaterial { get; set; }
    [Column("efficiency_1")] public string? Efficiency1 { get; set; }
    [Column("bypass_valve_lr")] public decimal? BypassValveLr { get; set; }
    [Column("collapse_pressure_bar")] public decimal? CollapsePressureBar { get; set; }
    [Column("temp_range")] public string? TempRange { get; set; }

    // 包装
    [Column("qty_per_carton")] public int? QtyPerCarton { get; set; }
    [Column("weight_kgs")] public decimal? WeightKgs { get; set; }
    [Column("carton_length_mm")] public decimal? CartonLengthMm { get; set; }
    [Column("carton_width_mm")] public decimal? CartonWidthMm { get; set; }
    [Column("carton_height_mm")] public decimal? CartonHeightMm { get; set; }

    // 图片(只存 S3 key)
    [Column("image_key")] public string? ImageKey { get; set; }
    [Column("image_status")] public string ImageStatus { get; set; } = "pending";

    // 软删除
    [Column("is_discontinued")] public bool IsDiscontinued { get; set; }
    [Column("discontinued_at")] public DateTime? DiscontinuedAt { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // 导航属性
    public ICollection<CrossReference> CrossReferences { get; set; } = new List<CrossReference>();
    public ICollection<MachineApplication> MachineApplications { get; set; } = new List<MachineApplication>();
}

/// <summary>
/// 交叉引用(替代品牌+替代号,5-20 个/产品)
/// </summary>
public class CrossReference
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    [Column("product_name_1")] public string? ProductName1 { get; set; }
    [Column("oem_brand")] public string? OemBrand { get; set; }
    [Column("oem_no_3")] public string? OemNo3 { get; set; }
    [Column("is_discontinued")] public bool IsDiscontinued { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 机型适配(1-30 个/产品)
/// </summary>
public class MachineApplication
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    [Column("machine_brand")] public string? MachineBrand { get; set; }
    [Column("machine_model")] public string? MachineModel { get; set; }
    [Column("model_name")] public string? ModelName { get; set; }
    [Column("engine_brand")] public string? EngineBrand { get; set; }
    [Column("engine_type")] public string? EngineType { get; set; }
    [Column("engine_energy")] public string? EngineEnergy { get; set; }
    [Column("production_date_start")] public DateTime? ProductionDateStart { get; set; }
    [Column("is_ongoing")] public bool IsOngoing { get; set; } = true;
    [Column("is_discontinued")] public bool IsDiscontinued { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 产品变更历史(可配置保留时长)
/// </summary>
public class ProductHistory
{
    public long Id { get; set; }
    [Column("product_id")] public long ProductId { get; set; }
    [Column("change_type")] public string ChangeType { get; set; } = "";
    [Column("changed_fields")] public string? ChangedFields { get; set; }
    [Column("changed_by")] public string? ChangedBy { get; set; }
    [Column("changed_at")] public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 系统配置 (key-value)
/// </summary>
public class SystemSetting
{
    public string Key { get; set; } = "";
    public string? Value { get; set; }
    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 搜索索引写入补偿队列 (Day 5)
/// - Meili 写入失败时入队
/// - IndexReplayWorker 每 10s 重试,指数退避
/// - 超过 5 次重试后转入 dead_letter (Day 7)
/// </summary>
public class SearchIndexPending
{
    public long Id { get; set; }
    [Column("operation")] public string Operation { get; set; } = "";   // "index" or "delete"
    [Column("payload")] public string Payload { get; set; } = "";       // JSONB
    [Column("retry_count")] public int RetryCount { get; set; }
    [Column("last_error")] public string? LastError { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("next_retry_at")] public DateTime NextRetryAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 搜索索引写入死信队列 (Day 7)
/// - retry_count >= 5 仍失败的条目从 search_index_pending 转入此处
/// - 等待人工排查 (Meili schema 错误、payload 损坏、DB 索引冲突等)
/// - 不会自动重试,需手动 delete 或 update retry_count 后移回 pending
/// </summary>
public class SearchIndexDeadLetter
{
    public long Id { get; set; }
    [Column("original_id")] public long OriginalId { get; set; }     // 来源 pending.id
    [Column("operation")] public string Operation { get; set; } = "";
    [Column("payload")] public string Payload { get; set; } = "";
    [Column("retry_count")] public int RetryCount { get; set; }
    [Column("last_error")] public string? LastError { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }    // 原入队时间
    [Column("moved_at")] public DateTime MovedAt { get; set; } = DateTime.UtcNow;  // 转入死信时间
}

/// <summary>
/// ETL 运行历史 (Day 7.7)
/// - ETL Finish()/Fail() 时 INSERT 一行,作为"progress_log"快照
/// - 解决:进程重启后 EtlProgress 单例丢失,无法回溯昨天 14:00 的 ETL 状态
/// - 用途: 运维查"今天跑了哪些 ETL/成功失败/读入多少",Dashboard 直接 SELECT
/// - 不存 recentErrors 数组 (单条 message 体量大,留 last_error 即可)
/// </summary>
public class EtlProgressLog
{
    public long Id { get; set; }
    [Column("entity_type")] public string EntityType { get; set; } = "";   // products/xrefs/apps
    [Column("mode")] public string Mode { get; set; } = "";               // full-load/insert-only/upsert
    [Column("file_path")] public string FilePath { get; set; } = "";
    [Column("status")] public string Status { get; set; } = "";           // completed/failed
    [Column("read_count")] public long ReadCount { get; set; }
    [Column("inserted_count")] public long InsertedCount { get; set; }
    [Column("updated_count")] public long UpdatedCount { get; set; }
    [Column("skipped_count")] public long SkippedCount { get; set; }
    [Column("skipped_missing_oem")] public long SkippedMissingOem { get; set; }
    [Column("skipped_null_field")] public long SkippedNullField { get; set; }
    [Column("skipped_duplicate")] public long SkippedDuplicate { get; set; }
    [Column("error_count")] public long ErrorCount { get; set; }
    [Column("indexed_count")] public long IndexedCount { get; set; }
    [Column("index_pending_count")] public long IndexPendingCount { get; set; }
    [Column("last_error")] public string? LastError { get; set; }
    [Column("started_at")] public DateTime StartedAt { get; set; }
    [Column("finished_at")] public DateTime FinishedAt { get; set; }
    [Column("duration_sec")] public double DurationSec { get; set; }
    [Column("alert_sent")] public bool AlertSent { get; set; }  // Day 7.9: 失败告警是否已推送
}
