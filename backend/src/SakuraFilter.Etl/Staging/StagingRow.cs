namespace SakuraFilter.Etl.Staging;

public enum StagingSourceKind
{
    ProductSpecs,
    OemNumbers,
    Applications
}

/// <summary>
/// 单个暂存原始行，不做按 OEM 的聚合。
/// </summary>
public sealed record StagingRow(
    StagingSourceKind SourceKind,
    int SourceRowNo,
    string? OemNo1Raw,
    string? OemNo1Normalized,
    IReadOnlyDictionary<string, string?> Fields,
    string RowHash);
