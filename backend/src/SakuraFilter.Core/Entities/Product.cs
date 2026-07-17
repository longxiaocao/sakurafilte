namespace SakuraFilter.Core.Entities;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// 滤芯产品主表 (100 万级,按 Type 分区)
/// Day 8.1: 扩展 19 字段支撑后台 7 分区录入表单 (规格 新思路.xlsx)
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

    // ========== Day 8.1: 分区 1 (Product Name 1, Product Name 2, MR.1, OEM 2, 上架) ==========
    // WHY product_name_1 在主表: 规格"一个产品一个主名", 与 xref 的 product_name_1 区分
    [Column("product_name_1")] public string? ProductName1 { get; set; }
    [Column("product_name_2")] public string? ProductName2 { get; set; }
    [Column("mr_1")]           public string? Mr1 { get; set; }
    [Column("oem_2")]          public string? Oem2 { get; set; }
    [Column("is_published")]   public bool IsPublished { get; set; } = true;  // 上架

    // 尺寸 (mm) - 显式列名(因 NamingConvention 不识别 D1Mm 中的 Mm 后缀)
    [Column("d1_mm")] public decimal? D1Mm { get; set; }
    [Column("d2_mm")] public decimal? D2Mm { get; set; }
    [Column("d3_mm")] public decimal? D3Mm { get; set; }
    [Column("d4_mm")] public decimal? D4Mm { get; set; }  // Day 8.1
    [Column("h1_mm")] public decimal? H1Mm { get; set; }
    [Column("h2_mm")] public decimal? H2Mm { get; set; }
    [Column("h3_mm")] public decimal? H3Mm { get; set; }
    [Column("h4_mm")] public decimal? H4Mm { get; set; }  // Day 8.1
    [Column("d7_thread")] public string? D7Thread { get; set; }
    [Column("d8_thread")] public string? D8Thread { get; set; }
    [Column("media")] public string? Media { get; set; }

    // V2: 尺寸原始值(ETL 导入的原始文本,保留用于溯源)
    [Column("d1_mm_raw")] public string? D1MmRaw { get; set; }
    [Column("d2_mm_raw")] public string? D2MmRaw { get; set; }
    [Column("d3_mm_raw")] public string? D3MmRaw { get; set; }
    [Column("d4_mm_raw")] public string? D4MmRaw { get; set; }
    [Column("h1_mm_raw")] public string? H1MmRaw { get; set; }
    [Column("h2_mm_raw")] public string? H2MmRaw { get; set; }
    [Column("h3_mm_raw")] public string? H3MmRaw { get; set; }
    [Column("h4_mm_raw")] public string? H4MmRaw { get; set; }

    // ========== Day 8.1: 分区 3 (No. Check / Bypass Valves) ==========
    [Column("no_check_valves")]  public int? NoCheckValves { get; set; }
    [Column("no_bypass_valves")] public int? NoBypassValves { get; set; }

    // ========== Day 8.1: 分区 5 (Media Model, Bypass Valve HR, Efficiency 2, Bypass Pressure) ==========
    [Column("media_model")]     public string? MediaModel { get; set; }
    [Column("sealing_material")] public string? SealingMaterial { get; set; }
    [Column("efficiency_1")]    public string? Efficiency1 { get; set; }
    [Column("efficiency_2")]    public string? Efficiency2 { get; set; }  // Day 8.1
    [Column("bypass_valve_lr")] public decimal? BypassValveLr { get; set; }
    [Column("bypass_valve_hr")] public decimal? BypassValveHr { get; set; }  // Day 8.1
    [Column("bypass_pressure")] public decimal? BypassPressure { get; set; }  // Day 8.1 (列早存在, 类型 NUMERIC)
    [Column("collapse_pressure_bar")] public decimal? CollapsePressureBar { get; set; }
    [Column("temp_range")]      public string? TempRange { get; set; }

    // 包装 (Carton)
    [Column("qty_per_carton")]     public int? QtyPerCarton { get; set; }
    [Column("weight_kgs")]         public decimal? WeightKgs { get; set; }
    [Column("carton_length_mm")]   public decimal? CartonLengthMm { get; set; }
    [Column("carton_width_mm")]    public decimal? CartonWidthMm { get; set; }
    [Column("carton_height_mm")]   public decimal? CartonHeightMm { get; set; }

    // ========== Day 8.1: 分区 6 (MasterBox + 派生体积) ==========
    [Column("master_box_qty")]          public int? MasterBoxQty { get; set; }
    [Column("master_box_weight_kgs")]   public decimal? MasterBoxWeightKgs { get; set; }
    [Column("master_box_length_mm")]    public decimal? MasterBoxLengthMm { get; set; }
    [Column("master_box_width_mm")]     public decimal? MasterBoxWidthMm { get; set; }
    [Column("master_box_height_mm")]    public decimal? MasterBoxHeightMm { get; set; }
    [Column("volume_per_carton_m3")]    public decimal? VolumePerCartonM3 { get; set; }  // 派生

    // 图片(只存 S3 key, 6 张图走 product_images 表)
    [Column("image_key")]    public string? ImageKey { get; set; }  // 主图(图1)
    [Column("image_status")] public string ImageStatus { get; set; } = "pending";

    // 软删除
    [Column("is_discontinued")] public bool IsDiscontinued { get; set; }
    [Column("discontinued_at")] public DateTime? DiscontinuedAt { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // 乐观锁并发控制 (E2E BD.3 修复 v2)
    //   WHY: 之前无并发令牌, 两个管理员同时编辑同一产品时, 后提交者会覆盖前者的修改 (lost update)
    //   方案: 使用 PostgreSQL 系统列 xmin 作为并发令牌 (Npgsql 官方推荐, 无需新增列, 无需触发器)
    //   EF Core 在 UPDATE 时自动 SET WHERE xmin = @original_xmin, 不匹配抛 DbUpdateConcurrencyException
    //   端点层捕获后返回 409 Conflict, 前端提示"数据已被修改, 请刷新后重试"
    //   注意 1: xmin 是 PG 系统列 (每个表自动有), 不能 INSERT/UPDATE, 只能 SELECT
    //   注意 2: xmin 类型为 xid (uint32), 必须用 uint, 不能用 byte[] (否则 Npgsql 抛 InvalidCastException)
    //           EF Core [Timestamp] 特性强制 byte[], 不能用! 改用 Fluent API IsRowVersion()
    //   参考: https://www.npgsql.org/efcore/versioning?tabs=without-attribute
    [Column("xmin")]
    public uint RowVersion { get; set; }

    // 导航属性
    public ICollection<CrossReference> CrossReferences { get; set; } = new List<CrossReference>();
    public ICollection<MachineApplication> MachineApplications { get; set; } = new List<MachineApplication>();
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();  // Day 8.1
}

