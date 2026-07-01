namespace SakuraFilter.Core.DTOs;

/// <summary>
/// 产品表单 DTO (Day 8.1)
/// 用途: 后台产品录入表单 (规格 新思路.xlsx - 后台新增产品格式 7 个分区)
/// 设计:
///   - 单个 DTO 对应整个产品 (7 分区全在一个对象), 一次 POST 完成
///   - xref 和 machine_application 用嵌套 List, 一次提交
///   - 所有字段 nullable, 允许部分填写
/// </summary>
public record ProductFormDto
{
    // ============ 分区 1: 主信息 ============
    // OEM 2 作为产品主号(规格要求), 必填
    public string Oem2 { get; init; } = "";
    public string? ProductName1 { get; init; }
    public string? ProductName2 { get; init; }
    public string? Type { get; init; }
    public string? Mr1 { get; init; }
    public bool IsPublished { get; init; } = true;
    public string? Remark { get; init; }

    // ============ 分区 3: 尺寸 (mm) ============
    public decimal? D1Mm { get; init; }
    public decimal? D2Mm { get; init; }
    public decimal? D3Mm { get; init; }
    public decimal? D4Mm { get; init; }
    public decimal? H1Mm { get; init; }
    public decimal? H2Mm { get; init; }
    public decimal? H3Mm { get; init; }
    public decimal? H4Mm { get; init; }
    public string? D7Thread { get; init; }
    public string? D8Thread { get; init; }
    public int? NoCheckValves { get; init; }
    public int? NoBypassValves { get; init; }

    // ============ 分区 5: 技术参数 ============
    public string? Media { get; init; }
    public string? MediaModel { get; init; }
    public decimal? BypassValveLr { get; init; }
    public decimal? BypassValveHr { get; init; }
    public string? Efficiency1 { get; init; }
    public string? Efficiency2 { get; init; }
    public decimal? BypassPressure { get; init; }  // NUMERIC 列, 用 decimal 而非 string
    public decimal? CollapsePressureBar { get; init; }
    public string? SealingMaterial { get; init; }
    public string? TempRange { get; init; }

    // ============ 分区 6: 包装 ============
    public int? QtyPerCarton { get; init; }
    public decimal? WeightKgs { get; init; }
    public decimal? CartonLengthMm { get; init; }
    public decimal? CartonWidthMm { get; init; }
    public decimal? CartonHeightMm { get; init; }
    public int? MasterBoxQty { get; init; }
    public decimal? MasterBoxWeightKgs { get; init; }
    public decimal? MasterBoxLengthMm { get; init; }
    public decimal? MasterBoxWidthMm { get; init; }
    public decimal? MasterBoxHeightMm { get; init; }

    // ============ 分区 2: 交叉引用 (xref, 5-20 条/产品) ============
    public List<XrefInput> CrossReferences { get; init; } = new();

    // ============ 分区 7: 机型适配 (1-30 条/产品) ============
    public List<MachineAppInput> MachineApplications { get; init; } = new();
}

/// <summary>交叉引用输入项 (Day 8.1: 分区 2)</summary>
public record XrefInput(
    string? ProductName1,
    string? OemBrand,
    string? OemNo3
);

/// <summary>机型适配输入项 (Day 8.1: 分区 7 全部字段)</summary>
public record MachineAppInput(
    string? MachineBrand,
    string? MachineModel,
    string? ModelName,
    string? EngineBrand,
    string? EngineType,
    string? EngineEnergy,
    DateTime? ProductionDateStart,
    DateTime? ProductionDateEnd,
    string? Power,
    string? SerialNumberFrom,
    string? SerialNumberTo,
    string? CarBodyType,
    string? Series,
    string? Co2EmissionStandard,
    string? TransmissionType,
    string? EngineDisplacement,
    int? NumberOfCylinders,
    string? Gvwr,
    string? Tonnage,
    string? GeographicArea,
    string? ChassisType,
    string? EngineModel,
    string? CabinType,
    string? Capacity,
    string? EngineSerialNumber
);

/// <summary>产品图片信息 (Day 8.1: 分区 4, slot 1-6)</summary>
public record ProductImageInfo(
    long Id,
    long ProductId,
    short Slot,
    string ImageKey,
    string Url,        // 预签名 URL
    long? FileSize,
    string? ContentType,
    int? Width,
    int? Height,
    bool IsPrimary,
    DateTime UploadedAt,
    string? UploadedBy
);

/// <summary>产品列表查询响应 (Day 8.1: 后台产品列表分页)</summary>
public record ProductListItem(
    long Id,
    string OemNoDisplay,
    string? Oem2,
    string? Mr1,
    string? ProductName1,
    string? ProductName2,
    string Type,
    bool IsPublished,
    bool IsDiscontinued,
    string? ImageKey,
    string? ImageUrl,   // 主图预签名 URL
    DateTime UpdatedAt
);

/// <summary>产品详情响应 (Day 8.1: 后台编辑表单用, 含 xref + apps + images)</summary>
public record ProductDetailDto(
    long Id,
    string OemNoDisplay,
    string? Oem2,
    string? Mr1,
    string? ProductName1,
    string? ProductName2,
    string Type,
    bool IsPublished,
    string? Remark,
    decimal? D1Mm, decimal? D2Mm, decimal? D3Mm, decimal? D4Mm,
    decimal? H1Mm, decimal? H2Mm, decimal? H3Mm, decimal? H4Mm,
    string? D7Thread, string? D8Thread,
    int? NoCheckValves, int? NoBypassValves,
    string? Media, string? MediaModel,
    decimal? BypassValveLr, decimal? BypassValveHr,
    string? Efficiency1, string? Efficiency2, decimal? BypassPressure,
    decimal? CollapsePressureBar,
    string? SealingMaterial, string? TempRange,
    int? QtyPerCarton, decimal? WeightKgs,
    decimal? CartonLengthMm, decimal? CartonWidthMm, decimal? CartonHeightMm,
    int? MasterBoxQty, decimal? MasterBoxWeightKgs,
    decimal? MasterBoxLengthMm, decimal? MasterBoxWidthMm, decimal? MasterBoxHeightMm,
    decimal? VolumePerCartonM3,
    bool IsDiscontinued,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<XrefInfo> CrossReferences,
    List<MachineAppInfo> MachineApplications,
    List<ProductImageInfo> Images
);

public record XrefInfo(long Id, string? ProductName1, string? OemBrand, string? OemNo3);

public record MachineAppInfo(
    long Id,
    string? MachineBrand, string? MachineModel, string? ModelName,
    string? EngineBrand, string? EngineType, string? EngineEnergy,
    DateTime? ProductionDateStart, DateTime? ProductionDateEnd,
    string? Power,
    string? SerialNumberFrom, string? SerialNumberTo,
    string? CarBodyType, string? Series,
    string? Co2EmissionStandard, string? TransmissionType,
    string? EngineDisplacement, int? NumberOfCylinders,
    string? Gvwr, string? Tonnage, string? GeographicArea,
    string? ChassisType, string? EngineModel,
    string? CabinType, string? Capacity, string? EngineSerialNumber
);
