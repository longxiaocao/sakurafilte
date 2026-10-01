namespace SakuraFilter.Etl.Staging;

/// <summary>
/// 已审核 OEM 映射发布命令选项。Apply=false 时只能执行只读预检。
/// </summary>
public sealed record ApprovedOemPublishOptions(long BatchId, string PgConnectionString, bool Apply);

public sealed record ApprovedOemPublishParseResult(ApprovedOemPublishOptions? Options, string? Error);

/// <summary>
/// 预检只统计 approved 映射涉及的 clean 行，不代表允许立即清空或发布正式库。
/// </summary>
public sealed record ApprovedOemPublishPreflightReport(
    long BatchId,
    long ApprovedMappingCount,
    long PublishableMappingCount,
    long BlockedMappingCount,
    long ProductSpecCount,
    long CrossReferenceCount,
    long ApplicationCount,
    long ProductSpecConflictCount)
{
    public bool HasNoApprovedMappings => ApprovedMappingCount == 0;
    public bool IsReadyToApply => ApprovedMappingCount > 0 && BlockedMappingCount == 0;
}