/// <summary>
/// 产品图片 (Day 8.1: 规格分区 4, 1-6 张图)
/// V2: 新增 oem_no_3 + image_role 字段,主图按 OEM 3 命名,详情图按 MR.1 共享
/// </summary>
public class ProductImage
{
    public long Id { get; set; }
    [Column("product_id")]   public long ProductId { get; set; }
    public short Slot { get; set; }  // 1-6
    [Column("image_key")]    public string ImageKey { get; set; } = "";
    [Column("file_size")]    public long? FileSize { get; set; }
    [Column("content_type")] public string? ContentType { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    [Column("is_primary")]   public bool IsPrimary { get; set; }
    [Column("display_order")] public int DisplayOrder { get; set; }
    [Column("uploaded_at")]  public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    [Column("uploaded_by")]  public string? UploadedBy { get; set; }

    // V2: 图片分层(主图按 OEM 3 / 详情图按 MR.1)
    [Column("oem_no_3")] public string? OemNo3 { get; set; }  // V2: 主图关联的 OEM 3
    [Column("image_role")] public string ImageRole { get; set; } = "detail";  // V2: "primary" / "detail"

    // 导航
    public Product? Product { get; set; }
}

/// <summary>
/// 交叉引用(替代品牌+替代号,5-20 个/产品)
/// V2: oem_no_3 升级为对外展示主键,新增 sort_order/machine_type/is_published/oem_2 + xmin 并发令牌
/// </summary>
public class CrossReference
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    [Column("product_name_1")] public string? ProductName1 { get; set; }
    [Column("oem_brand")] public string? OemBrand { get; set; }
    [Column("oem_no_3")] public string? OemNo3 { get; set; }
    [Column("oem_2")] public string? Oem2 { get; set; }  // V2: OEM 2 全量收纳
    [Column("sort_order")] public int SortOrder { get; set; } = 0;  // V2: OEM 3 排序(类竞价排名)
    [Column("machine_type")] public string? MachineType { get; set; } = "others";  // V2: 机型类型双轨
    [Column("is_published")] public bool IsPublished { get; set; } = true;  // V2: 是否发布
    [Column("is_discontinued")] public bool IsDiscontinued { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // V2: xmin 乐观锁令牌(复用 PostgreSQL 系统列,与 Product.RowVersion 同机制)
    [Column("xmin")]
    public uint RowVersion { get; set; }

    // 导航属性
    public Product? Product { get; set; }
}

/// <summary>
/// 机型适配(1-30 个/产品)
/// Day 8.1: 扩展 18 字段支撑后台规格分区 7 录入
/// V2: 新增 machine_category 字段(与 cross_references.machine_type 双轨存储,前端分类树读此字段)
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

    // V2: 机型分类(与 cross_references.machine_type 枚举一致)
    [Column("machine_category")] public string? MachineCategory { get; set; } = "others";  // V2: agriculture/commercial/construction/industrial/others

    // ========== Day 8.1: 分区 7 扩展 (生产日期 / 动力 / 车架号 / 车身 / 底盘 / 发动机 / 排放) ==========
    [Column("production_date_start")] public DateTime? ProductionDateStart { get; set; }
    [Column("production_date_end")]   public DateTime? ProductionDateEnd { get; set; }
    [Column("power")]                  public string? Power { get; set; }
    [Column("serial_number_from")]     public string? SerialNumberFrom { get; set; }
    [Column("serial_number_to")]       public string? SerialNumberTo { get; set; }
    [Column("car_body_type")]          public string? CarBodyType { get; set; }
    [Column("series")]                 public string? Series { get; set; }
    [Column("co2_emission_standard")]  public string? Co2EmissionStandard { get; set; }
    [Column("transmission_type")]      public string? TransmissionType { get; set; }
    [Column("engine_displacement")]    public string? EngineDisplacement { get; set; }
    [Column("number_of_cylinders")]    public int? NumberOfCylinders { get; set; }
    [Column("gvwr")]                   public string? Gvwr { get; set; }
    [Column("tonnage")]                public string? Tonnage { get; set; }
    [Column("geographic_area")]        public string? GeographicArea { get; set; }
    [Column("chassis_type")]           public string? ChassisType { get; set; }
    [Column("engine_model")]           public string? EngineModel { get; set; }
    [Column("cabin_type")]             public string? CabinType { get; set; }
    [Column("capacity")]               public string? Capacity { get; set; }
    [Column("engine_serial_number")]   public string? EngineSerialNumber { get; set; }

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
/// 交叉引用 OEM 品牌字典 (Day 10: P1.3)
/// 用途: 规格后台新增产品格式 分区 2 的 oem_brand 自动补全 + 前端拖拽排序
/// 设计:
///   - brand 唯一索引 (DbContext 加 .IsUnique())
///   - sort_order 决定在前台 / 后台 typeahead 的展示顺序
///   - deleted_at 软删除标记: 有值的行不进 typeahead, 但保留历史 xref.oem_brand 可追溯
///     WHY 软删除: cross_references.oem_brand 是历史数据, 字典删了不等于 xref 也失效
///   - 计数 count_by_brand 不存表: 需要时 SQL 实时聚合, 数据量小开销可忽略
/// </summary>
public class XrefOemBrand
{
    public long Id { get; set; }
    [Column("brand")]       public string Brand { get; set; } = "";
    [Column("sort_order")]  public int SortOrder { get; set; }  // 默认 0
    [Column("created_at")]  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")]  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [Column("deleted_at")]  public DateTime? DeletedAt { get; set; }  // 软删除: 非 null 即隐藏
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

    // Day 7.10 Item 4: 自动恢复元数据
    [Column("recovery_count")] public int RecoveryCount { get; set; } = 0;
    [Column("last_recovery_at")] public DateTime? LastRecoveryAt { get; set; }
    [Column("last_recovery_error")] public string? LastRecoveryError { get; set; }

    // Day 7.10.1 Bug Fix: status 列 — 死信永不删除,改 status 标记
    //   'active'    = 等待处理 (worker 扫描候选)
    //   'recovered' = 已恢复到 pending, 历史留痕 (worker 跳过, cleanup 可清)
    [Column("status")] public string Status { get; set; } = "active";
    [Column("recovered_at")] public DateTime? RecoveredAt { get; set; }
    [Column("recovered_to_pending_id")] public long? RecoveredToPendingId { get; set; }
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
    // Day 9.4: 取消审计 (DELETE /api/admin/etl/task 时写入)
    [Column("cancel_reason")] public string? CancelReason { get; set; }
    [Column("cancelled_at")] public DateTime? CancelledAt { get; set; }
    // Day 9.5: 取消原因标准化枚举码 (USER_REQUEST/TIMEOUT/SYSTEM_SHUTDOWN/ADMIN_OVERRIDE/OTHER)
    [Column("reason_code")] public string? ReasonCode { get; set; }
    // P1.1 (Task 3): 暂停/恢复 checkpoint — 最后成功 COMMIT 的批次 ID, 暂停时更新
    //   语义:
    //     - NULL  = 该次 ETL 未暂停过 (正常完成或失败)
    //     - 数字   = 最后一次 Pause 时刻已 COMMIT 的最大批次 id (后续用此值续读)
    //   不影响历史 ETL 日志 (nullable), 仅新生成的暂停任务会填
    [Column("checkpoint_id")] public long? CheckpointId { get; set; }
}

// ========== Day 10+ P2.2: 字典实体 (复用 P2.1 IDictService + BaseDictService 抽象) ==========
// 设计:
//   - 表名固定 dict_* 前缀, 与历史业务表 (products / cross_references / machine_applications) 区分
//   - 统一字段: id / value / sort_order / created_at / updated_at / deleted_at
//   - xrefCount 不存表, 服务层 GetXrefCountAsync 实时聚合来源表
//   - 单字段字典 (ProductName1/2, Type, OemNo3) 与 Day 10 XrefOemBrand 一致
//   - 多字段字典 (Media: 2 字段, Machine: 3 字段, Engine: 2 字段) 主值字段为 *Name/Brand
//     UNIQUE 索引覆盖主字段, ExtraSearchProperties 让 List/Typeahead 走 OR 匹配所有字段

/// <summary>
/// 产品名 1 字典 (Day 10+ P2.2)
/// 用途: 后台产品表单分区 1 product_name_1 自动补全, 来源 products.product_name_1
/// </summary>
public class DictProductName1
{
    public long Id { get; set; }
    [Column("product_name_1")] public string ProductName1 { get; set; } = "";
    [Column("sort_order")] public int SortOrder { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [Column("deleted_at")] public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// 产品名 2 字典 (Day 10+ P2.2)
/// 用途: 后台产品表单分区 1 product_name_2 自动补全, 来源 products.product_name_2
/// </summary>
public class DictProductName2
{
    public long Id { get; set; }
    [Column("product_name_2")] public string ProductName2 { get; set; } = "";
    [Column("sort_order")] public int SortOrder { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [Column("deleted_at")] public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// Type 字典 (Day 10+ P2.2) - 固定 5 值: oil/fuel/air/cabin/others
/// 用途: 后台产品表单分区 1 type 自动补全, 来源 products.type
/// </summary>
public class DictType
{
    public long Id { get; set; }
    [Column("type")] public string Type { get; set; } = "";
    [Column("sort_order")] public int SortOrder { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [Column("deleted_at")] public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// OEM 3 字典 (Day 10+ P2.2)
/// 用途: 后台产品表单分区 2 oem_no_3 自动补全, 来源 cross_references.oem_no_3
/// </summary>
public class DictOemNo3
{
    public long Id { get; set; }
    [Column("oem_no_3")] public string OemNo3 { get; set; } = "";
    [Column("sort_order")] public int SortOrder { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [Column("deleted_at")] public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// Media 字典 (Day 10+ P2.2) - 2 字段: media_name + media_model
/// 用途: 后台产品表单分区 4 media + media_model 自动补全 (二合一)
/// 设计: 主值字段 media_name, UNIQUE (media_name, media_model), ExtraSearchProperties=[MediaModel]
/// </summary>
public class DictMedia
{
    public long Id { get; set; }
    [Column("media_name")] public string MediaName { get; set; } = "";
    [Column("media_model")] public string? MediaModel { get; set; }
    [Column("sort_order")] public int SortOrder { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [Column("deleted_at")] public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// Machine 字典 (Day 10+ P2.2) - 3 字段: machine_brand + machine_model + machine_name
/// 用途: 后台产品表单分区 7 machine_brand/model/name 自动补全 (三合一)
/// 设计: 主值字段 machine_brand, UNIQUE (machine_brand, machine_model, machine_name),
///       ExtraSearchProperties=[MachineModel, MachineName]
/// P2.3: 新增 machine_category (4 大类: Agriculture/Commercial/Construction/others)
///   - 用途: 前台按场景聚合品牌, /api/public/machine-brands/aggregated 端点用
///   - 默认值 'others' (兼容老数据, 不强制回填)
///   - max 50 (与 type 字典保持一致)
/// </summary>
public class DictMachine
{
    public long Id { get; set; }
    [Column("machine_brand")] public string MachineBrand { get; set; } = "";
    [Column("machine_model")] public string? MachineModel { get; set; }
    [Column("machine_name")] public string? MachineName { get; set; }
    [Column("machine_category")] public string MachineCategory { get; set; } = "others";
    [Column("sort_order")] public int SortOrder { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [Column("deleted_at")] public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// Engine 字典 (Day 10+ P2.2) - 2 字段: engine_brand + engine_type
/// 用途: 后台产品表单分区 7 engine_brand/type 自动补全 (二合一)
/// 设计: 主值字段 engine_brand, UNIQUE (engine_brand, engine_type), ExtraSearchProperties=[EngineType]
/// </summary>
public class DictEngine
{
    public long Id { get; set; }
    [Column("engine_brand")] public string EngineBrand { get; set; } = "";
    [Column("engine_type")] public string? EngineType { get; set; }
    [Column("sort_order")] public int SortOrder { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [Column("deleted_at")] public DateTime? DeletedAt { get; set; }
}
